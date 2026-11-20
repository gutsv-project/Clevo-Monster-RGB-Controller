using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    private bool _initDone;

    // Debounced settings save (sliders change many times per second)
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer;

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

    // Filled from the driver after detection (All + every addressable zone)
    [ObservableProperty]
    private System.Collections.Generic.List<Zone> _availableZones = new()
    {
        Zone.All,
        Zone.Left,
        Zone.Center,
        Zone.Right
    };

    // ---- Zones: multi-select list + per-zone preview ----
    // Every zone of the detected keyboard (Section 1-4 / Left-Center-Right, Light Bar, Logo LED)
    public ObservableCollection<ZoneItem> ZoneItems { get; } = new();
    // Keyboard column blocks of the preview, and the Light Bar / Logo indicators
    public ObservableCollection<ZoneItem> KeyboardSections { get; } = new();
    public ObservableCollection<ZoneItem> ExtraZones { get; } = new();
    private List<ZoneSlot> _zoneLayout = new();
    // Static color per zone, before global brightness (what "Apply" set for that zone)
    private readonly Dictionary<Zone, ColorRGB> _zoneBaseColors = new();
    // Last raw animation frame per zone (to keep the stopped state)
    private readonly Dictionary<Zone, ColorRGB> _lastRawFrames = new();

    public string SelectedZonesText =>
        ZoneItems.Count <= 1 ? "Keyboard"
        : ZoneItems.All(z => z.IsSelected) ? "All zones"
        : ZoneItems.Any(z => z.IsSelected) ? string.Join(", ", ZoneItems.Where(z => z.IsSelected).Select(z => z.DisplayName))
        : "No zone selected";

    [RelayCommand]
    private void SelectAllZones()
    {
        foreach (var z in ZoneItems) z.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNoZones()
    {
        foreach (var z in ZoneItems) z.IsSelected = false;
    }

    private void BuildZoneItems(IReadOnlyList<Zone> driverZones)
    {
        _zoneLayout = ZoneLayout.Describe(driverZones);
        ZoneItems.Clear(); KeyboardSections.Clear(); ExtraZones.Clear();
        var savedSelection = _currentSettings.SelectedZones;
        var start = new ColorRGB(Red, Green, Blue);
        foreach (var slot in _zoneLayout)
        {
            var item = new ZoneItem(slot)
            {
                IsSelected = savedSelection == null || savedSelection.Contains(slot.Zone.ToString())
            };
            _zoneBaseColors[slot.Zone] = TryParseHex(_currentSettings.ZoneColors, slot.Zone) ?? start;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ZoneItem.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedZonesText));
                    RefreshZonePreview();
                    ScheduleSave();
                }
            };
            ZoneItems.Add(item);
            (slot.IsKeyboardSection ? KeyboardSections : ExtraZones).Add(item);
        }
        // A saved selection from another keyboard layout must never leave nothing selected
        // (on a single-zone keyboard the list is hidden, so it could not be fixed by hand)
        if (_zoneLayout.Count <= 1 || !ZoneItems.Any(z => z.IsSelected))
            foreach (var z in ZoneItems) z.IsSelected = true;
        OnPropertyChanged(nameof(SelectedZonesText));
        RefreshZonePreview();
    }

    private static ColorRGB? TryParseHex(Dictionary<string, string>? map, Zone zone)
    {
        if (map == null || !map.TryGetValue(zone.ToString(), out var hex)) return null;
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(hex);
            return new ColorRGB(c.R, c.G, c.B);
        }
        catch { return null; }
    }

    private static System.Windows.Media.Color ToMedia(ColorRGB c) => System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);

    /// <summary>
    /// Static preview: selected zones show the slider color (what Apply will set),
    /// the others show their applied color. During an animation the frames drive the preview.
    /// </summary>
    private void RefreshZonePreview()
    {
        if (IsAnimationRunning) return;
        foreach (var item in ZoneItems)
        {
            var c = item.IsSelected ? new ColorRGB(Red, Green, Blue)
                : _zoneBaseColors.TryGetValue(item.Zone, out var b) ? b : new ColorRGB(Red, Green, Blue);
            item.DisplayColor = ToMedia(c);
        }
    }

    /// <summary>Sends every zone's static color (dirty-checked by the queue, so unchanged zones cost nothing).</summary>
    private async Task SendAllZonesAsync(bool now = false)
    {
        if (_driver == null || _queue == null) return;
        float bright = (float)(Brightness / 100.0);
        var colors = _zoneLayout.Select(s => (s.Zone, Color: _zoneBaseColors.TryGetValue(s.Zone, out var c) ? c : new ColorRGB(Red, Green, Blue))).ToList();
        bool uniform = colors.Count > 0 && colors.All(x => x.Color == colors[0].Color);
        if (uniform || colors.Count <= 1)
        {
            var c = (colors.Count > 0 ? colors[0].Color : new ColorRGB(Red, Green, Blue)).ApplyBrightness(bright);
            if (now) await _queue.SetColorNowAsync(Zone.All, c); else await _driver.SetColorAsync(Zone.All, c);
            return;
        }
        foreach (var (zone, color) in colors)
        {
            var c = color.ApplyBrightness(bright);
            // "now": every zone is written directly, so a following effect cannot discard any of them
            if (now) await _queue.SetColorNowAsync(zone, c);
            else await _driver.SetColorAsync(zone, c);
        }
    }

    // Firmware effects / brightness (Clevo, Monster, Acer DCHU)
    [ObservableProperty]
    private bool _isHardwareEffectsSupported;

    public HardwareEffect[] HardwareEffects { get; } = Enum.GetValues<HardwareEffect>();

    [ObservableProperty]
    private HardwareEffect _selectedHardwareEffect = HardwareEffect.Breathe;

    [ObservableProperty]
    private int _hardwareBrightness = 4;

    // Firmware brightness is only sent once the user actually moved the slider
    private bool _hardwareBrightnessSet;
    private bool _isHardwareEffectActive;

    partial void OnHardwareBrightnessChanged(int value)
    {
        _hardwareBrightnessSet = true;
        if (_driver != null && IsReady)
            _ = _driver.SetHardwareBrightnessAsync(value)
                .ContinueWith(_ => _driver.SaveStateAsync()) // Fn brightness keys continue from this level
                .ContinueWith(_ => ReapplySleepTimerAsync());
        ScheduleSave();
    }

    // ---- Override Boot Effect / Sleep Timer (firmware command 121) ----
    [ObservableProperty] private bool _isBootEffectSupported;
    [ObservableProperty] private bool _isSleepTimerSupported;
    [ObservableProperty] private bool _bootEffectEnabled;
    [ObservableProperty] private bool _sleepTimerEnabled;
    [ObservableProperty] private int _sleepTimerHours;
    [ObservableProperty] private int _sleepTimerMinutes;
    [ObservableProperty] private int _sleepTimerSecondsPart = 30;
    // Only settings the user changed in GutsV are sent to the firmware
    private bool _bootEffectSet;
    private bool _sleepTimerSet;
    // Seconds the user actually applied (box edits are not saved until "Apply")
    private int _appliedSleepSeconds = 30;

    private int SleepTimerTotalSeconds =>
        Math.Clamp(SleepTimerHours * 3600 + SleepTimerMinutes * 60 + SleepTimerSecondsPart,
            ClevoLed.SleepTimerMinSeconds, ClevoLed.SleepTimerMaxSeconds);

    private void SetSleepTimerFields(int totalSeconds)
    {
        totalSeconds = Math.Clamp(totalSeconds, ClevoLed.SleepTimerMinSeconds, ClevoLed.SleepTimerMaxSeconds);
#pragma warning disable MVVMTK0034
        _sleepTimerHours = totalSeconds / 3600;
        _sleepTimerMinutes = totalSeconds / 60 % 60;
        _sleepTimerSecondsPart = totalSeconds % 60;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(SleepTimerHours));
        OnPropertyChanged(nameof(SleepTimerMinutes));
        OnPropertyChanged(nameof(SleepTimerSecondsPart));
    }

    partial void OnBootEffectEnabledChanged(bool value)
    {
        if (_driver == null || !IsReady) return;
        _bootEffectSet = true;
        _ = _driver.SetBootEffectAsync(value);
        StatusText = value ? "Boot effect: ON" : "Boot effect: OFF";
        ScheduleSave();
    }

    partial void OnSleepTimerEnabledChanged(bool value)
    {
        if (_driver == null || !IsReady) return;
        ApplySleepTimer();
    }

    [RelayCommand]
    private void ApplySleepTimer()
    {
        if (_driver == null || !IsReady) return;
        _sleepTimerSet = true;
        int total = SleepTimerTotalSeconds;
        SetSleepTimerFields(total); // show the clamped value
        _appliedSleepSeconds = total;
        _ = _driver.SetSleepTimerAsync(SleepTimerEnabled ? total : null);
        StatusText = SleepTimerEnabled ? $"Sleep timer: {total} s" : "Sleep timer: OFF";
        ScheduleSave();
    }

    [RelayCommand]
    private async Task ApplyHardwareEffect()
    {
        if (_driver == null || _queue == null || !IsReady) return;
        if (IsAnimationRunning)
        {
            StopAnimation(syncSlidersToLastFrame: false);
            if (_animationEngine != null) await _animationEngine.StopAsync(); // no late frame after the effect
        }
        // Effects such as Breathe use the stored zone colors: give them the user's colors, not the last animation frame
        await SendAllZonesAsync(now: true);
        await _driver.SetHardwareEffectAsync(SelectedHardwareEffect);
        await _driver.SaveStateAsync(); // Fn+LED hotkeys restore this effect, not an old Control Center one
        await ReapplySleepTimerAsync(); // Control Center re-sends it after every mode change
        _isHardwareEffectActive = true;
        StatusText = $"Hardware effect: {SelectedHardwareEffect}";
        ScheduleSave();
    }

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
        if (value != null)
        {
            _currentSettings.LastAnimation = value.Name;
            if (IsAnimationRunning)
            {
                // Auto-restart with new animation
                _animationEngine?.Start(value);
                StatusText = $"Running: {value.Name}";
            }
            ScheduleSave();
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
             RefreshZonePreview();
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

        // If static color, re-send every zone at the new brightness (keeps per-zone colors)
        if (!IsAnimationRunning && IsReady && !_isHardwareEffectActive)
        {
            _ = SendAllZonesAsync();
        }
        ScheduleSave();
    }

    [ObservableProperty]
    private double _animationSpeed = 1.0;

    partial void OnAnimationSpeedChanged(double value)
    {
        if (_animationEngine != null)
        {
            _animationEngine.Speed = value;
        }
        ScheduleSave();
    }

    public MainViewModel()
    {
        Animations = new System.Collections.ObjectModel.ObservableCollection<GutsV.Core.Animations.IAnimation>();

        _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (s, e) => SaveSettings();

        // Load Settings
        _currentSettings = SettingsManager.Load();
        if (_currentSettings.Packet44MinGapMs is int gap and >= 0 and <= 2000)
            GutsV.Drivers.Insyde.InsydeDriver.Packet44MinGapMs = gap;
        _red = _currentSettings.Red;
        _green = _currentSettings.Green;
        _blue = _currentSettings.Blue;
        _driverType = _currentSettings.DriverType;
        _brightness = _currentSettings.Brightness; // Load Brightness
        _animationSpeed = _currentSettings.AnimationSpeed;
        if (_animationSpeed < 0.1) _animationSpeed = 1.0; // Safety check
        _hardwareBrightnessSet = _currentSettings.HardwareBrightness.HasValue;
        _hardwareBrightness = Math.Clamp(_currentSettings.HardwareBrightness ?? 4, 0, 4);
        _isHardwareEffectActive = _currentSettings.IsHardwareEffectActive;
        _bootEffectSet = _currentSettings.BootEffectEnabled.HasValue;
        _bootEffectEnabled = _currentSettings.BootEffectEnabled ?? false;
        _sleepTimerSet = _currentSettings.SleepTimerEnabled.HasValue;
        _sleepTimerEnabled = _currentSettings.SleepTimerEnabled ?? false;
        _appliedSleepSeconds = _currentSettings.SleepTimerSeconds;
        SetSleepTimerFields(_currentSettings.SleepTimerSeconds);
        if (Enum.IsDefined(typeof(HardwareEffect), _currentSettings.HardwareEffect))
            _selectedHardwareEffect = (HardwareEffect)_currentSettings.HardwareEffect;

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

            // Capability comes from the driver itself (WMI: 3 zones, Insyde: firmware-reported 1-4 zones + logo)
            var zones = baseDriver.Zones.Where(z => z != Zone.All).ToList();
            IsMultiZoneCapable = zones.Count > 1;
            AvailableZones = new System.Collections.Generic.List<Zone> { Zone.All }.Concat(zones).ToList();
            if (!IsMultiZoneCapable || !AvailableZones.Contains(SelectedZone))
            {
                SelectedZone = Zone.All; // Reset to All if not capable
            }
            BuildZoneItems(baseDriver.Zones);
            IsHardwareEffectsSupported = baseDriver.Features.HasFlag(KeyboardFeatures.HardwareEffects);
            IsBootEffectSupported = baseDriver.Features.HasFlag(KeyboardFeatures.BootEffect);
            IsSleepTimerSupported = baseDriver.Features.HasFlag(KeyboardFeatures.SleepTimer);
            // Show what the firmware / Control Center has, unless the user chose something in GutsV
            var stored = baseDriver.StoredOptions;
#pragma warning disable MVVMTK0034
            if (!_bootEffectSet && stored.BootEffect is bool sb) { _bootEffectEnabled = sb; OnPropertyChanged(nameof(BootEffectEnabled)); }
            if (!_sleepTimerSet && stored.SleepTimerEnabled is bool ss) { _sleepTimerEnabled = ss; OnPropertyChanged(nameof(SleepTimerEnabled)); }
#pragma warning restore MVVMTK0034
            if (!_sleepTimerSet && stored.SleepTimerSeconds is int sec) { _appliedSleepSeconds = sec; SetSleepTimerFields(sec); }

            _driver = _queue;
            IsReady = true;
            StatusText = $"Ready · {DescribeDevice(baseDriver)}";

             // Init Animation Engine
            _animationEngine = new GutsV.Core.Animations.AnimationEngine(_driver);
            _animationEngine.Speed = AnimationSpeed;
            _animationEngine.Brightness = (float)(Brightness / 100.0);
            _animationEngine.Error += ex =>
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    IsAnimationRunning = false;
                    StatusText = $"Animation stopped: {ex.Message}";
                    ScheduleSave(); // do not resume a crashing animation on next start
                });

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

             // Hook into Animation Engine for Visualizer (before starting, so the first frames show)
            _animationEngine.FrameApplied += (color, zone) =>
            {
                // Dispatch to UI Thread (async: never block the animation thread on the UI)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    // Late callbacks after Stop must not overwrite the static preview
                    if (IsAnimationRunning)
                    {
                        var media = ToMedia(color);
                        PreviewColor = media;
                        foreach (var z in ZoneLayout.Affected(_zoneLayout, zone))
                        {
                            var item = ZoneItems.FirstOrDefault(i => i.Zone == z);
                            if (item != null) item.DisplayColor = media;
                        }
                    }
                });
            };
            _animationEngine.RawFrameApplied += (color, zone) =>
            {
                lock (_lastRawFrames)
                    foreach (var z in ZoneLayout.Affected(_zoneLayout, zone)) _lastRawFrames[z] = color;
            };

             if (IsHardwareEffectsSupported && _hardwareBrightnessSet)
             {
                 await _driver.SetHardwareBrightnessAsync(HardwareBrightness);
             }


             // Check if we should auto-start the animation
             if (IsHardwareEffectsSupported && _isHardwareEffectActive && !_currentSettings.IsAnimationActive)
             {
                 await SendAllZonesAsync(now: true);
                 await _driver.SetHardwareEffectAsync(SelectedHardwareEffect);
                 IsAnimationRunning = false;
                 StatusText = $"Hardware effect: {SelectedHardwareEffect}";
             }
             else if (_currentSettings.IsAnimationActive && SelectedAnimation != null)
             {
                 StatusText = $"Resuming: {SelectedAnimation.Name}";
                 IsAnimationRunning = true;
                 _animationEngine.Start(SelectedAnimation);
             }
             else
             {
                 // Static Color Mode: restore every zone's saved color (brightness applied)
                 IsAnimationRunning = false;
                 await SendAllZonesAsync();
                 RefreshZonePreview();
             }

             // Firmware options the user chose in GutsV, after the LED state (same order as Control Center)
             if (IsBootEffectSupported && _bootEffectSet)
             {
                 await _driver.SetBootEffectAsync(BootEffectEnabled);
             }
             await ReapplySleepTimerAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Initialization Failed: {ex.Message}";
        }
        finally
        {
            _initDone = true;
        }
    }

    private static string DescribeDevice(IKeyboardController driver)
    {
        // e.g. "Insyde · 4 zones + Logo", "Wmi · 3 zones", "Insyde · 1 zone"
        int sections = driver.Zones.Count(z => z is Zone.Left or Zone.Center or Zone.Right or Zone.Zone4);
        var extras = new List<string>();
        if (driver.Zones.Contains(Zone.LightBar)) extras.Add("Light Bar");
        if (driver.Zones.Contains(Zone.Logo)) extras.Add("Logo");
        string kind = driver.GetType().Name.Replace("Driver", "");
        string zones = sections <= 1 ? "1 zone" : $"{sections} zones";
        return extras.Count == 0 ? $"{kind} · {zones}" : $"{kind} · {zones} + {string.Join(" + ", extras)}";
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

        // Nothing selected: change nothing (no effect/animation is stopped either)
        var selected = ZoneItems.Where(z => z.IsSelected).ToList();
        if (ZoneItems.Count > 0 && selected.Count == 0)
        {
            StatusText = "Select at least one zone.";
            return;
        }

        _isHardwareEffectActive = false;

        // Stop animation if running, but keep the color the user picked
        if (IsAnimationRunning)
        {
            StopAnimation(syncSlidersToLastFrame: false);
            if (_animationEngine != null) await _animationEngine.StopAsync(); // no late frame after the static colors
            UpdateHexFromRgb();
        }

        // Selected zones take the slider color; the others keep theirs
        var color = new ColorRGB(Red, Green, Blue);
        if (ZoneItems.Count == 0) // driver without zone info (should not happen)
            foreach (var s in _zoneLayout) _zoneBaseColors[s.Zone] = color;
        foreach (var item in selected) _zoneBaseColors[item.Zone] = color;

        // All zones are (re)sent: after an animation or effect the unselected zones must also
        // return to their static colors. The queue skips zones whose color did not change.
        await SendAllZonesAsync();
        _ = _driver.SaveStateAsync(); // Fn+LED hotkeys keep these colors (queued after the color writes)
        RefreshZonePreview();
        StatusText = ZoneItems.Count > 1 ? $"Applied to: {SelectedZonesText}" : "Color applied.";
        ScheduleSave();
    }

    // Sleep timer is re-sent after LED mode/brightness writes, like Control Center does
    private Task ReapplySleepTimerAsync() =>
        _driver != null && IsSleepTimerSupported && _sleepTimerSet
            ? _driver.SetSleepTimerAsync(SleepTimerEnabled ? _appliedSleepSeconds : null)
            : Task.CompletedTask;

    // Slider color with the global (software) brightness applied
    private ColorRGB CurrentFinalColor() =>
        new ColorRGB(Red, Green, Blue).ApplyBrightness((float)(Brightness / 100.0));

    [RelayCommand]
    private void ToggleAnimation()
    {
        if (_animationEngine == null || SelectedAnimation == null) return;

        if (IsAnimationRunning)
        {
            StopAnimation(syncSlidersToLastFrame: true);
        }
        else
        {
            _isHardwareEffectActive = false;
            lock (_lastRawFrames) _lastRawFrames.Clear();
            // Zones the animation does not drive keep showing their real (static) color
            foreach (var item in ZoneItems)
                if (_zoneBaseColors.TryGetValue(item.Zone, out var b)) item.DisplayColor = ToMedia(b);
            _animationEngine.Start(SelectedAnimation);
            IsAnimationRunning = true;
            StatusText = $"Running: {SelectedAnimation.Name}";
        }
        ScheduleSave();
    }

    private void StopAnimation(bool syncSlidersToLastFrame)
    {
        if (_animationEngine == null) return;

        _animationEngine.Stop();
        IsAnimationRunning = false;
        StatusText = "Animation Stopped.";

        if (syncSlidersToLastFrame)
        {
            // Sliders hold the color *before* global brightness, so use the raw frame color
            // (the preview shows the dimmed one; using it would darken the color on every stop).
            var finalColor = _animationEngine.LastFrameColor;

            // Every zone keeps the color it had when the animation stopped;
            // the sliders take the color of the first selected zone (what the user is editing)
            lock (_lastRawFrames)
            {
                foreach (var kv in _lastRawFrames) _zoneBaseColors[kv.Key] = kv.Value;
                var firstSelected = ZoneItems.FirstOrDefault(z => z.IsSelected) ?? ZoneItems.FirstOrDefault();
                if (firstSelected != null && _lastRawFrames.TryGetValue(firstSelected.Zone, out var zoneFrame))
                    finalColor = zoneFrame;
            }

            // Update Sliders/Values to match the visual
            Red = finalColor.R;
            Green = finalColor.G;
            Blue = finalColor.B;
            UpdateColorPreview();
        }
        else
        {
            UpdateColorPreview();
        }
    }

    [RelayCommand]
    private async Task Shutdown()
    {
        _saveTimer.Stop();
        SaveSettings();
        if (_queue != null) await _queue.ShutdownAsync();
    }

    private void ScheduleSave()
    {
        if (!_initDone) return; // startup restores state; it must not save a half-restored state
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettings()
    {
        _saveTimer.Stop();
        _currentSettings.Red = Red;
        _currentSettings.Green = Green;
        _currentSettings.Blue = Blue;
        _currentSettings.DriverType = DriverType;
        _currentSettings.Brightness = Brightness;
        _currentSettings.IsAnimationActive = IsAnimationRunning; // Save Running State
        _currentSettings.AnimationSpeed = AnimationSpeed;
        _currentSettings.HardwareBrightness = _hardwareBrightnessSet ? HardwareBrightness : null;
        _currentSettings.HardwareEffect = (uint)SelectedHardwareEffect;
        _currentSettings.IsHardwareEffectActive = _isHardwareEffectActive;
        _currentSettings.BootEffectEnabled = _bootEffectSet ? BootEffectEnabled : null;
        _currentSettings.SleepTimerEnabled = _sleepTimerSet ? SleepTimerEnabled : null;
        _currentSettings.SleepTimerSeconds = _appliedSleepSeconds;
        if (_zoneLayout.Count > 0)
        {
            // Merge: zones of another keyboard layout (e.g. a run that fell back to 1 zone) are kept
            var colors = _currentSettings.ZoneColors ?? new Dictionary<string, string>();
            foreach (var s in _zoneLayout)
                if (_zoneBaseColors.TryGetValue(s.Zone, out var c))
                    colors[s.Zone.ToString()] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _currentSettings.ZoneColors = colors;
            if (_zoneLayout.Count > 1)
                _currentSettings.SelectedZones = ZoneItems.Where(z => z.IsSelected).Select(z => z.Zone.ToString()).ToList();
        }

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

    partial void OnDriverTypeChanged(int value) => ScheduleSave();

    public void ResetState()
    {
        // 1. Stop Animations
        if (IsAnimationRunning) ToggleAnimation();

        // 2. Reset Values (zone colors first, so the brightness change below does not resend old ones)
        foreach (var s in _zoneLayout) _zoneBaseColors[s.Zone] = new ColorRGB(0, 212, 255);
        Red = 0;
        Green = 212;
        Blue = 255;
        Brightness = 100;
        SelectedZone = Zone.All;
        DriverType = 0;
        AnimationSpeed = 1.0;
        _isHardwareEffectActive = false;
        _hardwareBrightnessSet = false;
        _bootEffectSet = false;
        _sleepTimerSet = false;
        foreach (var s in _zoneLayout) _zoneBaseColors[s.Zone] = new ColorRGB(0, 212, 255);
        foreach (var z in ZoneItems) z.IsSelected = true;
        _currentSettings.ZoneColors = null;
        _currentSettings.SelectedZones = null;
        RefreshZonePreview();

        // 3. Reset Internal Settings Object
        _currentSettings.IsStartupEnabled = false;
        _currentSettings.IsAnimationActive = false;
        _currentSettings.LastAnimation = null;

        // Changes above only schedule a debounced save; cancel it so the cleanup
        // that follows (settings folder deletion) is not undone by a late write.
        _saveTimer.Stop();
    }
}
