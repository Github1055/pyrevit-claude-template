using System.IO;
using System.Text.Json;

namespace SmartFiler.Services;

/// <summary>
/// Persists user settings (scan sources, custom colors) to %APPDATA%\SmartFiler\settings.json.
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartFiler");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public UserSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
            else
            {
                Settings = UserSettings.CreateDefaults();
                Save(); // Write defaults on first run
            }
        }
        catch
        {
            Settings = UserSettings.CreateDefaults();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort save
        }
    }
}

public class UserSettings
{
    public List<string> ScanSources { get; set; } = [];
    public string ProjectRoot { get; set; } = @"D:\";
    public int StaleThresholdDays { get; set; } = 90;

    // Custom UI colors (hex strings, null = use theme default)
    public string? AccentColor { get; set; }
    public string? BgColor { get; set; }
    public string? SurfaceColor { get; set; }
    public string? TextColor { get; set; }

    // Saved color presets
    public List<ColorPreset> ColorPresets { get; set; } = [];

    // Filter states (persist between sessions)
    public bool FilterRevit { get; set; } = true;
    public bool FilterBlender { get; set; } = true;
    public bool FilterCad { get; set; } = true;
    public bool FilterWord { get; set; } = true;
    public bool FilterExcel { get; set; } = true;
    public bool FilterPowerPoint { get; set; } = true;
    public bool FilterPdf { get; set; } = true;
    public bool FilterText { get; set; } = true;
    public bool FilterImages { get; set; } = true;
    public bool FilterShortcuts { get; set; } = true;
    public bool FilterWebLinks { get; set; } = true;
    public bool FilterInstallers { get; set; } = true;
    public bool FilterArchives { get; set; } = true;
    public bool FilterOther { get; set; } = true;

    public static UserSettings CreateDefaults()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var settings = new UserSettings
        {
            ScanSources =
            [
                // Standard Windows folders
                Path.Combine(userProfile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),

                // OneDrive Personal (Grant Teasdale)
                Path.Combine(userProfile, "OneDrive", "Desktop"),
                Path.Combine(userProfile, "OneDrive", "Documents"),

                // OneDrive Work (Sworder Belcher Holt)
                Path.Combine(userProfile, "OneDrive - Sworder Belcher Holt", "Desktop"),
                Path.Combine(userProfile, "OneDrive - Sworder Belcher Holt", "Documents"),
            ]
        };

        return settings;
    }
}

public class ColorPreset
{
    public string Name { get; set; } = "";
    public string AccentColor { get; set; } = "#0EA5A9";
    public string BgColor { get; set; } = "#0F0F14";
    public string SurfaceColor { get; set; } = "#18181F";
    public string TextColor { get; set; } = "#EAEAEF";
}
