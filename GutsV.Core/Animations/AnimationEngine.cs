using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public class AnimationEngine
{
    private readonly IKeyboardController _controller;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public event Action<ColorRGB, Zone>? FrameApplied;

    public float Brightness { get; set; } = 1.0f;
    public double Speed { get; set; } = 1.0;

    public AnimationEngine(IKeyboardController controller)
    {
        _controller = controller;
    }

    public void Start(IAnimation animation)
    {
        Stop();
        _currentAnimation = animation;
        _cts = new CancellationTokenSource();
        _isRunning = true;
        
        // Run loop in background
        Task.Run(() => RunLoopAsync(animation, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _isRunning = false;
        
        // Dispose animation if it needs it (like AudioSync)
        if (_currentAnimation is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _currentAnimation = null;
    }

    private IAnimation? _currentAnimation;

    private async Task RunLoopAsync(IAnimation animation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var frame in animation.GenerateFrames())
                {
                    if (token.IsCancellationRequested) break;

                    // Apply Global Brightness
                    var finalColor = frame.Color.ApplyBrightness(Brightness);

                    await _controller.SetColorAsync(frame.TargetZone, finalColor);
                    
                    // Notify UI (Visualizer also needs to see the dimmed color?)
                    // Yes, showing real output is better.
                    FrameApplied?.Invoke(finalColor, frame.TargetZone);

                    if (frame.DurationMs > 0)
                    {
                        // Apply Speed Multiplier (Higher speed = Lower delay)
                        // Clamp to minimum 10ms to prevent CPU spike
                        int delay = (int)(frame.DurationMs / Speed);
                        if (delay < 10) delay = 10; 
                        
                        await Task.Delay(delay, token);
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Expected on stop
        }
        catch (Exception)
        {
            // Handle error (maybe log)
        }
        finally
        {
             _isRunning = false;
        }
    }
}
