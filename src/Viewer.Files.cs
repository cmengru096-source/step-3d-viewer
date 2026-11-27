using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace AssemblyViewer
{
    internal sealed partial class Viewer
    {
        private enum ImportState { Idle, Opening, Unsupported, Failed }
        private ImportState importState;
        private CancellationTokenSource pendingImport;
        private string pendingName;
        private bool showingSample;
        private bool currentIsLeft = true;
        private bool updatingSelection;
        private string leftSourceName;
        private string rightSourceName;
        private string sourceName;
        private int partCount;

        private void ConnectFileImport()
        {
            Element<Button>("OpenButton").Click += (sender, e) => OpenFile();
            window.InputBindings.Add(new KeyBinding(ApplicationCommands.Open, new KeyGesture(Key.O, ModifierKeys.Control)));
            window.CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (sender, e) => OpenFile()));
            window.PreviewDragOver += (sender, e) =>
            {
                e.Effects = DroppedFile(e.Data) == null ? DragDropEffects.None : DragDropEffects.Copy;
                e.Handled = true;
            };
            window.Drop += async (sender, e) =>
            {
                var file = DroppedFile(e.Data);
                e.Handled = true;
                if (file != null) await OpenPathAsync(file);
            };
            window.Closed += (sender, e) => CancelImport();
        }

        private void ShowSample()
        {
            CancelImport();
            SetImportState(ImportState.Idle);
            SelectAssemblyButton(null);
            ShowModel(sample, "stepped-block.step", true);
        }

        private void ShowAssembly(bool isLeft)
        {
            var model = isLeft ? left : right;
            if (model == null) return;
            CancelImport();
            SetImportState(ImportState.Idle);
            currentIsLeft = isLeft;
            ShowModel(model, isLeft ? leftSourceName : rightSourceName, false);
        }

        private void SelectAssemblyButton(bool? isLeft)
        {
            updatingSelection = true;
            try
            {
                Element<RadioButton>("LeftAssembly").IsChecked = isLeft == true;
                Element<RadioButton>("RightAssembly").IsChecked = isLeft == false;
            }
            finally { updatingSelection = false; }
        }

        private bool ImportSide(string filename)
        {
            var name = Path.GetFileNameWithoutExtension(filename);
            var isLeft = name.Contains("左") || Regex.IsMatch(name, @"(?:^|[^a-z])left(?:[^a-z]|$)|(?:^|[^\p{L}])лев\p{L}*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var isRight = name.Contains("右") || Regex.IsMatch(name, @"(?:^|[^a-z])right(?:[^a-z]|$)|(?:^|[^\p{L}])прав\p{L}*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return isLeft == isRight ? currentIsLeft : isLeft;
        }

        private void ShowModel(LoadedModel model, string name, bool isSample)
        {
            sourceName = name;
            showingSample = isSample;
            partCount = model.PartCount;
            modelVisual.Content = model.Geometry;
            bounds = model.Geometry.Bounds;
            Element<Button>("LeftEndButton").Visibility = IsElongated ? Visibility.Visible : Visibility.Collapsed;
            Element<Button>("RightEndButton").Visibility = IsElongated ? Visibility.Visible : Visibility.Collapsed;
            UpdateModelLabels();
            ResetView();
        }

        private bool IsElongated
        {
            get { return !bounds.IsEmpty && bounds.SizeX > 5 * Math.Max(bounds.SizeY, bounds.SizeZ); }
        }

        private async void OpenFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = Translate("Open STEP file", "打开 STEP 文件"),
                Filter = Translate("STEP files", "STEP 文件") + " (*.step;*.stp)|*.step;*.stp",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(window) == true) await OpenPathAsync(dialog.FileName);
        }

        private static string DroppedFile(IDataObject data)
        {
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return null;
            foreach (var file in files) if (StepImporter.Supports(file)) return file;
            return null;
        }

        public async Task OpenPathAsync(string filename)
        {
            CancelImport();
            if (!StepImporter.Supports(filename))
            {
                SetImportState(ImportState.Unsupported);
                return;
            }

            pendingName = Path.GetFileName(filename);
            var isLeft = ImportSide(filename);
            var request = new CancellationTokenSource();
            pendingImport = request;
            SetImportState(ImportState.Opening);
            try
            {
                var result = await Task.Run(() => StepImporter.Read(filename, request.Token), request.Token);
                if (pendingImport != request) return;
                if (isLeft)
                {
                    left = result;
                    leftSourceName = pendingName;
                }
                else
                {
                    right = result;
                    rightSourceName = pendingName;
                }
                currentIsLeft = isLeft;
                SelectAssemblyButton(isLeft);
                ShowModel(result, pendingName, false);
                SetImportState(ImportState.Idle);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (pendingImport == request) SetImportState(ImportState.Failed);
            }
            finally
            {
                if (pendingImport == request) pendingImport = null;
                request.Dispose();
            }
        }

        private void CancelImport()
        {
            var request = pendingImport;
            pendingImport = null;
            if (request != null) request.Cancel();
        }

        private void SetImportState(ImportState state)
        {
            importState = state;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var status = Element<TextBlock>("LoadStatus");
            status.Visibility = importState == ImportState.Idle ? Visibility.Collapsed : Visibility.Visible;
            switch (importState)
            {
                case ImportState.Opening:
                    status.Text = string.Format(Translate("Opening {0}…", "正在打开 {0}…"), pendingName);
                    break;
                case ImportState.Unsupported:
                    status.Text = Translate("Choose a .step or .stp file.", "请选择 .step 或 .stp 文件。");
                    break;
                case ImportState.Failed:
                    status.Text = string.Format(Translate("{0} could not be read as a STEP model.", "无法将 {0} 读取为 STEP 模型。"), pendingName);
                    break;
                default:
                    status.Text = string.Empty;
                    break;
            }
        }
    }
}
