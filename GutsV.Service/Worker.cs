using GutsV.Core.Animations;
using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;
using GutsV.Core.Settings;
using GutsV.Drivers;
using System.Runtime.Versioning;

namespace GutsV.Service;

[SupportedOSPlatform("windows")]
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private IKeyboardController? _driver;
    private AnimationEngine? _engine;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GutsV Service starting...");

        // 1. Load Settings
        var settings = SettingsManager.Load();
        
        try
        {
            // 2. Init Driver
            var baseDriver = await DeviceManager.CreateDriverAsync(settings.DriverType);
            if (baseDriver == null)
            {
                _logger.LogError("GutsV Service: No supported device found.");
                return;
            }

            // Drivers should already be platform attribute marked, but double check
            if (OperatingSystem.IsWindows())
            {
                 // No queue needed for simple service loop really, but safe to use
                 // However, AsyncQueue does a Loop inside. 
                 // AnimationEngine DOES a Loop inside.
                 // We need to coordinate.
                 
                // DeviceManager already initialized the driver
                _driver = baseDriver;
                
                // 3. Apply Config
                // If animation was running
                IAnimation? anim = settings.IsAnimationActive && !string.IsNullOrEmpty(settings.LastAnimation)
                    ? GetAnimationByName(settings.LastAnimation)
                    : null;
                if (anim != null)
                {
                    _logger.LogInformation($"Restoring Animation: {settings.LastAnimation}");
                    
                    _engine = new AnimationEngine(_driver)
                    {
                        Brightness = (float)(settings.Brightness / 100.0),
                        Speed = settings.AnimationSpeed >= 0.1 ? settings.AnimationSpeed : 1.0
                    };
                    
                    
                    // Warning: AudioSync in Service (Session 0) might fail or be silent.
                    // We allow it, but catch errors.
                    
                    if (anim != null)
                    {
                         try 
                         {
                             // Engine.Start is async void or fire/forget usually? 
                             // No, Engine.Start marks _currentAnimation.
                             // But Engine doesn't have its own thread loop? 
                             // Wait, AnimationEngine.Start just sets the variable. 
                             // AnimationEngine.RunLoopAsync is the main loop.
                             
                             // Actually looking at AnimationEngine.cs:
                             // Start() sets _cancellationTokenSource and calls RunLoopAsync().
                             // RunLoopAsync is async void (fire and forget) internally? 
                             // Let's check AnimationEngine implementation memory.
                             // Assuming it runs on its own Task.
                             
                             _engine.Start(anim);
                         } 
                         catch (Exception ex)
                         {
                             _logger.LogError($"Failed to start animation: {ex.Message}");
                         }
                    }
                }
                else
                {
                    // Static Color (also used when the saved animation cannot run in a service)
                    _logger.LogInformation($"Restoring Static Color: R{settings.Red} G{settings.Green} B{settings.Blue}");
                    float bright = (float)(settings.Brightness / 100.0);
                    var color = new ColorRGB(settings.Red, settings.Green, settings.Blue).ApplyBrightness(bright);
                    await _driver.SetColorAsync(Zone.All, color);

                    // Per-zone colors saved by the UI (multi-zone keyboards)
                    if (settings.ZoneColors != null && _driver.Zones.Count > 1)
                    {
                        foreach (var (name, hex) in settings.ZoneColors)
                        {
                            if (!Enum.TryParse<Zone>(name, out var zone) || !_driver.Zones.Contains(zone)) continue;
                            if (hex.Length != 7 || hex[0] != '#') continue;
                            try
                            {
                                var zc = new ColorRGB(Convert.ToByte(hex.Substring(1, 2), 16),
                                    Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
                                await _driver.SetColorAsync(zone, zc.ApplyBrightness(bright));
                            }
                            catch (FormatException) { }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in GutsV Service");
        }

        // A firmware effect that was active in the UI (colors above are its base colors)
        try
        {
            if (_driver != null && _engine == null && settings.IsHardwareEffectActive && !settings.IsAnimationActive
                && _driver.Features.HasFlag(KeyboardFeatures.HardwareEffects)
                && Enum.IsDefined(typeof(HardwareEffect), settings.HardwareEffect))
            {
                await _driver.SetHardwareEffectAsync((HardwareEffect)settings.HardwareEffect);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GutsV Service: restoring hardware effect failed");
        }

        // Firmware options chosen in GutsV (no-ops when the keyboard does not support them)
        try
        {
            if (_driver != null && settings.BootEffectEnabled is bool boot)
                await _driver.SetBootEffectAsync(boot);
            if (_driver != null && settings.SleepTimerEnabled is bool sleep)
                await _driver.SetSleepTimerAsync(sleep ? settings.SleepTimerSeconds : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GutsV Service: restoring boot effect / sleep timer failed");
        }

        // Keep service alive
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
        
        // Cleanup
        _engine?.Stop();
        if (_driver is IDisposable d) d.Dispose();
    }

    private IAnimation? GetAnimationByName(string name)
    {
        // Simple factory
        return name switch
        {
            "Breathing" => new BreatheAnimation(),
            "Neon Pulse" => new FreshBreatheAnimation(),
            "Spectrum Cycle" => new ColorTransformAnimation(),
            "Strobe Blink" => new PulsatingBlinkAnimation(),
            "Wave" => new ColorShiftAnimation(),
            "System Monitor (Heatmap)" => new SystemMonitorAnimation(),
            "System Monitor" => new SystemMonitorAnimation(), // older settings files
            "Ambient (Screen Sync)" => null, // screen capture is not possible from Session 0
            // Audio Sync implies Session issues, skip or try
            "Audio Sync (Spectrum)" => null, // Disable for service to avoid crash
            _ => null
        };
    }
}
