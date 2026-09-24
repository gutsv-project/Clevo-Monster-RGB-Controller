using System.Windows;
using System.Windows.Input;

namespace GutsV.UI
{
    public partial class ModernMessageBox : Window
    {
        public bool Result { get; private set; } = false;

        public ModernMessageBox(string message, string title, bool showYesNo = true)
        {
            InitializeComponent();
            TitleText.Text = title.ToUpper();
            MessageText.Text = message;

            if (!showYesNo)
            {
                BtnYes.Content = "OK";
                BtnNo.Visibility = Visibility.Collapsed;
            }
        }

        public static bool Show(string message, string title = "NOTIFICATION")
        {
            var msg = new ModernMessageBox(message, title, true);
            msg.ShowDialog();
            return msg.Result;
        }

        public static void ShowInfo(string message, string title = "INFO")
        {
            var msg = new ModernMessageBox(message, title, false);
            msg.ShowDialog();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
