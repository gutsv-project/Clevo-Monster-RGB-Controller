using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GutsV.Core.Timing;

public class PrecisionTimer : IDisposable
{
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts;
    private readonly Stopwatch _stopwatch;
    private Task? _loopTask;
    private bool _isRunning;

    public event Action<double>? Tick;

    public PrecisionTimer(int fps)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / fps);
        _timer = new PeriodicTimer(interval);
        _cts = new CancellationTokenSource();
        _stopwatch = new Stopwatch();
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _stopwatch.Start();
        _loopTask = LoopAsync();
    }

    public void Stop()
    {
        _isRunning = false;
        _cts.Cancel();
        _stopwatch.Stop();
    }

    private async Task LoopAsync()
    {
        double previousTime = _stopwatch.Elapsed.TotalSeconds;

        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                if (!_isRunning) break;

                double currentTime = _stopwatch.Elapsed.TotalSeconds;
                double deltaTime = currentTime - previousTime;
                previousTime = currentTime;

                Tick?.Invoke(deltaTime);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful stop
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
