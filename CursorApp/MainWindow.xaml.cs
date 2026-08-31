using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HololiveCursorApp
{
    public partial class MainWindow : Window
    {
        private List<CharacterThemeItem> _allThemes = new();
        private ObservableCollection<CursorSlot> _currentSlots = new();
        private string _currentLoadedFolder = string.Empty;
        private string _currentThemeName = "自訂游標";
        private DispatcherTimer? _aniTimer;

        public MainWindow()
        {
            InitializeComponent();
            ItemsCursorSlots.ItemsSource = _currentSlots;

            // Initialize Animation playback timer (every 16ms ~ 60 FPS)
            _aniTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _aniTimer.Tick += AniTimer_Tick;
            _aniTimer.Start();

            // Display dynamic assembly version (e.g. v1.3)
            try
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (ver != null)
                {
                    TxtVersion.Text = $"v{ver.Major}.{ver.Minor}";
                }
            }
            catch { }

            try
            {
                // Set window icon safely from application icon
                var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (iconStream != null)
                {
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconStream.Stream);
                }
            }
            catch
            {
                // Fallback without crashing
            }

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await ReloadThemesAsync();

            // Check if launched with folder argument (e.g. dragged onto exe)
            if (!string.IsNullOrEmpty(App.StartupFolder) && Directory.Exists(App.StartupFolder))
            {
                ImportAndLoadFolder(App.StartupFolder);
                // Automatically apply if launched with folder
                BtnApplyTheme_Click(this, new RoutedEventArgs());
            }
            else if (_allThemes.Count > 0)
            {
                // Auto select first theme
                LstThemes.SelectedIndex = 0;
            }
            else
            {
                // Initialize blank slots
                var slots = CursorMatcher.CreateDefaultSlots();
                _currentSlots.Clear();
                foreach (var s in slots) _currentSlots.Add(s);
            }
        }

        private const string ConfigFileName = "config.ini";

        private static string GetConfigFilePath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDir, ConfigFileName);
        }

        private static string GetCursorsDataFolder()
        {
            string configPath = GetConfigFilePath();
            if (File.Exists(configPath))
            {
                try
                {
                    string savedPath = File.ReadAllText(configPath).Trim();
                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        if (!Directory.Exists(savedPath)) Directory.CreateDirectory(savedPath);
                        return savedPath;
                    }
                }
                catch { }
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string parentDir = Directory.GetParent(appDir)?.FullName ?? appDir;
            string grandParentDir = Directory.GetParent(parentDir)?.FullName ?? parentDir;

            var candidates = new[]
            {
                Path.Combine(appDir, "CursorsData"),
                Path.Combine(parentDir, "CursorsData"),
                Path.Combine(grandParentDir, "CursorsData")
            };

            foreach (var c in candidates)
            {
                if (Directory.Exists(c)) return c;
            }

            // Default fallback to CursorsData in app directory
            string def = Path.Combine(appDir, "CursorsData");
            if (!Directory.Exists(def)) Directory.CreateDirectory(def);
            return def;
        }

        private static void SetCustomCursorsDataFolder(string newPath)
        {
            try
            {
                if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
                string configPath = GetConfigFilePath();
                File.WriteAllText(configPath, newPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("儲存設定檔失敗：" + ex.Message);
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(destinationDir);

            foreach (var file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (var subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        private async Task ReloadThemesAsync(string? selectFolderPath = null)
        {
            string cursorsData = GetCursorsDataFolder();
            _allThemes = await Task.Run(() => FolderScanner.ScanDirectory(cursorsData));
            TxtThemeCount.Text = $"{_allThemes.Count} 個主題";
            FilterThemes();

            if (!string.IsNullOrEmpty(selectFolderPath))
            {
                var match = _allThemes.FirstOrDefault(t => t.FolderPath.Equals(selectFolderPath, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    LstThemes.SelectedItem = match;
                    LstThemes.ScrollIntoView(match);
                }
            }
        }

        private void ReloadThemes(string? selectFolderPath = null)
        {
            _ = ReloadThemesAsync(selectFolderPath);
        }

        private void FilterThemes()
        {
            string query = TxtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                LstThemes.ItemsSource = _allThemes;
            }
            else
            {
                LstThemes.ItemsSource = _allThemes
                    .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                t.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterThemes();
        }

        private void LstThemes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstThemes.SelectedItem is CharacterThemeItem item)
            {
                LoadFolder(item.FolderPath, item.Name);
            }
        }

        private void ImportAndLoadFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            string cursorsData = GetCursorsDataFolder();
            string folderName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
            string targetDir = folderPath;

            // If the dragged folder is not already inside the current storage folder
            if (!folderPath.StartsWith(cursorsData, StringComparison.OrdinalIgnoreCase))
            {
                var askResult = MessageBox.Show(
                    $"檢測到新拖入的游標資料夾：「{folderName}」\n\n是否要將此游標主題複製存入您的游標庫中？\n\n" +
                    $"【目前儲存庫目錄】：\n{cursorsData}\n\n" +
                    $"• 點選「是 (Yes)」：複製存入游標庫（推薦，方便統一管理）\n" +
                    $"• 點選「否 (No)」：僅本次直接讀取，不複製檔案\n" +
                    $"（若想自訂儲存目錄名稱或路徑，可隨時點擊右上角「⚙️ 儲存庫位置」更改）",
                    "匯入游標主題確認",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (askResult == MessageBoxResult.Cancel)
                {
                    return;
                }

                if (askResult == MessageBoxResult.Yes)
                {
                    targetDir = Path.Combine(cursorsData, folderName);
                    try
                    {
                        CopyDirectory(folderPath, targetDir);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"複製至儲存庫時發生錯誤：{ex.Message}\n將直接讀取原目錄。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        targetDir = folderPath;
                    }
                }
                else
                {
                    targetDir = folderPath;
                }
            }

            // Reload sidebar list and select the theme
            ReloadThemes(targetDir);
            LoadFolder(targetDir);
        }

        public void LoadFolder(string folderPath, string? themeName = null)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            _currentLoadedFolder = folderPath;
            _currentThemeName = themeName ?? Path.GetFileName(folderPath).Replace("Mouse cursor", "").Trim();
            if (string.IsNullOrWhiteSpace(_currentThemeName)) _currentThemeName = Path.GetFileName(folderPath);

            TxtCurrentThemeTitle.Text = _currentThemeName;
            TxtCurrentFolderPath.Text = folderPath;

            var slots = CursorMatcher.MatchFolder(folderPath);
            _currentSlots.Clear();
            foreach (var s in slots)
            {
                _currentSlots.Add(s);
            }

            int matchedCount = _currentSlots.Count(s => s.HasFile);
            SetStatus("💡", $"已配對 {matchedCount} / {_currentSlots.Count} 項游標。點擊「一鍵套用」即可立即生效！", Color.FromRgb(0x89, 0xB4, 0xFA));
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x25));

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string path = files[0];
                    if (Directory.Exists(path))
                    {
                        ImportAndLoadFolder(path);
                    }
                    else if (File.Exists(path))
                    {
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        if (ext == ".exe" || ext == ".zip" || ext == ".rar" || ext == ".7z")
                        {
                            HandleExecutableOrArchiveDrop(path);
                        }
                        else
                        {
                            string? dir = Path.GetDirectoryName(path);
                            if (dir != null) ImportAndLoadFolder(dir);
                        }
                    }
                }
            }
        }

        private void HandleExecutableOrArchiveDrop(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string cursorsData = GetCursorsDataFolder();

            // 1. First run the installer/exe
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"執行安裝檔失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2. Ask user to import the applied cursor theme
            var ask = MessageBox.Show(
                $"【{fileName}】已啟動！\n\n" +
                $"若您已在該安裝工具中完成套用，是否要將當前已套用的游標主題「提取並儲存」到您的游標庫中永久管理？\n\n" +
                $"• 點選「是 (Yes)」：自動從系統註冊表讀取游標並存入游標庫\n" +
                $"• 點選「否 (No)」：僅執行安裝程式",
                "提取游標主題",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (ask == MessageBoxResult.Yes)
            {
                ImportCurrentSystemCursors(baseName);
            }
        }

        private void ImportCurrentSystemCursors(string defaultThemeName)
        {
            try
            {
                string cursorsData = GetCursorsDataFolder();
                string themeName = defaultThemeName;

                // Read current registry cursor paths
                var currentPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
                {
                    if (key != null)
                    {
                        var regTheme = key.GetValue("")?.ToString();
                        if (!string.IsNullOrWhiteSpace(regTheme) && regTheme != "Windows Default")
                        {
                            themeName = regTheme;
                        }

                        string[] standardKeys = new[]
                        {
                            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam",
                            "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW",
                            "SizeAll", "UpArrow", "Hand"
                        };

                        foreach (var k in standardKeys)
                        {
                            var p = key.GetValue(k)?.ToString();
                            if (!string.IsNullOrEmpty(p) && File.Exists(p))
                            {
                                currentPaths[k] = p;
                            }
                        }
                    }
                }

                if (currentPaths.Count == 0)
                {
                    MessageBox.Show("未能從系統中檢測到已套用的游標檔案，請確認安裝檔是否已成功套用游標。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string targetFolder = Path.Combine(cursorsData, themeName);
                Directory.CreateDirectory(targetFolder);

                // Collect the unique directories of the source files
                var sourceDirs = currentPaths.Values
                    .Select(Path.GetDirectoryName)
                    .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var schemeMapLines = new List<string>();

                // If all files come from a single directory that is not cursorsData, copy whole directory
                if (sourceDirs.Count == 1 && !sourceDirs[0]!.Equals(cursorsData, StringComparison.OrdinalIgnoreCase) && !sourceDirs[0]!.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    CopyDirectory(sourceDirs[0]!, targetFolder);
                    foreach (var kvp in currentPaths)
                    {
                        string fileName = Path.GetFileName(kvp.Value);
                        schemeMapLines.Add($"{kvp.Key}={fileName}");
                    }
                }
                else
                {
                    // Copy all matched files individually
                    foreach (var kvp in currentPaths)
                    {
                        string fileName = Path.GetFileName(kvp.Value);
                        string destFile = Path.Combine(targetFolder, fileName);
                        try
                        {
                            File.Copy(kvp.Value, destFile, true);
                            schemeMapLines.Add($"{kvp.Key}={fileName}");
                        }
                        catch { }
                    }
                }

                try
                {
                    File.WriteAllLines(Path.Combine(targetFolder, "scheme_map.ini"), schemeMapLines);
                }
                catch { }

                ReloadThemes(targetFolder);
                LoadFolder(targetFolder, themeName);
                SetStatus("✨", $"已成功將「{themeName}」游標主題存入游標庫！", Color.FromRgb(0xA6, 0xE3, 0xA1));
                MessageBox.Show($"已成功將「{themeName}」游標提取並儲存至游標庫！\n\n您往後隨時可以在左側清單切換回此游標。", "匯入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("提取游標失敗：" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DropZone_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "選擇包含游標 (.ani / .cur) 的資料夾"
            };

            if (dialog.ShowDialog() == true)
            {
                ImportAndLoadFolder(dialog.FolderName);
            }
        }

        private void BtnRematch_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentLoadedFolder))
            {
                LoadFolder(_currentLoadedFolder, _currentThemeName);
            }
        }

        private void BtnApplyTheme_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSlots.Count == 0 || !_currentSlots.Any(s => s.HasFile))
            {
                SetStatus("⚠️", "請先選擇包含游標檔案的資料夾！", Color.FromRgb(0xF9, 0xE2, 0xAF));
                MessageBox.Show("請先選擇或拖曳包含游標檔案的資料夾！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                CursorInstaller.ApplyCursors(_currentSlots, _currentThemeName);
                SetStatus("✅", $"套用成功！已即時切換為「{_currentThemeName}」游標主題！", Color.FromRgb(0xA6, 0xE3, 0xA1));
                MessageBox.Show($"已成功套用「{_currentThemeName}」游標！\n\n系統已即時更新鼠標圖標，不需重開機。", "套用成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus("❌", "套用失敗：" + ex.Message, Color.FromRgb(0xF3, 0x8B, 0xA8));
                MessageBox.Show("套用失敗：" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("確定要將滑鼠游標還原為 Windows 預設樣式嗎？", "還原確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                bool ok = CursorInstaller.RestoreDefaultCursors();
                if (ok)
                {
                    SetStatus("🔄", "已成功恢復為 Windows 預設游標！", Color.FromRgb(0x89, 0xB4, 0xFA));
                    MessageBox.Show("已成功恢復為 Windows 預設游標！", "還原成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    SetStatus("❌", "還原失敗。", Color.FromRgb(0xF3, 0x8B, 0xA8));
                }
            }
        }

        private void OpenWindowsMouseSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open classic Mouse Properties or Windows 10/11 Settings
                Process.Start(new ProcessStartInfo
                {
                    FileName = "main.cpl",
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ms-settings:mousetouchpad",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("無法開啟 Windows 設定：" + ex.Message);
                }
            }
        }

        private void MenuRenameTheme_Click(object sender, RoutedEventArgs e)
        {
            if (LstThemes.SelectedItem is CharacterThemeItem item)
            {
                var dialog = new RenameDialog(item.Name)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    string newName = dialog.NewName;
                    if (string.Equals(item.Name, newName, StringComparison.Ordinal))
                        return;

                    string oldPath = item.FolderPath;
                    string? parentDir = Path.GetDirectoryName(oldPath);
                    if (string.IsNullOrEmpty(parentDir)) return;

                    string newPath = Path.Combine(parentDir, newName);

                    try
                    {
                        if (Directory.Exists(newPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show("已存在相同名稱的資料夾，請使用其他名稱！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        Directory.Move(oldPath, newPath);

                        // Reload list and reselect the renamed folder
                        ReloadThemes(newPath);
                        LoadFolder(newPath, newName);

                        SetStatus("✏️", $"已成功重新命名為「{newName}」！", Color.FromRgb(0xA6, 0xE3, 0xA1));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"重新命名失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (LstThemes.SelectedItem is CharacterThemeItem item && Directory.Exists(item.FolderPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.FolderPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("無法開啟資料夾：" + ex.Message);
                }
            }
        }

        private void MenuDeleteTheme_Click(object sender, RoutedEventArgs e)
        {
            if (LstThemes.SelectedItem is CharacterThemeItem item)
            {
                var result = MessageBox.Show(
                    $"確定要永久刪除主題「{item.Name}」嗎？\n\n這將會移除該游標資料夾：\n{item.FolderPath}",
                    "刪除主題確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (Directory.Exists(item.FolderPath))
                        {
                            Directory.Delete(item.FolderPath, true);
                        }

                        // Reload list
                        ReloadThemes();

                        // If the currently displayed theme was deleted, switch to first available or reset
                        if (string.Equals(_currentLoadedFolder, item.FolderPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (_allThemes.Count > 0)
                            {
                                LstThemes.SelectedIndex = 0;
                            }
                            else
                            {
                                _currentLoadedFolder = string.Empty;
                                _currentThemeName = "未選擇任何主題";
                                TxtCurrentThemeTitle.Text = _currentThemeName;
                                TxtCurrentFolderPath.Text = "請從左側選擇或將資料夾拖曳進來";
                                var slots = CursorMatcher.CreateDefaultSlots();
                                _currentSlots.Clear();
                                foreach (var s in slots) _currentSlots.Add(s);
                            }
                        }

                        SetStatus("🗑️", $"已成功刪除「{item.Name}」！", Color.FromRgb(0xF3, 0x8B, 0xA8));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"刪除失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void OpenStorageSettings_Click(object sender, RoutedEventArgs e)
        {
            string currentPath = GetCursorsDataFolder();
            var dialog = new SettingsDialog(currentPath)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                string newPath = dialog.SelectedPath;
                if (!string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    SetCustomCursorsDataFolder(newPath);
                    ReloadThemes();
                    SetStatus("⚙️", $"已將游標庫儲存目錄更新為：{newPath}", Color.FromRgb(0xA6, 0xE3, 0xA1));
                    MessageBox.Show($"已成功切換游標儲存庫目錄！\n\n新目錄：\n{newPath}", "設定已更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnCaptureCurrentSystem_Click(object sender, RoutedEventArgs e)
        {
            ImportCurrentSystemCursors("擷取的自訂游標");
        }

        private void SetStatus(string icon, string msg, Color color)
        {
            TxtStatusIcon.Text = icon;
            TxtStatusMessage.Text = msg;
            TxtStatusMessage.Foreground = new SolidColorBrush(color);
        }

        private void AniTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentSlots.Count == 0) return;

            foreach (var slot in _currentSlots)
            {
                if (slot.AniSequence != null && slot.AniSequence.Frames.Count > 1)
                {
                    slot.NextFrameCountdown--;
                    if (slot.NextFrameCountdown <= 0)
                    {
                        slot.CurrentFrameIndex = (slot.CurrentFrameIndex + 1) % slot.AniSequence.Frames.Count;
                        slot.PreviewImage = slot.AniSequence.Frames[slot.CurrentFrameIndex];

                        int jiffies = slot.CurrentFrameIndex < slot.AniSequence.FrameRatesInJiffies.Count
                            ? slot.AniSequence.FrameRatesInJiffies[slot.CurrentFrameIndex]
                            : 10;

                        // 1 jiffy is 1/60th second (~1 tick of our 16ms timer)
                        slot.NextFrameCountdown = Math.Max(1, jiffies);
                    }
                }
            }
        }
    }
}
