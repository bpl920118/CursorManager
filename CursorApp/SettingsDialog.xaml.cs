using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace CursorManager
{
    public partial class SettingsDialog : Window
    {
        public string SelectedPath { get; private set; } = string.Empty;
        public string SelectedAppTheme { get; private set; } = "System";
        public string SelectedBgMode { get; private set; } = "Theme";
        public string SelectedUiScale { get; private set; } = UiScaleHelper.DefaultPreset;
        public int SelectedCursorSizePx { get; private set; } = MousePointerSizeHelper.DefaultPx;
        public string SelectedCursorScaleMode { get; private set; } = MousePointerSizeHelper.DefaultMode;

        public event Action<string, string, string>? PreviewChanged;

        public SettingsDialog(
            string currentPath,
            string currentAppTheme = "System",
            string currentBgMode = "Theme",
            string currentUiScale = UiScaleHelper.DefaultPreset,
            int cursorSizePx = MousePointerSizeHelper.DefaultPx,
            string cursorScaleMode = MousePointerSizeHelper.DefaultMode)
        {
            InitializeComponent();
            TxtStoragePath.Text = currentPath;
            SelectedPath = currentPath;

            SelectedAppTheme = string.IsNullOrEmpty(currentAppTheme) ? "System" : currentAppTheme;
            if (SelectedAppTheme.Equals("Light", StringComparison.OrdinalIgnoreCase))
                RadioAppThemeLight.IsChecked = true;
            else if (SelectedAppTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                RadioAppThemeDark.IsChecked = true;
            else
                RadioAppThemeSystem.IsChecked = true;

            SelectedBgMode = string.IsNullOrEmpty(currentBgMode) ? "Theme" : currentBgMode;
            if (SelectedBgMode.Equals("Light", StringComparison.OrdinalIgnoreCase))
                RadioBgLight.IsChecked = true;
            else if (SelectedBgMode.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                RadioBgDark.IsChecked = true;
            else if (SelectedBgMode.Equals("Checkerboard", StringComparison.OrdinalIgnoreCase) ||
                     SelectedBgMode.Equals("Checker", StringComparison.OrdinalIgnoreCase) ||
                     SelectedBgMode.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
                RadioBgChecker.IsChecked = true;
            else
                RadioBgTheme.IsChecked = true;

            SelectedUiScale = UiScaleHelper.NormalizePreset(currentUiScale);
            switch (SelectedUiScale)
            {
                case UiScaleHelper.Small:
                    RadioUiScaleSmall.IsChecked = true;
                    break;
                case UiScaleHelper.Large:
                    RadioUiScaleLarge.IsChecked = true;
                    break;
                default:
                    RadioUiScaleNormal.IsChecked = true;
                    break;
            }

            SelectedCursorSizePx = MousePointerSizeHelper.NormalizePx(
                cursorSizePx <= MousePointerSizeHelper.MaxLevel
                    ? MousePointerSizeHelper.GetBaseSize(cursorSizePx)
                    : cursorSizePx);
            SelectedCursorScaleMode = MousePointerSizeHelper.NormalizeMode(cursorScaleMode);
            SliderCursorSize.Value = SelectedCursorSizePx;
            TxtCursorSizeLabel.Text = MousePointerSizeHelper.GetPxLabel(SelectedCursorSizePx);
            if (SelectedCursorScaleMode == MousePointerSizeHelper.ModeForced)
                RadioScaleForced.IsChecked = true;
            else
                RadioScaleSystem.IsChecked = true;

            WirePreviewEvents();
        }

        private void WirePreviewEvents()
        {
            RadioAppThemeSystem.Checked += OnAppearanceOptionChanged;
            RadioAppThemeDark.Checked += OnAppearanceOptionChanged;
            RadioAppThemeLight.Checked += OnAppearanceOptionChanged;
            RadioUiScaleSmall.Checked += OnAppearanceOptionChanged;
            RadioUiScaleNormal.Checked += OnAppearanceOptionChanged;
            RadioUiScaleLarge.Checked += OnAppearanceOptionChanged;
            RadioBgTheme.Checked += OnAppearanceOptionChanged;
            RadioBgDark.Checked += OnAppearanceOptionChanged;
            RadioBgLight.Checked += OnAppearanceOptionChanged;
            RadioBgChecker.Checked += OnAppearanceOptionChanged;
        }

        private void OnAppearanceOptionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton { IsChecked: true })
                return;

            ReadAppearanceSelections();
            PreviewChanged?.Invoke(SelectedAppTheme, SelectedBgMode, SelectedUiScale);
        }

        private void SliderCursorSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtCursorSizeLabel == null) return;
            int px = MousePointerSizeHelper.NormalizePx((int)Math.Round(e.NewValue));
            TxtCursorSizeLabel.Text = MousePointerSizeHelper.GetPxLabel(px);
        }

        private void ReadAppearanceSelections()
        {
            if (RadioAppThemeLight.IsChecked == true)
                SelectedAppTheme = "Light";
            else if (RadioAppThemeDark.IsChecked == true)
                SelectedAppTheme = "Dark";
            else
                SelectedAppTheme = "System";

            if (RadioBgLight.IsChecked == true)
                SelectedBgMode = "Light";
            else if (RadioBgDark.IsChecked == true)
                SelectedBgMode = "Dark";
            else if (RadioBgChecker.IsChecked == true)
                SelectedBgMode = "Transparent";
            else
                SelectedBgMode = "Theme";

            if (RadioUiScaleSmall.IsChecked == true)
                SelectedUiScale = UiScaleHelper.Small;
            else if (RadioUiScaleLarge.IsChecked == true)
                SelectedUiScale = UiScaleHelper.Large;
            else
                SelectedUiScale = UiScaleHelper.Normal;

            SelectedCursorSizePx = MousePointerSizeHelper.NormalizePx((int)Math.Round(SliderCursorSize.Value));
            SelectedCursorScaleMode = RadioScaleForced.IsChecked == true
                ? MousePointerSizeHelper.ModeForced
                : MousePointerSizeHelper.ModeSystem;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "選擇資料夾位置",
                InitialDirectory = Directory.Exists(TxtStoragePath.Text) ? TxtStoragePath.Text : AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
                TxtStoragePath.Text = dialog.FolderName;
        }

        private void BtnOpenCurrent_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtStoragePath.Text.Trim();
            if (Directory.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ConfirmDialog.Alert(this, "錯誤", "無法開啟目錄：" + ex.Message, kind: ConfirmDialogKind.Error);
                }
            }
            else
            {
                ConfirmDialog.Alert(this, "提示", "該目錄目前尚不存在", "儲存後系統將自動建立。");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtStoragePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                ConfirmDialog.Alert(this, "提示", "資料夾路徑不可為空！", kind: ConfirmDialogKind.Warning);
                return;
            }

            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                ConfirmDialog.Alert(this, "錯誤", $"無法建立或存取該目錄：{ex.Message}", kind: ConfirmDialogKind.Error);
                return;
            }

            SelectedPath = path;
            ReadAppearanceSelections();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
