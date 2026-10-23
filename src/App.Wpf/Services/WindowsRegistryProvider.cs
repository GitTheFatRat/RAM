namespace RbxManager.App.Services;

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using RbxManager.Core.Diagnostics;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryProvider : IRegistryProvider
{
    public bool ProtocolHandlerExists(string protocol)
    {
        (RegistryKey hive, string subKey)[] locations =
        [
            (Registry.CurrentUser, $@"Software\Classes\{protocol}"),
            (Registry.LocalMachine, $@"Software\Classes\{protocol}"),
            (Registry.ClassesRoot, $@"{protocol}")
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
            }
            catch
            {
            }
        }

        return false;
    }

    public string? GetProtocolHandlerCommand(string protocol)
    {
        (RegistryKey hive, string subKey)[] locations =
        [
            (Registry.CurrentUser, $@"Software\Classes\{protocol}\shell\open\command"),
            (Registry.LocalMachine, $@"Software\Classes\{protocol}\shell\open\command"),
            (Registry.ClassesRoot, $@"{protocol}\shell\open\command")
        ];

        foreach (var (hive, subKey) in locations)
        {
            try
            {
                using RegistryKey? key = hive.OpenSubKey(subKey);
                string? command = key?.GetValue(string.Empty) as string;
                if (!string.IsNullOrEmpty(command))
                {
                    return command;
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
            {
            }
            catch
            {
            }
        }

        return null;
    }

    public System.Collections.Generic.IReadOnlyList<(string HiveName, string? Command)> GetProtocolHandlerHives(string protocol)
    {
        (string name, RegistryKey hive, string subKey)[] locations =
        [
            ("HKCU", Registry.CurrentUser, $@"Software\Classes\{protocol}\shell\open\command"),
            ("HKLM", Registry.LocalMachine, $@"Software\Classes\{protocol}\shell\open\command"),
            ("HKCR", Registry.ClassesRoot, $@"{protocol}\shell\open\command")
        ];

        System.Collections.Generic.List<(string, string?)> results = new();
        foreach (var (name, hive, subKey) in locations)
        {
            try
            {
                using RegistryKey? key = hive.OpenSubKey(subKey);
                if (key != null)
                {
                    string? command = key.GetValue(string.Empty) as string;
                    results.Add((name, command));
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
            {
            }
            catch
            {
            }
        }

        return results;
    }

    public bool StandardRobloxInstallFolderExists()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string robloxDir = Path.Combine(localAppData, "Roblox", "Versions");
            if (Directory.Exists(robloxDir))
            {
                return true;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfRobloxDir = Path.Combine(programFiles, "Roblox", "Versions");
            if (Directory.Exists(pfRobloxDir))
            {
                return true;
            }

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pfX86RobloxDir = Path.Combine(programFilesX86, "Roblox", "Versions");
            return Directory.Exists(pfX86RobloxDir);
        }
        catch
        {
            return false;
        }
    }

    public string? GetWebView2Version()
    {
        const string webView2Guid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        (RegistryKey hive, string subKey)[] locations =
        [
            (Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{webView2Guid}"),
            (Registry.CurrentUser, $@"Software\Microsoft\EdgeUpdate\Clients\{webView2Guid}"),
            (Registry.LocalMachine, $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{webView2Guid}")
        ];

        foreach (var (hive, subKey) in locations)
        {
            try
            {
                using RegistryKey? key = hive.OpenSubKey(subKey);
                string? pv = key?.GetValue("pv") as string;
                if (!string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0")
                {
                    return pv;
                }
            }
            catch
            {
            }
        }

        return null;
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
