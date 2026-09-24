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
                 
                _driver = baseDriver;
                if (_driver is GutsV.Drivers.Wmi.WmiDriver wmi) await wmi.InitializeAsync();
                
                // 3. Apply Config
                // If animation was running
                if (!string.IsNullOrEmpty(settings.LastAnimation))
                {
                    _logger.LogInformation($"Restoring Animation: {settings.LastAnimation}");
                    
                    _engine = new AnimationEngine(_driver);
                    
                    // Match name to instance
                    IAnimation? anim = GetAnimationByName(settings.LastAnimation);
                    
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
                    // Static Color
                    _logger.LogInformation($"Restoring Static Color: R{settings.Red} G{settings.Green} B{settings.Blue}");
                    await _driver.SetColorAsync(Zone.All, new ColorRGB(settings.Red, settings.Green, settings.Blue));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in GutsV Service");
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
            "System Monitor" => new SystemMonitorAnimation(),
            // Audio Sync implies Session issues, skip or try
            "Audio Sync (Spectrum)" => null, // Disable for service to avoid crash
            _ => null
        };
    }
}
