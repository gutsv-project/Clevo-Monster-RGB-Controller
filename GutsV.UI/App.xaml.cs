using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Reflection;
using System.IO;
using GutsV.Core.Settings;

namespace GutsV.UI;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    public static bool IsExplicitExit { get; set; } = false;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows is shutting down/logging off
        IsExplicitExit = true; // Allow OnClosing to proceed and save settings
        _notifyIcon?.Dispose();
        
        // We can manually call ShutdownCommand here if needed, but usually OnClosing follows.
        // However, setting IsExplicitExit ensures OnClosing used in MainWindow won't cancel it.
        base.OnSessionEnding(e);
    }

    private static System.Threading.Mutex? _mutex = null;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // FIX: Ensure working directory is the app folder (for DllImport finding InsydeDCHU.dll)
        // Task Scheduler defaults to System32, causing "Device Not Found".
        System.IO.Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);

        const string appName = "GutsV_SingleInstance_Mutex";
        bool createdNew;
        _mutex = new System.Threading.Mutex(true, appName, out createdNew);

        if (!createdNew)
        {
            // App is already running!
            // Ideally we should bring it to front, but simple exit is safer for now
            // or maybe show a message?
            // Let's just exit silently as per standard tray app behavior or show info if manual start?
            // If manual start, let user know.
            if (e.Args.Length == 0 || e.Args[0] != "--tray")
            {
                 System.Windows.MessageBox.Show("GutsV is already running! Check your system tray (near clock).", "GutsV");
            }
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        // Prevent auto-shutdown
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 1. Setup Tray Icon IMMEDIATELY for feedback
        SetupTrayIcon();
        _notifyIcon!.Text = "GutsV: Starting...";

        // 2. Determine Mode
        bool startInTray = false;
        if (e.Args.Length > 0 && e.Args[0] == "--tray")
        {
            startInTray = true;
        }

        // 3. Process Check (FnKey.exe)
        // This handles Waiting logic and UI Feedback
        await WaitForControlCenter(startInTray);
        
        if (IsExplicitExit) return; // Exit happened during check

        // 4. Auto-Start Logic (Ask user if not present AND manual start)
        if (!startInTray)
        {
            CheckAndAskStartup();
        }

        // 5. Launch Window
        var mainWindow = new MainWindow();
        this.MainWindow = mainWindow; 
        
        if (startInTray)
        {
            // Already hidden, just ensure window exists
            _notifyIcon.Text = "GutsV Colour (Running)";
        }
        else
        {
            mainWindow.Show();
            _notifyIcon.Text = "GutsV Colour";
        }

        // Revert to normal shutdown mode
        this.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private void CheckAndAskStartup()
    {
        try
        {
            // Simple check using registry key marker or assuming if user never set it up
            // actually we can check if task exists via schtasks but that parses output.
            // Let's rely on our settings file marker or just ask if not configured.
            // Better: Check settings.
            bool isConfigured = SettingsManager.Load().IsStartupEnabled;

            if (!isConfigured)
            {
                // Custom Dark Theme Dialog
                bool enable = ModernMessageBox.Show(
                    "Enable Service Mode (Auto-Start)?\n\nThis ensures your keyboard lights are set automatically every time your computer starts.",
                    "SETUP REQUIRED");

                if (enable)
                {
                    RegisterStartup();
                    // Save marker
                    var settings = SettingsManager.Load();
                    settings.IsStartupEnabled = true;
                    SettingsManager.Save(settings);
                }
            }
        }
        catch { }
    }

    public static void RemoveDataAndStartup()
    {
        try
        {
            // 1. Remove Scheduled Task
            Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = "/delete /tn GutsV /f",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();

            // 1.5 Legacy Registry Cleanup (Just in case)
            try { 
                Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)?.DeleteValue("GutsV", false); 
            } catch {}

            // 2. Remove Settings File
            try 
            {
                string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GutsV");
                if (Directory.Exists(settingsPath))
                {
                    Directory.Delete(settingsPath, true);
                }
            }
            catch { }

            ModernMessageBox.ShowInfo("Service Mode removed and settings cleared.\nYou will be prompted to setup again on next launch.", "CLEANUP COMPLETE");
        }
        catch (Exception ex)
        {
             ModernMessageBox.ShowInfo($"Error during cleanup: {ex.Message}", "ERROR");
        }
    }

    private async Task WaitForControlCenter(bool isAutoStart)
    {
        const string TargetProcess = "FnKey";
        int attempts = 0;
        
        if (isAutoStart)
        {
            // AUTO START: Be patient, wait forever, keep user informed via Tray
            // SetupTrayIcon() called in OnStartup already
            _notifyIcon!.Text = "GutsV: Waiting for FnKey... (Auto)";
            
            while (true)
            {
                 var processes = Process.GetProcessesByName(TargetProcess);
                 if (processes.Length > 0)
                 {
                     _notifyIcon.Text = "FnKey Found! Initializing (12s)...";
                     await Task.Delay(12000); // 12s Buffer
                     _notifyIcon.Text = "GutsV Colour";
                     return;
                 }
                 await Task.Delay(1000);
            }
        }
        else
        {
            // MANUAL START: Check quickly, error if missing
            _notifyIcon!.Text = "GutsV: Checking System (8s)...";
            
            while (attempts < 10) // 5 seconds wait (checks every 500ms * 10 = 5s in old logic, but logic changed)
            {
                 var processes = Process.GetProcessesByName(TargetProcess);
                 if (processes.Length > 0)
                 {
                     _notifyIcon.Text = "FnKey Found! Starting...";
                     // Even if found manually, wait a bit for it to be ready
                     await Task.Delay(8000); 
                     return;
                 }
                 await Task.Delay(500);
                 attempts++;
            }
            
            // Fatal Error
            ModernMessageBox.ShowInfo("Control Center (FnKey.exe) is NOT running.\n\nPlease start Control Center first.", "FATAL ERROR");
            Shutdown();
            IsExplicitExit = true; // Prevent further logic
        }
    }

    private void RegisterStartup()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                 // Create Scheduled Task to bypass UAC at logon
                 // /rl HIGHEST = Run with highest privileges (Admin)
                 // /sc onlogon = Run at login
                 // /tr ... = Path with arguments
                 string cmd = $"/create /tn GutsV /tr \"'\\\"{exePath}\\\"' --tray\" /sc onlogon /rl HIGHEST /f";
                 
                 Process.Start(new ProcessStartInfo
                 {
                     FileName = "schtasks",
                     Arguments = cmd,
                     UseShellExecute = true, // To show admin prompt if needed (though app is already admin)
                     CreateNoWindow = true
                 })?.WaitForExit();
            }
        }
        catch { }
    }

    private void SetupTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon();
        try
        {
             // Load from WPF Resource safely
             var uri = new System.Uri("pack://application:,,,/icon.png");
             var stream = System.Windows.Application.GetResourceStream(uri).Stream;
             var bitmap = new System.Drawing.Bitmap(stream);
             _notifyIcon.Icon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
             // Fallback
             _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        _notifyIcon.Visible = true;
        _notifyIcon.Text = "GutsV Colour";
        
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Renderer = new DarkRenderer(); // Custom Cyberpunk Theme
        contextMenu.BackColor = System.Drawing.Color.FromArgb(20, 20, 20);
        contextMenu.ForeColor = System.Drawing.Color.White;
        contextMenu.ShowImageMargin = true; // Enable icons
        
        // Items
        var itemOpen = new System.Windows.Forms.ToolStripMenuItem("Open GutsV Colour");
        itemOpen.Click += (s, e) => ShowWindow();
        // Use the app icon if available
        try 
        {
             var uri = new System.Uri("pack://application:,,,/icon.png");
             var stream = System.Windows.Application.GetResourceStream(uri).Stream;
             var originalBmp = new System.Drawing.Bitmap(stream);
             itemOpen.Image = new System.Drawing.Bitmap(originalBmp, new System.Drawing.Size(16, 16));
        } catch {}

        var itemExit = new System.Windows.Forms.ToolStripMenuItem("Exit");
        itemExit.Click += (s, e) => ExitApp();
        // Create simple red exit icon
        var exitBmp = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(exitBmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 68, 68)))
            {
               g.FillEllipse(brush, 2, 2, 12, 12);
            }
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2))
            {
                g.DrawLine(pen, 5, 8, 11, 8); // Minus sign or power line? Let's do simple power dot.
            }
        }
        itemExit.Image = exitBmp;

        contextMenu.Items.Add(itemOpen);
        contextMenu.Items.Add("-");
        contextMenu.Items.Add(itemExit);
        
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowWindow();
    }

    // Custom Renderer for Cyberpunk Look
    private class DarkRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { }
    }

    private class DarkColors : System.Windows.Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(0, 212, 255); // Neon Blue
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(0, 212, 255);
        public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(30, 30, 30);
        public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(30, 30, 30);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(60, 60, 60);
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(20, 20, 20);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(20, 20, 20);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(20, 20, 20);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(20, 20, 20);
    }

    private void ShowWindow()
    {
        if (MainWindow != null)
        {
            if (MainWindow.Visibility == Visibility.Hidden || MainWindow.Visibility == Visibility.Collapsed)
            {
                MainWindow.Show();
            }
            
            if (MainWindow.WindowState == WindowState.Minimized)
            {
                MainWindow.WindowState = WindowState.Normal;
            }
            
            MainWindow.Activate();
        }
    }

    private void ExitApp()
    {
        IsExplicitExit = true;
        _notifyIcon?.Dispose(); // Identify clean cleanup
        Shutdown();
    }



    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show($"FATAL ERROR: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}", 
                        "GutsV Crash Report", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
        e.Handled = true;
        
        // Try to verify if we can recover or should shutdown?
        // Usually crash means shutdown
        ExitApp();
    }
}
