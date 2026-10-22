namespace RbxManager.Infrastructure.Launch;

using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessStarter : IProcessStarter
{
    public bool IsRobloxPlayerInstalled()
    {
        (RegistryKey hive, string subKey)[] locations =
        [
            (Registry.CurrentUser, @"Software\Classes\roblox-player\shell\open\command"),
            (Registry.LocalMachine, @"Software\Classes\roblox-player\shell\open\command"),
            (Registry.ClassesRoot, @"roblox-player\shell\open\command")
        ];

        foreach (var (hive, subKey) in locations)
        {
            try
            {
                using RegistryKey? key = hive.OpenSubKey(subKey);
                if (key != null)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
            {
                // Gracefully move to the next registry location if permissions are restricted
            }
            catch
            {
                // Gracefully fall through to the next check
            }
        }

        return false;
    }

    public bool Start(string uri)
    {
        ProcessStartInfo psi = new(uri)
        {
            UseShellExecute = true
        };
        using Process? process = Process.Start(psi);
        return process != null;
    }
}
