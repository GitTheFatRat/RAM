namespace RbxManager.Core.Launch;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RbxManager.Core.Common;

public static class LaunchTemplateValidator
{
    public const string DefaultLaunchUriTemplate =
        "roblox-player:1+launchmode:play+gameinfo:{ticket}+launchtime:{launchTimeMs}+placelauncherurl:{placeLauncherUrl}+browsertrackerid:{trackerId}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";

    public const string DefaultPlaceLauncherUrlTemplate =
        "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request={request}&browserTrackerId={trackerId}&placeId={placeId}{gameIdParam}&isPlayTogetherGame=false";

    public static readonly HashSet<string> AllowedPlaceholders = new(StringComparer.Ordinal)
    {
        "{ticket}",
        "{launchTimeMs}",
        "{placeLauncherUrl}",
        "{trackerId}",
        "{request}",
        "{placeId}",
        "{gameIdParam}"
    };

    private static readonly Regex PlaceholderRegex = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    public static Result ValidateLaunchUriTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return Result.Success();
        }

        if (!template.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("LaunchUriTemplate must start with 'roblox-player:'.", "InvalidScheme");
        }

        if (!template.Contains("{ticket}", StringComparison.Ordinal))
        {
            return Result.Failure("LaunchUriTemplate must contain '{ticket}'.", "MissingTicketPlaceholder");
        }

        foreach (Match match in PlaceholderRegex.Matches(template))
        {
            if (!AllowedPlaceholders.Contains(match.Value))
            {
                return Result.Failure($"LaunchUriTemplate contains unknown placeholder '{match.Value}'.", "UnknownPlaceholder");
            }
        }

        return Result.Success();
    }

    public static Result ValidatePlaceLauncherUrlTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return Result.Success();
        }

        if (!template.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("PlaceLauncherUrlTemplate must use https scheme.", "InvalidScheme");
        }

        string withoutScheme = template.Substring("https://".Length);
        int delimiterIndex = withoutScheme.IndexOfAny(['/', '?', '#']);
        string host = delimiterIndex >= 0 ? withoutScheme.Substring(0, delimiterIndex) : withoutScheme;

        if (string.IsNullOrWhiteSpace(host) ||
            (!string.Equals(host, "roblox.com", StringComparison.OrdinalIgnoreCase) &&
             !host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure("PlaceLauncherUrlTemplate host must be roblox.com or a subdomain of roblox.com.", "InvalidHost");
        }

        if (!template.Contains("{placeId}", StringComparison.Ordinal))
        {
            return Result.Failure("PlaceLauncherUrlTemplate must contain '{placeId}'.", "MissingPlaceIdPlaceholder");
        }

        foreach (Match match in PlaceholderRegex.Matches(template))
        {
            if (!AllowedPlaceholders.Contains(match.Value))
            {
                return Result.Failure($"PlaceLauncherUrlTemplate contains unknown placeholder '{match.Value}'.", "UnknownPlaceholder");
            }
        }

        return Result.Success();
    }
}
