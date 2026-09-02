using System.Windows;

namespace CursorManager
{
    public partial class TextInputDialog : Window
    {
        public string Value { get; private set; } = string.Empty;

        public TextInputDialog(string title, string prompt, string initialValue = "", bool allowEmpty = false)
        {
            InitializeComponent();
            Title = title;
            TxtPrompt.Text = prompt;
            TxtValue.Text = initialValue;
            Tag = allowEmpty;
            Loaded += (_, _) => TxtValue.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtValue.Text.Trim();
            if (string.IsNullOrWhiteSpace(text) && Tag is not true)
            {
                ConfirmDialog.Alert(this, "提示", "內容不可為空！", kind: ConfirmDialogKind.Warning);
                return;
            }

            Value = text;
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
