using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CursorManager
{
    public enum ConfirmDialogResult
    {
        None,
        Yes,
        No,
        Cancel
    }

    public enum ConfirmDialogButtons
    {
        YesNo,
        YesNoCancel,
        Ok
    }

    public enum ConfirmDialogKind
    {
        Question,
        Information,
        Success,
        Warning,
        Error
    }

    public sealed class ConfirmDialogOptions
    {
        public string Title { get; set; } = "確認";
        public string Headline { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? PathLabel { get; set; }
        public string? PathHighlight { get; set; }
        public IEnumerable<string>? BulletPoints { get; set; }
        public string? FooterNote { get; set; }
        public ConfirmDialogButtons Buttons { get; set; } = ConfirmDialogButtons.YesNoCancel;
        public ConfirmDialogKind Kind { get; set; } = ConfirmDialogKind.Question;
        public string OkText { get; set; } = "確定";
        public string YesText { get; set; } = "是";
        public string NoText { get; set; } = "否";
        public string CancelText { get; set; } = "取消";
    }

    public partial class ConfirmDialog : ThemedDialogWindow
    {
        public ConfirmDialogResult Result { get; private set; } = ConfirmDialogResult.Cancel;
        private readonly ConfirmDialogButtons _buttons;

        private ConfirmDialog(ConfirmDialogOptions options)
        {
            InitializeComponent();

            _buttons = options.Buttons;
            Title = options.Title;
            TxtHeadline.Text = options.Headline;
            ApplyKind(options.Kind);

            if (string.IsNullOrWhiteSpace(options.Message))
                TxtMessage.Visibility = Visibility.Collapsed;
            else
                TxtMessage.Text = options.Message;

            if (string.IsNullOrWhiteSpace(options.PathHighlight))
            {
                PathPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtPathLabel.Text = string.IsNullOrWhiteSpace(options.PathLabel)
                    ? "路徑"
                    : options.PathLabel;
                TxtPathValue.Text = options.PathHighlight;
            }

            var bullets = options.BulletPoints?.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
            if (bullets == null || bullets.Count == 0)
            {
                BulletList.Visibility = Visibility.Collapsed;
            }
            else
            {
                BulletList.ItemsSource = bullets;
            }

            if (string.IsNullOrWhiteSpace(options.FooterNote))
            {
                TxtFooterNote.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtFooterNote.Text = options.FooterNote;
            }

            ConfigureButtons(options);
            PreviewKeyDown += ConfirmDialog_PreviewKeyDown;
        }

        private void ConfigureButtons(ConfirmDialogOptions options)
        {
            BtnYes.Content = options.Buttons == ConfirmDialogButtons.Ok ? options.OkText : options.YesText;
            BtnNo.Content = options.NoText;
            BtnCancel.Content = options.CancelText;

            if (options.Buttons == ConfirmDialogButtons.Ok)
            {
                BtnNo.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Collapsed;
                BtnYes.IsDefault = true;
                BtnYes.IsCancel = true;
                BtnYes.Margin = new Thickness(0);
            }
            else
            {
                BtnNo.Visibility = Visibility.Visible;
                BtnCancel.Visibility = options.Buttons == ConfirmDialogButtons.YesNoCancel
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                BtnYes.IsDefault = true;
                BtnYes.IsCancel = false;
                BtnCancel.IsCancel = true;
            }
        }

        private void ApplyKind(ConfirmDialogKind kind)
        {
            string glyph;
            string foreground;
            string background;
            string border;

            switch (kind)
            {
                case ConfirmDialogKind.Success:
                    glyph = "✓";
                    foreground = "#A6E3A1";
                    background = "#A6E3A122";
                    border = "#A6E3A155";
                    break;
                case ConfirmDialogKind.Warning:
                    glyph = "!";
                    foreground = "#F9E2AF";
                    background = "#F9E2AF22";
                    border = "#F9E2AF55";
                    break;
                case ConfirmDialogKind.Error:
                    glyph = "!";
                    foreground = "#F38BA8";
                    background = "#F38BA822";
                    border = "#F38BA855";
                    break;
                case ConfirmDialogKind.Information:
                    glyph = "i";
                    foreground = "#89B4FA";
                    background = "#89B4FA22";
                    border = "#89B4FA55";
                    break;
                default:
                    glyph = "?";
                    foreground = "#89B4FA";
                    background = "#1E66F522";
                    border = "#7287FD55";
                    break;
            }

            IconGlyph.Text = glyph;
            IconGlyph.Foreground = BrushFrom(foreground);
            IconBorder.Background = BrushFrom(background);
            IconBorder.BorderBrush = BrushFrom(border);
        }

        private static SolidColorBrush BrushFrom(string hex) =>
            (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        public static ConfirmDialogResult Show(Window? owner, ConfirmDialogOptions options)
        {
            var dialog = new ConfirmDialog(options);
            dialog.AttachToOwner(owner);
            dialog.ShowDialog();
            return dialog.Result;
        }

        /// <summary>Theme-matched OK alert (replaces MessageBox OK).</summary>
        public static void Alert(
            Window? owner,
            string title,
            string headline,
            string? message = null,
            ConfirmDialogKind kind = ConfirmDialogKind.Information,
            string? pathLabel = null,
            string? pathHighlight = null)
        {
            Show(owner, new ConfirmDialogOptions
            {
                Title = title,
                Headline = headline,
                Message = message ?? string.Empty,
                PathLabel = pathLabel,
                PathHighlight = pathHighlight,
                Buttons = ConfirmDialogButtons.Ok,
                Kind = kind
            });
        }

        private void ConfirmDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_buttons == ConfirmDialogButtons.Ok)
                return;

            if (e.Key == Key.Y)
            {
                SetResult(ConfirmDialogResult.Yes);
                e.Handled = true;
            }
            else if (e.Key == Key.N)
            {
                SetResult(ConfirmDialogResult.No);
                e.Handled = true;
            }
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e) => SetResult(ConfirmDialogResult.Yes);

        private void BtnNo_Click(object sender, RoutedEventArgs e) => SetResult(ConfirmDialogResult.No);

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => SetResult(ConfirmDialogResult.Cancel);

        private void SetResult(ConfirmDialogResult result)
        {
            Result = result;
            // Avoid throwing if already closed; Yes/No both close the modal.
            try { DialogResult = result != ConfirmDialogResult.Cancel; }
            catch { }
            try { Close(); }
            catch { }
        }
    }
}
