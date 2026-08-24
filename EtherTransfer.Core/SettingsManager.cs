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

    public static string SettingsFolder
    {
        get
        {
            if (_customSettingsDirectory != null)
                return _customSettingsDirectory;

            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(appDir))
                {
                    var localSettingsFile = Path.Combine(appDir, "settings.json");
                    var localPortableMarker = Path.Combine(appDir, ".portable");

                    if (File.Exists(localSettingsFile) || File.Exists(localPortableMarker))
                    {
                        return appDir;
                    }
                }
            }
            catch
            {
                // Fallback to ApplicationData on permission or path error
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EtherTransfer");
        }
    }

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

            }
        }
    }

    public static void SetCustomSettingsDirectory(string? directory)
    {
        lock (_lock)
        {
            _customSettingsDirectory = directory;
            _cachedSettings = null;
        }
    }

    public static void ResetForTesting()
    {
        lock (_lock)
        {
            _cachedSettings = null;
            _customSettingsDirectory = null;
        }
    }
}

