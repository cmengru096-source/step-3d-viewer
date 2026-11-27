using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

internal static class SlotCheck
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var assembly = Assembly.LoadFrom(args[0]);
            Window window;
            using (var layout = assembly.GetManifestResourceStream("Viewer.Layout"))
                window = (Window)XamlReader.Load(layout);
            var viewer = Activator.CreateInstance(assembly.GetType("AssemblyViewer.Viewer"), new object[] { window, false });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var directory = args[1];
            Directory.CreateDirectory(directory);
            var samplePath = args[2];
            var left = window.FindName("LeftAssembly") as RadioButton;
            var right = window.FindName("RightAssembly") as RadioButton;
            Require(left != null && right != null, "The public viewer must expose left and right assembly slots");
            Require(!left.IsEnabled && !right.IsEnabled, "Empty assembly slots must start disabled");
            var demo = Geometry(window);

            var leftFile = Fixture(samplePath, directory, "测试（左） updated.stp");
            var rightFile = Fixture(samplePath, directory, "测试（右） updated.STEP");
            Open(viewer, leftFile);
            Require(left.IsEnabled && left.IsChecked == true, "A left-named file must enable and select the left slot");
            Require(!right.IsEnabled, "Importing left must leave the empty right slot disabled");
            var expectedLeft = Geometry(window);
            Open(viewer, rightFile);
            Require(right.IsEnabled && right.IsChecked == true, "A right-named file must enable and select the right slot");
            var expectedRight = Geometry(window);
            Require(!ReferenceEquals(expectedLeft, expectedRight), "Independent imports must have independent geometry objects");
            CheckSwitching(window, expectedLeft, leftFile, expectedRight, rightFile);

            // A replacement must follow its name even when the opposite side is active.
            leftFile = Fixture(samplePath, directory, "left_replacement.stp");
            Open(viewer, leftFile);
            Require(left.IsChecked == true, "An English left filename must select the left slot");
            expectedLeft = Geometry(window);
            rightFile = Fixture(samplePath, directory, "sample（Правый）.STEP");
            Open(viewer, rightFile);
            Require(right.IsChecked == true, "A Russian right filename must select the right slot");
            expectedRight = Geometry(window);
            leftFile = Fixture(samplePath, directory, "sample（Левый）.STEP");
            Open(viewer, leftFile);
            Require(left.IsChecked == true, "A Russian left filename must select the left slot");
            expectedLeft = Geometry(window);
            rightFile = Fixture(samplePath, directory, "right-replacement.step");
            Open(viewer, rightFile);
            Require(right.IsChecked == true, "An English right filename must select the right slot");
            expectedRight = Geometry(window);
            CheckSwitching(window, expectedLeft, leftFile, expectedRight, rightFile);

            var invalid = Path.Combine(directory, "broken（左）.step");
            File.WriteAllText(invalid, "invalid STEP");
            Open(viewer, invalid);
            Require(ReferenceEquals(Geometry(window), expectedRight), "A failed import must preserve the visible model");
            CheckSwitching(window, expectedLeft, leftFile, expectedRight, rightFile);

            var pending = BeginOpen(viewer, leftFile);
            left.IsChecked = true;
            Complete(pending);
            Require(ReferenceEquals(Geometry(window), expectedLeft), "Switching sides must cancel a pending replacement");
            CheckSwitching(window, expectedLeft, leftFile, expectedRight, rightFile);

            // The demo remains accessible without discarding either imported slot.
            pending = BeginOpen(viewer, leftFile);
            Click(window, "SampleButton");
            Complete(pending);
            Require(ReferenceEquals(Geometry(window), demo), "Opening the demo must cancel the pending import and display the demo");
            CheckSwitching(window, expectedLeft, leftFile, expectedRight, rightFile);

            rightFile = Fixture(samplePath, directory, "replacement.stp");
            Open(viewer, rightFile);
            Require(right.IsChecked == true, "An unlabelled filename must replace the selected right slot");
            expectedRight = Geometry(window);
            CheckSwitching(window, expectedLeft, leftFile, expectedRight, rightFile);

            var supersededLeft = BeginOpen(viewer, Fixture(samplePath, directory, "left-superseded.step"));
            rightFile = Fixture(samplePath, directory, "right-latest.step");
            var latestRight = BeginOpen(viewer, rightFile);
            Complete(Task.WhenAll(supersededLeft, latestRight));
            Require(right.IsChecked == true && Filename(window) == Path.GetFileName(rightFile),
                "The latest import must remain active after the superseded request finishes");
            expectedRight = Geometry(window);
            CheckSwitching(window, expectedLeft, leftFile, expectedRight, rightFile);

            Console.WriteLine("PASS: empty slots, named imports, latest geometry and filenames, failed/canceled imports, demo return, unlabelled replacement, and superseded requests.");
            app.Shutdown();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string Fixture(string sample, string directory, string name)
    {
        var destination = Path.Combine(directory, name);
        File.Copy(sample, destination, true);
        return destination;
    }

    private static void CheckSwitching(Window window, Model3D expectedLeft, string leftFile, Model3D expectedRight, string rightFile)
    {
        for (int index = 0; index < 3; index++)
        {
            ((RadioButton)window.FindName("LeftAssembly")).IsChecked = true;
            Require(ReferenceEquals(Geometry(window), expectedLeft), "The left button must retain its latest imported geometry");
            Require(Filename(window) == Path.GetFileName(leftFile), "The left button must retain its latest imported filename");
            ((RadioButton)window.FindName("RightAssembly")).IsChecked = true;
            Require(ReferenceEquals(Geometry(window), expectedRight), "The right button must retain its latest imported geometry");
            Require(Filename(window) == Path.GetFileName(rightFile), "The right button must retain its latest imported filename");
        }
    }

    private static Model3D Geometry(Window window)
    {
        return ((ModelVisual3D)((Viewport3D)window.FindName("Scene")).Children[1]).Content;
    }

    private static string Filename(Window window)
    {
        return ((TextBlock)window.FindName("SourceName")).Text;
    }

    private static void Click(Window window, string name)
    {
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static Task BeginOpen(object viewer, string path)
    {
        return (Task)viewer.GetType().GetMethod("OpenPathAsync").Invoke(viewer, new object[] { path });
    }

    private static void Open(object viewer, string path)
    {
        Complete(BeginOpen(viewer, path));
    }

    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(done => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
