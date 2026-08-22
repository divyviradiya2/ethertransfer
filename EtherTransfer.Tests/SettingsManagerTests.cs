using System;
using System.IO;
using EtherTransfer.Core;
using NUnit.Framework;

namespace EtherTransfer.Tests;

[TestFixture]
public class SettingsManagerTests
{
    private string _tempTestDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "EtherTransfer_SettingsTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempTestDir);
        SettingsManager.ResetForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.ResetForTesting();
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, true);
            }
        }
        catch { }
    }

    [Test]
    public void Load_WhenNoFileExists_ReturnsDefaultSettings()
    {
        SettingsManager.SetCustomSettingsDirectory(_tempTestDir);

        var settings = SettingsManager.Load();

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.CustomDeviceName, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Save_ThenLoad_PersistsCustomDeviceName()
    {
        SettingsManager.SetCustomSettingsDirectory(_tempTestDir);

        var settingsToSave = new AppSettings
        {
            CustomDeviceName = "CustomLaptop-42"
        };
        SettingsManager.Save(settingsToSave);

        // Reset memory cache to force re-reading from disk
        SettingsManager.SetCustomSettingsDirectory(_tempTestDir);
        var loaded = SettingsManager.Load();

        Assert.That(loaded.CustomDeviceName, Is.EqualTo("CustomLaptop-42"));
        Assert.That(File.Exists(Path.Combine(_tempTestDir, "settings.json")), Is.True);
    }

    [Test]
    public void Load_WhenJsonCorrupted_ReturnsDefaultSettingsGracefully()
    {
        SettingsManager.SetCustomSettingsDirectory(_tempTestDir);
        var settingsFile = Path.Combine(_tempTestDir, "settings.json");
        File.WriteAllText(settingsFile, "{ this is invalid json !!! }");

        var loaded = SettingsManager.Load();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.CustomDeviceName, Is.EqualTo(string.Empty));
    }

    [Test]
    public void SettingsFolder_WhenCustomDirectorySet_MatchesCustomDirectory()
    {
        SettingsManager.SetCustomSettingsDirectory(_tempTestDir);

        Assert.That(SettingsManager.SettingsFolder, Is.EqualTo(_tempTestDir));
        Assert.That(SettingsManager.SettingsFile, Is.EqualTo(Path.Combine(_tempTestDir, "settings.json")));
    }
}
