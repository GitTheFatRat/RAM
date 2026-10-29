namespace RbxManager.Infrastructure.Tests.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        Assert.Equal(20, loaded.LaunchDelaySeconds);
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
            LaunchDelaySeconds: 45,
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
            LaunchDelaySeconds: 0,
            RejoinDelaySeconds: -5,
            RejoinMaxAttempts: 0);

        await service.SaveSettingsAsync(invalid);

        SettingsService reader = new(settingsPath);
        AppSettings clamped = await reader.LoadSettingsAsync();

        Assert.Equal("vi", clamped.Language);
        Assert.Equal("Dark", clamped.Theme);
        Assert.Equal(AppSettings.MinimumLaunchDelaySeconds, clamped.LaunchDelaySeconds);
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

    [Fact]
    public async Task UpdateSettingsAsync_ConcurrentUpdatesToDifferentFields_BothPersist()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        using SettingsService service = new(settingsPath);
        await service.LoadSettingsAsync();

        using ManualResetEventSlim startGate = new(false);
        Task<AppSettings> themeUpdate = Task.Run(() =>
        {
            startGate.Wait();
            return service.UpdateSettingsAsync(s => s with { Theme = "Light" });
        });
        Task<AppSettings> delayUpdate = Task.Run(() =>
        {
            startGate.Wait();
            return service.UpdateSettingsAsync(s => s with { LaunchDelaySeconds = 45 });
        });

        startGate.Set();
        await Task.WhenAll(themeUpdate, delayUpdate);

        Assert.Equal("Light", service.CurrentSettings.Theme);
        Assert.Equal(45, service.CurrentSettings.LaunchDelaySeconds);

        SettingsService reader = new(settingsPath);
        AppSettings persisted = await reader.LoadSettingsAsync();
        Assert.Equal("Light", persisted.Theme);
        Assert.Equal(45, persisted.LaunchDelaySeconds);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ManyConcurrentIncrements_NoneAreLost()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        using SettingsService service = new(settingsPath);
        await service.SaveSettingsAsync(AppSettings.Default with { LaunchDelaySeconds = 20 });

        const int updateCount = 20;
        Task[] updates = new Task[updateCount];
        for (int i = 0; i < updateCount; i++)
        {
            updates[i] = Task.Run(() => service.UpdateSettingsAsync(s => s with { LaunchDelaySeconds = s.LaunchDelaySeconds + 1 }));
        }

        await Task.WhenAll(updates);

        SettingsService reader = new(settingsPath);
        AppSettings persisted = await reader.LoadSettingsAsync();
        Assert.Equal(20 + updateCount, persisted.LaunchDelaySeconds);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ClampsAndRaisesSettingsChangedOnce()
    {
        using TempDirectory tempDir = new();
        string settingsPath = tempDir.CreateFilePath();

        using SettingsService service = new(settingsPath);
        List<AppSettings> raised = [];
        service.SettingsChanged += raised.Add;

        AppSettings returned = await service.UpdateSettingsAsync(s => s with { RejoinMaxAttempts = 0 });

        Assert.Equal(AppSettings.MinimumRejoinMaxAttempts, returned.RejoinMaxAttempts);
        Assert.Equal(returned, service.CurrentSettings);
        AppSettings notified = Assert.Single(raised);
        Assert.Equal(returned, notified);
    }
}
