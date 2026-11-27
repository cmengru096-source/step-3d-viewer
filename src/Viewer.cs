using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;

[assembly: AssemblyTitle("STEP 3D Viewer")]
[assembly: AssemblyDescription("Portable offline viewer for STEP and STP files")]
[assembly: AssemblyProduct("STEP 3D Viewer")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace AssemblyViewer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var app = new Application();
                Window window;
                using (var layout = Assembly.GetExecutingAssembly().GetManifestResourceStream("Viewer.Layout"))
                    window = (Window)XamlReader.Load(layout);

                var viewer = new Viewer(window, true);
                if (args.Length > 0) window.Loaded += async (sender, e) => await viewer.OpenPathAsync(args[0]);
                return app.Run(window);
            }
            catch (Exception error)
            {
                MessageBox.Show("The viewer could not start.\n\n" + error.Message, "STEP 3D Viewer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }

    internal sealed partial class Viewer
    {
        private const double Degrees = Math.PI / 180.0;
        private readonly Window window;
        private readonly Grid canvas;
        private readonly ModelVisual3D modelVisual = new ModelVisual3D();
        private readonly OrthographicCamera camera = new OrthographicCamera();
        private readonly LoadedModel sample = MeshReader.Read("Viewer.Sample");
        private LoadedModel left;
        private LoadedModel right;
        private enum Angle { Full, Front, Top, End, LeftEnd, RightEnd, Free }
        private Angle view;

        private Rect3D bounds;
        private Point3D target;
        private double yaw;
        private double pitch;
        private double fitWidth;
        private double zoom = 1.0;
        private Point previousMouse;
        private MouseButton? dragButton;

        public Viewer(Window window, bool rememberLanguage)
        {
            this.window = window;
            canvas = Element<Grid>("Canvas");
            var scene = Element<Viewport3D>("Scene");
            scene.Camera = camera;
            scene.Children.Add(new ModelVisual3D { Content = CreateLights() });
            scene.Children.Add(modelVisual);

            Element<Button>("SampleButton").Click += (sender, e) => ShowSample();
            Element<RadioButton>("LeftAssembly").Checked += (sender, e) => { if (!updatingSelection) ShowAssembly(true); };
            Element<RadioButton>("RightAssembly").Checked += (sender, e) => { if (!updatingSelection) ShowAssembly(false); };
            Element<Button>("FitButton").Click += (sender, e) => ResetView();
            Element<Button>("FrontButton").Click += (sender, e) => SetView(-90, 0, Angle.Front);
            Element<Button>("TopButton").Click += (sender, e) => SetView(-90, 90, Angle.Top);
            Element<Button>("EndButton").Click += (sender, e) => SetView(0, 0, Angle.End);
            Element<Button>("LeftEndButton").Click += (sender, e) => ShowEnd(true);
            Element<Button>("RightEndButton").Click += (sender, e) => ShowEnd(false);

            canvas.SizeChanged += (sender, e) => UpdateFit();
            canvas.MouseDown += StartDrag;
            canvas.MouseMove += MoveDrag;
            canvas.MouseUp += EndDrag;
            canvas.LostMouseCapture += (sender, e) => ClearDrag();
            canvas.MouseWheel += ZoomAtCursor;
            canvas.KeyDown += (sender, e) =>
            {
                if (e.Key != Key.Home) return;
                ResetView();
                e.Handled = true;
            };

            ConnectFileImport();
            ConnectLanguage(rememberLanguage);
            ShowSample();
        }

        private T Element<T>(string name) where T : FrameworkElement
        {
            return (T)window.FindName(name);
        }

        private static Model3DGroup CreateLights()
        {
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(125, 125, 125)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(201, 201, 201), new Vector3D(0.25, 1.2, -1.1)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(106, 106, 106), new Vector3D(-0.6, -0.75, -0.1)));
            lights.Freeze();
            return lights;
        }

        private Point3D Center()
        {
            return new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        }

        private void SetView(double azimuth, double elevation, Angle angle)
        {
            yaw = azimuth;
            pitch = elevation;
            target = Center();
            zoom = 1;
            view = angle;
            UpdateViewLabel();
            UpdateFit();
        }

        private void ResetView()
        {
            SetView(IsElongated ? -108 : -63, 27, Angle.Full);
        }

        private void ShowEnd(bool isLeft)
        {
            SetView(isLeft ? -137 : -43, 25, isLeft ? Angle.LeftEnd : Angle.RightEnd);
            var section = Math.Max(bounds.SizeY, bounds.SizeZ);
            target.X = isLeft ? bounds.X + section * 0.7 : bounds.X + bounds.SizeX - section * 0.7;
            zoom = Math.Min(110, fitWidth / (section * 2.9 * Math.Max(1, AspectRatio)));
            UpdateCamera();
        }

        private double AspectRatio
        {
            get { return Math.Max(1, canvas.ActualWidth) / Math.Max(1, canvas.ActualHeight); }
        }

        private Vector3D Outward
        {
            get
            {
                var cosPitch = Math.Cos(pitch * Degrees);
                return new Vector3D(cosPitch * Math.Cos(yaw * Degrees),
                    cosPitch * Math.Sin(yaw * Degrees), Math.Sin(pitch * Degrees));
            }
        }

        private Vector3D ScreenRight
        {
            get { return new Vector3D(-Math.Sin(yaw * Degrees), Math.Cos(yaw * Degrees), 0); }
        }

        private Vector3D ScreenUp
        {
            get { return Vector3D.CrossProduct(Outward, ScreenRight); }
        }

        private double ProjectedSize(Vector3D axis)
        {
            return Math.Abs(axis.X) * bounds.SizeX + Math.Abs(axis.Y) * bounds.SizeY + Math.Abs(axis.Z) * bounds.SizeZ;
        }

        private void UpdateFit()
        {
            if (modelVisual.Content == null) return;
            fitWidth = Math.Max(ProjectedSize(ScreenRight), ProjectedSize(ScreenUp) * AspectRatio) * 1.17;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            double size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            var offset = Outward * (size * 1.6);
            camera.Position = target + offset;
            camera.LookDirection = -offset;
            camera.UpDirection = ScreenUp;
            camera.NearPlaneDistance = size / 1000;
            camera.FarPlaneDistance = size * 4.0;
            camera.Width = fitWidth / zoom;
            UpdateZoomLabel();
        }

        private void StartDrag(object sender, MouseButtonEventArgs e)
        {
            if (dragButton.HasValue || (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)) return;
            canvas.Focus();
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                ResetView();
            }
            else if (canvas.CaptureMouse())
            {
                dragButton = e.ChangedButton;
                previousMouse = e.GetPosition(canvas);
                canvas.Cursor = dragButton == MouseButton.Right ? Cursors.SizeAll : Cursors.Hand;
            }
            e.Handled = true;
        }

        private void MoveDrag(object sender, MouseEventArgs e)
        {
            if (!dragButton.HasValue) return;
            var position = e.GetPosition(canvas);
            var change = position - previousMouse;
            previousMouse = position;
            if (change.LengthSquared == 0) return;
            if (dragButton == MouseButton.Left)
            {
                yaw = (yaw - change.X * 0.35) % 360;
                pitch = Math.Max(-89.8, Math.Min(89.8, pitch + change.Y * 0.35));
                view = Angle.Free;
                UpdateViewLabel();
            }
            else
            {
                var unitsPerPixel = camera.Width / Math.Max(1, canvas.ActualWidth);
                target -= ScreenRight * (change.X * unitsPerPixel) - ScreenUp * (change.Y * unitsPerPixel);
            }
            UpdateCamera();
        }

        private void EndDrag(object sender, MouseButtonEventArgs e)
        {
            if (dragButton != e.ChangedButton) return;
            canvas.ReleaseMouseCapture();
            ClearDrag();
            e.Handled = true;
        }

        private void ClearDrag()
        {
            dragButton = null;
            canvas.ClearValue(FrameworkElement.CursorProperty);
        }

        private void ZoomAtCursor(object sender, MouseWheelEventArgs e)
        {
            var cursor = e.GetPosition(canvas);
            var canvasWidth = Math.Max(1, canvas.ActualWidth);
            var oldWidth = camera.Width;
            zoom = Math.Max(0.42, Math.Min(110, zoom * Math.Pow(1.18, e.Delta / 120.0)));
            var scale = (oldWidth - fitWidth / zoom) / canvasWidth;
            target += scale * ((cursor.X - canvasWidth / 2) * ScreenRight +
                (canvas.ActualHeight / 2 - cursor.Y) * ScreenUp);
            UpdateCamera();
            e.Handled = true;
        }
    }

    internal sealed class LoadedModel
    {
        public readonly Model3DGroup Geometry;
        public readonly int PartCount;

        public LoadedModel(Model3DGroup geometry, int partCount)
        {
            Geometry = geometry;
            PartCount = partCount;
        }
    }

    internal static class MeshReader
    {
        private static Material CreateMaterial(byte red, byte green, byte blue)
        {
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(red, green, blue))));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(100, 215, 230, 249)), 35));
            material.Freeze();
            return material;
        }

        public static LoadedModel Read(string resource)
        {
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                return Read(input);
        }

        public static LoadedModel Read(Stream input)
        {
            using (var compressed = new GZipStream(input, CompressionMode.Decompress, true))
            using (var binary = new BinaryReader(new BufferedStream(compressed, 65536)))
            {
                if (binary.ReadInt32() != 0x3131354D) throw new InvalidDataException("Unrecognized mesh data.");
                var assembly = new Model3DGroup();
                var partCount = binary.ReadInt32();
                var rotate = (binary.ReadInt32() & 1) != 0;
                for (int part = 0; part < partCount; part++)
                {
                    var chunks = binary.ReadInt32();
                    var red = binary.ReadByte();
                    var green = binary.ReadByte();
                    var blue = binary.ReadByte();
                    var flags = binary.ReadByte();
                    var hasNormals = (flags & 1) != 0;
                    var material = (flags & 2) != 0 ? CreateMaterial(red, green, blue) : CreateMaterial(109, 137, 167);
                    for (int chunk = 0; chunk < chunks; chunk++)
                    {
                        var geometry = ReadMesh(binary, hasNormals, rotate);
                        var model = new GeometryModel3D(geometry, material) { BackMaterial = material };
                        assembly.Children.Add(model);
                    }
                }
                var bounds = assembly.Bounds;
                if (bounds.IsEmpty || Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)) <= 0)
                    throw new InvalidDataException("The STEP file does not contain renderable geometry.");
                assembly.Freeze();
                return new LoadedModel(assembly, partCount);
            }
        }

        private static MeshGeometry3D ReadMesh(BinaryReader binary, bool hasNormals, bool rotate)
        {
            var vertexCount = binary.ReadInt32();
            var indexCount = binary.ReadInt32();
            var positions = new Point3DCollection(vertexCount);
            var indices = new Int32Collection(indexCount);
            // Slender models on the STEP Z axis are displayed horizontally.
            for (int i = 0; i < vertexCount; i++)
            {
                var x = binary.ReadSingle();
                var y = binary.ReadSingle();
                var z = binary.ReadSingle();
                positions.Add(rotate ? new Point3D(z, y, -x) : new Point3D(x, y, z));
            }
            var mesh = new MeshGeometry3D { Positions = positions };
            if (hasNormals)
            {
                var normals = new Vector3DCollection(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    var x = binary.ReadSingle();
                    var y = binary.ReadSingle();
                    var z = binary.ReadSingle();
                    normals.Add(rotate ? new Vector3D(z, y, -x) : new Vector3D(x, y, z));
                }
                mesh.Normals = normals;
            }
            for (int i = 0; i < indexCount; i++) indices.Add(binary.ReadInt32());
            mesh.TriangleIndices = indices;
            mesh.Freeze();
            return mesh;
        }
    }
}
