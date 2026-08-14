using System;
using System.IO;
using System.Text.Json;

namespace EtherTransfer.Core;

public class AppSettings
{
    public string CustomDeviceName { get; set; } = string.Empty;
}

public static class SettingsManager
{
    private static readonly string SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EtherTransfer");
    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    private static AppSettings? _cachedSettings;

    public static AppSettings Load()
    {
        if (_cachedSettings != null)
            return _cachedSettings;

        if (!File.Exists(SettingsFile))
        {
            _cachedSettings = new AppSettings();
            return _cachedSettings;
        }

        try
        {
            var json = File.ReadAllText(SettingsFile);
            _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            _cachedSettings = new AppSettings();
        }

        return _cachedSettings;
    }

    public static void Save(AppSettings settings)
    {
        _cachedSettings = settings;
        try
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Fail silently if we can't save settings, we'll just use defaults
        }
    }
}
