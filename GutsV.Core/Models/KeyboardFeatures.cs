namespace GutsV.Core.Models;

[Flags]
public enum KeyboardFeatures
{
    None = 0,
    HardwareEffects = 1,
    HardwareBrightness = 2,
    BootEffect = 4,  // "Override Boot Effect" (firmware LED effect while booting)
    SleepTimer = 8   // keyboard backlight turns off after N seconds without input
}

/// <summary>Boot effect / sleep timer as stored by Control Center (null = unknown).</summary>
public readonly record struct FirmwareOptions(bool? BootEffect, bool? SleepTimerEnabled, int? SleepTimerSeconds);

// Firmware effects; values are the Clevo/Insyde mode words (feedback/PROTOCOL.md).
public enum HardwareEffect : uint
{
    RandomColor = 0x70000000,
    Breathe = 0x1002A000,
    Cycle = 0x33010000,
    Wave = 0xB0000000,
    Dance = 0x80000000,
    Tempo = 0x90000000,
    Flash = 0xA0000000
}
