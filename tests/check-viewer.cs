using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

internal static class ViewerCheck
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var assembly = Assembly.LoadFrom(args[0]);
            var resources = assembly.GetManifestResourceNames();
            Require(resources.Contains("Viewer.Node") && resources.Contains("Viewer.OcctWasm"),
                "The EXE must embed its local STEP reader and require no installed CAD tools");
            Require(resources.Contains("Viewer.Sample") && !resources.Contains("Viewer.Left") && !resources.Contains("Viewer.Right"),
                "The public build must only embed the demo model");
            Window window;
            using (var source = assembly.GetManifestResourceStream("Viewer.Layout"))
                window = (Window)XamlReader.Load(source);
            var viewer = Activator.CreateInstance(assembly.GetType("AssemblyViewer.Viewer"), new object[] { window, false });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1124, 700));
            root.Arrange(new Rect(0, 0, 1124, 700));
            root.UpdateLayout();
            var viewport = (Viewport3D)window.FindName("Scene");
            var canvas = (Grid)window.FindName("Canvas");
            var camera = (OrthographicCamera)viewport.Camera;

            Require(window.Title == "STEP 3D Viewer", "Default language is English");
            Require(Label(window, "SourceName") == "stepped-block.step", "The public demo is the default");
            Require(Label(window, "Extent").Contains("80 mm"), "The embedded model is the 80 mm demo block");
            var demoView = Render(root, viewport, args[1], "demo");
            var language = (ComboBox)window.FindName("LanguageChoice");
            language.SelectedIndex = 1;
            Require(window.Title == "STEP 三维查看器", "Language selector applies Chinese");
            Click(window, "FrontButton");
            Require(Label(window, "ViewName") == "正视图" && Math.Abs(camera.LookDirection.Z) < 0.001, "Front view faces horizontally");
            Require(!demoView.SequenceEqual(Render(root, viewport, args[1], "front")), "Front changes the demo projection");
            Click(window, "TopButton");
            Require(Label(window, "ViewName") == "俯视图" && Math.Abs(camera.LookDirection.Y) < 0.001, "Top view faces down");
            Click(window, "EndButton");
            Require(Label(window, "ViewName") == "右视图" && Math.Abs(camera.LookDirection.X) > 10, "Right view faces from the positive X axis");
            Click(window, "FitButton");
            Require(camera.Width > 80 && Label(window, "ZoomText") == "缩放 100%", "Reset fits the demo");
            var fitWidth = camera.Width;
            canvas.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.MouseWheelEvent });
            Require(camera.Width < fitWidth, "Wheel zooms in");
            Click(window, "FitButton");
            Require(camera.Width == fitWidth, "Reset restores the original scale after zooming");
            var look = camera.LookDirection;
            SimulateDrag(canvas, viewer, MouseButton.Left);
            Require(camera.LookDirection != look && Label(window, "ViewName") == "自由视图", "Left drag rotates the model");
            look = camera.LookDirection;
            var position = camera.Position;
            SimulateDrag(canvas, viewer, MouseButton.Right);
            Require(camera.LookDirection == look && camera.Position != position, "Right drag pans without rotating");
            language.SelectedIndex = 0;
            Require(Label(window, "ViewName") == "Free view", "Switching languages preserves the active view");

            var sourceFile = Path.Combine(args[1], "测试 model.stp");
            File.Copy(args[2], sourceFile, true);
            OpenPath(viewer, sourceFile);
            Require(Label(window, "SourceName") == Path.GetFileName(sourceFile), "Open an external STEP with a Unicode name");
            Require(Label(window, "Extent").Contains("1 part"), "A one-part STEP has the correct part count");
            Render(root, viewport, args[1], "external-step");
            var badFile = Path.Combine(args[1], "invalid.step");
            File.WriteAllText(badFile, "invalid STEP input");
            OpenPath(viewer, badFile);
            Require(Label(window, "LoadStatus").Contains("could not be read"), "Invalid STEP is reported in English");
            Require(Label(window, "SourceName") == Path.GetFileName(sourceFile), "Invalid STEP keeps the previous model");
            OpenPath(viewer, Path.ChangeExtension(sourceFile, ".dwg"));
            Require(Label(window, "LoadStatus") == "Choose a .step or .stp file.", "Other formats are rejected clearly");
            var pending = BeginOpen(viewer, sourceFile);
            Click(window, "SampleButton");
            Complete(pending);
            Require(Label(window, "SourceName") == "stepped-block.step", "Switching to the demo cancels the pending import");
            Require(Label(window, "LoadStatus") == "", "Sample remains usable after canceling an import");
            Console.WriteLine("PASS: Public demo, English/Chinese, views and gestures, external STEP/STP, Unicode paths, invalid files and cancellation.");
            app.Shutdown();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void OpenPath(object viewer, string path)
    {
        Complete(BeginOpen(viewer, path));
    }

    private static Task BeginOpen(object viewer, string path)
    {
        return (Task)viewer.GetType().GetMethod("OpenPathAsync").Invoke(viewer, new object[] { path });
    }

    private static void Complete(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(done => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void Click(Window window, string name)
    {
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void SimulateDrag(Grid canvas, object viewer, MouseButton button)
    {
        // Exercise the in-process movement handler without taking OS mouse capture.
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var move = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent };
        var viewerType = viewer.GetType();
        var activeButton = viewerType.GetField("dragButton", fields);
        activeButton.SetValue(viewer, button);
        viewerType.GetField("previousMouse", fields).SetValue(viewer, move.GetPosition(canvas) - new Vector(32, 24));
        canvas.RaiseEvent(move);
        activeButton.SetValue(viewer, null);
    }

    private static string Label(Window window, string name)
    {
        return ((TextBlock)window.FindName(name)).Text;
    }

    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }

    private static byte[] Render(FrameworkElement root, Viewport3D viewport, string folder, string name)
    {
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(folder, name + ".png"))) encoder.Save(output);

        var origin = viewport.TranslatePoint(new Point(0, 0), root);
        var crop = new Int32Rect((int)origin.X + 2, (int)origin.Y + 65, (int)viewport.ActualWidth - 4, (int)viewport.ActualHeight - 67);
        var buffer = new byte[crop.Width * crop.Height * 4];
        bitmap.CopyPixels(crop, buffer, crop.Width * 4, 0);
        int geometryPixels = 0;
        for (int offset = 0; offset < buffer.Length; offset += 4)
            if (buffer[offset + 3] > 200 && (buffer[offset] < 210 || buffer[offset + 1] < 210 || buffer[offset + 2] < 175))
                geometryPixels++;
        Require(geometryPixels > 1200, "The 3D canvas does not show enough visible geometry: " + name + " (" + geometryPixels + " pixels)");
        Console.WriteLine(name + ": " + geometryPixels + " geometry pixels");
        return buffer;
    }
}
