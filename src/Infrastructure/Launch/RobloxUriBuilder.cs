namespace RbxManager.Infrastructure.Launch;

using System;
using System.Globalization;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;

public static class RobloxUriBuilder
{
    public const string AssetGameHost = "https://assetgame.roblox.com";
    public const string PlaceLauncherPath = "/game/PlaceLauncher.ashx";

    public static string BuildPlaceLauncherUrl(
        long placeId,
        string? jobId,
        string browserTrackerId,
        string? templateOverride = null)
    {
        string template = LaunchTemplateValidator.DefaultPlaceLauncherUrlTemplate;
        if (!string.IsNullOrWhiteSpace(templateOverride))
        {
            Result validation = LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate(templateOverride);
            if (validation.IsSuccess)
            {
                template = templateOverride;
            }
        }

        string request = string.IsNullOrWhiteSpace(jobId) ? "RequestGame" : "RequestGameJob";
        string gameIdParam = string.IsNullOrWhiteSpace(jobId) ? string.Empty : $"&gameId={jobId.Trim()}";

        return template
            .Replace("{request}", request, StringComparison.Ordinal)
            .Replace("{trackerId}", browserTrackerId, StringComparison.Ordinal)
            .Replace("{placeId}", placeId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{gameIdParam}", gameIdParam, StringComparison.Ordinal);
    }

    public static string BuildLaunchUri(
        string ticket,
        long placeId,
        string? jobId,
        long unixTimestampMs,
        string browserTrackerId,
        string? launchUriTemplateOverride = null,
        string? placeLauncherUrlTemplateOverride = null)
    {
        string placeLauncherUrl = BuildPlaceLauncherUrl(placeId, jobId, browserTrackerId, placeLauncherUrlTemplateOverride);
        string encodedPlaceLauncherUrl = Uri.EscapeDataString(placeLauncherUrl);

        string template = LaunchTemplateValidator.DefaultLaunchUriTemplate;
        if (!string.IsNullOrWhiteSpace(launchUriTemplateOverride))
        {
            Result validation = LaunchTemplateValidator.ValidateLaunchUriTemplate(launchUriTemplateOverride);
            if (validation.IsSuccess)
            {
                template = launchUriTemplateOverride;
            }
        }

        return template
            .Replace("{ticket}", ticket, StringComparison.Ordinal)
            .Replace("{launchTimeMs}", unixTimestampMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{placeLauncherUrl}", encodedPlaceLauncherUrl, StringComparison.Ordinal)
            .Replace("{trackerId}", browserTrackerId, StringComparison.Ordinal);
    }
}
