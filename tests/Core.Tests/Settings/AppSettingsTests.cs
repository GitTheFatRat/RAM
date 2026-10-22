namespace RbxManager.Core.Tests.Settings;

using System.Text.Json;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;

public sealed class AppSettingsTests
{
    [Fact]
    public void AppSettings_Defaults_MatchSpec()
    {
        AppSettings settings = AppSettings.Default;

        Assert.Equal("vi", settings.Language);
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(20, settings.LaunchDelaySeconds);
        Assert.Equal(10, settings.RejoinDelaySeconds);
        Assert.Equal(5, settings.RejoinMaxAttempts);
        Assert.False(settings.MultiInstanceEnabled);
        Assert.False(settings.MultiInstanceAcknowledged);
        Assert.False(settings.EnableRamCleaner);
        Assert.Equal(15, settings.RamCleanerIntervalMinutes);
        Assert.True(settings.EnableSmartServerHopping);
        Assert.Equal(GhostDisconnectMode.NotifyOnly, settings.GhostDisconnectMode);
        Assert.True(settings.EnableGhostDisconnectDetection);
    }

    [Fact]
    public void AppSettings_Clamp_ClampsInvalidValues()
    {
        AppSettings invalid = new(
            Language: "fr",
            Theme: "NeonPink",
            LaunchDelaySeconds: 0,
            RejoinDelaySeconds: 0,
            RejoinMaxAttempts: -1,
            RamCleanerIntervalMinutes: 2);

        AppSettings clamped = invalid.Clamp();

        Assert.Equal("vi", clamped.Language);
        Assert.Equal("Dark", clamped.Theme);
        Assert.Equal(1, clamped.LaunchDelaySeconds);
        Assert.Equal(1, clamped.RejoinDelaySeconds);
        Assert.Equal(1, clamped.RejoinMaxAttempts);
        Assert.Equal(5, clamped.RamCleanerIntervalMinutes);
    }

    [Fact]
    public void AppSettings_Clamp_ClampsUpperLimits()
    {
        AppSettings invalid = new(
            LaunchDelaySeconds: 9999,
            RamCleanerIntervalMinutes: 500);

        AppSettings clamped = invalid.Clamp();

        Assert.Equal(600, clamped.LaunchDelaySeconds);
        Assert.Equal(120, clamped.RamCleanerIntervalMinutes);
    }

    [Fact]
    public void AppSettings_Clamp_PreservesValidValues()
    {
        AppSettings valid = new(
            Language: "en",
            Theme: "Light",
            LaunchDelaySeconds: 45,
            RejoinDelaySeconds: 15,
            RejoinMaxAttempts: 10,
            MultiInstanceEnabled: true,
            MultiInstanceAcknowledged: true,
            ThrottleBackgroundRoblox: true,
            ThrottleBackgroundAcknowledged: true,
            EnableRamCleaner: true,
            RamCleanerAcknowledged: true,
            RamCleanerIntervalMinutes: 45,
            EnableSmartServerHopping: false,
            GhostDisconnectMode: GhostDisconnectMode.Off);

        AppSettings clamped = valid.Clamp();

        Assert.Equal("en", clamped.Language);
        Assert.Equal("Light", clamped.Theme);
        Assert.Equal(45, clamped.LaunchDelaySeconds);
        Assert.Equal(15, clamped.RejoinDelaySeconds);
        Assert.Equal(10, clamped.RejoinMaxAttempts);
        Assert.True(clamped.MultiInstanceEnabled);
        Assert.True(clamped.MultiInstanceAcknowledged);
        Assert.True(clamped.ThrottleBackgroundRoblox);
        Assert.True(clamped.EnableRamCleaner);
        Assert.Equal(45, clamped.RamCleanerIntervalMinutes);
        Assert.False(clamped.EnableSmartServerHopping);
        Assert.Equal(GhostDisconnectMode.Off, clamped.GhostDisconnectMode);
        Assert.False(clamped.EnableGhostDisconnectDetection);
    }

    [Theory]
    [InlineData("{\"LaunchDelayMs\": 3000}", 20)] // Old default -> 20s
    [InlineData("{\"LaunchDelayMs\": 1500}", 2)]  // 1500ms -> rounded up to 2s
    [InlineData("{\"LaunchDelayMs\": 100}", 1)]   // 100ms -> rounded up and clamped to min 1s
    [InlineData("{\"LaunchDelayMs\": 650000}", 600)] // 650s -> clamped to max 600s
    [InlineData("{\"LaunchDelaySeconds\": 35}", 35)]
    [InlineData("{\"LaunchDelaySeconds\": 0}", 1)]
    [InlineData("{\"LaunchDelaySeconds\": 1000}", 600)]
    public void AppSettings_Migration_ConvertsLegacyLaunchDelayMs(string json, int expectedSeconds)
    {
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(settings);

        AppSettings clamped = settings.Clamp();
        Assert.Equal(expectedSeconds, clamped.LaunchDelaySeconds);
    }
}
