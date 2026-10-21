namespace RbxManager.Infrastructure.Tests.Settings;

using System;
using System.IO;
using System.Threading.Tasks;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Settings;
using RbxManager.Infrastructure.Tests.TestHelpers;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task LoadSettingsAsync_FileDoesNotExist_ReturnsDefaults()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        SettingsService service = new(settingsPath);
        AppSettings loaded = await service.LoadSettingsAsync();

        Assert.Equal(AppSettings.Default, loaded);
        Assert.Equal("vi", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(3000, loaded.LaunchDelayMs);
        Assert.Equal(10, loaded.RejoinDelaySeconds);
        Assert.Equal(5, loaded.RejoinMaxAttempts);
        Assert.False(loaded.MultiInstanceEnabled);
        Assert.False(loaded.MultiInstanceAcknowledged);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesValues()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        SettingsService service = new(settingsPath);
        AppSettings expected = new(
            Language: "en",
            Theme: "Light",
            LaunchDelayMs: 4500,
            RejoinDelaySeconds: 15,
            RejoinMaxAttempts: 8,
            MultiInstanceEnabled: true,
            MultiInstanceAcknowledged: true);

        await service.SaveSettingsAsync(expected);

        SettingsService reader = new(settingsPath);
        AppSettings actual = await reader.LoadSettingsAsync();

        Assert.Equal(expected, actual);
        Assert.Equal(expected, service.CurrentSettings);
    }

    [Fact]
    public async Task SaveSettingsAsync_InvalidValues_ClampsAutomatically()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        SettingsService service = new(settingsPath);
        AppSettings invalid = new(
            Language: "fr",
            Theme: "CustomTheme",
            LaunchDelayMs: 100,
            RejoinDelaySeconds: -5,
            RejoinMaxAttempts: 0);

        await service.SaveSettingsAsync(invalid);

        SettingsService reader = new(settingsPath);
        AppSettings clamped = await reader.LoadSettingsAsync();

        Assert.Equal("vi", clamped.Language);
        Assert.Equal("Dark", clamped.Theme);
        Assert.Equal(AppSettings.MinimumLaunchDelayMs, clamped.LaunchDelayMs);
        Assert.Equal(AppSettings.MinimumRejoinDelaySeconds, clamped.RejoinDelaySeconds);
        Assert.Equal(AppSettings.MinimumRejoinMaxAttempts, clamped.RejoinMaxAttempts);
    }

    [Fact]
    public async Task LoadSettingsAsync_CorruptFile_RecoversWithDefaultsWithoutThrowing()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        await File.WriteAllTextAsync(settingsPath, "{ invalid json content [[");

        SettingsService service = new(settingsPath);
        AppSettings loaded = await service.LoadSettingsAsync();

        Assert.Equal(AppSettings.Default, loaded);
    }

    [Fact]
    public async Task SaveSettingsAsync_AtomicWrite_LeavesNoTempFile()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        SettingsService service = new(settingsPath);
        await service.SaveSettingsAsync(AppSettings.Default);

        Assert.True(File.Exists(settingsPath));
        Assert.False(File.Exists(settingsPath + ".tmp"));
    }
}
