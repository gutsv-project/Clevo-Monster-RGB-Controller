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
    private Task _loopTask = Task.CompletedTask;

    public bool IsRunning => _isRunning;
    public event Action<ColorRGB, Zone>? FrameApplied;
    // Same frame before global brightness (what the animation produced for that zone)
    public event Action<ColorRGB, Zone>? RawFrameApplied;
    // Raised (on a background thread) when an animation loop dies with an error
    public event Action<Exception>? Error;

    // Last frame color before global brightness (what the animation itself produced)
    public ColorRGB LastFrameColor { get; private set; } = ColorRGB.Black;

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
        var cts = _cts;
        _loopTask = Task.Run(() => RunLoopAsync(animation, cts));
    }

    /// <summary>Stops and waits (bounded) until the old loop can no longer write a frame.</summary>
    public async Task StopAsync()
    {
        var loop = _loopTask;
        Stop();
        await Task.WhenAny(loop, Task.Delay(1000));
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

    private async Task RunLoopAsync(IAnimation animation, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var frame in animation.GenerateFrames())
                {
                    if (token.IsCancellationRequested) break;

                    LastFrameColor = frame.Color;

                    // Apply Global Brightness
                    var finalColor = frame.Color.ApplyBrightness(Brightness);

                    await _controller.SetColorAsync(frame.TargetZone, finalColor);

                    // Notify UI (Visualizer also needs to see the dimmed color?)
                    // Yes, showing real output is better.
                    FrameApplied?.Invoke(finalColor, frame.TargetZone);
                    RawFrameApplied?.Invoke(frame.Color, frame.TargetZone);

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
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) Error?.Invoke(ex);
        }
        finally
        {
            // Only the loop that is still current may clear the flag (a newer Start may already run)
            if (ReferenceEquals(_cts, cts)) _isRunning = false;
        }
    }
}
