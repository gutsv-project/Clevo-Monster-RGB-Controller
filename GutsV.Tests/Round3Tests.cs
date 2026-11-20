using System.Text.Json;
using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;
using GutsV.Core.Settings;

namespace GutsV.Tests;

// Third-round review: command 121 (boot effect / sleep timer), checked against Acer CC40 BaseKB / KBModule.
public class Round3Tests
{
    private sealed class Rec : IKeyboardController
    {
        public readonly List<string> Log = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public Task SetColorAsync(Zone zone, ColorRGB color) => Task.CompletedTask;
        public Task SetBootEffectAsync(bool enabled) { lock (Log) Log.Add($"boot:{enabled}"); return Task.CompletedTask; }
        public Task SetSleepTimerAsync(int? s) { lock (Log) Log.Add($"sleep:{s?.ToString() ?? "off"}"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Queue_forwards_command_121_calls_instead_of_interface_default()
    {
        var be = new Rec();
        var q = new AsyncDriverQueue(be);
        await q.InitializeAsync();
        await q.SetBootEffectAsync(true);
        await q.SetSleepTimerAsync(45);
        await q.SetSleepTimerAsync(null);
        Assert.Equal(new[] { "boot:True", "sleep:45", "sleep:off" }, be.Log);
        await q.ShutdownAsync();
    }

    // CC40 BaseKB.SetSleepTimerTrigger: (0x18000000 + (v << 8)) | 0xFF
    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(7199)]   // CC40 UI maximum (hours combo 0..1)
    public void Sleep_word_equals_cc40_formula(int s) =>
        Assert.Equal((uint)((402653184 + (s << 8)) | 0xFF), ClevoLed.SleepTimerWord(s));

    [Fact]
    public void Sleep_word_is_capped_at_cc40_maximum() =>
        Assert.Equal(ClevoLed.SleepTimerWord(7199), ClevoLed.SleepTimerWord(65535));

    [Fact]
    public void Old_settings_file_leaves_firmware_options_untouched()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("{\"Red\":1}")!;
        Assert.Null(s.BootEffectEnabled);
        Assert.Null(s.SleepTimerEnabled);
        Assert.Equal(30, s.SleepTimerSeconds);
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { SleepTimerEnabled = false, BootEffectEnabled = true }))!;
        Assert.False(back.SleepTimerEnabled);
        Assert.True(back.BootEffectEnabled);
    }
}
