using System.Windows;
using System.Windows.Input;

namespace GutsV.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }

    private void OpenTelegramClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://t.me/acerhizm",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private void CleanClick(object sender, RoutedEventArgs e)
    {
        bool confirm = ModernMessageBox.Show(
            "Do you want to reset GutsV?\n\nThis will:\n1. Disable Auto-Start.\n2. Delete all saved color settings.\n\nOnly the GutsV.exe file will remain.",
            "RESET CONFIRMATION");
        
        if (confirm)
        {
            App.RemoveDataAndStartup();
            
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ResetState();
            }
        }
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsExplicitExit)
        {
            // Real exit: Save and Cleanup
            if (DataContext is ViewModels.MainViewModel vm)
            {
                await vm.ShutdownCommand.ExecuteAsync(null);
            }
            base.OnClosing(e);
        }
        else
        {
            // User X click -> Minimize to Tray
            e.Cancel = true;
            this.Hide();
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}