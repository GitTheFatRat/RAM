namespace RbxManager.App.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public static class IncognitoBrowserLauncher
{
    public const string LoginUrl = "https://www.roblox.com/NewLogin";
    public const string SignUpUrl = "https://www.roblox.com/";
    public const string DpiBypassArgs = "--disable-quic --disable-features=EncryptedClientHello,Kyber,PostQuantumCECPQ2";

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

        foreach (var (cmdName, args) in GetPathBrowserCommands(url))
        {
            if (TryStartProcess(cmdName, args))
            {
                return true;
            }
        }

        return TryStartProcess(url, string.Empty);
    }

    internal static (string CmdName, string Args)[] GetPathBrowserCommands(string url = LoginUrl)
    {
        string incognitoArgs = BuildIncognitoArguments(url);
        string inPrivateArgs = BuildInPrivateArguments(url);

        return
        [
            ("chrome.exe", incognitoArgs),
            ("thorium.exe", incognitoArgs),
            ("msedge.exe", inPrivateArgs),
            ("brave.exe", incognitoArgs)
        ];
    }

    internal static (string ExePath, string Args)[] GetCandidateBrowserPaths(string url = LoginUrl)
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string incognitoArgs = BuildIncognitoArguments(url);
        string inPrivateArgs = BuildInPrivateArguments(url);

        return
        [
            (Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"), incognitoArgs),
            (Path.Combine(pfX86, "Google", "Chrome", "Application", "chrome.exe"), incognitoArgs),
            (Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"), incognitoArgs),
            (Path.Combine(localAppData, "Thorium", "Application", "thorium.exe"), incognitoArgs),
            (Path.Combine(pf, "Thorium", "Application", "thorium.exe"), incognitoArgs),
            (Path.Combine(pfX86, "Microsoft", "Edge", "Application", "msedge.exe"), inPrivateArgs),
            (Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"), inPrivateArgs),
            (Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"), incognitoArgs),
            (Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"), incognitoArgs)
        ];
    }

    internal static string BuildIncognitoArguments(string url = LoginUrl) =>
        $"--incognito {DpiBypassArgs} \"{url}\"";

    internal static string BuildInPrivateArguments(string url = LoginUrl) =>
        $"--inprivate {DpiBypassArgs} \"{url}\"";

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
