namespace RbxManager.Core.Settings;

using System;

public sealed record AppSettings(
    string Language = AppSettings.DefaultLanguage,
    string Theme = AppSettings.DefaultTheme,
    int LaunchDelayMs = AppSettings.DefaultLaunchDelayMs,
    int RejoinDelaySeconds = AppSettings.DefaultRejoinDelaySeconds,
    int RejoinMaxAttempts = AppSettings.DefaultRejoinMaxAttempts,
    bool MultiInstanceEnabled = false,
    bool MultiInstanceAcknowledged = false)
{
    public const string DefaultLanguage = "vi";
    public const string DefaultTheme = "Dark";
    public const int DefaultLaunchDelayMs = 3000;
    public const int MinimumLaunchDelayMs = 1000;
    public const int DefaultRejoinDelaySeconds = 10;
    public const int MinimumRejoinDelaySeconds = 1;
    public const int DefaultRejoinMaxAttempts = 5;
    public const int MinimumRejoinMaxAttempts = 1;

    public static AppSettings Default => new();

    public AppSettings Clamp()
    {
        string lang = string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
        string theme = Theme switch
        {
            var t when string.Equals(t, "Light", StringComparison.OrdinalIgnoreCase) => "Light",
            var t when string.Equals(t, "System", StringComparison.OrdinalIgnoreCase) => "System",
            _ => "Dark"
        };

        int launchDelay = Math.Max(MinimumLaunchDelayMs, LaunchDelayMs);
        int rejoinDelay = Math.Max(MinimumRejoinDelaySeconds, RejoinDelaySeconds);
        int rejoinMax = Math.Max(MinimumRejoinMaxAttempts, RejoinMaxAttempts);

        return this with
        {
            Language = lang,
            Theme = theme,
            LaunchDelayMs = launchDelay,
            RejoinDelaySeconds = rejoinDelay,
            RejoinMaxAttempts = rejoinMax
        };
    }
}
