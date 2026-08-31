using System;
using System.Windows;

namespace HololiveCursorApp
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; } = string.Empty;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            TxtName.Text = currentName;
            TxtName.SelectAll();
            Loaded += (s, e) => TxtName.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("主題名稱不可為空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NewName = text;
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
