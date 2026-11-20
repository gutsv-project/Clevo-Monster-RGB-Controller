using GutsV.Core.Models;

namespace GutsV.Core.Drivers;

/// <summary>
/// LED encodings used by Clevo / Monster / Acer firmware, decoded from Acer Control Center
/// (feedback/PROTOCOL.md and feedback/PROTOCOL_ZONE4.md).
/// 1) 32-bit words: Clevo WMI SetKBLED and Insyde SetDCHU_Data(103) (1- and 3-part keyboards).
/// 2) "Packet 44": SetDCHU_DataEx(4, 256 bytes) for 4-zone keyboards (KBType 7 / 23).
/// </summary>
public static class ClevoLed
{
    public const int DchuLedCommand = 103;
    public const int DchuPackageCommand = 4;
    public const int DchuBootEffectSleepCommand = 121;

    // ---- 32-bit word protocol ----

    public static bool HasWordCode(Zone zone) =>
        zone is Zone.Left or Zone.Center or Zone.Right or Zone.LightBar or Zone.Logo;

    public static byte ZoneCode(Zone zone) => zone switch
    {
        Zone.Left => 0xF0,
        Zone.Center => 0xF1,
        Zone.Right => 0xF2,
        Zone.LightBar => 0xF3,
        Zone.Logo => 0xF6,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "No 32-bit zone code (Zone.All must be expanded; Zone4 only exists on packet-44 boards)")
    };

    // Layout (little endian): G, R, B, zone code
    public static uint ColorWord(Zone zone, ColorRGB color) =>
        (uint)(ZoneCode(zone) << 24 | color.B << 16 | color.R << 8 | color.G);

    /// <summary>White-balance correction Acer applies before sending a color (by keyboard type).</summary>
    public static ColorRGB VendorColorCorrection(int keyboardType, ColorRGB c)
    {
        if (keyboardType is 6 or 22)
        {
            // CC40: special mint green fix, then all channels halved
            byte b = (c.R == 0 && ((c.G == 254 && c.B == 129) || (c.G == 255 && c.B == 127))) ? (byte)70 : c.B;
            return new ColorRGB((byte)(c.R / 2), (byte)(c.G / 2), (byte)(b / 2));
        }
        if (keyboardType is 2 or 38)
            return c with { B = (byte)Math.Round(200.0 / 255.0 * c.B, 0) };
        return c;
    }

    // Acer uses 4 steps: 1..4; 0 = off
    private static readonly uint[] BrightnessSteps = { 0, 47, 95, 143, 191 };
    private static readonly uint[] BrightnessStepsType38 = { 0, 47, 102, 155, 212 };
    // Clevo WMI accepts 0..255
    private static readonly uint[] BrightnessStepsWmi = { 0, 64, 128, 192, 255 };

    public static uint BrightnessWord(int level) =>
        0xF4000000u | BrightnessSteps[Math.Clamp(level, 0, 4)];

    public static uint BrightnessWord(int level, int keyboardType) =>
        0xF4000000u | (keyboardType == 38 ? BrightnessStepsType38 : BrightnessSteps)[Math.Clamp(level, 0, 4)];

    public static uint WmiBrightnessWord(int level) =>
        0xF4000000u | BrightnessStepsWmi[Math.Clamp(level, 0, 4)];

    public static uint EffectWord(HardwareEffect effect) => (uint)effect;

    /// <summary>
    /// Flags word Acer sends after colors/effects/brightness: bit12 = LED on,
    /// bits 16..18 = zones 1..3 enabled, bit 15 = light bar / slot 4, bit 13 = logo. Low bits 1 (on).
    /// With everything enabled this is 0xE007B001 (Acer default).
    /// </summary>
    public static uint FlagsWord(bool hasLogo) =>
        0xE0000000u | 1u << 18 | 1u << 17 | 1u << 16 | 1u << 15 | (hasLogo ? 1u << 13 : 0) | 1u << 12 | 1u;

    public const uint FlagsWordOff = 0xE0000007u;

    public static byte[] ToBytes(uint word) => BitConverter.GetBytes(word);

    // ---- Command 121 (all Insyde keyboard types) ----

    public const int SleepTimerMinSeconds = 5;
    // Acer CC40 UI allows 0..1 h, 0..59 min, 0..59 s (the firmware field itself is 16-bit)
    public const int SleepTimerMaxSeconds = 7199;

    /// <summary>Firmware supports "Override Boot Effect" (WMI13 byte 43 bit 0).</summary>
    public static bool SupportsBootEffect(byte[] wmi13) => wmi13.Length > 43 && (wmi13[43] & 1) == 1;

    public static uint BootEffectWord(bool enabled) => 0x1E000000u + (enabled ? 1u : 0u);

    public static uint SleepTimerWord(int? seconds) =>
        seconds is int s
            ? 0x18000000u | (uint)Math.Clamp(s, SleepTimerMinSeconds, SleepTimerMaxSeconds) << 8 | 0xFFu
            : 0x18000000u;

    // ---- Packet 44 protocol (4-zone keyboards) ----

    public const byte Packet44StaticMode = 0xFF;
    private static readonly byte[] Packet44Brightness = { 0, 47, 95, 143, 191 };
    private static readonly byte[] Packet44LightBarBrightness = { 0, 31, 46, 87, 128 };

    public static byte Packet44EffectCode(HardwareEffect effect) => effect switch
    {
        HardwareEffect.RandomColor => 6,
        HardwareEffect.Breathe => 5,
        HardwareEffect.Cycle => 7,
        HardwareEffect.Wave => 4,
        HardwareEffect.Dance => 1,
        HardwareEffect.Tempo => 2,
        HardwareEffect.Flash => 3,
        _ => Packet44StaticMode
    };

    /// <summary>
    /// Builds the 256-byte buffer for SetDCHU_DataEx(4, ...).
    /// zones: [0..3] = sections 1..4, [4] = light bar (KBType 23), [5] = logo. RGB order R,G,B.
    /// </summary>
    public static byte[] BuildPacket44(byte mode, byte speed, int level, IReadOnlyList<ColorRGB> zones,
        bool hasLightBar, bool hasLogo, bool lightBarOn = true)
    {
        var p = new byte[256];
        level = Math.Clamp(level, 0, 4);
        p[0] = 44;
        p[1] = mode;
        p[2] = (byte)Math.Clamp((int)speed, 1, 3);
        p[16] = Packet44Brightness[level];
        for (int i = 0; i < 4; i++)
        {
            var c = zones[i];
            p[17 + 3 * i] = c.R; p[18 + 3 * i] = c.G; p[19 + 3 * i] = c.B;
        }
        if (hasLightBar)
        {
            var c = zones[4];
            p[29] = c.R; p[30] = c.G; p[31] = c.B;
            if (lightBarOn) p[32] = Packet44LightBarBrightness[level];
        }
        if (hasLogo)
        {
            var c = zones[5];
            p[33] = c.R; p[34] = c.G; p[35] = c.B;
            p[36] = Packet44Brightness[level];
        }
        return p;
    }

    /// <summary>
    /// Control Center's UI index for a packet-44 mode, stored in app setting (2, 32).
    /// FnKey rebuilds the LED packet from that store on Fn+LED hotkeys (seen in the Nitro DCHU logs).
    /// </summary>
    public static byte Packet44UiIndex(byte modeCode) => modeCode switch
    {
        6 => 0,    // Random
        0xFF => 1, // Static
        5 => 2,    // Breathing
        7 => 3,    // Cycle
        4 => 4,    // Wave
        1 => 5,    // Dance
        2 => 6,    // Tempo
        3 => 7,    // Blink
        _ => 1
    };

    /// <summary>Index of a zone inside the packet-44 color array, or -1.</summary>
    public static int Packet44Slot(Zone zone) => zone switch
    {
        Zone.Left => 0,
        Zone.Center => 1,
        Zone.Right => 2,
        Zone.Zone4 => 3,
        Zone.LightBar => 4,
        Zone.Logo => 5,
        _ => -1
    };
}
