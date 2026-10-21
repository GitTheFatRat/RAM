namespace RbxManager.Infrastructure.Launch;

using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessStarter : IProcessStarter
{
    public bool IsRobloxPlayerInstalled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public bool Start(string uri)
    {
        ProcessStartInfo psi = new(uri)
        {
            UseShellExecute = true
        };
        return Process.Start(psi) != null;
    }
}
