using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;
using GutsV.Drivers;

using GutsV.Core.Settings;

namespace GutsV.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private IKeyboardController? _driver;
    private AsyncDriverQueue? _queue;
    private GutsV.Core.Animations.AnimationEngine? _animationEngine;
    private AppSettings _currentSettings;

    // ... (Existing properties)
    [ObservableProperty]
    private string _statusText = "Initializing GutsV Engine...";

    [ObservableProperty]
    private bool _isReady = false;

    [ObservableProperty]
    private int _driverType = 0; // 0=Auto, 1=Insyde, 2=WMI

    // Bound Properties for Color Picker (0-255)
    [ObservableProperty] private byte _red = 0;
    [ObservableProperty] private byte _green = 212; // Default Neon Blue
    [ObservableProperty] private byte _blue = 255;

    [ObservableProperty]
    private string _hexColor = "#00D4FF";

    // Zone Selection Logic
    [ObservableProperty]
    private Zone _selectedZone = Zone.All;

    [ObservableProperty]
    private bool _isMultiZoneCapable = false;

    public System.Collections.Generic.List<Zone> AvailableZones => new() 
    { 
        Zone.All, 
        Zone.Left, 
        Zone.Center, 
        Zone.Right 
    };

    partial void OnRedChanged(byte value) => UpdateHexFromRgb();
    partial void OnGreenChanged(byte value) => UpdateHexFromRgb();
    partial void OnBlueChanged(byte value) => UpdateHexFromRgb();

    partial void OnHexColorChanged(string value)
    {
        if (value.Length == 7 && value.StartsWith("#"))
        {
            try 
            {
                var color = System.Drawing.ColorTranslator.FromHtml(value);
                // Set directly backing fields to avoid loop, or use a flag
#pragma warning disable MVVMTK0034
                _red = color.R;
                _green = color.G;
                _blue = color.B;
#pragma warning restore MVVMTK0034
                OnPropertyChanged(nameof(Red));
                OnPropertyChanged(nameof(Green));
                OnPropertyChanged(nameof(Blue));
                UpdateColorPreview();
            }
            catch { }
        }
    }

    [ObservableProperty]
    private bool _isAnimationRunning;
    
    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<GutsV.Core.Animations.IAnimation> _animations;

    [ObservableProperty]
    private GutsV.Core.Animations.IAnimation? _selectedAnimation;

    partial void OnSelectedAnimationChanged(GutsV.Core.Animations.IAnimation? value)
    {
        if (IsAnimationRunning && value != null)
        {
             // Auto-restart with new animation
             _animationEngine?.Start(value);
             StatusText = $"Running: {value.Name}";
        }
    }

    private void UpdateHexFromRgb()
    {
        if (IsAnimationRunning) return; // Block updates during anim
        HexColor = $"#{Red:X2}{Green:X2}{Blue:X2}";
        OnPropertyChanged(nameof(HexColor));
        UpdateColorPreview();
    }

    private void UpdateColorPreview()
    {
        OnPropertyChanged(nameof(CurrentColor));
        OnPropertyChanged(nameof(CurrentColorBrush));
        
        // While not animating, preview matches selection
        if (!IsAnimationRunning)
        {
             PreviewColor = CurrentColor;
        }
    }

    public System.Windows.Media.Color CurrentColor => System.Windows.Media.Color.FromRgb(Red, Green, Blue);
    public System.Windows.Media.SolidColorBrush CurrentColorBrush => new System.Windows.Media.SolidColorBrush(CurrentColor);
    [ObservableProperty]
    private double _brightness = 100;

    partial void OnBrightnessChanged(double value)
    {
        // Update Animation Engine Brightness (0.0f - 1.0f)
        if (_animationEngine != null)
        {
            _animationEngine.Brightness = (float)(value / 100.0);
        }
        
        // If static color, re-apply
        if (!IsAnimationRunning)
        {
            _ = ApplyColor();
        }
    }

    [ObservableProperty]
    private double _animationSpeed = 1.0;

    partial void OnAnimationSpeedChanged(double value)
    {
        if (_animationEngine != null)
        {
            _animationEngine.Speed = value;
        }
        SaveSettings();
    }

    public MainViewModel()
    {
        Animations = new System.Collections.ObjectModel.ObservableCollection<GutsV.Core.Animations.IAnimation>();
        
        // Load Settings
        _currentSettings = SettingsManager.Load();
        _red = _currentSettings.Red;
        _green = _currentSettings.Green;
        _blue = _currentSettings.Blue;
        _driverType = _currentSettings.DriverType;
        _brightness = _currentSettings.Brightness; // Load Brightness
        _animationSpeed = _currentSettings.AnimationSpeed;
        if (_animationSpeed < 0.1) _animationSpeed = 1.0; // Safety check
        
        UpdateHexFromRgb();

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await Task.Delay(500); // UI breathing room

        try
        {
            IKeyboardController? baseDriver = null;
            int retries = 0;
            const int MaxRetries = 5;

            while (retries < MaxRetries)
            {
                try
                {
                    StatusText = retries > 0 
                        ? $"Connection Retry {retries}/{MaxRetries}..." 
                        : "Scanning hardware...";
                        
                    baseDriver = await DeviceManager.CreateDriverAsync(DriverType);
                    
                    if (baseDriver != null) break; // Success
                }
                catch { } // Ignore transient errors

                retries++;
                await Task.Delay(3000); // Wait 3s before retry
            }

            if (baseDriver == null)
            {
                StatusText = "Device Initialization Failed (Timeout).";
                // Don't disable UI, just show error
                return;
            }
            StatusText = "Initializing Anti-Lag System...";
            // Wrap the raw driver in our Async Queue for performance
            _queue = new AsyncDriverQueue(baseDriver);
            await _queue.InitializeAsync();
            
            // Determine Capability based on Driver
            // Simple check: WMI driver usually implies Clevo 3-Zone
            // Insyde driver usually implies 1-Zone
            // We can check the type name of baseDriver
            if (baseDriver.GetType().Name.Contains("Wmi"))
            {
                IsMultiZoneCapable = true;
            }
            else
            {
                IsMultiZoneCapable = false;
                SelectedZone = Zone.All; // Reset to All if not capable
            }
            
            _driver = _queue;
            IsReady = true;
            StatusText = "GutsV Active | Ready";

             // Init Animation Engine
            _animationEngine = new GutsV.Core.Animations.AnimationEngine(_driver);
            _animationEngine.Speed = AnimationSpeed;
            _animationEngine.Brightness = (float)(Brightness / 100.0);
            
            // Load Animations
            Animations.Clear();
            Animations.Add(new GutsV.Core.Animations.BreatheAnimation());
            Animations.Add(new GutsV.Core.Animations.FreshBreatheAnimation());
            Animations.Add(new GutsV.Core.Animations.ColorTransformAnimation());
            Animations.Add(new GutsV.Core.Animations.PulsatingBlinkAnimation());
            Animations.Add(new GutsV.Core.Animations.ColorShiftAnimation());
            Animations.Add(new GutsV.Core.Animations.AudioSyncAnimation());
            Animations.Add(new GutsV.Core.Animations.SystemMonitorAnimation());
            Animations.Add(new GutsV.Core.Animations.AmbientLightingAnimation());
            
            // Restore Last Animation & State
             if (!string.IsNullOrEmpty(_currentSettings.LastAnimation))
             {
                 var target = System.Linq.Enumerable.FirstOrDefault(Animations, a => a.Name == _currentSettings.LastAnimation);
                 if (target != null)
                 {
                     SelectedAnimation = target;
                 }
             }

             if (SelectedAnimation == null && Animations.Count > 0)
                 SelectedAnimation = Animations[0];

             // Check if we should auto-start the animation
             if (_currentSettings.IsAnimationActive && SelectedAnimation != null)
             {
                 StatusText = $"Resuming: {SelectedAnimation.Name}";
                 IsAnimationRunning = true;
                 _animationEngine.Start(SelectedAnimation);
             }
             else
             {
                 // Static Color Mode
                 IsAnimationRunning = false;
                 // Apply static color (auto applies brightness too)
                 await ApplyColor();
             }

            // Hook into Animation Engine for Visualizer
            _animationEngine.FrameApplied += (color, zone) =>
            {
                // Dispatch to UI Thread
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    PreviewColor = System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);
                });
            };
        }
        catch (Exception ex)
        {
            StatusText = $"Initialization Failed: {ex.Message}";
        }
    }

    // This is the color shown on the UI Visualizer (Keys)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewColorBrush))]
    private System.Windows.Media.Color _previewColor = System.Windows.Media.Colors.Gray;

    public System.Windows.Media.SolidColorBrush PreviewColorBrush => new System.Windows.Media.SolidColorBrush(PreviewColor);

    // Called when any color slider changes
    [RelayCommand]
    private async Task ApplyColor()
    {
        if (_driver == null || !IsReady) return;
        
        // Stop animation if running
        if (IsAnimationRunning) ToggleAnimation();

        var color = new ColorRGB(Red, Green, Blue);
        
        // Apply Global Brightness
        var finalColor = color.ApplyBrightness((float)(Brightness / 100.0));

        var zone = IsMultiZoneCapable ? SelectedZone : Zone.All;
        
        await _driver.SetColorAsync(zone, finalColor);
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleAnimation()
    {
        if (_animationEngine == null || SelectedAnimation == null) return;

        if (IsAnimationRunning)
        {
            _animationEngine.Stop();
            IsAnimationRunning = false;
            StatusText = "Animation Stopped.";
            
            // 1. Capture the final color BEFORE updating anything
            // Because updating Red/Green/Blue will eventually update PreviewColor via bindings,
            // creating a loop if we read from PreviewColor sequentially.
            var finalColor = PreviewColor;

            // 2. Update Sliders/Values to match the visual
            Red = finalColor.R;
            Green = finalColor.G;
            Blue = finalColor.B;
            
            // Hex and Preview will automatically update to match Red/Green/Blue
            // ending up exactly where the animation stopped.
        }
        else
        {
            _animationEngine.Start(SelectedAnimation);
            IsAnimationRunning = true;
            StatusText = $"Running: {SelectedAnimation.Name}";
        }
        SaveSettings();
    }

    [RelayCommand]
    private async Task Shutdown()
    {
        SaveSettings();
        if (_queue != null) await _queue.ShutdownAsync();
    }

    private void SaveSettings()
    {
        _currentSettings.Red = Red;
        _currentSettings.Green = Green;
        _currentSettings.Blue = Blue;
        _currentSettings.DriverType = DriverType;
        _currentSettings.Brightness = Brightness;
        _currentSettings.IsAnimationActive = IsAnimationRunning; // Save Running State
        _currentSettings.AnimationSpeed = AnimationSpeed;
        
        if (IsAnimationRunning && SelectedAnimation != null)
        {
            _currentSettings.LastAnimation = SelectedAnimation.Name;
        }
        else
        {
             // Even if static, we might want to remember selection, 
             // but logic above handles re-selection locally.
             if (SelectedAnimation != null) 
                _currentSettings.LastAnimation = SelectedAnimation.Name;
        }

        SettingsManager.Save(_currentSettings);
    }

    public void ResetState()
    {
        // 1. Stop Animations
        if (IsAnimationRunning) ToggleAnimation();

        // 2. Reset Values
        Red = 0;
        Green = 212;
        Blue = 255;
        Brightness = 100;
        SelectedZone = Zone.All;
        DriverType = 0;
        AnimationSpeed = 1.0;
        
        // 3. Reset Internal Settings Object
        _currentSettings.IsStartupEnabled = false;
        _currentSettings.IsAnimationActive = false;
        _currentSettings.LastAnimation = null;
        
        // No need to save here, as file is deleted by App.Clean logic.
        // But crucially, this ensures next Shutdown() writes a "clean" file (or false flags)
        // instead of resurrecting old state.
    }
}
