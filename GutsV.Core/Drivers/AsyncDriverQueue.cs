using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;

namespace GutsV.Core.Drivers;

public class AsyncDriverQueue : IKeyboardController, IDisposable
{
    private readonly IKeyboardController _backend;
    private readonly BlockingCollection<(Zone zone, ColorRGB color)> _queue;
    private readonly CancellationTokenSource _cts;
    private Task? _workerTask;
    
    // Dirty state cache
    private ColorRGB _lastLeft = ColorRGB.Black;
    private ColorRGB _lastCenter = ColorRGB.Black;
    private ColorRGB _lastRight = ColorRGB.Black;

    public AsyncDriverQueue(IKeyboardController backend)
    {
        _backend = backend;
        _queue = new BlockingCollection<(Zone, ColorRGB)>(new ConcurrentQueue<(Zone, ColorRGB)>(), boundedCapacity: 5); // Lag protection: Drop frames if full
        _cts = new CancellationTokenSource();
    }

    public async Task InitializeAsync()
    {
        await _backend.InitializeAsync();
        _workerTask = Task.Run(ProcessQueue);
    }

    public Task SetColorAsync(Zone zone, ColorRGB color)
    {
        // Dirty check before queueing
        if (IsDirty(zone, color))
        {
            if (!_queue.TryAdd((zone, color))) 
            {
                // Queue full? Skip frame (Anti-Lag)
                // In a bounded queue, TryAdd returns false immediately if full.
            }
        }
        return Task.CompletedTask;
    }

    private bool IsDirty(Zone zone, ColorRGB color)
    {
        switch (zone)
        {
            case Zone.Left:
                if (_lastLeft == color) return false;
                _lastLeft = color;
                return true;
            case Zone.Center:
                if (_lastCenter == color) return false;
                _lastCenter = color;
                return true;
            case Zone.Right:
                if (_lastRight == color) return false;
                _lastRight = color;
                return true;
            case Zone.All:
                bool anyChange = _lastLeft != color || _lastCenter != color || _lastRight != color;
                if (anyChange)
                {
                    _lastLeft = color;
                    _lastCenter = color;
                    _lastRight = color;
                }
                return anyChange;
            default:
                return true;
        }
    }

    public async Task SetModeAsync(int mode)
    {
         await _backend.SetModeAsync(mode);
    }

    public async Task SetBrightnessAsync(byte brightness)
    {
         await _backend.SetBrightnessAsync(brightness);
    }

    private async Task ProcessQueue()
    {
        foreach (var (zone, color) in _queue.GetConsumingEnumerable(_cts.Token))
        {
            try
            {
                await _backend.SetColorAsync(zone, color);
            }
            catch (Exception)
            {
                // Log error but don't crash
            }
        }
    }

    public async Task ShutdownAsync()
    {
        _cts.Cancel();
        if (_workerTask != null) await Task.WhenAny(_workerTask, Task.Delay(1000));
        await _backend.ShutdownAsync();
    }

    public void Dispose()
    {
        _cts.Dispose();
        _queue.Dispose();
    }
}
