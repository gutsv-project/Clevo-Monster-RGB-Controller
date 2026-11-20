using System.Text.Json;

namespace GutsV.Core.Settings;

public class AppSettings
{
    public byte Red { get; set; } = 0;
    public byte Green { get; set; } = 212;
    public byte Blue { get; set; } = 255;
    public int DriverType { get; set; } = 0;
    // Store animation name to restore it
    public string? LastAnimation { get; set; }
    public double Brightness { get; set; } = 100;
    public bool IsStartupEnabled { get; set; } = false;
    public bool IsAnimationActive { get; set; } = false;
    public double AnimationSpeed { get; set; } = 1.0;
    // Firmware brightness 0-4 and last firmware effect (keyboards that support them)
    // null = never changed by the user -> GutsV does not touch the firmware brightness
    public int? HardwareBrightness { get; set; }
    public uint HardwareEffect { get; set; } = 0x1002A000; // Breathe
    // true while a firmware effect (not a static color / software animation) is active
    public bool IsHardwareEffectActive { get; set; } = false;
    // null = never set in GutsV -> left as the firmware / Control Center has it
    public bool? BootEffectEnabled { get; set; }
    public bool? SleepTimerEnabled { get; set; }
    public int SleepTimerSeconds { get; set; } = 30; // Acer default
    // Per-zone static colors ("Left", "Zone4", "Logo", ... -> "#RRGGBB") and the zones selected in the UI
    public Dictionary<string, string>? ZoneColors { get; set; }
    public List<string>? SelectedZones { get; set; }
    // Packet-44 (Acer Nitro 4-zone) minimum gap between LED packets; null = driver default
    public int? Packet44MinGapMs { get; set; }
}

public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "GutsV", 
        "settings.json"
    );

    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* Best effort save */ }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }
}
