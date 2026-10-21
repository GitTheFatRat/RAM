namespace RbxManager.App.Services;

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using RbxManager.Core.Diagnostics;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryProvider : IRegistryProvider
{
    public bool ProtocolHandlerExists(string protocol)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{protocol}");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public string? GetProtocolHandlerCommand(string protocol)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{protocol}\shell\open\command");
            return key?.GetValue(string.Empty) as string;
        }
        catch
        {
            return null;
        }
    }

    public bool StandardRobloxInstallFolderExists()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string robloxDir = Path.Combine(localAppData, "Roblox", "Versions");
            return Directory.Exists(robloxDir);
        }
        catch
        {
            return false;
        }
    }

    public string? GetWebView2Version()
    {
        try
        {
            return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            return null;
        }
    }

    public bool IsProcessElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
