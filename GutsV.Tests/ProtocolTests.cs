using GutsV.Core.Drivers;
using GutsV.Core.Models;

namespace GutsV.Tests;

// Expected values come from the decompiled Acer Control Center (feedback/PROTOCOL*.md).
public class Packet44Tests
{
    private static readonly ColorRGB[] Zones =
    {
        new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12), new(13, 14, 15), new(16, 17, 18)
    };

    [Fact]
    public void Static_packet_has_acer_layout()
    {
        var p = ClevoLed.BuildPacket44(ClevoLed.Packet44StaticMode, 3, 4, Zones, hasLightBar: false, hasLogo: false);
        Assert.Equal(256, p.Length);
        Assert.Equal(44, p[0]);
        Assert.Equal(0xFF, p[1]);
        Assert.Equal(3, p[2]);
        Assert.Equal(191, p[16]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, p[17..29]); // R,G,B per section
        Assert.All(p[29..37], b => Assert.Equal(0, b)); // no light bar / logo
    }

    [Fact]
    public void Light_bar_and_logo_are_filled_only_when_present()
    {
        var p = ClevoLed.BuildPacket44(5, 1, 2, Zones, hasLightBar: true, hasLogo: true);
        Assert.Equal(new byte[] { 13, 14, 15 }, p[29..32]);
        Assert.Equal(46, p[32]);   // light-bar table {31,46,87,128}
        Assert.Equal(new byte[] { 16, 17, 18 }, p[33..36]);
        Assert.Equal(95, p[36]);   // logo uses keyboard table
        Assert.Equal(95, p[16]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 47)]
    [InlineData(3, 143)]
    [InlineData(9, 191)]
    [InlineData(-1, 0)]
    public void Brightness_table(int level, int expected) =>
        Assert.Equal(expected, ClevoLed.BuildPacket44(0xFF, 3, level, Zones, false, false)[16]);

    [Fact]
    public void Speed_is_clamped_to_1_3()
    {
        Assert.Equal(1, ClevoLed.BuildPacket44(0xFF, 0, 4, Zones, false, false)[2]);
        Assert.Equal(3, ClevoLed.BuildPacket44(0xFF, 9, 4, Zones, false, false)[2]);
    }

    [Theory]
    [InlineData(HardwareEffect.RandomColor, 6)]
    [InlineData(HardwareEffect.Breathe, 5)]
    [InlineData(HardwareEffect.Cycle, 7)]
    [InlineData(HardwareEffect.Wave, 4)]
    [InlineData(HardwareEffect.Dance, 1)]
    [InlineData(HardwareEffect.Tempo, 2)]
    [InlineData(HardwareEffect.Flash, 3)]
    public void Effect_codes(HardwareEffect e, int code) => Assert.Equal(code, ClevoLed.Packet44EffectCode(e));

    [Theory]
    [InlineData(Zone.Left, 0)]
    [InlineData(Zone.Center, 1)]
    [InlineData(Zone.Right, 2)]
    [InlineData(Zone.Zone4, 3)]
    [InlineData(Zone.LightBar, 4)]
    [InlineData(Zone.Logo, 5)]
    [InlineData(Zone.All, -1)]
    public void Slots(Zone z, int slot) => Assert.Equal(slot, ClevoLed.Packet44Slot(z));
}

public class WordProtocolTests
{
    [Fact]
    public void Zone4_has_no_word_code() => Assert.False(ClevoLed.HasWordCode(Zone.Zone4));

    [Fact]
    public void Flags_word_matches_acer_default() => Assert.Equal(0xE007B001u, ClevoLed.FlagsWord(hasLogo: true));

    [Fact]
    public void Flags_word_without_logo() => Assert.Equal(0xE0079001u, ClevoLed.FlagsWord(hasLogo: false));

    [Fact]
    public void Type2_scales_blue() =>
        Assert.Equal(new ColorRGB(255, 255, 200), ClevoLed.VendorColorCorrection(2, ColorRGB.White));

    [Fact]
    public void Type6_and_22_halve_and_fix_mint() 
    {
        Assert.Equal(new ColorRGB(127, 127, 127), ClevoLed.VendorColorCorrection(22, ColorRGB.White));
        Assert.Equal(new ColorRGB(0, 127, 35), ClevoLed.VendorColorCorrection(6, new ColorRGB(0, 255, 127)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(3)]
    public void Other_types_are_unchanged(int type) =>
        Assert.Equal(new ColorRGB(10, 20, 30), ClevoLed.VendorColorCorrection(type, new ColorRGB(10, 20, 30)));

    [Fact]
    public void Type38_brightness_table() => Assert.Equal(0xF40000D4u, ClevoLed.BrightnessWord(4, 38));

    [Fact]
    public void Wmi_brightness_reaches_full_scale() => Assert.Equal(0xF40000FFu, ClevoLed.WmiBrightnessWord(4));
}

// Command 121 words, from Acer CC40 BaseKB.SetSleepTimerTrigger / SetOverrideBootEffect
public class BootEffectSleepTimerTests
{
    [Fact]
    public void Boot_effect_words() 
    {
        Assert.Equal(0x1E000001u, ClevoLed.BootEffectWord(true));
        Assert.Equal(0x1E000000u, ClevoLed.BootEffectWord(false));
    }

    [Theory]
    [InlineData(30, 0x18001EFFu)]          // Acer default 30 s
    [InlineData(3600, 0x180E10FFu)]
    [InlineData(1, 0x180005FFu)]            // clamped to 5 s minimum
    [InlineData(100000, 0x181C1FFFu)]       // clamped to Acer's maximum 7199 s
    public void Sleep_timer_on_words(int seconds, uint word) => Assert.Equal(word, ClevoLed.SleepTimerWord(seconds));

    [Fact]
    public void Sleep_timer_off_word() => Assert.Equal(0x18000000u, ClevoLed.SleepTimerWord(null));

    [Fact]
    public void Sleep_timer_never_spills_into_command_byte()
    {
        for (int s = 0; s <= 70000; s += 997)
            Assert.Equal(0x18u, ClevoLed.SleepTimerWord(s) >> 24);
    }

    [Fact]
    public void Boot_effect_support_bit()
    {
        var w = new byte[256];
        Assert.False(ClevoLed.SupportsBootEffect(w));
        w[43] = 0x01;
        Assert.True(ClevoLed.SupportsBootEffect(w));
        w[43] = 0xFE;
        Assert.False(ClevoLed.SupportsBootEffect(w));
        Assert.False(ClevoLed.SupportsBootEffect(new byte[10]));
    }

    [Fact]
    public void Old_settings_leave_firmware_options_untouched()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<GutsV.Core.Settings.AppSettings>("""{ "Red": 1 }""")!;
        Assert.Null(s.BootEffectEnabled);
        Assert.Null(s.SleepTimerEnabled);
        Assert.Equal(30, s.SleepTimerSeconds);
    }
}

public class ZoneLayoutTests
{
    [Fact]
    public void Four_zone_acer_layout_uses_section_names_and_fills_15_columns()
    {
        var l = ZoneLayout.Describe(new[] { Zone.Left, Zone.Center, Zone.Right, Zone.Zone4, Zone.LightBar, Zone.Logo });
        Assert.Equal(new[] { "Section 1", "Section 2", "Section 3", "Section 4", "Light Bar", "Logo LED" }, l.Select(s => s.DisplayName));
        Assert.Equal(15, l.Where(s => s.IsKeyboardSection).Sum(s => s.KeyColumns));
        Assert.Equal(new[] { 4, 4, 4, 3 }, l.Where(s => s.IsKeyboardSection).Select(s => s.KeyColumns));
        Assert.False(l[4].IsKeyboardSection);
    }

    [Fact]
    public void Three_zone_layout_keeps_left_center_right()
    {
        var l = ZoneLayout.Describe(new[] { Zone.Left, Zone.Center, Zone.Right });
        Assert.Equal(new[] { "Left", "Center", "Right" }, l.Select(s => s.DisplayName));
        Assert.All(l, s => Assert.Equal(5, s.KeyColumns));
    }

    [Fact]
    public void Single_zone_layout_is_one_keyboard_block()
    {
        var l = ZoneLayout.Describe(new[] { Zone.All });
        Assert.Single(l);
        Assert.Equal(Zone.All, l[0].Zone);
        Assert.Equal(15, l[0].KeyColumns);
    }

    [Fact]
    public void Three_zone_with_light_bar_and_logo()
    {
        var l = ZoneLayout.Describe(new[] { Zone.Left, Zone.Center, Zone.Right, Zone.LightBar, Zone.Logo });
        Assert.Equal(5, l.Count);
        Assert.Equal(3, l.Count(s => s.IsKeyboardSection));
    }

    [Fact]
    public void Frames_map_to_preview_zones()
    {
        var four = ZoneLayout.Describe(new[] { Zone.Left, Zone.Center, Zone.Right, Zone.Zone4, Zone.Logo });
        Assert.Equal(new[] { Zone.Zone4 }, ZoneLayout.Affected(four, Zone.Zone4));
        Assert.Equal(5, ZoneLayout.Affected(four, Zone.All).Count());
        Assert.Empty(ZoneLayout.Affected(four, Zone.LightBar)); // not on this keyboard

        var single = ZoneLayout.Describe(new[] { Zone.All });
        Assert.Equal(new[] { Zone.All }, ZoneLayout.Affected(single, Zone.Left));   // legacy: Left drives all
        Assert.Empty(ZoneLayout.Affected(single, Zone.Center));                    // ignored like the driver
        Assert.Equal(new[] { Zone.All }, ZoneLayout.Affected(single, Zone.All));
    }
}

// From the Nitro DCHU logs (Control Center writes app setting (2,32) with these values)
public class Packet44UiIndexTests
{
    [Theory]
    [InlineData(0x06, 0)] // Random
    [InlineData(0xFF, 1)] // Static
    [InlineData(0x05, 2)] // Breathing
    [InlineData(0x07, 3)] // Cycle
    [InlineData(0x04, 4)] // Wave
    [InlineData(0x01, 5)] // Dance
    [InlineData(0x02, 6)] // Tempo
    [InlineData(0x03, 7)] // Blink
    public void Ui_index_matches_control_center(int code, int index) =>
        Assert.Equal(index, ClevoLed.Packet44UiIndex((byte)code));

    [Fact]
    public void Static_packet_from_log_is_reproduced()
    {
        // 00:15:16.594 CC40: all zones 00 ff 00, logo 00 ff 00, brightness level 2 (5f)
        var green = new ColorRGB(0, 0xFF, 0);
        var p = ClevoLed.BuildPacket44(0xFF, 1, 2, new[] { green, green, green, green, ColorRGB.Black, green }, false, true);
        // exactly the logged bytes: "2c ff 01" + 13 x "00" + "5f" + 4 x "00 ff 00" + light bar "00 00 00" + "00" + logo "00 ff 00" + "5f"
        var expected = Convert.FromHexString(
            "2cff01" + string.Concat(Enumerable.Repeat("00", 13)) + "5f" +
            string.Concat(Enumerable.Repeat("00ff00", 4)) + "000000" + "00" + "00ff00" + "5f");
        Assert.Equal(37, expected.Length);
        Assert.Equal(expected, p[..expected.Length]);
        Assert.All(p[expected.Length..], b => Assert.Equal(0, b));
    }
}
