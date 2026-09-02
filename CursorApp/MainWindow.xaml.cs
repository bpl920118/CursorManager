using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CursorManager
{
    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty IsBatchDeleteModeProperty =
            DependencyProperty.Register(
                nameof(IsBatchDeleteMode),
                typeof(bool),
                typeof(MainWindow),
                new PropertyMetadata(false));

        public bool IsBatchDeleteMode
        {
            get => (bool)GetValue(IsBatchDeleteModeProperty);
            set => SetValue(IsBatchDeleteModeProperty, value);
        }

        private List<CharacterThemeItem> _allThemes = new();
        private readonly List<CharacterThemeItem> _temporaryThemes = new();
        private readonly ObservableCollection<ThemeGroupNode> _themeGroups = new();
        private readonly ObservableCollection<CharacterThemeItem> _flatThemeList = new();
        private bool IsFlatThemeList => ThemeMetadataStore.FilterMode == ThemeFilterMode.Recent;
        private Dictionary<string, bool> _groupExpandedState = new(StringComparer.OrdinalIgnoreCase);
        private ObservableCollection<CursorSlot> _currentSlots = new();
        private string _currentLoadedFolder = string.Empty;
        private string _appliedFolderPath = string.Empty;
        private string _appliedThemeName = string.Empty;
        private string _currentThemeName = "自訂鼠標";
        private DispatcherTimer? _aniTimer;
        private string _currentAppTheme = "System";
        private string _currentBgMode = "Theme";
        private string _currentUiScale = UiScaleHelper.DefaultPreset;
        private int _cursorSizePx = MousePointerSizeHelper.DefaultPx;
        private string _cursorScaleMode = MousePointerSizeHelper.DefaultMode;
        private bool _suppressPointerSizeEvent;
        private bool _suppressThemeFilterEvent;
        private bool _schemePromptOpen;
        private bool _schemePromptDismissed;
        private bool _skipSchemeMismatchCheck;
        private DispatcherTimer? _pointerSizeApplyTimer;
        private UpdateInfo? _pendingUpdate;
        private string _skippedUpdateVersion = string.Empty;
        private bool _userCollapsedAllGroups;
        private bool _themeTreeShowsFlatList;
        private int _loadFolderToken;

        public MainWindow()
        {
            InitializeComponent();
            ItemsCursorSlots.ItemsSource = _currentSlots;

            // Animation preview timer (~30 FPS; enough for slot previews without overloading UI)
            _aniTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33)
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
            Activated += MainWindow_Activated;

            var settings = LoadAppSettings();
            _currentAppTheme = settings.AppTheme;
            _currentBgMode = settings.BgMode;
            _currentUiScale = settings.UiScale;
            _cursorSizePx = settings.CursorSizePx;
            _cursorScaleMode = settings.CursorScaleMode;
            _appliedFolderPath = settings.AppliedFolderPath;
            _appliedThemeName = settings.AppliedThemeName;
            _skippedUpdateVersion = settings.SkippedUpdateVersion;
            ApplyAppTheme(_currentAppTheme);
            ApplyPreviewBackground(_currentBgMode, _currentAppTheme);
            ApplyUiScale(_currentUiScale);
            SyncPointerSizeUi();
            ThemeMetadataStore.EnsureLoaded();
        }

        private void SyncThemeFilterUi()
        {
            _suppressThemeFilterEvent = true;
            try
            {
                BtnFilterAll.IsChecked = ThemeMetadataStore.FilterMode == ThemeFilterMode.All;
                BtnFilterFavorites.IsChecked = ThemeMetadataStore.FilterMode == ThemeFilterMode.Favorites;
                BtnFilterRecent.IsChecked = ThemeMetadataStore.FilterMode == ThemeFilterMode.Recent;

                foreach (ComboBoxItem comboItem in CmbThemeSort.Items)
                {
                    if (comboItem.Tag is string tag &&
                        tag.Equals(ThemeMetadataStore.SortMode.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        CmbThemeSort.SelectedItem = comboItem;
                        break;
                    }
                }
            }
            finally
            {
                _suppressThemeFilterEvent = false;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SyncThemeFilterUi();
            await ReloadThemesAsync();

            // Check if launched with folder argument (e.g. dragged onto exe)
            if (!string.IsNullOrEmpty(App.StartupFolder) && Directory.Exists(App.StartupFolder))
            {
                ImportAndLoadFolder(App.StartupFolder);
            }
            else if (_allThemes.Count > 0)
            {
                ExpandAllThemeGroups();
                SelectFirstThemeIfAny();
            }
            else
            {
                // Initialize blank slots
                var slots = CursorMatcher.CreateDefaultSlots();
                _currentSlots.Clear();
                foreach (var s in slots) _currentSlots.Add(s);
            }

            // Silent background update check on startup
            _ = CheckUpdateSilentlyAsync();
            RefreshInUseBadges();
            MaybePromptSchemeRestore();
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            MaybePromptSchemeRestore();
        }

        private async Task CheckUpdateSilentlyAsync()
        {
            var settings = LoadAppSettings();
            var update = await Task.Run(() => UpdateChecker.CheckForUpdatesAsync(settings.SkippedUpdateVersion));
            if (update.HasUpdate)
            {
                Dispatcher.Invoke(() => ShowPendingUpdateBadge(update));
            }
        }

        private void ShowPendingUpdateBadge(UpdateInfo update)
        {
            _pendingUpdate = update;
            BtnNewUpdateFound.Content = $"🚀 發現新版 {update.LatestVersion}！點擊更新";
            BtnNewUpdateFound.Visibility = Visibility.Visible;
        }

        private void ShowUpdateDialog(UpdateInfo update)
        {
            var result = UpdateDialog.Show(this, update, skippedVersion =>
            {
                _skippedUpdateVersion = skippedVersion;
                SaveAppSettings(
                    GetCursorsDataFolder(),
                    _currentAppTheme,
                    _currentBgMode,
                    _currentUiScale,
                    _cursorSizePx,
                    _cursorScaleMode,
                    _appliedFolderPath,
                    _appliedThemeName,
                    skippedVersion);
                BtnNewUpdateFound.Visibility = Visibility.Collapsed;
                _pendingUpdate = null;
            });

            if (result == UpdateDialogResult.Skipped)
            {
                SetStatus("💡", $"已略過版本 {update.LatestVersion}，下次有新版本時會再提示。", StatusTone.Info);
            }
        }

        private sealed class AppSettingsData
        {
            public string FolderPath { get; set; } = string.Empty;
            public string AppTheme { get; set; } = "System";
            public string BgMode { get; set; } = "Theme";
            public string UiScale { get; set; } = UiScaleHelper.DefaultPreset;
            public int CursorSizePx { get; set; } = MousePointerSizeHelper.DefaultPx;
            public string CursorScaleMode { get; set; } = MousePointerSizeHelper.DefaultMode;
            public string AppliedFolderPath { get; set; } = string.Empty;
            public string AppliedThemeName { get; set; } = string.Empty;
            public string SkippedUpdateVersion { get; set; } = string.Empty;
        }

        private static string GetConfigFilePath() => AppPaths.ConfigFilePath;

        private static AppSettingsData LoadAppSettings()
        {
            var data = new AppSettingsData();
            string configPath = GetConfigFilePath();
            if (!File.Exists(configPath))
            {
                string? legacyPath = AppPaths.FindLegacyConfigPath();
                if (!string.IsNullOrEmpty(legacyPath))
                    configPath = legacyPath;
                else
                    return data;
            }

            if (!File.Exists(configPath))
                return data;

            try
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;

                    if (trimmed.Contains("="))
                    {
                        var parts = trimmed.Split(new[] { '=' }, 2);
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();
                        if (key.Equals("CursorsDataPath", StringComparison.OrdinalIgnoreCase) || key.Equals("FolderPath", StringComparison.OrdinalIgnoreCase) || key.Equals("StoragePath", StringComparison.OrdinalIgnoreCase))
                            data.FolderPath = val;
                        else if (key.Equals("AppTheme", StringComparison.OrdinalIgnoreCase) || key.Equals("Theme", StringComparison.OrdinalIgnoreCase) || key.Equals("UITheme", StringComparison.OrdinalIgnoreCase))
                            data.AppTheme = val;
                        else if (key.Equals("PreviewBg", StringComparison.OrdinalIgnoreCase) || key.Equals("PreviewBackground", StringComparison.OrdinalIgnoreCase) || key.Equals("BgMode", StringComparison.OrdinalIgnoreCase))
                            data.BgMode = val;
                        else if (key.Equals("UiScale", StringComparison.OrdinalIgnoreCase) || key.Equals("UIScale", StringComparison.OrdinalIgnoreCase) || key.Equals("InterfaceScale", StringComparison.OrdinalIgnoreCase))
                            data.UiScale = val;
                        else if (key.Equals("CursorSizePx", StringComparison.OrdinalIgnoreCase) ||
                                 key.Equals("CursorSizeLevel", StringComparison.OrdinalIgnoreCase) ||
                                 key.Equals("PointerSize", StringComparison.OrdinalIgnoreCase) ||
                                 key.Equals("MouseSize", StringComparison.OrdinalIgnoreCase))
                            data.CursorSizePx = MousePointerSizeHelper.ParseSize(val);
                        else if (key.Equals("CursorScaleMode", StringComparison.OrdinalIgnoreCase) || key.Equals("PointerScaleMode", StringComparison.OrdinalIgnoreCase))
                            data.CursorScaleMode = MousePointerSizeHelper.NormalizeMode(val);
                        else if (key.Equals("AppliedFolderPath", StringComparison.OrdinalIgnoreCase) || key.Equals("LastAppliedFolder", StringComparison.OrdinalIgnoreCase))
                            data.AppliedFolderPath = val;
                        else if (key.Equals("AppliedThemeName", StringComparison.OrdinalIgnoreCase) || key.Equals("LastAppliedTheme", StringComparison.OrdinalIgnoreCase))
                            data.AppliedThemeName = val;
                        else if (key.Equals("SkippedUpdateVersion", StringComparison.OrdinalIgnoreCase))
                            data.SkippedUpdateVersion = val;
                    }
                    else if (string.IsNullOrEmpty(data.FolderPath))
                    {
                        data.FolderPath = trimmed;
                    }
                }
            }
            catch { }

            data.UiScale = UiScaleHelper.NormalizePreset(data.UiScale);
            data.CursorSizePx = MousePointerSizeHelper.NormalizePx(data.CursorSizePx);
            data.CursorScaleMode = MousePointerSizeHelper.NormalizeMode(data.CursorScaleMode);
            return data;
        }

        private void PersistAppSettings()
        {
            SaveAppSettings(
                GetCursorsDataFolder(),
                _currentAppTheme,
                _currentBgMode,
                _currentUiScale,
                _cursorSizePx,
                _cursorScaleMode,
                _appliedFolderPath,
                _appliedThemeName,
                _skippedUpdateVersion);
        }

        private static void SaveAppSettings(
            string folderPath,
            string appTheme,
            string bgMode,
            string uiScale,
            int cursorSizePx = MousePointerSizeHelper.DefaultPx,
            string cursorScaleMode = MousePointerSizeHelper.DefaultMode,
            string appliedFolderPath = "",
            string appliedThemeName = "",
            string skippedUpdateVersion = "")
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataRoot);
                string configPath = GetConfigFilePath();
                var content =
                    $"FolderPath={folderPath}\n" +
                    $"AppTheme={appTheme}\n" +
                    $"PreviewBg={bgMode}\n" +
                    $"UiScale={UiScaleHelper.NormalizePreset(uiScale)}\n" +
                    $"CursorSizePx={MousePointerSizeHelper.NormalizePx(cursorSizePx)}\n" +
                    $"CursorScaleMode={MousePointerSizeHelper.NormalizeMode(cursorScaleMode)}\n" +
                    $"AppliedFolderPath={appliedFolderPath}\n" +
                    $"AppliedThemeName={appliedThemeName}\n" +
                    $"SkippedUpdateVersion={skippedUpdateVersion}\n";
                File.WriteAllText(configPath, content);
            }
            catch (Exception ex)
            {
                ConfirmDialog.Alert(Application.Current?.MainWindow, "錯誤", "儲存設定檔失敗：" + ex.Message, kind: ConfirmDialogKind.Error);
            }
        }

        private void SyncPointerSizeUi()
        {
            _suppressPointerSizeEvent = true;
            try
            {
                if (SliderPointerSize != null)
                    SliderPointerSize.Value = _cursorSizePx;
                if (TxtPointerSizeLabel != null)
                    TxtPointerSizeLabel.Text = MousePointerSizeHelper.GetPxLabel(_cursorSizePx);
            }
            finally
            {
                _suppressPointerSizeEvent = false;
            }
        }

        private void SliderPointerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressPointerSizeEvent || TxtPointerSizeLabel == null)
                return;

            int px = MousePointerSizeHelper.NormalizePx((int)Math.Round(e.NewValue));
            TxtPointerSizeLabel.Text = MousePointerSizeHelper.GetPxLabel(px);

            if (px == _cursorSizePx)
                return;

            _cursorSizePx = px;
            SchedulePointerSizeApply();
        }

        private void SchedulePointerSizeApply()
        {
            _pointerSizeApplyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _pointerSizeApplyTimer.Tick -= PointerSizeApplyTimer_Tick;
            _pointerSizeApplyTimer.Tick += PointerSizeApplyTimer_Tick;
            _pointerSizeApplyTimer.Stop();
            _pointerSizeApplyTimer.Start();
        }

        private void PointerSizeApplyTimer_Tick(object? sender, EventArgs e)
        {
            _pointerSizeApplyTimer?.Stop();
            ApplyPointerSizeFromUi();
        }

        private void ApplyPointerSizeFromUi()
        {
            PersistAppSettings();

            if (!string.IsNullOrEmpty(_appliedFolderPath) && Directory.Exists(_appliedFolderPath))
            {
                if (string.Equals(_currentLoadedFolder, _appliedFolderPath, StringComparison.OrdinalIgnoreCase)
                    && _currentSlots.Any(s => s.HasFile))
                {
                    TryApplyCurrentTheme(silent: true);
                }
                else
                {
                    ReapplyStoredTheme(silent: true);
                }
            }
            else if (_currentSlots.Any(s => s.HasFile))
            {
                CursorInstaller.ApplyPointerSizeOnly(_cursorSizePx, _cursorScaleMode, _currentSlots);
            }
            else
            {
                CursorInstaller.ApplyPointerSizeOnly(_cursorSizePx, _cursorScaleMode);
            }

            SetStatus("🖱️", $"鼠標大小已調整成 {MousePointerSizeHelper.NormalizePx(_cursorSizePx)} PX", StatusTone.Success);
        }

        private void BtnReapplyLast_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_appliedFolderPath) || !Directory.Exists(_appliedFolderPath))
            {
                ConfirmDialog.Alert(this, "提示", "尚無已記住的鼠標方案。", "請先成功「套用」一次主題。");
                return;
            }

            if (ReapplyStoredTheme(silent: false))
            {
                _schemePromptDismissed = false;
                ConfirmDialog.Alert(this, "套用成功",
                    $"已套用「{(_appliedThemeName.Length > 0 ? _appliedThemeName : Path.GetFileName(_appliedFolderPath))}」",
                    $"鼠標大小：{MousePointerSizeHelper.GetPxLabel(_cursorSizePx)}",
                    ConfirmDialogKind.Success);
            }
        }

        private bool ReapplyStoredTheme(bool silent)
        {
            if (string.IsNullOrEmpty(_appliedFolderPath) || !Directory.Exists(_appliedFolderPath))
            {
                if (!silent)
                    ConfirmDialog.Alert(this, "提示", "找不到上次套用的主題資料夾。", kind: ConfirmDialogKind.Warning);
                return false;
            }

            try
            {
                string name = string.IsNullOrEmpty(_appliedThemeName) ? Path.GetFileName(_appliedFolderPath) : _appliedThemeName;
                LoadFolder(_appliedFolderPath, name);
                TryApplyCurrentTheme(silent: true);
                _schemePromptDismissed = false;
                if (!silent)
                    SetStatus("🔁", $"已套用「{name}」（{MousePointerSizeHelper.GetPxLabel(_cursorSizePx)}）", StatusTone.Success);
                return true;
            }
            catch (Exception ex)
            {
                if (!silent)
                    ConfirmDialog.Alert(this, "錯誤", "套用失敗：" + ex.Message, kind: ConfirmDialogKind.Error);
                return false;
            }
        }

        private void MaybePromptSchemeRestore()
        {
            if (_schemePromptOpen) return;
            if (_skipSchemeMismatchCheck) return;
            if (string.IsNullOrEmpty(_appliedFolderPath)) return;
            if (!Directory.Exists(_appliedFolderPath)) return;

            // Previewing another theme in the list — system cursor is expected to differ.
            if (!string.IsNullOrEmpty(_currentLoadedFolder) &&
                !string.Equals(_currentLoadedFolder, _appliedFolderPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (CursorInstaller.IsAppliedSchemeStillActive(_appliedFolderPath))
            {
                _schemePromptDismissed = false;
                return;
            }

            // User already chose「略過」for this mismatch — don't spam on every Activated.
            if (_schemePromptDismissed) return;

            _schemePromptOpen = true;
            try
            {
                string name = string.IsNullOrEmpty(_appliedThemeName) ? Path.GetFileName(_appliedFolderPath) : _appliedThemeName;
                var result = ConfirmDialog.Show(this, new ConfirmDialogOptions
                {
                    Title = "鼠標方案已變更",
                    Headline = $"目前鼠標不是「{name}」",
                    Message = "偵測到系統鼠標已被外部程式變更。您可以擷取目前鼠標存入鼠標庫，或重新套用先前的主題。",
                    Buttons = ConfirmDialogButtons.YesNoCancel,
                    Kind = ConfirmDialogKind.Question,
                    YesText = "擷取目前鼠標",
                    NoText = "重新套用",
                    CancelText = "略過"
                });

                if (result == ConfirmDialogResult.Yes)
                {
                    ImportCurrentSystemCursors("擷取的自訂鼠標");
                }
                else if (result == ConfirmDialogResult.No)
                {
                    if (!ReapplyStoredTheme(silent: true))
                        _schemePromptDismissed = true;
                }
                else
                {
                    _schemePromptDismissed = true;
                }
            }
            finally
            {
                _schemePromptOpen = false;
            }
        }

        private static bool IsSystemInDarkMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("AppsUseLightTheme");
                        if (val is int lightMode)
                        {
                            return lightMode == 0;
                        }
                    }
                }
            }
            catch { }
            return true; // Default to dark mode
        }

        private void ApplyUiScale(string uiScalePreset)
        {
            try
            {
                _currentUiScale = UiScaleHelper.NormalizePreset(uiScalePreset);
                double scale = UiScaleHelper.GetEffectiveScale(_currentUiScale);
                RootScaleGrid.LayoutTransform = new ScaleTransform(scale, scale);
            }
            catch { }
        }

        private void ApplyAppTheme(string appTheme)
        {
            try
            {
                _currentAppTheme = appTheme;
                bool isLight = false;
                if (appTheme.Equals("Light", StringComparison.OrdinalIgnoreCase))
                {
                    isLight = true;
                }
                else if (appTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                {
                    isLight = false;
                }
                else
                {
                    // "System" or default -> follow Windows theme
                    isLight = !IsSystemInDarkMode();
                }

                var appRes = Application.Current.Resources;

                if (isLight)
                {
                    // Light Theme Palette
                    appRes["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xEF, 0xF1, 0xF5));
                    appRes["HeaderBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xED, 0xF3));
                    appRes["SidebarBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEF));
                    appRes["ContentBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xEF, 0xF1, 0xF5));
                    appRes["BottomBarBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE0, 0xE8));
                    appRes["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x4C, 0x4F, 0x69));
                    appRes["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x5C, 0x5F, 0x77));
                    appRes["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x8C, 0x8F, 0xA1));
                    appRes["CardBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    appRes["CardItemInnerBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
                    appRes["BorderColorBrush"] = new SolidColorBrush(Color.FromRgb(0xCC, 0xD0, 0xDA));
                    appRes["ButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xDF, 0xE3, 0xEB));
                    appRes["ButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xC4, 0xCA, 0xD8));
                    appRes["ButtonPressedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xB0, 0xB7, 0xC8));
                    appRes["ButtonHoverForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0x2C, 0x2E, 0x42));
                    appRes["SecondaryButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    appRes["SecondaryButtonForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xD6, 0x33, 0x6C));
                    appRes["SecondaryButtonBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xC4, 0xCC));
                    appRes["SecondaryButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xFD, 0xE8, 0xEF));
                    appRes["SecondaryButtonHoverForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x5C));
                    appRes["ItemHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF4));
                    appRes["ItemSelectedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE6, 0xFA));
                    appRes["ItemSelectedHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xCF, 0xDB, 0xF5));
                    appRes["SlotHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF4));
                    appRes["SlotSelectedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE6, 0xFA));
                    appRes["DialogCardBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    appRes["DialogInputBrush"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xED, 0xF3));
                    appRes["SuccessFileTextBrush"] = new SolidColorBrush(Color.FromRgb(0x2D, 0x8A, 0x4E));
                    appRes["InputSelectionBrush"] = new SolidColorBrush(Color.FromRgb(0xB4, 0xD0, 0xFE));
                    appRes["InputSelectionTextBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
                    appRes["ChipCheckedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE6, 0xFA));
                    appRes["ChipCheckedForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x6E));
                    appRes["AccentTitleBrush"] = new SolidColorBrush(Color.FromRgb(0x6E, 0x4F, 0xB8));
                    appRes["SubtleButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xE4, 0xE8, 0xF0));
                    appRes["SecondaryActionBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    appRes["PrimaryButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x3B, 0x7A, 0xED));
                    appRes["PrimaryButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xFF));
                    appRes["PrimaryButtonPressedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xD4));
                    appRes["DropZoneBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x6A, 0x90, 0xD8));
                    appRes["PopupBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    appRes["FavoriteStarBrush"] = new SolidColorBrush(Color.FromRgb(0xDF, 0x8E, 0x1D));
                    appRes["FavoriteStarMutedBrush"] = new SolidColorBrush(Color.FromRgb(0xA0, 0xA4, 0xB8));
                    appRes["DangerTextBrush"] = new SolidColorBrush(Color.FromRgb(0xD6, 0x33, 0x6C));
                    appRes["DangerBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0x18, 0xD6, 0x33, 0x6C));
                    appRes["DangerBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x55, 0xD6, 0x33, 0x6C));
                    appRes["GroupHeaderBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xEC));
                    appRes["InUseBadgeBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xD8, 0xF0, 0xDE));
                    appRes["InUseBadgeBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x2D, 0x8A, 0x4E));
                    appRes["InUseBadgeTextBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0x32));
                    appRes["StatusSuccessBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x7D, 0x3E));
                    appRes["StatusInfoBrush"] = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xD4));
                    appRes["StatusWarningBrush"] = new SolidColorBrush(Color.FromRgb(0x9A, 0x6F, 0x00));
                    appRes["StatusErrorBrush"] = new SolidColorBrush(Color.FromRgb(0xD6, 0x33, 0x6C));
                    appRes["SuccessColor"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x7D, 0x3E));
                }
                else
                {
                    // Dark Theme Palette (Default)
                    appRes["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x1C));
                    appRes["HeaderBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x22));
                    appRes["SidebarBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x1C));
                    appRes["ContentBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x25));
                    appRes["BottomBarBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x16));
                    appRes["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF8));
                    appRes["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xA8, 0xAF, 0xC9));
                    appRes["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x72, 0x78, 0x90));
                    appRes["CardBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x28));
                    appRes["CardItemInnerBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1E));
                    appRes["BorderColorBrush"] = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x48));
                    appRes["ButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x48));
                    appRes["ButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
                    appRes["ButtonPressedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x58, 0x5B, 0x70));
                    appRes["ButtonHoverForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF8));
                    appRes["SecondaryButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x48));
                    appRes["SecondaryButtonForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                    appRes["SecondaryButtonBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
                    appRes["SecondaryButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
                    appRes["SecondaryButtonHoverForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0xD0));
                    appRes["ItemHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x33));
                    appRes["ItemSelectedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x2E, 0x33, 0x50));
                    appRes["ItemSelectedHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x36, 0x3C, 0x5C));
                    appRes["SlotHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x30));
                    appRes["SlotSelectedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x2E, 0x33, 0x50));
                    appRes["DialogCardBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x28));
                    appRes["DialogInputBrush"] = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x1C));
                    appRes["SuccessFileTextBrush"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                    appRes["InputSelectionBrush"] = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
                    appRes["InputSelectionTextBrush"] = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF8));
                    appRes["ChipCheckedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x2E, 0x33, 0x50));
                    appRes["ChipCheckedForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xC8, 0xD8, 0xFF));
                    appRes["AccentTitleBrush"] = new SolidColorBrush(Color.FromRgb(0xC4, 0xA8, 0xFF));
                    appRes["SubtleButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x30));
                    appRes["SecondaryActionBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x28));
                    appRes["PrimaryButtonBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x3B, 0x7A, 0xED));
                    appRes["PrimaryButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xFF));
                    appRes["PrimaryButtonPressedBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xD4));
                    appRes["DropZoneBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xA8));
                    appRes["PopupBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x28));
                    appRes["FavoriteStarBrush"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
                    appRes["FavoriteStarMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6B, 0x6F, 0x85));
                    appRes["DangerTextBrush"] = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                    appRes["DangerBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0x18, 0xF3, 0x8B, 0xA8));
                    appRes["DangerBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x44, 0xF3, 0x8B, 0xA8));
                    appRes["GroupHeaderBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x30));
                    appRes["InUseBadgeBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x3D, 0x2E));
                    appRes["InUseBadgeBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x40, 0xA0, 0x2B));
                    appRes["InUseBadgeTextBrush"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                    appRes["StatusSuccessBrush"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                    appRes["StatusInfoBrush"] = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
                    appRes["StatusWarningBrush"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
                    appRes["StatusErrorBrush"] = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                    appRes["SuccessColor"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                }

                // Keep preview background in sync if it's following the theme
                ApplyPreviewBackground(_currentBgMode, _currentAppTheme);
            }
            catch { }
        }

        private void ApplyPreviewBackground(string bgMode, string? appTheme = null)
        {
            try
            {
                _currentBgMode = bgMode;
                string themeToUse = appTheme ?? _currentAppTheme;
                var appRes = Application.Current.Resources;
                bool isLightPreviewBg;

                if (bgMode.Equals("Light", StringComparison.OrdinalIgnoreCase))
                {
                    appRes["SlotPreviewBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
                    isLightPreviewBg = true;
                }
                else if (bgMode.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                {
                    appRes["SlotPreviewBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x1B));
                    isLightPreviewBg = false;
                }
                else if (bgMode.Equals("Checkerboard", StringComparison.OrdinalIgnoreCase) ||
                         bgMode.Equals("Checker", StringComparison.OrdinalIgnoreCase) ||
                         bgMode.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
                {
                    if (appRes["CheckerboardBrush"] is Brush checkerBrush)
                        appRes["SlotPreviewBackgroundBrush"] = checkerBrush;
                    isLightPreviewBg = true;
                }
                else
                {
                    bool isLight = false;
                    if (themeToUse.Equals("Light", StringComparison.OrdinalIgnoreCase))
                        isLight = true;
                    else if (themeToUse.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                        isLight = false;
                    else
                        isLight = !IsSystemInDarkMode();

                    if (isLight)
                    {
                        appRes["SlotPreviewBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
                        isLightPreviewBg = true;
                    }
                    else
                    {
                        appRes["SlotPreviewBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x1B));
                        isLightPreviewBg = false;
                    }
                }

                appRes["MissingSlotIconBrush"] = new SolidColorBrush(
                    isLightPreviewBg
                        ? Color.FromRgb(0x8C, 0x8F, 0xA1)
                        : Color.FromRgb(0xF3, 0x8B, 0xA8));
            }
            catch { }
        }

        private static bool _hasPromptedStorageLocation = false;

        private static string EnsureCursorsDataFolderInteractive(Window? owner)
        {
            var settings = LoadAppSettings();
            string savedPath = settings.FolderPath;
            if (!string.IsNullOrEmpty(savedPath))
            {
                if (!Directory.Exists(savedPath))
                {
                    try { Directory.CreateDirectory(savedPath); } catch { }
                }
                return savedPath;
            }

            string defaultFolder = AppPaths.DefaultCursorsDataFolder;

            // If running for the first time without config and hasn't prompted yet in this session
            if (!_hasPromptedStorageLocation)
            {
                _hasPromptedStorageLocation = true;
                var res = ConfirmDialog.Show(owner, new ConfirmDialogOptions
                {
                    Title = "鼠標資料夾位置設定",
                    Headline = "【首次使用設定】",
                    Message = "即將建立存放與管理鼠標主題的資料夾。",
                    PathLabel = "預設位置",
                    PathHighlight = defaultFolder,
                    BulletPoints = new[]
                    {
                        "點選「是」：使用預設位置建立 CursorsData 資料夾",
                        "點選「否」：自訂選擇或連結現有的資料夾"
                    },
                    Buttons = ConfirmDialogButtons.YesNo
                });

                if (res == ConfirmDialogResult.No)
                {
                    var cur = LoadAppSettings();
                    var dlg = new SettingsDialog(defaultFolder, cur.AppTheme, cur.BgMode, cur.UiScale, cur.CursorSizePx, cur.CursorScaleMode)
                    {
                        Owner = owner
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        SaveAppSettings(dlg.SelectedPath, dlg.SelectedAppTheme, dlg.SelectedBgMode, dlg.SelectedUiScale,
                            dlg.SelectedCursorSizePx, dlg.SelectedCursorScaleMode, cur.AppliedFolderPath, cur.AppliedThemeName,
                            cur.SkippedUpdateVersion);
                        return dlg.SelectedPath;
                    }
                }
            }

            if (!Directory.Exists(defaultFolder))
            {
                try { Directory.CreateDirectory(defaultFolder); } catch { }
            }
            SaveAppSettings(defaultFolder, settings.AppTheme, settings.BgMode, settings.UiScale,
                settings.CursorSizePx, settings.CursorScaleMode, settings.AppliedFolderPath, settings.AppliedThemeName,
                settings.SkippedUpdateVersion);
            return defaultFolder;
        }

        private static string GetCursorsDataFolder()
        {
            var settings = LoadAppSettings();
            string savedPath = settings.FolderPath;
            if (!string.IsNullOrEmpty(savedPath))
            {
                if (!Directory.Exists(savedPath))
                {
                    try { Directory.CreateDirectory(savedPath); } catch { }
                }
                return savedPath;
            }

            string appDir = AppPaths.InstallDirectory;
            string parentDir = Directory.GetParent(appDir)?.FullName ?? appDir;
            string grandParentDir = Directory.GetParent(parentDir)?.FullName ?? parentDir;

            var candidates = new[]
            {
                AppPaths.DefaultCursorsDataFolder,
                Path.Combine(appDir, "CursorsData"),
                Path.Combine(parentDir, "CursorsData"),
                Path.Combine(grandParentDir, "CursorsData")
            };

            foreach (var c in candidates)
            {
                if (Directory.Exists(c)) return c;
            }

            return AppPaths.DefaultCursorsDataFolder;
        }

        private void SetCustomCursorsDataFolder(string newPath)
        {
            SaveAppSettings(newPath, _currentAppTheme, _currentBgMode, _currentUiScale,
                _cursorSizePx, _cursorScaleMode, _appliedFolderPath, _appliedThemeName, _skippedUpdateVersion);
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
            var scanned = await Task.Run(() => FolderScanner.ScanDirectory(cursorsData));

            // Drop temporary entries that were later saved into the library, or whose folder disappeared
            _temporaryThemes.RemoveAll(t =>
                !Directory.Exists(t.FolderPath) ||
                scanned.Any(s => s.FolderPath.Equals(t.FolderPath, StringComparison.OrdinalIgnoreCase)));

            _allThemes = _temporaryThemes
                .Concat(scanned)
                .ToList();

            foreach (var theme in _allThemes)
                ThemeMetadataStore.ApplyTo(theme);

            int tempCount = _temporaryThemes.Count;
            TxtThemeCount.Text = tempCount > 0
                ? $"{scanned.Count} 個主題 · {tempCount} 未存入庫"
                : $"{scanned.Count} 個主題";
            FilterThemes();
            RefreshInUseBadges();

            if (!string.IsNullOrEmpty(selectFolderPath))
            {
                var match = _allThemes.FirstOrDefault(t => t.FolderPath.Equals(selectFolderPath, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    SelectThemeItem(match);
            }
        }

        private void RefreshInUseBadges()
        {
            foreach (var theme in _allThemes)
            {
                theme.IsCurrentlyInUse = !string.IsNullOrEmpty(_appliedFolderPath) &&
                    theme.FolderPath.Equals(_appliedFolderPath, StringComparison.OrdinalIgnoreCase);
            }

            // Also update temporary list copies if any are not yet merged into _allThemes
            foreach (var theme in _temporaryThemes)
            {
                theme.IsCurrentlyInUse = !string.IsNullOrEmpty(_appliedFolderPath) &&
                    theme.FolderPath.Equals(_appliedFolderPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void FilterThemes()
        {
            string query = TxtSearch.Text.Trim();
            IEnumerable<CharacterThemeItem> items = _allThemes;

            switch (ThemeMetadataStore.FilterMode)
            {
                case ThemeFilterMode.Favorites:
                    items = items.Where(t => t.IsFavorite);
                    break;
                case ThemeFilterMode.Recent:
                    var recent = ThemeMetadataStore.GetRecentPaths();
                    items = items
                        .Where(t => recent.Any(p => p.Equals(t.FolderPath, StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(t =>
                        {
                            int idx = recent.ToList().FindIndex(p => p.Equals(t.FolderPath, StringComparison.OrdinalIgnoreCase));
                            return idx < 0 ? int.MaxValue : idx;
                        });
                    break;
            }

            if (!string.IsNullOrEmpty(query))
            {
                items = items.Where(t =>
                    t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.GroupDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var themeList = items.ToList();
            UpdateThemeListToolbarVisibility();

            if (IsFlatThemeList)
            {
                _flatThemeList.Clear();
                foreach (var theme in themeList)
                    _flatThemeList.Add(theme);

                BindThemeTreeView(_flatThemeList, flatList: true);
                return;
            }

            _groupExpandedState = _themeGroups.ToDictionary(g => g.Name, g => g.IsExpanded);
            _themeGroups.Clear();

            IEnumerable<IGrouping<string, CharacterThemeItem>> grouped = themeList
                .GroupBy(t => t.GroupDisplay)
                .OrderBy(g => GetGroupSortOrder(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var grp in grouped)
            {
                var node = new ThemeGroupNode
                {
                    Name = grp.Key,
                    IsExpanded = _groupExpandedState.TryGetValue(grp.Key, out var expanded) ? expanded : true
                };

                foreach (var theme in SortThemes(grp))
                    node.Themes.Add(theme);

                if (node.Themes.Count > 0)
                    _themeGroups.Add(node);
            }

            bool wasFlatList = _themeTreeShowsFlatList;
            BindThemeTreeView(_themeGroups, flatList: false);

            if (wasFlatList)
                ExpandAllThemeGroups();
            else if (!string.IsNullOrEmpty(query))
                ExpandAllThemeGroups();
            else if (_userCollapsedAllGroups)
            {
                foreach (var group in _themeGroups)
                    group.IsExpanded = false;
                ScheduleGroupExpansion();
            }
            else
                ScheduleGroupExpansion();
        }

        private void BindThemeTreeView(System.Collections.IEnumerable source, bool flatList)
        {
            bool layoutChanged = _themeTreeShowsFlatList != flatList;
            _themeTreeShowsFlatList = flatList;

            if (layoutChanged || !ReferenceEquals(TrvThemes.ItemsSource, source))
            {
                TrvThemes.ItemsSource = null;
                TrvThemes.ItemsSource = source;
                return;
            }

            if (!flatList)
            {
                TrvThemes.ItemsSource = null;
                TrvThemes.ItemsSource = source;
            }
        }

        private void ScheduleGroupExpansion()
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyGroupExpansionToTree();
            }, DispatcherPriority.Loaded);
        }

        private void UpdateThemeListToolbarVisibility()
        {
            BtnCollapseAllGroups.Visibility = IsFlatThemeList ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ExpandAllThemeGroups()
        {
            _userCollapsedAllGroups = false;

            foreach (var group in _themeGroups)
                group.IsExpanded = true;

            ScheduleGroupExpansion();
        }

        private void CollapseAllThemeGroups()
        {
            _userCollapsedAllGroups = true;
            _groupExpandedState = _themeGroups.ToDictionary(g => g.Name, _ => false);

            foreach (var group in _themeGroups)
                group.IsExpanded = false;

            ScheduleGroupExpansion();
        }

        private void ApplyGroupExpansionToTree()
        {
            TrvThemes.UpdateLayout();
            foreach (var group in _themeGroups)
            {
                if (TrvThemes.ItemContainerGenerator.ContainerFromItem(group) is TreeViewItem groupContainer)
                    groupContainer.IsExpanded = group.IsExpanded;
            }
        }

        private IEnumerable<CharacterThemeItem> GetVisibleThemes()
        {
            return IsFlatThemeList ? _flatThemeList : _themeGroups.SelectMany(g => g.Themes);
        }

        private void SetBatchDeleteMode(bool enabled)
        {
            IsBatchDeleteMode = enabled;

            if (!enabled)
            {
                foreach (var theme in _allThemes)
                    theme.IsSelectedForBatch = false;
            }

            UpdateBatchDeleteSelectionUi();
        }

        private void UpdateBatchDeleteSelectionUi()
        {
            int count = GetVisibleThemes().Count(t => t.IsSelectedForBatch);
            BtnDeleteSelectedThemes.Content = $"刪除所選 ({count})";
            BtnDeleteSelectedThemes.IsEnabled = count > 0;
        }

        private void BtnCollapseAllGroups_Click(object sender, RoutedEventArgs e)
        {
            CollapseAllThemeGroups();
        }

        private void BtnBatchDelete_Click(object sender, RoutedEventArgs e)
        {
            SetBatchDeleteMode(true);
            ExpandAllThemeGroups();
        }

        private void BtnCancelBatchDelete_Click(object sender, RoutedEventArgs e)
        {
            SetBatchDeleteMode(false);
        }

        private void BtnBatchSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var theme in GetVisibleThemes())
                theme.IsSelectedForBatch = true;

            RefreshGroupBatchCheckStates();
            UpdateBatchDeleteSelectionUi();
        }

        private void BtnBatchSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var theme in GetVisibleThemes())
                theme.IsSelectedForBatch = false;

            RefreshGroupBatchCheckStates();
            UpdateBatchDeleteSelectionUi();
        }

        private void RefreshGroupBatchCheckStates()
        {
            foreach (var group in _themeGroups)
                group.RefreshGroupBatchChecked();
        }

        private void BatchCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            RefreshGroupBatchCheckStates();
            UpdateBatchDeleteSelectionUi();
        }

        private void GroupBatchCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { DataContext: ThemeGroupNode group })
            {
                bool selectAll = group.IsGroupBatchChecked == true;
                foreach (var theme in group.Themes)
                    theme.IsSelectedForBatch = selectAll;
                group.RefreshGroupBatchChecked();
            }

            UpdateBatchDeleteSelectionUi();
        }

        private void BtnDeleteSelectedThemes_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetVisibleThemes().Where(t => t.IsSelectedForBatch).ToList();
            if (selected.Count == 0)
                return;

            int libraryCount = selected.Count(t => !t.IsTemporary);
            int temporaryCount = selected.Count - libraryCount;
            string summary = libraryCount > 0 && temporaryCount > 0
                ? $"將永久刪除 {libraryCount} 個主題，並從列表移除 {temporaryCount} 個未存入庫項目。"
                : libraryCount > 0
                    ? $"將永久刪除 {libraryCount} 個主題資料夾。"
                    : $"將從列表移除 {temporaryCount} 個未存入庫項目，不會刪除原始資料夾。";

            var confirm = ConfirmDialog.Show(this, new ConfirmDialogOptions
            {
                Title = "批量刪除確認",
                Headline = $"確定要刪除所選的 {selected.Count} 個主題嗎？",
                Message = summary,
                PathLabel = "所選主題",
                PathHighlight = string.Join(Environment.NewLine, selected.Select(t => $"• {t.Name}")),
                Buttons = ConfirmDialogButtons.YesNo,
                Kind = ConfirmDialogKind.Warning
            });

            if (confirm != ConfirmDialogResult.Yes)
                return;

            DeleteThemeItems(selected);
            SetBatchDeleteMode(false);
        }

        private static int GetGroupSortOrder(string groupName) => groupName switch
        {
            ThemeGroupNames.Temporary => 0,
            ThemeGroupNames.Ungrouped => 1,
            ThemeGroupNames.LegacyRoot => 1,
            _ => 2
        };

        private static IEnumerable<CharacterThemeItem> SortThemes(IEnumerable<CharacterThemeItem> items)
        {
            return ThemeMetadataStore.SortMode switch
            {
                ThemeSortMode.Date => items.OrderByDescending(t => t.FolderModifiedUtc ?? DateTime.MinValue).ThenBy(t => t.Name),
                ThemeSortMode.Recent => items.OrderByDescending(t => t.LastUsedUtc ?? DateTime.MinValue).ThenBy(t => t.Name),
                _ => items.OrderBy(t => t.Name)
            };
        }

        private void ThemeFilter_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ToggleButton clicked)
                return;

            ThemeFilterMode mode = clicked.Name switch
            {
                nameof(BtnFilterFavorites) => ThemeFilterMode.Favorites,
                nameof(BtnFilterRecent) => ThemeFilterMode.Recent,
                _ => ThemeFilterMode.All
            };

            ApplyThemeFilterMode(mode);
        }

        private void ApplyThemeFilterMode(ThemeFilterMode mode)
        {
            _suppressThemeFilterEvent = true;
            try
            {
                BtnFilterAll.IsChecked = mode == ThemeFilterMode.All;
                BtnFilterFavorites.IsChecked = mode == ThemeFilterMode.Favorites;
                BtnFilterRecent.IsChecked = mode == ThemeFilterMode.Recent;
            }
            finally
            {
                _suppressThemeFilterEvent = false;
            }

            ThemeMetadataStore.SetFilterMode(mode);

            if (!IsFlatThemeList)
                _userCollapsedAllGroups = false;

            FilterThemes();
        }

        private void CmbThemeSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressThemeFilterEvent || CmbThemeSort.SelectedItem is not ComboBoxItem item)
                return;

            if (item.Tag is string tag && Enum.TryParse<ThemeSortMode>(tag, true, out var mode))
            {
                ThemeMetadataStore.SetSortMode(mode);
                FilterThemes();
            }
        }

        private void SelectThemeItem(CharacterThemeItem item, bool scrollIntoView = true)
        {
            if (!IsFlatThemeList)
            {
                foreach (var group in _themeGroups)
                {
                    if (group.Themes.Contains(item))
                    {
                        group.IsExpanded = true;
                        break;
                    }
                }
            }

            TrvThemes.UpdateLayout();
            if (scrollIntoView)
                ScrollThemeIntoView(item);
            else
                SetThemeItemSelected(item);
            LoadFolder(item.FolderPath, item.Name);
        }

        private void SetThemeItemSelected(CharacterThemeItem item)
        {
            if (IsFlatThemeList)
            {
                if (TrvThemes.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem flatContainer)
                {
                    flatContainer.IsSelected = true;
                    flatContainer.Focus();
                }

                return;
            }

            foreach (var group in _themeGroups)
            {
                var groupContainer = TrvThemes.ItemContainerGenerator.ContainerFromItem(group) as TreeViewItem;
                if (groupContainer == null)
                    continue;

                groupContainer.IsExpanded = true;
                groupContainer.UpdateLayout();

                var themeContainer = groupContainer.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (themeContainer != null)
                {
                    themeContainer.IsSelected = true;
                    themeContainer.Focus();
                    return;
                }
            }
        }

        private void ScrollThemeIntoView(CharacterThemeItem item)
        {
            SetThemeItemSelected(item);

            if (IsFlatThemeList)
            {
                if (TrvThemes.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem flatContainer)
                    flatContainer.BringIntoView();
                return;
            }

            foreach (var group in _themeGroups)
            {
                var groupContainer = TrvThemes.ItemContainerGenerator.ContainerFromItem(group) as TreeViewItem;
                if (groupContainer == null)
                    continue;

                var themeContainer = groupContainer.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (themeContainer != null)
                {
                    themeContainer.BringIntoView();
                    return;
                }
            }
        }

        private void SelectFirstThemeIfAny()
        {
            var first = GetVisibleThemes().FirstOrDefault() ?? _allThemes.FirstOrDefault();
            if (first != null)
                SelectThemeItem(first);
        }

        private void RememberTemporaryTheme(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            string cursorsData = GetCursorsDataFolder();
            if (folderPath.StartsWith(cursorsData, StringComparison.OrdinalIgnoreCase))
                return;

            // Keep a single unsaved session entry so switching themes can still return to it
            _temporaryThemes.RemoveAll(t => !t.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
            if (_temporaryThemes.Any(t => t.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
                return;

            var item = FolderScanner.TryCreateThemeItem(folderPath, isTemporary: true);
            if (item != null)
                _temporaryThemes.Insert(0, item);
        }

        private void ForgetTemporaryTheme(string folderPath)
        {
            _temporaryThemes.RemoveAll(t => t.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
        }

        private void ReloadThemes(string? selectFolderPath = null)
        {
            _ = ReloadThemesAsync(selectFolderPath);
        }

        private async void BtnRefreshThemes_Click(object sender, RoutedEventArgs e)
        {
            // Preserve the selection so manually refreshing the library does not interrupt browsing.
            string? selectedFolderPath = (TrvThemes.SelectedItem as CharacterThemeItem)?.FolderPath
                ?? (!string.IsNullOrEmpty(_currentLoadedFolder) ? _currentLoadedFolder : null);

            BtnRefreshThemes.IsEnabled = false;
            try
            {
                await ReloadThemesAsync(selectedFolderPath);
                SetStatus("↻", "鼠標庫已重新整理", StatusTone.Info);
            }
            finally
            {
                BtnRefreshThemes.IsEnabled = true;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterThemes();
        }

        private void TrvThemes_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (TrvThemes.SelectedItem is CharacterThemeItem item)
                LoadFolder(item.FolderPath, item.Name);
        }

        private void ImportAndLoadFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            string? themeRoot = ImportScanner.FindThemeRootFolder(folderPath);
            if (themeRoot == null)
            {
                ConfirmDialog.Alert(this, "提示",
                    "未找到可辨識的游標檔案",
                    "請確認資料夾內含有 .ani 或 .cur 游標檔。",
                    ConfirmDialogKind.Information);
                return;
            }

            var scan = ImportScanner.ScanFolder(themeRoot);
            if (scan.TotalCursorFiles == 0)
            {
                ConfirmDialog.Alert(this, "提示",
                    "未找到可辨識的游標檔案",
                    "請確認資料夾內含有 .ani 或 .cur 游標檔。",
                    ConfirmDialogKind.Information);
                return;
            }

            string cursorsData = EnsureCursorsDataFolderInteractive(this);
            string folderName = Path.GetFileName(themeRoot.TrimEnd('\\', '/'));
            string targetDir = themeRoot;

            // If the dragged folder is not already inside the current storage folder
            if (!themeRoot.StartsWith(cursorsData, StringComparison.OrdinalIgnoreCase))
            {
                var previewBullets = scan.ToPreviewBulletPoints().ToList();
                previewBullets.Add("點選「是」：複製存入鼠標庫，並立即套用此主題");
                previewBullets.Add("點選「否」：不複製檔案，仍立即套用，並在左側以「未存入庫」列出方便再回來");

                var askResult = ConfirmDialog.Show(this, new ConfirmDialogOptions
                {
                    Title = "匯入鼠標主題確認",
                    Headline = $"檢測到新拖入的鼠標資料夾：「{folderName}」",
                    Message = "是否要將此鼠標主題複製存入您的鼠標庫中？",
                    PathLabel = "目前儲存庫目錄",
                    PathHighlight = cursorsData,
                    BulletPoints = previewBullets,
                    FooterNote = "若想自訂資料夾位置，可隨時點擊右上角「⚙️ 設定」更改",
                    Buttons = ConfirmDialogButtons.YesNoCancel
                });

                if (askResult == ConfirmDialogResult.Cancel)
                {
                    return;
                }

                if (askResult == ConfirmDialogResult.Yes)
                {
                    targetDir = Path.Combine(cursorsData, folderName);
                    try
                    {
                        CopyDirectory(themeRoot, targetDir);
                        ForgetTemporaryTheme(themeRoot);
                    }
                    catch (Exception ex)
                    {
                        ConfirmDialog.Alert(this, "提示",
                            $"複製至儲存庫時發生錯誤：{ex.Message}",
                            "將直接讀取原目錄。",
                            ConfirmDialogKind.Warning);
                        targetDir = themeRoot;
                        RememberTemporaryTheme(targetDir);
                    }
                }
                else
                {
                    targetDir = themeRoot;
                    RememberTemporaryTheme(targetDir);
                }
            }

            // Reload sidebar list and select the theme
            ReloadThemes(targetDir);
            LoadFolder(targetDir, applyWhenReady: true);
        }

        public void LoadFolder(string folderPath, string? themeName = null, bool applyWhenReady = false)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            _currentLoadedFolder = folderPath;
            _currentThemeName = themeName ?? Path.GetFileName(folderPath).Replace("Mouse cursor", "").Trim();
            if (string.IsNullOrWhiteSpace(_currentThemeName)) _currentThemeName = Path.GetFileName(folderPath);

            TxtCurrentThemeTitle.Text = _currentThemeName;
            TxtCurrentFolderPath.Text = folderPath;

            int token = ++_loadFolderToken;
            _ = LoadFolderAsync(folderPath, token, applyWhenReady);
        }

        private async Task LoadFolderAsync(string folderPath, int token, bool applyWhenReady)
        {
            List<CursorSlot> slots;
            try
            {
                slots = await Task.Run(() => CursorMatcher.MatchFolder(folderPath, loadAniSequences: false));
            }
            catch
            {
                return;
            }

            if (token != _loadFolderToken)
                return;

            _currentSlots.Clear();
            foreach (var slot in slots)
                _currentSlots.Add(slot);

            int matchedCount = _currentSlots.Count(s => !s.IsExtra && s.HasFile);
            int extraCount = _currentSlots.Count(s => s.IsExtra);
            string extraNote = extraCount > 0 ? $"（另有 {extraCount} 個額外檔案，不會套用）" : "";
            SetStatus("💡", $"已配對 {matchedCount} / {WindowsCursorSlots.StandardCount} 項鼠標{extraNote}。點擊「套用」即可立即生效！", StatusTone.Info);

            if (applyWhenReady && token == _loadFolderToken)
                TryApplyCurrentTheme(silent: true);

            try
            {
                await Task.Run(() => CursorMatcher.LoadSlotAniSequences(slots));
            }
            catch
            {
                return;
            }

            if (token != _loadFolderToken)
                return;

            CursorMatcher.ApplyAniPreviewFrames(slots);
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
                        if (ext == ".zip")
                        {
                            HandleZipArchiveDrop(path);
                        }
                        else if (ext == ".exe")
                        {
                            HandleExeDrop(path);
                        }
                        else if (ext == ".rar" || ext == ".7z")
                        {
                            HandleUnsupportedArchiveDrop(path);
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

        private void HandleZipArchiveDrop(string zipPath)
        {
            var (extractDir, skippedEntries, error) = ImportScanner.ExtractZipSafely(zipPath);
            if (extractDir == null)
            {
                ConfirmDialog.Alert(this, "錯誤",
                    $"解壓縮失敗：{error}",
                    kind: ConfirmDialogKind.Error);
                return;
            }

            string? themeRoot = ImportScanner.FindThemeRootFolder(extractDir);
            if (themeRoot == null)
            {
                ConfirmDialog.Alert(this, "提示",
                    "壓縮檔內未找到鼠標檔案",
                    "請確認 ZIP 內含有 .ani 或 .cur 游標檔。本工具不會執行壓縮檔內的安裝程式。",
                    ConfirmDialogKind.Information);
                return;
            }

            if (skippedEntries > 0)
            {
                ConfirmDialog.Alert(this, "安全提示",
                    $"已略過 {skippedEntries} 個可疑的壓縮項目",
                    "其餘內容已安全解壓並繼續掃描游標檔。",
                    ConfirmDialogKind.Warning);
            }

            ImportAndLoadFolder(themeRoot);
        }

        private void HandleExeDrop(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            ConfirmDialog.Show(this, new ConfirmDialogOptions
            {
                Title = "安全提示",
                Kind = ConfirmDialogKind.Warning,
                Headline = "本工具不會執行外部安裝程式",
                Message = $"為保護您的電腦，CursorManager 不會執行「{fileName}」。",
                BulletPoints = new[]
                {
                    "請在檔案總管中手動解壓或提取內含的游標圖示資料夾",
                    "將包含 .ani / .cur 的資料夾或 .zip 壓縮檔拖曳進本視窗即可安全匯入",
                    "若您已在別處安裝並套用，可使用右上角「📸 擷取鼠標」"
                },
                Buttons = ConfirmDialogButtons.Ok
            });
        }

        private void HandleUnsupportedArchiveDrop(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            ConfirmDialog.Show(this, new ConfirmDialogOptions
            {
                Title = "請先手動解壓",
                Kind = ConfirmDialogKind.Information,
                Headline = $"暫不支援直接匯入「{fileName}」",
                Message = "請先將壓縮檔解壓縮後，將含有游標圖示的資料夾拖曳進來。",
                BulletPoints = new[]
                {
                    "支援直接拖入：資料夾、.zip 壓縮檔",
                    "不支援直接匯入：.rar、.7z（請手動解壓）",
                    "本工具不會執行任何外部安裝程式，以確保安全"
                },
                Buttons = ConfirmDialogButtons.Ok
            });
        }

        private void ImportCurrentSystemCursors(string defaultThemeName)
        {
            try
            {
                string cursorsData = EnsureCursorsDataFolderInteractive(this);
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

                        foreach (var k in WindowsCursorSlots.RegistryKeyOrder)
                        {
                            var p = key.GetValue(k)?.ToString();
                            if (!string.IsNullOrEmpty(p))
                            {
                                p = Environment.ExpandEnvironmentVariables(p);
                                if (File.Exists(p))
                                    currentPaths[k] = p;
                            }
                        }
                    }
                }

                if (currentPaths.Count == 0)
                {
                    ConfirmDialog.Alert(this, "提示",
                        "未能從系統中檢測到已套用的鼠標檔案",
                        "請確認安裝檔是否已成功套用鼠標。",
                        ConfirmDialogKind.Information);
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
                _appliedFolderPath = targetFolder;
                _appliedThemeName = themeName;
                _schemePromptDismissed = true;
                PersistAppSettings();
                RefreshInUseBadges();
                SetStatus("✨", $"已成功將「{themeName}」鼠標主題存入鼠標庫！", StatusTone.Success);
                ConfirmDialog.Alert(this, "匯入成功",
                    $"已成功將「{themeName}」鼠標提取並儲存至鼠標庫！",
                    "您往後隨時可以在左側清單切換回此鼠標。",
                    ConfirmDialogKind.Success);
            }
            catch (Exception ex)
            {
                ConfirmDialog.Alert(this, "錯誤", "提取鼠標失敗：" + ex.Message, kind: ConfirmDialogKind.Error);
            }
        }

        private void DropZone_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "選擇包含鼠標 (.ani / .cur) 的資料夾"
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

        private void BtnOpenCurrentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentLoadedFolder) || !Directory.Exists(_currentLoadedFolder))
            {
                ConfirmDialog.Alert(this, "提示", "尚未載入任何主題資料夾。", kind: ConfirmDialogKind.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _currentLoadedFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ConfirmDialog.Alert(this, "錯誤", "無法開啟資料夾：" + ex.Message, kind: ConfirmDialogKind.Error);
            }
        }

        private void BtnApplyTheme_Click(object sender, RoutedEventArgs e)
        {
            TryApplyCurrentTheme(silent: false);
        }

        private bool SlotsMatchLoadedFolder()
        {
            if (string.IsNullOrEmpty(_currentLoadedFolder))
                return false;

            return _currentSlots.Any(s =>
                s.HasFile &&
                s.FilePath.StartsWith(_currentLoadedFolder, StringComparison.OrdinalIgnoreCase));
        }

        private void TryApplyCurrentTheme(bool silent)
        {
            if (_currentSlots.Count == 0 || !_currentSlots.Any(s => s.HasFile))
            {
                if (!silent)
                {
                    SetStatus("⚠️", "請先選擇包含鼠標檔案的資料夾！", StatusTone.Warning);
                    ConfirmDialog.Alert(this, "提示", "請先選擇或拖曳包含鼠標檔案的資料夾！");
                }
                return;
            }

            if (!SlotsMatchLoadedFolder())
                return;

            try
            {
                CursorInstaller.ApplyCursors(_currentSlots, _currentThemeName, _cursorSizePx, _cursorScaleMode);
                _appliedFolderPath = _currentLoadedFolder;
                _appliedThemeName = _currentThemeName;
                ThemeMetadataStore.RecordApplied(_appliedFolderPath);
                PersistAppSettings();
                RefreshInUseBadges();
                _schemePromptDismissed = true;
                _skipSchemeMismatchCheck = true;
                Dispatcher.BeginInvoke(() => _skipSchemeMismatchCheck = false, DispatcherPriority.ApplicationIdle);
                SetStatus("✅", $"套用成功！已即時切換為「{_currentThemeName}」鼠標主題！", StatusTone.Success);
                if (!silent)
                {
                    ConfirmDialog.Alert(this, "套用成功",
                        $"已成功套用「{_currentThemeName}」鼠標！",
                        $"鼠標大小：{MousePointerSizeHelper.GetPxLabel(_cursorSizePx)}",
                        ConfirmDialogKind.Success);
                }
            }
            catch (Exception ex)
            {
                SetStatus("❌", "套用失敗：" + ex.Message, StatusTone.Error);
                ConfirmDialog.Alert(this, "錯誤", "套用失敗：" + ex.Message, kind: ConfirmDialogKind.Error);
            }
        }

        private void ThemeItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem listBoxItem)
                return;

            if (listBoxItem.DataContext is not CharacterThemeItem item)
                return;

            SelectThemeItem(item, scrollIntoView: false);
            listBoxItem.Focus();

            bool isTemporary = item.IsTemporary;
            string menuKey = isTemporary ? "SessionThemeContextMenu" : "LibraryThemeContextMenu";
            if (TryFindResource(menuKey) is ContextMenu menu)
            {
                if (listBoxItem.DataContext is CharacterThemeItem theme)
                {
                    string favHeader = theme.IsFavorite ? "☆ 移除收藏" : "⭐ 加入收藏";
                    foreach (var child in menu.Items)
                    {
                        if (child is MenuItem mi && (mi.Name == "MenuToggleFavorite" || mi.Name == "MenuToggleFavoriteSession"))
                            mi.Header = favHeader;
                    }
                }

                listBoxItem.ContextMenu = menu;
                menu.PlacementTarget = listBoxItem;
                menu.DataContext = listBoxItem.DataContext;
                menu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void ThemeTreeItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem treeViewItem)
                return;

            if (e.OriginalSource is not DependencyObject source)
                return;

            if (IsDescendantOfInteractiveControl(source))
                return;

            if (treeViewItem.DataContext is CharacterThemeItem theme)
            {
                treeViewItem.IsSelected = true;
                treeViewItem.Focus();
                LoadFolder(theme.FolderPath, theme.Name);
                e.Handled = true;
                return;
            }

            if (treeViewItem.DataContext is not ThemeGroupNode group)
                return;

            // Preview tunnels top-down; ignore clicks that belong to nested theme rows.
            if (FindTreeViewItemAncestor(source) is TreeViewItem clickedItem && clickedItem != treeViewItem)
                return;

            bool next = !treeViewItem.IsExpanded;
            treeViewItem.IsExpanded = next;
            group.IsExpanded = next;
            _userCollapsedAllGroups = false;
            _groupExpandedState[group.Name] = next;
            e.Handled = true;
        }

        private static TreeViewItem? FindTreeViewItemAncestor(DependencyObject source)
        {
            while (source != null)
            {
                if (source is TreeViewItem item)
                    return item;

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static bool IsDescendantOfInteractiveControl(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ToggleButton or CheckBox)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void MenuToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (GetThemeItemFromMenuSender(sender) is not CharacterThemeItem item)
                return;

            bool next = !item.IsFavorite;
            ThemeMetadataStore.SetFavorite(item.FolderPath, next);
            item.IsFavorite = next;
            FilterThemes();
            SetStatus(next ? "★" : "☆", next ? $"已將「{item.Name}」加入收藏" : $"已將「{item.Name}」移除收藏",
                StatusTone.Warning);
        }

        private void MenuSetCustomGroup_Click(object sender, RoutedEventArgs e)
        {
            if (GetThemeItemFromMenuSender(sender) is not CharacterThemeItem item)
                return;

            string currentGroup = ThemeGroupNames.IsRootLevel(item.Group) ? string.Empty : item.Group;
            var dlg = new TextInputDialog(
                "指定群組",
                "輸入群組名稱，主題資料夾將移至「鼠標庫\\群組名\\主題名」。\n留空則移至鼠標庫根目錄。",
                currentGroup,
                allowEmpty: true)
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true)
                return;

            string groupName = dlg.Value.Trim();
            if (!IsValidFolderName(groupName))
            {
                ConfirmDialog.Alert(this, "提示", "群組名稱含有不合法字元。", kind: ConfirmDialogKind.Warning);
                return;
            }

            if (!TryRelocateThemeToGroup(item, groupName, out string newPath, out string? errorMessage))
            {
                if (!string.IsNullOrEmpty(errorMessage))
                    ConfirmDialog.Alert(this, "錯誤", $"移動主題失敗：{errorMessage}", kind: ConfirmDialogKind.Error);
                return;
            }

            ReloadThemes(newPath);
            LoadFolder(newPath, item.Name);
            SetStatus("📁", string.IsNullOrEmpty(groupName)
                ? $"已將「{item.Name}」移至鼠標庫根目錄"
                : $"已將「{item.Name}」移至群組「{groupName}」",
                StatusTone.Success);
        }

        private static bool IsValidFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return true;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private bool TryRelocateThemeToGroup(CharacterThemeItem item, string groupName, out string newPath, out string? errorMessage)
        {
            newPath = item.FolderPath;
            errorMessage = null;

            string cursorsData = GetCursorsDataFolder();
            string themeFolderName = Path.GetFileName(item.FolderPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(themeFolderName))
            {
                errorMessage = "無法辨識主題資料夾名稱。";
                return false;
            }

            string targetDir = string.IsNullOrEmpty(groupName)
                ? Path.Combine(cursorsData, themeFolderName)
                : Path.Combine(cursorsData, groupName, themeFolderName);

            string currentDir = Path.GetFullPath(item.FolderPath.TrimEnd('\\', '/'));
            string targetFull = Path.GetFullPath(targetDir);

            if (currentDir.Equals(targetFull, StringComparison.OrdinalIgnoreCase))
            {
                newPath = currentDir;
                return true;
            }

            try
            {
                if (Directory.Exists(targetFull))
                {
                    var overwrite = ConfirmDialog.Show(this, new ConfirmDialogOptions
                    {
                        Title = "確認覆蓋",
                        Headline = $"目標位置已有同名資料夾「{themeFolderName}」。",
                        Message = "要覆蓋嗎？",
                        Buttons = ConfirmDialogButtons.YesNo,
                        Kind = ConfirmDialogKind.Question
                    });
                    if (overwrite != ConfirmDialogResult.Yes)
                        return false;
                    Directory.Delete(targetFull, true);
                }

                if (!string.IsNullOrEmpty(groupName))
                    Directory.CreateDirectory(Path.Combine(cursorsData, groupName));

                bool isOutsideLibrary = !currentDir.StartsWith(Path.GetFullPath(cursorsData), StringComparison.OrdinalIgnoreCase);
                if (item.IsTemporary || isOutsideLibrary)
                {
                    CopyDirectory(item.FolderPath, targetFull);
                    if (item.IsTemporary)
                        ForgetTemporaryTheme(item.FolderPath);
                }
                else
                {
                    Directory.Move(currentDir, targetFull);
                }

                string oldPath = item.FolderPath;
                newPath = targetFull;
                ThemeMetadataStore.RenamePath(oldPath, newPath);

                if (string.Equals(_appliedFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
                    _appliedFolderPath = newPath;
                if (string.Equals(_currentLoadedFolder, oldPath, StringComparison.OrdinalIgnoreCase))
                    _currentLoadedFolder = newPath;

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private CharacterThemeItem? GetThemeItemFromMenuSender(object sender)
        {
            if (sender is MenuItem { Parent: ContextMenu contextMenu })
            {
                if (contextMenu.PlacementTarget is TreeViewItem { DataContext: CharacterThemeItem item })
                    return item;
                if (contextMenu.DataContext is CharacterThemeItem ctxItem)
                    return ctxItem;
            }

            return TrvThemes.SelectedItem as CharacterThemeItem;
        }

        private void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = ConfirmDialog.Show(this, new ConfirmDialogOptions
            {
                Title = "還原確認",
                Headline = "確定要將鼠標還原為預設樣式嗎？",
                Buttons = ConfirmDialogButtons.YesNo,
                Kind = ConfirmDialogKind.Question
            });
            if (result == ConfirmDialogResult.Yes)
            {
                bool ok = CursorInstaller.RestoreDefaultCursors();
                if (ok)
                {
                    _appliedFolderPath = string.Empty;
                    _appliedThemeName = string.Empty;
                    PersistAppSettings();
                    RefreshInUseBadges();
                    SetStatus("🔄", "已成功還原為預設鼠標", StatusTone.Info);
                    ConfirmDialog.Alert(this, "還原成功", "已成功還原為預設鼠標",
                        $"鼠標大小設定仍保留為 {MousePointerSizeHelper.GetPxLabel(_cursorSizePx)}。",
                        ConfirmDialogKind.Success);
                }
                else
                {
                    SetStatus("❌", "還原失敗。", StatusTone.Error);
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
                    ConfirmDialog.Alert(this, "錯誤", "無法開啟 Windows 設定：" + ex.Message, kind: ConfirmDialogKind.Error);
                }
            }
        }

        private void MenuRenameTheme_Click(object sender, RoutedEventArgs e)
        {
            if (GetThemeItemFromMenuSender(sender) is not CharacterThemeItem item)
                return;

            if (item.IsTemporary)
            {
                var tempDialog = new RenameDialog(item.Name) { Owner = this };
                if (tempDialog.ShowDialog() == true)
                {
                    string newName = tempDialog.NewName;
                    if (string.IsNullOrWhiteSpace(newName) || string.Equals(item.Name, newName, StringComparison.Ordinal))
                        return;

                    item.Name = newName;
                    if (string.Equals(_currentLoadedFolder, item.FolderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentThemeName = newName;
                        TxtCurrentThemeTitle.Text = newName;
                    }
                    FilterThemes();
                    SetStatus("✏️", $"已將顯示名稱改為「{newName}」（原資料夾未更動）", StatusTone.Success);
                }
                return;
            }

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
                        ConfirmDialog.Alert(this, "提示", "已存在相同名稱的資料夾，請使用其他名稱！", kind: ConfirmDialogKind.Warning);
                        return;
                    }

                    Directory.Move(oldPath, newPath);

                    if (string.Equals(_appliedFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
                        _appliedFolderPath = newPath;

                    ThemeMetadataStore.RenamePath(oldPath, newPath);
                    ReloadThemes(newPath);
                    LoadFolder(newPath, newName);

                    SetStatus("✏️", $"已成功重新命名為「{newName}」！", StatusTone.Success);
                }
                catch (Exception ex)
                {
                    ConfirmDialog.Alert(this, "錯誤", $"重新命名失敗：{ex.Message}", kind: ConfirmDialogKind.Error);
                }
            }
        }

        private void MenuSaveTemporaryTheme_Click(object sender, RoutedEventArgs e)
        {
            if (GetThemeItemFromMenuSender(sender) is not CharacterThemeItem item)
                return;

            if (!item.IsTemporary)
            {
                ConfirmDialog.Alert(this, "提示", "此主題已在鼠標庫中，無需再存入。");
                return;
            }

            string cursorsData = EnsureCursorsDataFolderInteractive(this);
            string folderName = Path.GetFileName(item.FolderPath.TrimEnd('\\', '/'));
            string targetDir = Path.Combine(cursorsData, folderName);

            try
            {
                if (Directory.Exists(targetDir))
                {
                    var overwrite = ConfirmDialog.Show(this, new ConfirmDialogOptions
                    {
                        Title = "確認覆蓋",
                        Headline = $"鼠標庫中已有同名資料夾「{folderName}」。",
                        Message = "要覆蓋嗎？",
                        Buttons = ConfirmDialogButtons.YesNo,
                        Kind = ConfirmDialogKind.Question
                    });
                    if (overwrite != ConfirmDialogResult.Yes)
                        return;
                }

                CopyDirectory(item.FolderPath, targetDir);
                string oldPath = item.FolderPath;
                ForgetTemporaryTheme(oldPath);
                if (string.Equals(_appliedFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
                    _appliedFolderPath = targetDir;
                ReloadThemes(targetDir);
                LoadFolder(targetDir, item.Name);
                SetStatus("📦", $"已將「{item.Name}」存入鼠標庫！", StatusTone.Success);
            }
            catch (Exception ex)
            {
                ConfirmDialog.Alert(this, "錯誤", $"存入鼠標庫失敗：{ex.Message}", kind: ConfirmDialogKind.Error);
            }
        }

        private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (GetThemeItemFromMenuSender(sender) is CharacterThemeItem item && Directory.Exists(item.FolderPath))
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
                    ConfirmDialog.Alert(this, "錯誤", "無法開啟資料夾：" + ex.Message, kind: ConfirmDialogKind.Error);
                }
            }
        }

        private void MenuDeleteTheme_Click(object sender, RoutedEventArgs e)
        {
            if (GetThemeItemFromMenuSender(sender) is not CharacterThemeItem item)
                return;

            DeleteThemeItems(new[] { item });
        }

        private void DeleteThemeItems(IReadOnlyList<CharacterThemeItem> items)
        {
            if (items.Count == 0)
                return;

            if (items.Count == 1)
            {
                DeleteSingleThemeItem(items[0]);
                return;
            }

            int deleted = 0;
            var failures = new List<string>();
            string? keepCurrent = items.Any(t => string.Equals(_currentLoadedFolder, t.FolderPath, StringComparison.OrdinalIgnoreCase))
                ? null
                : _currentLoadedFolder;

            foreach (var item in items)
            {
                if (TryDeleteThemeItem(item, out string? error))
                    deleted++;
                else if (!string.IsNullOrEmpty(error))
                    failures.Add($"{item.Name}: {error}");
            }

            ReloadThemes(keepCurrent);

            if (keepCurrent == null)
                RestoreEmptyThemeViewIfNeeded();

            if (failures.Count > 0)
            {
                ConfirmDialog.Alert(this, "部分刪除失敗",
                    string.Join(Environment.NewLine, failures.Take(5)) +
                    (failures.Count > 5 ? Environment.NewLine + "..." : string.Empty),
                    kind: ConfirmDialogKind.Warning);
            }

            SetStatus("🗑️", failures.Count == 0
                ? $"已成功刪除 {deleted} 個主題"
                : $"已刪除 {deleted} 個主題，{failures.Count} 個失敗",
                failures.Count == 0 ? StatusTone.Success : StatusTone.Error);
        }

        private void DeleteSingleThemeItem(CharacterThemeItem item)
        {
            if (item.IsTemporary)
            {
                var removeAsk = ConfirmDialog.Show(this, new ConfirmDialogOptions
                {
                    Title = "從列表移除",
                    Headline = $"要從列表移除「{item.Name}」嗎？",
                    Message = "這只是未存入庫的項目，不會刪除原始資料夾。",
                    PathLabel = "原始資料夾",
                    PathHighlight = item.FolderPath,
                    Buttons = ConfirmDialogButtons.YesNo,
                    Kind = ConfirmDialogKind.Question
                });

                if (removeAsk != ConfirmDialogResult.Yes)
                    return;
            }
            else
            {
                var result = ConfirmDialog.Show(this, new ConfirmDialogOptions
                {
                    Title = "刪除主題確認",
                    Headline = $"確定要永久刪除主題「{item.Name}」嗎？",
                    Message = "這將會移除該鼠標資料夾。",
                    PathLabel = "將刪除",
                    PathHighlight = item.FolderPath,
                    Buttons = ConfirmDialogButtons.YesNo,
                    Kind = ConfirmDialogKind.Warning
                });

                if (result != ConfirmDialogResult.Yes)
                    return;
            }

            if (!TryDeleteThemeItem(item, out string? error))
            {
                if (!string.IsNullOrEmpty(error))
                    ConfirmDialog.Alert(this, "錯誤", error, kind: ConfirmDialogKind.Error);
                return;
            }

            string? keepCurrent = string.Equals(_currentLoadedFolder, item.FolderPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : _currentLoadedFolder;
            ReloadThemes(keepCurrent);

            if (keepCurrent == null)
                RestoreEmptyThemeViewIfNeeded();

            SetStatus("🗑️", item.IsTemporary
                ? $"已從列表移除「{item.Name}」"
                : $"已成功刪除「{item.Name}」！",
                item.IsTemporary ? StatusTone.Warning : StatusTone.Success);
        }

        private bool TryDeleteThemeItem(CharacterThemeItem item, out string? errorMessage)
        {
            errorMessage = null;

            try
            {
                if (item.IsTemporary)
                {
                    ForgetTemporaryTheme(item.FolderPath);
                    if (string.Equals(_appliedFolderPath, item.FolderPath, StringComparison.OrdinalIgnoreCase))
                        _appliedFolderPath = string.Empty;
                    return true;
                }

                if (Directory.Exists(item.FolderPath))
                    Directory.Delete(item.FolderPath, true);

                if (string.Equals(_appliedFolderPath, item.FolderPath, StringComparison.OrdinalIgnoreCase))
                    _appliedFolderPath = string.Empty;

                ThemeMetadataStore.RemovePath(item.FolderPath);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"刪除失敗：{ex.Message}";
                return false;
            }
        }

        private void RestoreEmptyThemeViewIfNeeded()
        {
            if (_allThemes.Count > 0)
            {
                SelectFirstThemeIfAny();
                return;
            }

            _currentLoadedFolder = string.Empty;
            _currentThemeName = "未選擇任何主題";
            TxtCurrentThemeTitle.Text = _currentThemeName;
            TxtCurrentFolderPath.Text = "請從左側選擇或將資料夾拖曳進來";
            var slots = CursorMatcher.CreateDefaultSlots();
            _currentSlots.Clear();
            foreach (var s in slots)
                _currentSlots.Add(s);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            string currentPath = GetCursorsDataFolder();
            var saved = LoadAppSettings();

            var dialog = new SettingsDialog(currentPath, saved.AppTheme, saved.BgMode, saved.UiScale, saved.CursorSizePx, saved.CursorScaleMode)
            {
                Owner = this
            };

            dialog.PreviewChanged += (theme, bg, scale) =>
            {
                ApplyAppTheme(theme);
                ApplyPreviewBackground(bg, theme);
                ApplyUiScale(scale);
            };

            if (dialog.ShowDialog() == true)
            {
                string newPath = dialog.SelectedPath;
                string newTheme = dialog.SelectedAppTheme;
                string newBg = dialog.SelectedBgMode;
                string newScale = dialog.SelectedUiScale;
                int newSize = dialog.SelectedCursorSizePx;
                string newMode = dialog.SelectedCursorScaleMode;

                bool sizeOrModeChanged = newSize != _cursorSizePx
                    || !string.Equals(newMode, _cursorScaleMode, StringComparison.OrdinalIgnoreCase);

                _currentAppTheme = newTheme;
                _currentBgMode = newBg;
                _currentUiScale = newScale;
                _cursorSizePx = newSize;
                _cursorScaleMode = newMode;

                SaveAppSettings(newPath, newTheme, newBg, newScale, newSize, newMode, _appliedFolderPath, _appliedThemeName,
                    _skippedUpdateVersion);
                ApplyAppTheme(newTheme);
                ApplyPreviewBackground(newBg, newTheme);
                ApplyUiScale(newScale);
                SyncPointerSizeUi();

                if (sizeOrModeChanged && !string.IsNullOrEmpty(_appliedFolderPath) && Directory.Exists(_appliedFolderPath))
                    ReapplyStoredTheme(silent: true);

                bool pathChanged = !string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase);
                if (pathChanged)
                {
                    ReloadThemes();
                    SetStatus("⚙️", $"設定已儲存，資料夾位置更新為：{newPath}", StatusTone.Success);
                    string themeDisplay = newTheme == "Light" ? "淺色" : (newTheme == "Dark" ? "深色" : "跟隨系統");
                    string bgDisplay = newBg switch
                    {
                        "Light" => "淺色",
                        "Dark" => "深色",
                        "Transparent" => "透明",
                        _ => "跟隨主題"
                    };
                    ConfirmDialog.Alert(this, "設定已儲存", "設定已更新！",
                        pathLabel: "資料夾位置",
                        pathHighlight: newPath,
                        message: $"介面主題：{themeDisplay}\n預覽背景：{bgDisplay}\n鼠標大小：{MousePointerSizeHelper.GetPxLabel(newSize)}",
                        kind: ConfirmDialogKind.Success);
                }
                else
                {
                    SetStatus("⚙️", "設定已儲存！", StatusTone.Success);
                }
            }
            else
            {
                ApplyAppTheme(saved.AppTheme);
                ApplyPreviewBackground(saved.BgMode, saved.AppTheme);
                ApplyUiScale(saved.UiScale);
            }
        }

        private void BtnCaptureCurrentSystem_Click(object sender, RoutedEventArgs e)
        {
            _schemePromptDismissed = true;
            ImportCurrentSystemCursors("擷取的自訂鼠標");
        }

        private enum StatusTone
        {
            Normal,
            Success,
            Info,
            Warning,
            Error
        }

        private void SetStatus(string icon, string msg, StatusTone tone = StatusTone.Normal)
        {
            TxtStatusMessage.Text = string.IsNullOrWhiteSpace(icon) ? msg : $"{icon} {msg}";
            string brushKey = tone switch
            {
                StatusTone.Success => "StatusSuccessBrush",
                StatusTone.Info => "StatusInfoBrush",
                StatusTone.Warning => "StatusWarningBrush",
                StatusTone.Error => "StatusErrorBrush",
                _ => "TextSecondaryBrush"
            };
            TxtStatusMessage.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
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

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            SetStatus("🔄", "正在檢查雲端最新版本...", StatusTone.Info);

            var settings = LoadAppSettings();
            var update = await Task.Run(() => UpdateChecker.CheckForUpdatesAsync(settings.SkippedUpdateVersion));
            BtnCheckUpdate.IsEnabled = true;

            if (update.HasUpdate)
            {
                ShowPendingUpdateBadge(update);
                SetStatus("🚀", $"發現新版本 {update.LatestVersion}！可點擊右側按鈕開始下載更新。", StatusTone.Error);
                ShowUpdateDialog(update);
            }
            else
            {
                SetStatus("✨", $"目前已是最新版本 ({update.CurrentVersion})！", StatusTone.Success);
                ConfirmDialog.Alert(this, "檢查更新",
                    $"目前運行的已是最新版本 ({update.CurrentVersion})",
                    "無需更新！",
                    ConfirmDialogKind.Success);
            }
        }

        private void BtnNewUpdateFound_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate != null)
                ShowUpdateDialog(_pendingUpdate);
            else
                UpdateChecker.OpenReleasePage();
        }
    }
}
