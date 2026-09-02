using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CursorManager
{
    public enum UpdateDialogResult
    {
        Closed,
        Skipped,
        Installed
    }

    public partial class UpdateDialog : ThemedDialogWindow
    {
        private readonly UpdateInfo _update;
        private readonly Action<string>? _onSkipVersion;
        private CancellationTokenSource? _downloadCts;
        private UpdateDialogState _state = UpdateDialogState.ReadyToDownload;

        public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.Closed;

        private enum UpdateDialogState
        {
            ReadyToDownload,
            Downloading,
            ReadyToInstall
        }

        public UpdateDialog(UpdateInfo update, Action<string>? onSkipVersion = null)
        {
            _update = update;
            _onSkipVersion = onSkipVersion;
            InitializeComponent();

            TxtHeadline.Text = $"發現新版本：{_update.LatestVersion}";
            TxtCurrentVersion.Text = $"目前版本：{_update.CurrentVersion}";
            TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(_update.ReleaseNotes)
                ? "（此版本未提供更新說明）"
                : _update.ReleaseNotes;

            if (UpdateDownloader.HasCachedDownload(_update.LatestVersion))
                SetReadyToInstallState();
            else if (!_update.CanAutoDownload)
                SetManualOnlyState();
            else
                SetReadyToDownloadState();

            Closed += (_, _) => _downloadCts?.Cancel();
        }

        public static UpdateDialogResult Show(Window? owner, UpdateInfo update, Action<string>? onSkipVersion = null)
        {
            var dialog = new UpdateDialog(update, onSkipVersion);
            dialog.AttachToOwner(owner);
            dialog.ShowDialog();
            return dialog.Result;
        }

        private void SetReadyToDownloadState()
        {
            _state = UpdateDialogState.ReadyToDownload;
            BtnPrimary.Content = "⬇ 下載更新";
            BtnPrimary.IsEnabled = _update.CanAutoDownload;
            BtnLater.IsEnabled = true;
            BtnManualDownload.IsEnabled = true;
            ProgressDownload.Visibility = Visibility.Collapsed;
            TxtProgress.Visibility = Visibility.Collapsed;
            TxtFooterNote.Text = _update.CanAutoDownload
                ? "下載完成後，您可選擇立即安裝並重啟程式。"
                : "此版本 Release 未附帶 exe 檔，請使用「手動下載」。";
        }

        private void SetManualOnlyState()
        {
            _state = UpdateDialogState.ReadyToDownload;
            BtnPrimary.Content = "⬇ 下載更新";
            BtnPrimary.IsEnabled = false;
            BtnLater.IsEnabled = true;
            BtnManualDownload.IsEnabled = true;
            ProgressDownload.Visibility = Visibility.Collapsed;
            TxtProgress.Visibility = Visibility.Collapsed;
            TxtFooterNote.Text = "GitHub Release 未找到 CursorManager.exe，請改用手動下載。";
        }

        private void SetDownloadingState()
        {
            _state = UpdateDialogState.Downloading;
            BtnPrimary.Content = "下載中…";
            BtnPrimary.IsEnabled = false;
            BtnLater.IsEnabled = false;
            BtnManualDownload.IsEnabled = false;
            ProgressDownload.Visibility = Visibility.Visible;
            ProgressDownload.Value = 0;
            TxtProgress.Visibility = Visibility.Visible;
            TxtProgress.Text = "正在下載更新…";
            TxtFooterNote.Text = "請保持網路連線，下載期間請勿關閉此視窗。";
        }

        private void SetReadyToInstallState()
        {
            _state = UpdateDialogState.ReadyToInstall;
            BtnPrimary.Content = "✨ 立即安裝並重啟";
            BtnPrimary.IsEnabled = true;
            BtnLater.IsEnabled = true;
            BtnManualDownload.IsEnabled = true;
            ProgressDownload.Visibility = Visibility.Visible;
            ProgressDownload.Value = 1;
            TxtProgress.Visibility = Visibility.Visible;
            TxtProgress.Text = "下載完成，可立即安裝。";
            TxtFooterNote.Text = UpdateInstaller.CanInstallToCurrentLocation()
                ? "安裝時程式會短暫關閉，完成後自動重新開啟。"
                : "目前程式位置無法自動覆寫，請使用「手動下載」後自行替換 exe。";

            if (!UpdateInstaller.CanInstallToCurrentLocation())
                BtnPrimary.IsEnabled = false;
        }

        private async void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            if (_state == UpdateDialogState.ReadyToInstall)
            {
                TryInstall();
                return;
            }

            if (_state != UpdateDialogState.ReadyToDownload || !_update.CanAutoDownload)
                return;

            SetDownloadingState();
            _downloadCts = new CancellationTokenSource();

            var progress = new Progress<double>(p =>
            {
                ProgressDownload.Value = Math.Clamp(p, 0, 1);
                if (p > 0 && p < 1)
                    TxtProgress.Text = $"正在下載… {Math.Round(p * 100)}%";
                else if (p >= 1)
                    TxtProgress.Text = "下載完成。";
            });

            try
            {
                await UpdateDownloader.DownloadAsync(_update, progress, _downloadCts.Token);

                SetReadyToInstallState();

                var ask = ConfirmDialog.Show(this, new ConfirmDialogOptions
                {
                    Title = "下載完成",
                    Headline = $"{_update.LatestVersion} 已下載完成",
                    Message = "是否立即安裝並重啟 CursorManager？",
                    FooterNote = "安裝期間程式會短暫關閉。",
                    Buttons = ConfirmDialogButtons.YesNo,
                    Kind = ConfirmDialogKind.Success,
                    YesText = "立即安裝",
                    NoText = "稍後"
                });

                if (ask == ConfirmDialogResult.Yes)
                    TryInstall();
            }
            catch (OperationCanceledException)
            {
                SetReadyToDownloadState();
            }
            catch (Exception ex)
            {
                SetReadyToDownloadState();
                ConfirmDialog.Alert(this, "下載失敗", ex.Message,
                    "請稍後重試，或改用手動下載。",
                    ConfirmDialogKind.Error);
            }
        }

        private void TryInstall()
        {
            if (!UpdateInstaller.CanInstallToCurrentLocation())
            {
                ConfirmDialog.Alert(this, "無法自動安裝",
                    "目前程式所在資料夾無法寫入。",
                    "請使用「手動下載」取得新版本後，手動覆蓋 exe。",
                    ConfirmDialogKind.Warning);
                return;
            }

            try
            {
                string path = UpdateDownloader.GetCachedExePath(_update.LatestVersion);
                Result = UpdateDialogResult.Installed;
                UpdateInstaller.InstallAndRestart(path);
            }
            catch (Exception ex)
            {
                ConfirmDialog.Alert(this, "安裝失敗", ex.Message,
                    "請改用手動下載覆蓋 exe。",
                    ConfirmDialogKind.Error);
            }
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.Skipped;
            _onSkipVersion?.Invoke(_update.LatestVersion);
            Close();
        }

        private void BtnManualDownload_Click(object sender, RoutedEventArgs e)
        {
            UpdateChecker.OpenReleasePage(_update.ReleaseUrl);
        }
    }
}
