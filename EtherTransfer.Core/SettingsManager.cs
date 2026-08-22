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
    private static readonly object _lock = new();
    private static AppSettings? _cachedSettings;
    private static string? _customSettingsDirectory;

    /// <summary>
    /// Gets the active directory path where configuration settings are stored (%AppData%\EtherTransfer).
    /// </summary>
    public static string SettingsFolder
    {
        get
        {
            if (_customSettingsDirectory != null)
                return _customSettingsDirectory;

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EtherTransfer");
        }
    }

    /// <summary>
    /// Gets the full file path to the active settings.json file.
    /// </summary>
    public static string SettingsFile => Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        lock (_lock)
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            var filePath = SettingsFile;
            if (!File.Exists(filePath))
            {
                _cachedSettings = new AppSettings();
                return _cachedSettings;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                _cachedSettings = new AppSettings();
            }

            return _cachedSettings;
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (_lock)
        {
            _cachedSettings = settings;
            try
            {
                var folder = SettingsFolder;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
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

    /// <summary>
    /// Overrides the settings directory for automated testing purposes.
    /// </summary>
    public static void SetCustomSettingsDirectory(string? directory)
    {
        lock (_lock)
        {
            _customSettingsDirectory = directory;
            _cachedSettings = null;
        }
    }

    /// <summary>
    /// Resets cached state for unit testing.
    /// </summary>
    public static void ResetForTesting()
    {
        lock (_lock)
        {
            _cachedSettings = null;
            _customSettingsDirectory = null;
        }
    }
}
