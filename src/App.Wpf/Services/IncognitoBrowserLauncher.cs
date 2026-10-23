namespace RbxManager.App.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public static class IncognitoBrowserLauncher
{
    public const string LoginUrl = "https://www.roblox.com/login";

    public static bool TryLaunch(string url = LoginUrl)
    {
        (string exePath, string args)[] knownPaths = GetCandidateBrowserPaths(url);
        foreach (var (exePath, args) in knownPaths)
        {
            if (File.Exists(exePath) && TryStartProcess(exePath, args))
            {
                return true;
            }
        }

        (string cmdName, string args)[] pathCommands =
        [
            ("chrome.exe", $"--incognito \"{url}\""),
            ("thorium.exe", $"--incognito \"{url}\""),
            ("msedge.exe", $"--inprivate \"{url}\""),
            ("brave.exe", $"--incognito \"{url}\"")
        ];

        foreach (var (cmdName, args) in pathCommands)
        {
            if (TryStartProcess(cmdName, args))
            {
                return true;
            }
        }

        return TryStartProcess(url, string.Empty);
    }

    private static (string ExePath, string Args)[] GetCandidateBrowserPaths(string url)
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            (Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"), $"--incognito \"{url}\""),
            (Path.Combine(pfX86, "Google", "Chrome", "Application", "chrome.exe"), $"--incognito \"{url}\""),
            (Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"), $"--incognito \"{url}\""),
            (Path.Combine(localAppData, "Thorium", "Application", "thorium.exe"), $"--incognito \"{url}\""),
            (Path.Combine(pf, "Thorium", "Application", "thorium.exe"), $"--incognito \"{url}\""),
            (Path.Combine(pfX86, "Microsoft", "Edge", "Application", "msedge.exe"), $"--inprivate \"{url}\""),
            (Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"), $"--inprivate \"{url}\""),
            (Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"), $"--incognito \"{url}\""),
            (Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"), $"--incognito \"{url}\"")
        ];
    }

    private static bool TryStartProcess(string fileName, string arguments)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };
            using Process? proc = Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
