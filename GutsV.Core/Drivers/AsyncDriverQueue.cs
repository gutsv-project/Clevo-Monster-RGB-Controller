using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;

namespace GutsV.Core.Drivers;

/// <summary>
/// Non-blocking front for a (slow) keyboard backend.
/// Pending writes are coalesced per zone: only the newest color of each zone waits, so the queue
/// stays small (anti-lag) and no zone ever loses its final color.
/// </summary>
public class AsyncDriverQueue : IKeyboardController, IDisposable
{
    private readonly IKeyboardController _backend;
    private readonly CancellationTokenSource _cts;
    private Task? _workerTask;

    // Pending writes in arrival order, at most one per zone key (Zone.All supersedes single zones)
    private readonly LinkedList<(Zone zone, ColorRGB color)> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);

    // Last color handed to the backend (or pending) per physical zone (dirty check)
    private readonly Dictionary<Zone, ColorRGB> _lastQueued = new();
    private readonly object _lock = new();
    // Serializes every backend call (queue worker vs. effect/brightness calls from the UI)
    private readonly SemaphoreSlim _backendGate = new(1, 1);

    public AsyncDriverQueue(IKeyboardController backend)
    {
        _backend = backend;
        _cts = new CancellationTokenSource();
    }

    public IReadOnlyList<Zone> Zones => _backend.Zones;
    public KeyboardFeatures Features => _backend.Features;
    public FirmwareOptions StoredOptions => _backend.StoredOptions;

    public async Task InitializeAsync()
    {
        await _backend.InitializeAsync();
        _workerTask = Task.Run(ProcessQueue);
    }

    public Task SetColorAsync(Zone zone, ColorRGB color)
    {
        lock (_lock)
        {
            var targets = Targets(zone);
            if (targets.Count == 0) return Task.CompletedTask; // zone not present on this keyboard
            bool dirty = false;
            foreach (var z in targets)
                if (!_lastQueued.TryGetValue(z, out var last) || last != color) dirty = true;
            if (!dirty) return Task.CompletedTask;

            // Coalesce: drop older pending writes this one fully replaces
            var node = _pending.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.zone == zone || (zone == Zone.All && node.Value.zone != Zone.All))
                    _pending.Remove(node);
                node = next;
            }
            _pending.AddLast((zone, color));
            foreach (var z in targets) _lastQueued[z] = color;
        }
        _signal.Release();
        return Task.CompletedTask;
    }

    private List<Zone> Targets(Zone zone)
    {
        var zones = _backend.Zones;
        bool singleZone = zones.Count == 1 && zones[0] == Zone.All;
        if (zone == Zone.All)
            return singleZone ? new List<Zone> { Zone.All } : new List<Zone>(zones);
        // legacy single-zone behavior: Left drives the whole keyboard, other zones are ignored
        if (singleZone) return zone == Zone.Left ? new List<Zone> { Zone.All } : new List<Zone>();
        return zones.Contains(zone) ? new List<Zone> { zone } : new List<Zone>();
    }

    private void Invalidate()
    {
        lock (_lock) _lastQueued.Clear();
    }

    private async Task OnBackendAsync(Func<Task> call)
    {
        await _backendGate.WaitAsync();
        try { await call(); }
        finally { _backendGate.Release(); }
    }

    public Task SetModeAsync(int mode) => OnBackendAsync(() => _backend.SetModeAsync(mode));

    public Task SetBrightnessAsync(byte brightness) => OnBackendAsync(() => _backend.SetBrightnessAsync(brightness));

    public Task SetHardwareEffectAsync(HardwareEffect effect)
    {
        lock (_lock)
        {
            // Pending animation frames would overwrite the effect: discard them.
            _pending.Clear();
            // Firmware now drives the LEDs; the next static color must be resent.
            _lastQueued.Clear();
        }
        return OnBackendAsync(() => _backend.SetHardwareEffectAsync(effect));
    }

    /// <summary>Discards pending frames and writes this color right away (e.g. before a firmware effect).</summary>
    public Task SetColorNowAsync(Zone zone, ColorRGB color)
    {
        lock (_lock)
        {
            _pending.Clear();
            _lastQueued.Clear();
        }
        return OnBackendAsync(() => _backend.SetColorAsync(zone, color));
    }

    public Task SetHardwareBrightnessAsync(int level) => OnBackendAsync(() => _backend.SetHardwareBrightnessAsync(level));

    /// <summary>Writes every pending color first, then lets the backend persist that exact state.</summary>
    public async Task SaveStateAsync()
    {
        await _backendGate.WaitAsync();
        try
        {
            List<(Zone zone, ColorRGB color)> pending;
            lock (_lock)
            {
                pending = new List<(Zone, ColorRGB)>(_pending);
                _pending.Clear();
            }
            foreach (var (zone, color) in pending)
            {
                try { await _backend.SetColorAsync(zone, color); }
                catch (Exception) { Invalidate(); }
            }
            await _backend.SaveStateAsync();
        }
        finally
        {
            _backendGate.Release();
        }
    }

    public Task SetBootEffectAsync(bool enabled) => OnBackendAsync(() => _backend.SetBootEffectAsync(enabled));

    public Task SetSleepTimerAsync(int? seconds) => OnBackendAsync(() => _backend.SetSleepTimerAsync(seconds));

    private async Task ProcessQueue()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_cts.Token);
                (Zone zone, ColorRGB color) item;
                lock (_lock)
                {
                    if (_pending.First == null) continue; // coalesced away
                    item = _pending.First.Value;
                    _pending.RemoveFirst();
                }

                await _backendGate.WaitAsync(_cts.Token);
                try
                {
                    await _backend.SetColorAsync(item.zone, item.color);
                }
                catch (Exception)
                {
                    Invalidate(); // failed write: allow the same color to be retried
                }
                finally
                {
                    _backendGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        catch (ObjectDisposedException)
        {
            // Disposed while a blocked backend call was still running
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
    }
}
