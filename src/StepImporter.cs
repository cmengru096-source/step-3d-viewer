using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;

namespace AssemblyViewer
{
    internal static class StepImporter
    {
        public static bool Supports(string filename)
        {
            try
            {
                var extension = Path.GetExtension(filename);
                return string.Equals(extension, ".step", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".stp", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }

        public static LoadedModel Read(string filename, CancellationToken cancellation)
        {
            filename = Path.GetFullPath(filename);
            if (!File.Exists(filename)) throw new FileNotFoundException();

            var folder = Path.Combine(Path.GetTempPath(), "StepViewer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var importerFolder = Directory.CreateDirectory(Path.Combine(folder, "occt")).FullName;
                var node = Path.Combine(folder, "node.exe");
                var script = Path.Combine(folder, "convert.cjs");
                var mesh = Path.Combine(folder, "import.mesh.gz");
                Extract("Viewer.Node", node, true);
                cancellation.ThrowIfCancellationRequested();
                Extract("Viewer.Converter", script, false);
                Extract("Viewer.OcctJs", Path.Combine(importerFolder, "occt-import-js.js"), false);
                Extract("Viewer.OcctWasm", Path.Combine(importerFolder, "occt-import-js.wasm"), false);
                var start = new ProcessStartInfo
                {
                    FileName = node,
                    Arguments = "\"" + script + "\" --import \"" + filename + "\" \"" + mesh + "\"",
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.EnvironmentVariables.Remove("NODE_OPTIONS");
                start.EnvironmentVariables.Remove("NODE_PATH");
                start.EnvironmentVariables.Remove("NODE_V8_COVERAGE");

                cancellation.ThrowIfCancellationRequested();
                using (var process = Process.Start(start))
                using (cancellation.Register(() => Stop(process)))
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0 || !File.Exists(mesh)) throw new InvalidDataException("STEP import failed.");
                }

                using (var input = File.OpenRead(mesh)) return MeshReader.Read(input);
            }
            finally
            {
                try { Directory.Delete(folder, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void Stop(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static void Extract(string resource, string filename, bool compressed)
        {
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (var output = File.Create(filename))
            {
                if (!compressed)
                {
                    source.CopyTo(output);
                    return;
                }
                using (var decompressed = new GZipStream(source, CompressionMode.Decompress)) decompressed.CopyTo(output);
            }
        }
    }
}
