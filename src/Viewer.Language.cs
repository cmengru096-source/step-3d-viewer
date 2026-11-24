using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AssemblyViewer
{
    internal sealed partial class Viewer
    {
        private const string settingsKey = @"Software\PortableStepViewer";
        private bool chinese;

        private string Translate(string english, string simplifiedChinese)
        {
            return chinese ? simplifiedChinese : english;
        }

        private void ConnectLanguage(bool remember)
        {
            var language = Element<ComboBox>("LanguageChoice");
            chinese = remember && StoredLanguageIsChinese();
            language.SelectedIndex = chinese ? 1 : 0;
            language.SelectionChanged += (sender, e) =>
            {
                chinese = language.SelectedIndex == 1;
                UpdateLanguage();
                if (remember) StoreLanguage();
            };
            Element<Button>("LicensesButton").Click += (sender, e) => ShowLicenses();
            UpdateLanguage();
        }

        private static bool StoredLanguageIsChinese()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(settingsKey))
                    return key != null && string.Equals(key.GetValue("Language") as string, "zh-CN", StringComparison.Ordinal);
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        private void StoreLanguage()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(settingsKey))
                    if (key != null) key.SetValue("Language", chinese ? "zh-CN" : "en");
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        private void UpdateLanguage()
        {
            window.Title = Translate("STEP 3D Viewer", "STEP 三维查看器");
            Element<TextBlock>("ViewerName").Text = Translate("3D Viewer", "三维查看器");
            Element<TextBlock>("DropHint").Text = Translate("or drop a .step or .stp file", "也可拖入 .step 或 .stp 文件");
            Element<TextBlock>("RotateHint").Text = Translate("Left drag · Rotate", "左键拖动 · 旋转");
            Element<TextBlock>("PanHint").Text = Translate("Right drag · Pan", "右键拖动 · 平移");
            Element<TextBlock>("ZoomHint").Text = Translate("Scroll · Zoom", "滚动滚轮 · 缩放");
            Element<TextBlock>("ResetHint").Text = Translate("Double-click · Reset", "双击视图区 · 复位");
            Element<Button>("OpenButton").Content = Translate("Open STEP…", "打开 STEP…");
            Element<Button>("SampleButton").Content = Translate("Open demo model", "打开示例模型");
            Element<Button>("FitButton").Content = Translate("Reset view", "复位");
            Element<Button>("FrontButton").Content = Translate("Front", "正视");
            Element<Button>("TopButton").Content = Translate("Top", "俯视");
            Element<Button>("LeftEndButton").Content = Translate("Left end", "左端细节");
            Element<Button>("RightEndButton").Content = Translate("Right end", "右端细节");
            Element<Button>("LicensesButton").Content = Translate("Open-source licenses", "开源许可");
            AutomationProperties.SetName(canvas, Translate("3D viewport", "三维视图区"));
            UpdateModelLabels();
            UpdateStatus();
            UpdateViewLabel();
            UpdateZoomLabel();
        }

        private void UpdateModelLabels()
        {
            Element<Button>("EndButton").Content = IsElongated ? Translate("End view", "端视") : Translate("Right view", "右视");
            if (sourceName == null) return;
            var title = showingSample ? Translate("Stepped block", "阶梯块") : Path.GetFileNameWithoutExtension(sourceName);
            Element<TextBlock>("AssemblyTitle").Text = title;
            Element<TextBlock>("SourceName").Text = sourceName;
            var size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            var english = partCount == 1 ? "Largest dimension: {0:0.##} mm  ·  {1} part" : "Largest dimension: {0:0.##} mm  ·  {1} parts";
            Element<TextBlock>("Extent").Text = string.Format(CultureInfo.InvariantCulture,
                Translate(english, "最大外形尺寸约 {0:0.##} mm  ·  {1} 个实体"), size, partCount);
        }

        private void UpdateViewLabel()
        {
            string label;
            switch (view)
            {
                case Angle.Front: label = Translate("Front view", "正视图"); break;
                case Angle.Top: label = Translate("Top view", "俯视图"); break;
                case Angle.End: label = IsElongated ? Translate("End view", "端视图") : Translate("Right view", "右视图"); break;
                case Angle.LeftEnd: label = Translate("Left end detail", "左端细节"); break;
                case Angle.RightEnd: label = Translate("Right end detail", "右端细节"); break;
                case Angle.Free: label = Translate("Free view", "自由视图"); break;
                default: label = IsElongated ? Translate("Full length", "全长视图") : Translate("Overview", "全景视图"); break;
            }
            Element<TextBlock>("ViewName").Text = label;
        }

        private void UpdateZoomLabel()
        {
            Element<TextBlock>("ZoomText").Text = string.Format(CultureInfo.InvariantCulture, Translate("Zoom {0:0}%", "缩放 {0:0}%"), zoom * 100);
        }

        private void ShowLicenses()
        {
            string notices;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Viewer.Licenses"))
            using (var reader = new StreamReader(stream)) notices = reader.ReadToEnd();
            var dialog = new Window
            {
                Title = Translate("Open-source licenses", "开源许可"),
                Owner = window, Width = 760, Height = 550, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBox
                {
                    Text = notices, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14)
                }
            };
            dialog.ShowDialog();
        }
    }
}
