namespace RbxManager.Infrastructure.Launch;

using System;
using System.Globalization;

public static class RobloxUriBuilder
{
    public const string AssetGameHost = "https://assetgame.roblox.com";
    public const string PlaceLauncherPath = "/game/PlaceLauncher.ashx";

    public static string BuildPlaceLauncherUrl(long placeId, string? jobId, string browserTrackerId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return $"{AssetGameHost}{PlaceLauncherPath}?request=RequestGame&browserTrackerId={browserTrackerId}&placeId={placeId}&isPlayTogetherGame=false";
        }

        return $"{AssetGameHost}{PlaceLauncherPath}?request=RequestGameJob&browserTrackerId={browserTrackerId}&placeId={placeId}&gameId={jobId.Trim()}&isPlayTogetherGame=false";
    }

    public static string BuildLaunchUri(string ticket, long placeId, string? jobId, long unixTimestampMs, string browserTrackerId)
    {
        string placeLauncherUrl = BuildPlaceLauncherUrl(placeId, jobId, browserTrackerId);
        string encodedPlaceLauncherUrl = Uri.EscapeDataString(placeLauncherUrl);

        return $"roblox-player:1+launchmode:play+gameinfo:{ticket}+launchtime:{unixTimestampMs.ToString(CultureInfo.InvariantCulture)}+placelauncherurl:{encodedPlaceLauncherUrl}+browsertrackerid:{browserTrackerId}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
    }
}
