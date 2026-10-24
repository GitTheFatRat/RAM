namespace RbxManager.Infrastructure.Launch;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

/// <summary>
/// Lowers Roblox's engine-side minimum window size via ClientSettings FastFlags so a custom
/// resolution survives user drags instead of being clamped back by WM_GETMINMAXINFO.
/// </summary>
public static class RobloxFastFlagInjector
{
    public const string MinWidthFlag = "DFIntWindowSizeMinX";
    public const string MinHeightFlag = "DFIntWindowSizeMinY";
    private const string PlayerExecutableName = "RobloxPlayerBeta.exe";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static bool InjectResolutionFastFlags(string robloxExePath, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(robloxExePath) || width <= 0 || height <= 0)
        {
            return false;
        }

        string? versionDirectory = Path.GetDirectoryName(robloxExePath);
        if (string.IsNullOrEmpty(versionDirectory))
        {
            return false;
        }

        string settingsPath = Path.Combine(versionDirectory, "ClientSettings", "ClientAppSettings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            JsonObject flags = ReadExistingFlags(settingsPath);
            flags[MinWidthFlag] = width;
            flags[MinHeightFlag] = height;
            File.WriteAllText(settingsPath, flags.ToJsonString(IndentedJson));
            Log.Information("Injected FastFlags {Width}x{Height} into {Path}", width, height, settingsPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Program Files installs need elevation; launch must still proceed without the flags.
            Log.Warning(ex, "Failed to write FastFlags to {Path}", settingsPath);
            return false;
        }
    }

    /// <summary>
    /// The bootstrapper picks the version folder at launch time, so every installed player build is patched.
    /// </summary>
    public static int InjectIntoInstalledClients(int width, int height) =>
        FindInstalledPlayerExecutables().Count(exePath => InjectResolutionFastFlags(exePath, width, height));

    internal static IReadOnlyList<string> FindInstalledPlayerExecutables(IEnumerable<string>? versionRoots = null)
    {
        versionRoots ??=
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox", "Versions"),
        ];

        return versionRoots
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(versionDir => Path.Combine(versionDir, PlayerExecutableName))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static JsonObject ReadExistingFlags(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            // A corrupt file would be ignored by Roblox anyway; replace it rather than block the launch.
            return [];
        }
    }
}
