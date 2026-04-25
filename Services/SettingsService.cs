using System.IO;
using System.Text.Json;
using Clokr.Models;

namespace Clokr.Services;

/// <summary>
/// Saves and loads application settings to/from a JSON file in AppData.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Clokr");

        Directory.CreateDirectory(appDataDir);
        _settingsPath = Path.Combine(appDataDir, "settings.json");
    }

    /// <summary>
    /// Loads settings from disk. Returns default settings if file doesn't exist.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>
    /// Saves settings to disk.
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}
