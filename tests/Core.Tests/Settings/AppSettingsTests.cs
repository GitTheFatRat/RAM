namespace RbxManager.Core.Tests.Settings;

using RbxManager.Core.Settings;

public sealed class AppSettingsTests
{
    [Fact]
    public void AppSettings_Defaults_MatchSpec()
    {
        AppSettings settings = AppSettings.Default;

        Assert.Equal("vi", settings.Language);
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(3000, settings.LaunchDelayMs);
        Assert.Equal(10, settings.RejoinDelaySeconds);
        Assert.Equal(5, settings.RejoinMaxAttempts);
        Assert.False(settings.MultiInstanceEnabled);
        Assert.False(settings.MultiInstanceAcknowledged);
    }

    [Fact]
    public void AppSettings_Clamp_ClampsInvalidValues()
    {
        AppSettings invalid = new(
            Language: "fr",
            Theme: "NeonPink",
            LaunchDelayMs: 200,
            RejoinDelaySeconds: 0,
            RejoinMaxAttempts: -1);

        AppSettings clamped = invalid.Clamp();

        Assert.Equal("vi", clamped.Language);
        Assert.Equal("Dark", clamped.Theme);
        Assert.Equal(1000, clamped.LaunchDelayMs);
        Assert.Equal(1, clamped.RejoinDelaySeconds);
        Assert.Equal(1, clamped.RejoinMaxAttempts);
    }

    [Fact]
    public void AppSettings_Clamp_PreservesValidValues()
    {
        AppSettings valid = new(
            Language: "en",
            Theme: "Light",
            LaunchDelayMs: 5000,
            RejoinDelaySeconds: 15,
            RejoinMaxAttempts: 10,
            MultiInstanceEnabled: true,
            MultiInstanceAcknowledged: true);

        AppSettings clamped = valid.Clamp();

        Assert.Equal("en", clamped.Language);
        Assert.Equal("Light", clamped.Theme);
        Assert.Equal(5000, clamped.LaunchDelayMs);
        Assert.Equal(15, clamped.RejoinDelaySeconds);
        Assert.Equal(10, clamped.RejoinMaxAttempts);
        Assert.True(clamped.MultiInstanceEnabled);
        Assert.True(clamped.MultiInstanceAcknowledged);
    }
}
