using GutsV.Core.Drivers;
using GutsV.Core.Models;

namespace GutsV.Tests;

// Fourth-round review: multi-zone selection + per-zone preview.
public class Round4Tests
{
    private static readonly ColorRGB A = new(10, 0, 0), B = new(0, 20, 0), C = new(0, 0, 30);

    // MainViewModel.SendAllZonesAsync(now: true) with differing zone colors sends the first zone via
    // SetColorNowAsync and queues the rest, then ApplyHardwareEffect calls SetHardwareEffectAsync, which
    // clears the pending queue. The worker is not started here to model "worker has not reached them yet".
    // Design: an effect discards frames still waiting in the queue (they would overwrite the effect).
    // That is why MainViewModel.SendAllZonesAsync(now: true) writes every zone directly instead
    // (see the next test); this one pins the queue behavior the ViewModel relies on.
    [Fact]
    public async Task Effect_discards_frames_still_waiting_in_the_queue()
    {
        var be = new ScriptedBackend { Zones = new[] { Zone.Left, Zone.Center, Zone.Right } };
        var q = new AsyncDriverQueue(be);
        await q.SetColorNowAsync(Zone.Left, A);
        await q.SetColorAsync(Zone.Center, B);    // queued, worker not started yet
        await q.SetColorAsync(Zone.Right, C);
        await q.SetHardwareEffectAsync(HardwareEffect.Breathe);
        await q.InitializeAsync();
        await Task.Delay(100);
        Assert.Equal(new[] { (Zone.Left, A) }, be.Snapshot());
        Assert.Single(be.Effects);
        await q.ShutdownAsync();
    }

    // Smallest fix: send every zone through SetColorNowAsync when now == true.
    [Fact]
    public async Task Effect_after_SetColorNow_for_every_zone_keeps_every_zone_color()
    {
        var be = new ScriptedBackend { Zones = new[] { Zone.Left, Zone.Center, Zone.Right } };
        var q = new AsyncDriverQueue(be);
        await q.SetColorNowAsync(Zone.Left, A);
        await q.SetColorNowAsync(Zone.Center, B);
        await q.SetColorNowAsync(Zone.Right, C);
        await q.SetHardwareEffectAsync(HardwareEffect.Breathe);
        Assert.Equal(new[] { (Zone.Left, A), (Zone.Center, B), (Zone.Right, C) }, be.Snapshot());
        Assert.Single(be.Effects);
    }

    // Wave sends Zone4 on a 3-zone WMI keyboard: the queue drops it and the preview maps it nowhere.
    [Fact]
    public async Task Zone4_frame_on_three_zone_keyboard_is_ignored_by_queue_and_preview()
    {
        var zones = new[] { Zone.Left, Zone.Center, Zone.Right };
        var be = new ScriptedBackend { Zones = zones };
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        await q.SetColorAsync(Zone.Zone4, A);
        await q.SetColorAsync(Zone.Left, B);
        await Wait.Until(() => be.Snapshot().Count == 1);
        Assert.Equal((Zone.Left, B), be.Snapshot()[0]);
        Assert.Empty(ZoneLayout.Affected(ZoneLayout.Describe(zones), Zone.Zone4));
        await q.ShutdownAsync();
    }

    // Brightness re-send after a static per-zone apply: unchanged zones are deduplicated, changed ones sent.
    [Fact]
    public async Task Per_zone_resend_only_writes_changed_zones()
    {
        var be = new ScriptedBackend { Zones = new[] { Zone.Left, Zone.Center, Zone.Right, Zone.LightBar, Zone.Logo } };
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        await q.SetColorAsync(Zone.All, A);
        await Wait.Until(() => be.Snapshot().Count == 1);
        await q.SetColorAsync(Zone.Left, A);   // unchanged: skipped
        await q.SetColorAsync(Zone.Logo, B);   // changed
        await Wait.Until(() => be.Snapshot().Count == 2);
        await Task.Delay(50);
        Assert.Equal(new[] { (Zone.All, A), (Zone.Logo, B) }, be.Snapshot());
        await q.ShutdownAsync();
    }
}

public class SaveStateOrderingTests
{
    private sealed class Recorder : GutsV.Core.Interfaces.IKeyboardController
    {
        public readonly List<string> Log = new();
        public IReadOnlyList<Zone> Zones => new[] { Zone.Left, Zone.Center, Zone.Right, Zone.Zone4 };
        public Task InitializeAsync() => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public Task SetColorAsync(Zone z, ColorRGB c) { lock (Log) Log.Add($"color:{z}"); return Task.CompletedTask; }
        public Task SaveStateAsync() { lock (Log) Log.Add("save"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Save_state_runs_after_all_pending_colors()
    {
        var be = new Recorder();
        var q = new AsyncDriverQueue(be); // worker not started: colors stay pending
        await q.SetColorAsync(Zone.Left, ColorRGB.Red);
        await q.SetColorAsync(Zone.Zone4, ColorRGB.Blue);
        await q.SaveStateAsync();
        Assert.Equal(new[] { "color:Left", "color:Zone4", "save" }, be.Log);
    }
}
