using System.Text.Json;
using GutsV.Core.Animations;
using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;
using GutsV.Core.Settings;

namespace GutsV.Tests;

internal static class Wait
{
    // Polls a condition instead of sleeping a fixed time; generous timeout for loaded CI machines.
    public static async Task Until(Func<bool> cond, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(5);
        Assert.True(cond(), "condition not met within timeout");
    }
}

// Backend with 4 zones + Logo; can block, can throw on chosen calls, counts attempts.
internal sealed class ScriptedBackend : IKeyboardController
{
    public readonly List<(Zone zone, ColorRGB color)> Writes = new();
    public readonly ManualResetEventSlim Gate = new(true);
    public int Attempts;
    public int ThrowOnAttempt = -1; // 1-based attempt number that throws
    public readonly List<HardwareEffect> Effects = new();
    public IReadOnlyList<Zone> Zones { get; init; } = new[] { Zone.Left, Zone.Center, Zone.Right, Zone.Zone4, Zone.Logo };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public Task SetHardwareEffectAsync(HardwareEffect effect) { lock (Effects) Effects.Add(effect); return Task.CompletedTask; }

    public Task SetColorAsync(Zone zone, ColorRGB color)
    {
        int n = Interlocked.Increment(ref Attempts);
        Gate.Wait();
        if (n == ThrowOnAttempt) throw new IOException("simulated WMI failure");
        lock (Writes) Writes.Add((zone, color));
        return Task.CompletedTask;
    }

    public List<(Zone zone, ColorRGB color)> Snapshot() { lock (Writes) return Writes.ToList(); }
    public ColorRGB? Last(Zone z) { var w = Snapshot().FindLast(x => x.zone == z); return w.zone == z ? w.color : null; }
    public int Count(ColorRGB c) => Snapshot().Count(w => w.color == c);
}

public class AsyncDriverQueueConcurrencyTests
{
    [Fact]
    public async Task Concurrent_writers_on_different_zones_final_color_reaches_backend()
    {
        var be = new ScriptedBackend();
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        var zones = be.Zones.ToArray();
        ColorRGB Final(int i) => new((byte)(10 + i), 255, 255);

        await Task.WhenAll(zones.Select((z, i) => Task.Run(async () =>
        {
            for (int k = 0; k < 300; k++) await q.SetColorAsync(z, new ColorRGB((byte)k, (byte)i, 0));
            await q.SetColorAsync(z, Final(i));
        })));

        // Frames may be dropped while the queue is full (by design); callers resend like an animation loop does.
        await Wait.Until(() =>
        {
            bool all = true;
            for (int i = 0; i < zones.Length; i++)
            {
                if (be.Last(zones[i]) == Final(i)) continue;
                all = false;
                q.SetColorAsync(zones[i], Final(i));
            }
            return all;
        });
        await q.ShutdownAsync();
    }

    [Fact]
    public async Task Hardware_effect_invalidates_dirty_cache()
    {
        var be = new ScriptedBackend();
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        var c = new ColorRGB(7, 8, 9);

        await q.SetColorAsync(Zone.Left, c);
        await Wait.Until(() => be.Count(c) == 1);
        await q.SetHardwareEffectAsync(HardwareEffect.Wave);
        await q.SetColorAsync(Zone.Left, c); // firmware overwrote the LEDs: must be resent
        await Wait.Until(() => be.Count(c) == 2);
        Assert.Equal(new[] { HardwareEffect.Wave }, be.Effects);
        await q.ShutdownAsync();
    }

    [Fact]
    public async Task Zone_all_on_four_zone_plus_logo_board_is_one_write_and_marks_every_zone()
    {
        var be = new ScriptedBackend();
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        var c = new ColorRGB(1, 2, 3);

        await q.SetColorAsync(Zone.All, c);
        await Wait.Until(() => be.Snapshot().Count == 1);
        foreach (var z in be.Zones) await q.SetColorAsync(z, c); // all already that color
        await q.SetColorAsync(Zone.Logo, ColorRGB.Red);           // only Logo changes
        await Wait.Until(() => be.Snapshot().Count == 2);
        await q.ShutdownAsync();

        var w = be.Snapshot();
        Assert.Equal((Zone.All, c), w[0]);
        Assert.Equal((Zone.Logo, ColorRGB.Red), w[1]);
    }

    [Fact]
    public async Task Failed_write_is_retried_on_next_identical_request()
    {
        var be = new ScriptedBackend { ThrowOnAttempt = 1 };
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        var c = new ColorRGB(50, 60, 70);

        await q.SetColorAsync(Zone.Center, c);
        await Wait.Until(() => Volatile.Read(ref be.Attempts) >= 1);
        // Invalidate runs right after the throw on the worker; resend the identical color until it lands.
        await Wait.Until(() => { q.SetColorAsync(Zone.Center, c); return be.Count(c) == 1; });
        Assert.True(be.Attempts >= 2);
        await q.ShutdownAsync();
    }

    [Fact]
    public async Task Shutdown_completes_when_backend_blocks_forever()
    {
        var be = new ScriptedBackend();
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        be.Gate.Reset();
        await q.SetColorAsync(Zone.Left, ColorRGB.Red);
        await Wait.Until(() => Volatile.Read(ref be.Attempts) == 1); // worker is stuck in the backend

        var shutdown = q.ShutdownAsync();
        var done = await Task.WhenAny(shutdown, Task.Delay(5000));
        be.Gate.Set();
        Assert.Same(shutdown, done);
        await shutdown;
    }
}

internal sealed class LambdaAnimation(Func<IEnumerable<AnimationFrame>> frames) : IAnimation
{
    public string Name => "test";
    public IEnumerable<AnimationFrame> GenerateFrames() => frames();
}

internal sealed class DisposableAnimation : IAnimation, IDisposable
{
    public int Disposed;
    public string Name => "disposable";
    public IEnumerable<AnimationFrame> GenerateFrames() { while (true) yield return new AnimationFrame(ColorRGB.Red, 1000); }
    public void Dispose() => Interlocked.Increment(ref Disposed);
}

public class AnimationEngineTests
{
    private static IAnimation Steady(ColorRGB c, int ms = 1000) =>
        new LambdaAnimation(() => Enumerable.Repeat(new AnimationFrame(c, ms), 1));

    [Fact]
    public async Task Rapid_start_stop_keeps_IsRunning_consistent()
    {
        var be = new ScriptedBackend();
        var engine = new AnimationEngine(be);
        var anims = new List<DisposableAnimation>();
        for (int i = 0; i < 100; i++)
        {
            var a = new DisposableAnimation();
            anims.Add(a);
            engine.Start(a);
            Assert.True(engine.IsRunning);
            if (i % 2 == 0) { engine.Stop(); Assert.False(engine.IsRunning); }
        }
        engine.Start(Steady(ColorRGB.Blue));
        await Wait.Until(() => be.Snapshot().Contains((Zone.All, ColorRGB.Blue)));
        Assert.True(engine.IsRunning); // stale loops finishing must not clear the flag of the current one
        engine.Stop();
        Assert.False(engine.IsRunning);
        Assert.All(anims, a => Assert.Equal(1, a.Disposed));
    }

    [Fact]
    public async Task Error_event_fires_and_running_flag_clears_when_animation_throws()
    {
        var engine = new AnimationEngine(new ScriptedBackend());
        Exception? seen = null;
        engine.Error += ex => seen = ex;
        engine.Start(new LambdaAnimation(() => throw new InvalidOperationException("boom")));
        await Wait.Until(() => seen != null && !engine.IsRunning);
        Assert.IsType<InvalidOperationException>(seen);
    }

    [Fact]
    public async Task LastFrameColor_is_undimmed_while_FrameApplied_and_backend_get_dimmed()
    {
        var be = new ScriptedBackend();
        var engine = new AnimationEngine(be) { Brightness = 0.5f };
        ColorRGB? applied = null;
        engine.FrameApplied += (c, _) => applied = c;
        engine.Start(Steady(new ColorRGB(200, 100, 0)));
        await Wait.Until(() => applied != null);
        engine.Stop();

        Assert.Equal(new ColorRGB(100, 50, 0), applied);
        Assert.Equal(new ColorRGB(200, 100, 0), engine.LastFrameColor);
        Assert.Equal((Zone.All, new ColorRGB(100, 50, 0)), be.Snapshot()[0]);
    }

    [Fact]
    public void Stop_disposes_disposable_animation_once()
    {
        var engine = new AnimationEngine(new ScriptedBackend());
        var a = new DisposableAnimation();
        engine.Start(a);
        engine.Stop();
        engine.Stop();
        Assert.Equal(1, a.Disposed);
    }
}

public class PureAnimationTests
{
    public static TheoryData<string, int, int> Cycles => new()
    {
        // name, frames in one cycle, total cycle duration (ms)
        { nameof(BreatheAnimation), 3 * 120, 3 * 120 * 25 },
        { nameof(FreshBreatheAnimation), 7 * 60, 7 * 60 * 25 },
        { nameof(ColorTransformAnimation), 3 * 41, 3 * 41 * 25 },
        { nameof(PulsatingBlinkAnimation), 4 * 3, 4 * 2200 },
        { nameof(ColorShiftAnimation), 12, 1500 },
    };

    private static IAnimation Create(string name) => name switch
    {
        nameof(BreatheAnimation) => new BreatheAnimation(),
        nameof(FreshBreatheAnimation) => new FreshBreatheAnimation(),
        nameof(ColorTransformAnimation) => new ColorTransformAnimation(),
        nameof(PulsatingBlinkAnimation) => new PulsatingBlinkAnimation(),
        _ => new ColorShiftAnimation(),
    };

    [Theory, MemberData(nameof(Cycles))]
    public void First_cycle_is_finite_with_expected_length_and_timing(string name, int frames, int totalMs)
    {
        var anim = Create(name);
        var list = anim.GenerateFrames().Take(100_000).ToList();
        Assert.Equal(frames, list.Count);
        Assert.Equal(totalMs, list.Sum(f => f.DurationMs));
        Assert.All(list, f => Assert.True(f.DurationMs >= 0));
        Assert.Equal(list, anim.GenerateFrames().ToList()); // restartable: engine re-enumerates each cycle
        Assert.False(string.IsNullOrWhiteSpace(anim.Name));
    }

    [Theory]
    [InlineData(nameof(BreatheAnimation))]
    [InlineData(nameof(FreshBreatheAnimation))]
    [InlineData(nameof(ColorTransformAnimation))]
    [InlineData(nameof(PulsatingBlinkAnimation))]
    public void Whole_keyboard_animations_target_all_and_never_stall(string name)
    {
        var list = Create(name).GenerateFrames().ToList();
        Assert.All(list, f => Assert.Equal(Zone.All, f.TargetZone));
        Assert.All(list, f => Assert.True(f.DurationMs > 0));
    }

    [Fact]
    public void Breathe_fades_from_black_and_peaks_near_full_per_channel()
    {
        var list = new BreatheAnimation().GenerateFrames().ToList();
        Assert.Equal(ColorRGB.Black, list[0].Color);
        Assert.Equal(255, list.Take(120).Max(f => f.Color.R));
        Assert.Equal(255, list.Skip(120).Take(120).Max(f => f.Color.G));
        Assert.Equal(255, list.Skip(240).Max(f => f.Color.B));
        Assert.All(list.Take(120), f => Assert.True(f.Color.G == 0 && f.Color.B == 0));
    }

    [Fact]
    public void ColorTransform_starts_and_ends_on_red_and_hits_each_primary()
    {
        var c = new ColorTransformAnimation().GenerateFrames().Select(f => f.Color).ToList();
        Assert.Equal(ColorRGB.Red, c[0]);
        Assert.Equal(ColorRGB.Red, c[^1]);
        Assert.Contains(ColorRGB.Green, c);
        Assert.Contains(ColorRGB.Blue, c);
        Assert.All(c, x => Assert.InRange(x.R + x.G + x.B, 250, 256)); // linear blend between primaries
    }
}

public class SettingsCompatibilityTests
{
    [Fact]
    public void Old_settings_without_hardware_fields_get_defaults()
    {
        const string old = """
        { "Red": 10, "Green": 20, "Blue": 30, "DriverType": 1, "LastAnimation": "Wave",
          "Brightness": 55, "IsStartupEnabled": true, "IsAnimationActive": true, "AnimationSpeed": 2.5 }
        """;
        var s = JsonSerializer.Deserialize<AppSettings>(old)!;
        Assert.Null(s.HardwareBrightness); // never set -> firmware brightness left alone
        Assert.False(s.IsHardwareEffectActive);
        Assert.Equal(0x1002A000u, s.HardwareEffect);
        Assert.Equal((uint)HardwareEffect.Breathe, s.HardwareEffect);
        Assert.Equal((byte)20, s.Green);
        Assert.Equal("Wave", s.LastAnimation);
        Assert.Equal(2.5, s.AnimationSpeed);
    }

    [Fact]
    public void Round_trip_keeps_all_fields()
    {
        var s = new AppSettings
        {
            Red = 1, Green = 2, Blue = 3, DriverType = 2, LastAnimation = "Neon Pulse", Brightness = 42.5,
            IsStartupEnabled = true, IsAnimationActive = true, AnimationSpeed = 0.25,
            HardwareBrightness = 0, HardwareEffect = (uint)HardwareEffect.Flash, IsHardwareEffectActive = true,
            BootEffectEnabled = false, SleepTimerEnabled = true, SleepTimerSeconds = 125,
            ZoneColors = new() { ["Left"] = "#FF0000", ["Zone4"] = "#00FF00" },
            SelectedZones = new() { "Center", "Logo" },
            Packet44MinGapMs = 150,
        };
        var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(0, back.HardwareBrightness);
        Assert.Equal((uint)HardwareEffect.Flash, back.HardwareEffect);
        Assert.True(back.IsHardwareEffectActive);
        Assert.False(back.BootEffectEnabled);
        Assert.True(back.SleepTimerEnabled);
        Assert.Equal(125, back.SleepTimerSeconds);
        Assert.Equal("#00FF00", back.ZoneColors!["Zone4"]);
        Assert.Equal(new[] { "Center", "Logo" }, back.SelectedZones);
        Assert.Equal(150, back.Packet44MinGapMs);
        // Guard: every public property is covered by this round trip
        Assert.Equal(18, typeof(AppSettings).GetProperties().Length);
    }
}

public class ClevoLedWordTests
{
    public static TheoryData<HardwareEffect> Effects => new(Enum.GetValues<HardwareEffect>());

    [Theory, MemberData(nameof(Effects))]
    public void Every_effect_round_trips_through_bytes(HardwareEffect e)
    {
        var bytes = ClevoLed.ToBytes(ClevoLed.EffectWord(e));
        Assert.Equal(4, bytes.Length);
        Assert.Equal(e, (HardwareEffect)BitConverter.ToUInt32(bytes));
        Assert.Equal((byte)((uint)e >> 24), bytes[3]); // mode byte is last (little endian)
    }

    [Theory]
    [InlineData(Zone.Left, 0xF0)]
    [InlineData(Zone.Center, 0xF1)]
    [InlineData(Zone.Right, 0xF2)]
    [InlineData(Zone.LightBar, 0xF3)]
    [InlineData(Zone.Logo, 0xF6)]
    public void Color_word_layout_is_G_R_B_zone_for_every_zone(Zone zone, int code)
    {
        var c = new ColorRGB(0xAB, 0xCD, 0xEF);
        Assert.Equal(new byte[] { 0xCD, 0xAB, 0xEF, (byte)code }, ClevoLed.ToBytes(ClevoLed.ColorWord(zone, c)));
        Assert.Equal(((uint)code << 24) | 0xEFABCDu, ClevoLed.ColorWord(zone, c));
        // extremes must not bleed between bytes
        Assert.Equal(new byte[] { 0, 0, 0, (byte)code }, ClevoLed.ToBytes(ClevoLed.ColorWord(zone, ColorRGB.Black)));
        Assert.Equal(new byte[] { 255, 255, 255, (byte)code }, ClevoLed.ToBytes(ClevoLed.ColorWord(zone, ColorRGB.White)));
    }

    [Fact]
    public void Brightness_word_bytes_and_negative_clamp()
    {
        Assert.Equal(new byte[] { 95, 0, 0, 0xF4 }, ClevoLed.ToBytes(ClevoLed.BrightnessWord(2)));
        Assert.Equal(0xF4000000u, ClevoLed.BrightnessWord(-3));
    }
}
