using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;

namespace GutsV.Tests;

// Second-round review checks (spec: feedback/PROTOCOL.md, PROTOCOL_ZONE4.md).
public class Round2Tests
{
    private static readonly ColorRGB[] Six =
    {
        new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12), new(13, 14, 15), new(16, 17, 18)
    };

    [Fact]
    public void Packet44_off_zeroes_every_brightness_byte_but_keeps_colors_and_mode()
    {
        var p = ClevoLed.BuildPacket44(5, 2, 0, Six, hasLightBar: true, hasLogo: true);
        Assert.Equal(new byte[] { 44, 5, 2 }, p[0..3]);
        Assert.All(p[3..16], b => Assert.Equal(0, b));
        Assert.Equal(0, p[16]);
        Assert.Equal(0, p[32]);
        Assert.Equal(0, p[36]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, p[17..32]);
        Assert.Equal(new byte[] { 16, 17, 18 }, p[33..36]);
        Assert.All(p[37..], b => Assert.Equal(0, b)); // zero padding to 256
    }

    [Theory]
    [InlineData(1, 31)]
    [InlineData(2, 46)]
    [InlineData(3, 87)]
    [InlineData(4, 128)]
    public void Packet44_light_bar_table_B(int level, int expected) =>
        Assert.Equal(expected, ClevoLed.BuildPacket44(0xFF, 3, level, Six, true, false)[32]);

    [Fact]
    public void Legacy_word_bytes_are_G_R_B_F0() =>
        Assert.Equal(new byte[] { 0x22, 0x11, 0x33, 0xF0 },
            ClevoLed.ToBytes(ClevoLed.ColorWord(Zone.Left, new ColorRGB(0x11, 0x22, 0x33))));

    [Fact]
    public void Light_bar_and_logo_word_codes() =>
        Assert.Equal((0xF3u, 0xF6u), ((uint)ClevoLed.ZoneCode(Zone.LightBar), (uint)ClevoLed.ZoneCode(Zone.Logo)));

    [Fact]
    public void Type2_blue_rounds_like_acer() =>
        // Math.Round(200/255*128) = round(100.39) = 100
        Assert.Equal(new ColorRGB(1, 2, 100), ClevoLed.VendorColorCorrection(2, new ColorRGB(1, 2, 128)));

    [Fact]
    public void Type22_odd_values_truncate() =>
        Assert.Equal(new ColorRGB(127, 0, 35), ClevoLed.VendorColorCorrection(22, new ColorRGB(255, 1, 70)));

    [Fact]
    public void Effect_words_match_spec()
    {
        Assert.Equal(0x1002A000u, ClevoLed.EffectWord(HardwareEffect.Breathe));
        Assert.Equal(0x33010000u, ClevoLed.EffectWord(HardwareEffect.Cycle));
        Assert.Equal(0xA0000000u, ClevoLed.EffectWord(HardwareEffect.Flash));
    }

    // Records colors, effects and brightness in one ordered log; brightness can block while holding the gate.
    private sealed class OrderBackend : IKeyboardController
    {
        public readonly List<string> Log = new();
        public readonly ManualResetEventSlim BrightnessGate = new(true);
        public IReadOnlyList<Zone> Zones { get; } = new[] { Zone.Left, Zone.Center, Zone.Right, Zone.Zone4, Zone.Logo };
        public Task InitializeAsync() => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public Task SetColorAsync(Zone zone, ColorRGB color) { lock (Log) Log.Add($"C{zone}:{color.R}"); return Task.CompletedTask; }
        public Task SetHardwareEffectAsync(HardwareEffect e) { lock (Log) Log.Add("E"); return Task.CompletedTask; }
        public Task SetHardwareBrightnessAsync(int level)
        {
            return Task.Run(() => { BrightnessGate.Wait(); lock (Log) Log.Add($"B{level}"); });
        }
        public List<string> Snap() { lock (Log) return Log.ToList(); }
    }

    [Fact]
    public async Task Frame_already_waiting_for_gate_runs_before_a_later_effect()
    {
        var be = new OrderBackend();
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();

        be.BrightnessGate.Reset();
        var bri = q.SetHardwareBrightnessAsync(2);   // holds the backend gate
        await q.SetColorAsync(Zone.Left, new ColorRGB(9, 0, 0));
        await Task.Delay(100);                        // worker dequeues the frame and waits on the gate
        var eff = q.SetHardwareEffectAsync(HardwareEffect.Wave);
        be.BrightnessGate.Set();
        await Task.WhenAll(bri, eff);
        await Wait.Until(() => be.Snap().Count == 3);

        // The effect must be last, otherwise a stale static frame would cancel it on the keyboard
        Assert.Equal(new[] { "B2", "CLeft:9", "E" }, be.Snap());
        await q.ShutdownAsync();
    }

    [Fact] // was skipped: fixed by per-zone coalescing queue
    public async Task Dropped_one_shot_zone_color_eventually_reaches_keyboard()
    {
        var be = new ScriptedBackend();
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        be.Gate.Reset();
        await q.SetColorAsync(Zone.Left, new ColorRGB(1, 1, 1));   // worker takes it and blocks
        await Wait.Until(() => be.Attempts == 1);
        var user = new ColorRGB(200, 0, 0);
        await q.SetColorAsync(Zone.Center, user);                  // user clicks Center = red
        await q.SetColorAsync(Zone.Right, new ColorRGB(2, 2, 2));
        await q.SetColorAsync(Zone.Zone4, new ColorRGB(2, 2, 2));
        await q.SetColorAsync(Zone.Logo, new ColorRGB(2, 2, 2));
        await q.SetColorAsync(Zone.Left, new ColorRGB(2, 2, 2));   // queue now full (5)
        await q.SetColorAsync(Zone.Left, new ColorRGB(3, 3, 3));   // drops Center = red
        be.Gate.Set();
        await Task.Delay(300);
        Assert.Equal(user, be.Last(Zone.Center));
        await q.ShutdownAsync();
    }
}
