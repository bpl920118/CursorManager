using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace HololiveCursorApp
{
    public partial class SettingsDialog : Window
    {
        public string SelectedPath { get; private set; } = string.Empty;
        public string SelectedBgMode { get; private set; } = "Dark";

        public SettingsDialog(string currentPath, string currentBgMode = "Dark")
        {
            InitializeComponent();
            TxtStoragePath.Text = currentPath;
            SelectedPath = currentPath;

            SelectedBgMode = string.IsNullOrEmpty(currentBgMode) ? "Dark" : currentBgMode;
            if (SelectedBgMode.Equals("Light", StringComparison.OrdinalIgnoreCase))
            {
                RadioBgLight.IsChecked = true;
            }
            else if (SelectedBgMode.Equals("Checkerboard", StringComparison.OrdinalIgnoreCase) || SelectedBgMode.Equals("Checker", StringComparison.OrdinalIgnoreCase))
            {
                RadioBgChecker.IsChecked = true;
            }
            else
            {
                RadioBgDark.IsChecked = true;
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "選擇資料夾位置",
                InitialDirectory = Directory.Exists(TxtStoragePath.Text) ? TxtStoragePath.Text : AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                TxtStoragePath.Text = dialog.FolderName;
            }
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
                    MessageBox.Show("無法開啟目錄：" + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("該目錄目前尚不存在，儲存後系統將自動建立。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtStoragePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("資料夾路徑不可為空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"無法建立或存取該目錄：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SelectedPath = path;

            if (RadioBgLight.IsChecked == true)
            {
                SelectedBgMode = "Light";
            }
            else if (RadioBgChecker.IsChecked == true)
            {
                SelectedBgMode = "Checkerboard";
            }
            else
            {
                SelectedBgMode = "Dark";
            }

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
