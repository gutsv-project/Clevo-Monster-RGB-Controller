using GutsV.Core.Animations;
using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;
using GutsV.Drivers;

namespace GutsV.Tests;

// Records what reaches the "hardware"; can block to fill the queue.
internal sealed class FakeController : IKeyboardController
{
    public readonly List<(Zone zone, ColorRGB color)> Writes = new();
    public readonly ManualResetEventSlim Gate = new(true);
    public IReadOnlyList<Zone> Zones { get; init; } = new[] { Zone.Left, Zone.Center, Zone.Right };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;

    public Task SetColorAsync(Zone zone, ColorRGB color)
    {
        Gate.Wait();
        lock (Writes) Writes.Add((zone, color));
        return Task.CompletedTask;
    }
}

public class AsyncDriverQueueTests
{
    private static async Task WaitFor(Func<bool> cond)
    {
        for (int i = 0; i < 200 && !cond(); i++) await Task.Delay(10);
        Assert.True(cond());
    }

    [Fact]
    public async Task Burst_while_hardware_stalls_ends_on_the_latest_color()
    {
        var fake = new FakeController();
        var q = new AsyncDriverQueue(fake);
        await q.InitializeAsync();

        fake.Gate.Reset(); // hardware stalls
        for (byte i = 1; i <= 10; i++) await q.SetColorAsync(Zone.Left, new ColorRGB(i, 0, 0));
        var target = new ColorRGB(200, 100, 50);
        await q.SetColorAsync(Zone.Left, target);
        fake.Gate.Set();

        // Pending frames for the same zone are coalesced: the last write must be the latest color
        await WaitFor(() => { lock (fake.Writes) return fake.Writes.Count > 0 && fake.Writes[^1].color == target; });
        await Task.Delay(100);
        lock (fake.Writes)
        {
            Assert.Equal(target, fake.Writes[^1].color);
            Assert.True(fake.Writes.Count <= 2); // at most the in-flight frame + the latest one
        }
        await q.ShutdownAsync();
    }

    [Fact]
    public async Task Same_color_is_not_sent_twice()
    {
        var fake = new FakeController();
        var q = new AsyncDriverQueue(fake);
        await q.InitializeAsync();
        var c = new ColorRGB(1, 2, 3);
        await q.SetColorAsync(Zone.All, c);
        await q.SetColorAsync(Zone.All, c);
        await q.SetColorAsync(Zone.Left, c); // already set by All
        await Task.Delay(100);
        lock (fake.Writes) Assert.Single(fake.Writes);
        await q.ShutdownAsync();
    }

    [Fact]
    public async Task Zones_missing_on_the_keyboard_are_ignored()
    {
        var fake = new FakeController(); // 3 zones, no Zone4
        var q = new AsyncDriverQueue(fake);
        await q.InitializeAsync();
        await q.SetColorAsync(Zone.Zone4, ColorRGB.Red);
        await Task.Delay(100);
        lock (fake.Writes) Assert.Empty(fake.Writes);
        await q.ShutdownAsync();
    }

    [Fact]
    public async Task Single_zone_keyboard_keeps_legacy_left_only_behavior()
    {
        var fake = new FakeController { Zones = new[] { Zone.All } };
        var q = new AsyncDriverQueue(fake);
        await q.InitializeAsync();
        await q.SetColorAsync(Zone.Center, ColorRGB.Green); // ignored like before
        await q.SetColorAsync(Zone.Left, ColorRGB.Red);
        await WaitFor(() => { lock (fake.Writes) return fake.Writes.Count == 1; });
        lock (fake.Writes) Assert.Equal(ColorRGB.Red, fake.Writes[0].color);
        await q.ShutdownAsync();
    }
}

public class ClevoLedTests
{
    [Fact]
    public void Color_word_matches_old_GutsV_bytes_for_zone_1()
    {
        // Old InsydeDriver sent [G, R, B, 0xF0]
        var bytes = ClevoLed.ToBytes(ClevoLed.ColorWord(Zone.Left, new ColorRGB(0x11, 0x22, 0x33)));
        Assert.Equal(new byte[] { 0x22, 0x11, 0x33, 0xF0 }, bytes);
    }

    [Theory]
    [InlineData(Zone.Left, 0xF0)]
    [InlineData(Zone.Center, 0xF1)]
    [InlineData(Zone.Right, 0xF2)]
    [InlineData(Zone.LightBar, 0xF3)]
    [InlineData(Zone.Logo, 0xF6)]
    public void Zone_codes_match_Acer_control_center(Zone zone, int code) =>
        Assert.Equal((byte)code, ClevoLed.ZoneCode(zone));

    [Theory]
    [InlineData(0, 0xF4000000u)]
    [InlineData(1, 0xF400002Fu)]
    [InlineData(4, 0xF40000BFu)]
    [InlineData(9, 0xF40000BFu)]
    public void Brightness_steps(int level, uint word) => Assert.Equal(word, ClevoLed.BrightnessWord(level));

    [Fact]
    public void Zone_all_must_be_expanded_by_caller() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ClevoLed.ZoneCode(Zone.All));
}

public class DeviceSelectionTests
{
    [Theory]
    [InlineData("Acer", true)]
    [InlineData("ACER INC.", true)]
    [InlineData("MONSTER", false)]
    [InlineData("Notebook", false)]
    [InlineData(null, false)]
    public void Acer_prefers_insyde(string? manufacturer, bool expected) =>
        Assert.Equal(expected, DeviceManager.PreferInsyde(manufacturer));
}

public class AnimationTests
{
    [Fact]
    public void Wave_drives_all_four_zones_and_keeps_its_timing()
    {
        var frames = new ColorShiftAnimation().GenerateFrames().ToList();
        Assert.Equal(3, frames.Count(f => f.TargetZone == Zone.Zone4));
        Assert.Equal(1500, frames.Sum(f => f.DurationMs));
    }

    [Fact]
    public void Brightness_scales_color()
    {
        Assert.Equal(new ColorRGB(100, 50, 0), new ColorRGB(200, 100, 0).ApplyBrightness(0.5f));
        Assert.Equal(ColorRGB.Black, ColorRGB.White.ApplyBrightness(0f));
    }
}
