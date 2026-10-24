namespace RbxManager.Core.Settings;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using RbxManager.Core.Common;
using RbxManager.Core.Processes;
using RbxManager.Core.Validation;

public sealed record AdvancedSettings(
    string? LaunchUriTemplate = null,
    string? PlaceLauncherUrlTemplate = null,
    IReadOnlyList<string>? GhostDisconnectKeywords = null);

public sealed record UiStateSettings(
    double? WindowWidth = null,
    double? WindowHeight = null,
    double? WindowTop = null,
    double? WindowLeft = null,
    bool IsMaximized = false,
    string? LastSelectedPage = null,
    string? SortColumn = null,
    bool SortDescending = false,
    Dictionary<string, double>? AccountsColumnWidths = null)
{
    public const double MinimumWindowWidth = 960.0;
    public const double MinimumWindowHeight = 600.0;
    public const double DefaultWindowWidth = 1100.0;
    public const double DefaultWindowHeight = 700.0;

    public UiStateSettings ValidateAndClamp(double screenWidth, double screenHeight)
    {
        double width = WindowWidth.HasValue
            ? Math.Max(MinimumWindowWidth, WindowWidth.Value)
            : DefaultWindowWidth;
        double height = WindowHeight.HasValue
            ? Math.Max(MinimumWindowHeight, WindowHeight.Value)
            : DefaultWindowHeight;

        double top;
        double left;

        // If coordinates fall offscreen or are invalid, reset to centered coordinates
        if (WindowTop.HasValue && WindowLeft.HasValue &&
            WindowLeft.Value >= 0 && WindowLeft.Value < screenWidth - 100 &&
            WindowTop.Value >= 0 && WindowTop.Value < screenHeight - 100)
        {
            top = WindowTop.Value;
            left = WindowLeft.Value;
        }
        else
        {
            top = Math.Max(0, (screenHeight - height) / 2.0);
            left = Math.Max(0, (screenWidth - width) / 2.0);
        }

        return this with
        {
            WindowWidth = width,
            WindowHeight = height,
            WindowTop = top,
            WindowLeft = left
        };
    }
}

public sealed record AppSettings(
    string Language = AppSettings.DefaultLanguage,
    string Theme = AppSettings.DefaultTheme,
    int LaunchDelaySeconds = AppSettings.DefaultLaunchDelaySeconds,
    int RejoinDelaySeconds = AppSettings.DefaultRejoinDelaySeconds,
    int RejoinMaxAttempts = AppSettings.DefaultRejoinMaxAttempts,
    bool MultiInstanceEnabled = false,
    bool MultiInstanceAcknowledged = false,
    bool IsMasterPasswordEnabled = false,
    bool ThrottleBackgroundRoblox = false,
    bool ThrottleBackgroundAcknowledged = false,
    bool EnableRamCleaner = false,
    bool RamCleanerAcknowledged = false,
    int RamCleanerIntervalMinutes = AppSettings.DefaultRamCleanerIntervalMinutes,
    bool EnableSmartServerHopping = true,
    GhostDisconnectMode GhostDisconnectMode = GhostDisconnectMode.NotifyOnly,
    bool EnableUatTelemetry = false,
    string? LastPlaceId = null,
    bool EnableCustomResolution = false,
    int ResolutionWidth = AppSettings.DefaultResolutionWidth,
    int ResolutionHeight = AppSettings.DefaultResolutionHeight,
    [property: JsonPropertyName("EnableGhostDisconnectDetection")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? LegacyEnableGhostDisconnectDetection = null,
    [property: JsonPropertyName("LaunchDelayMs")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? LegacyLaunchDelayMs = null,
    AdvancedSettings? Advanced = null,
    UiStateSettings? UiState = null)
{
    public const string DefaultLanguage = "vi";
    public const string DefaultTheme = "Dark";
    public const int DefaultLaunchDelaySeconds = 20;
    public const int MinimumLaunchDelaySeconds = 1;
    public const int MaximumLaunchDelaySeconds = 600;
    public const int DefaultRejoinDelaySeconds = 10;
    public const int MinimumRejoinDelaySeconds = 1;
    public const int DefaultRejoinMaxAttempts = 5;
    public const int MinimumRejoinMaxAttempts = 1;
    public const int DefaultRamCleanerIntervalMinutes = 15;
    public const int MinimumRamCleanerIntervalMinutes = 5;
    public const int MaximumRamCleanerIntervalMinutes = 120;
    public const int DefaultResolutionWidth = 800;
    public const int DefaultResolutionHeight = 600;
    public const int MinimumResolutionDimension = 64;
    public const int MaximumResolutionDimension = 7680;

    public static AppSettings Default => new();

    [JsonIgnore]
    public bool IsMultiInstanceEnabled => MultiInstanceEnabled;

    [JsonIgnore]
    public bool EnableGhostDisconnectDetection => GhostDisconnectMode != GhostDisconnectMode.Off;

    public AppSettings Clamp()
    {
        string lang = string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
        string theme = Theme switch
        {
            var t when string.Equals(t, "Light", StringComparison.OrdinalIgnoreCase) => "Light",
            var t when string.Equals(t, "System", StringComparison.OrdinalIgnoreCase) => "System",
            _ => "Dark"
        };

        int launchDelay;
        if (LegacyLaunchDelayMs.HasValue)
        {
            if (LegacyLaunchDelayMs.Value == 3000)
            {
                launchDelay = DefaultLaunchDelaySeconds;
            }
            else
            {
                int converted = (int)Math.Ceiling(LegacyLaunchDelayMs.Value / 1000.0);
                launchDelay = Math.Clamp(converted, MinimumLaunchDelaySeconds, MaximumLaunchDelaySeconds);
            }
        }
        else
        {
            launchDelay = Math.Clamp(LaunchDelaySeconds, MinimumLaunchDelaySeconds, MaximumLaunchDelaySeconds);
        }

        int rejoinDelay = Math.Max(MinimumRejoinDelaySeconds, RejoinDelaySeconds);
        int rejoinMax = Math.Max(MinimumRejoinMaxAttempts, RejoinMaxAttempts);
        int ramInterval = Math.Clamp(RamCleanerIntervalMinutes, MinimumRamCleanerIntervalMinutes, MaximumRamCleanerIntervalMinutes);
        int resWidth = ResolutionWidth <= 0
            ? DefaultResolutionWidth
            : Math.Clamp(ResolutionWidth, MinimumResolutionDimension, MaximumResolutionDimension);
        int resHeight = ResolutionHeight <= 0
            ? DefaultResolutionHeight
            : Math.Clamp(ResolutionHeight, MinimumResolutionDimension, MaximumResolutionDimension);

        GhostDisconnectMode mode = GhostDisconnectMode;
        if (LegacyEnableGhostDisconnectDetection.HasValue && mode == GhostDisconnectMode.NotifyOnly && !LegacyEnableGhostDisconnectDetection.Value)
        {
            mode = GhostDisconnectMode.Off;
        }

        bool throttle = ThrottleBackgroundRoblox && ThrottleBackgroundAcknowledged;
        bool ramCleaner = EnableRamCleaner && RamCleanerAcknowledged;

        AdvancedSettings? advanced = Advanced;
        if (advanced?.GhostDisconnectKeywords != null)
        {
            Result validation = GhostDisconnectKeywordValidator.Validate(advanced.GhostDisconnectKeywords);
            if (!validation.IsSuccess)
            {
                advanced = advanced with { GhostDisconnectKeywords = null };
            }
        }

        return this with
        {
            Language = lang,
            Theme = theme,
            LaunchDelaySeconds = launchDelay,
            RejoinDelaySeconds = rejoinDelay,
            RejoinMaxAttempts = rejoinMax,
            ThrottleBackgroundRoblox = throttle,
            EnableRamCleaner = ramCleaner,
            RamCleanerIntervalMinutes = ramInterval,
            ResolutionWidth = resWidth,
            ResolutionHeight = resHeight,
            GhostDisconnectMode = mode,
            LegacyEnableGhostDisconnectDetection = null,
            LegacyLaunchDelayMs = null,
            Advanced = advanced
        };
    }
}
