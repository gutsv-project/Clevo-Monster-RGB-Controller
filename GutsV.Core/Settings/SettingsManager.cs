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
