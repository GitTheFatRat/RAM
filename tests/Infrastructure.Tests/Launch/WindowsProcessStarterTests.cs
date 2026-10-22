namespace RbxManager.Infrastructure.Tests.Launch;

using System;
using System.Runtime.Versioning;
using RbxManager.Infrastructure.Launch;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessStarterTests
{
    [Fact]
    public void IsRobloxPlayerInstalled_DoesNotThrowExceptions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowsProcessStarter starter = new();
        Exception? recordedException = Record.Exception(() =>
        {
            bool installed = starter.IsRobloxPlayerInstalled();
            // Should be a boolean without throwing
            Assert.True(installed || !installed);
        });

        Assert.Null(recordedException);
    }

    [Fact]
    public void IsRobloxPlayerInstalled_DetectsRobloxIfRegistryKeyPresent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowsProcessStarter starter = new();
        bool isInstalled = starter.IsRobloxPlayerInstalled();

        // On Windows where Roblox is registered in HKLM or HKCR, starter returns true
        using Microsoft.Win32.RegistryKey? hklm = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command");
        using Microsoft.Win32.RegistryKey? hkcr = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");
        using Microsoft.Win32.RegistryKey? hkcu = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command");

        if (hklm != null || hkcr != null || hkcu != null)
        {
            Assert.True(isInstalled, "Expected IsRobloxPlayerInstalled to return true when registry key is present in HKCU, HKLM, or HKCR.");
        }
    }
}
