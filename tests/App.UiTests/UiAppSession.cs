namespace RbxManager.App.UiTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;

public sealed class UiAppSession : IDisposable
{
    private readonly string _tempDataDir;
    private readonly Process _process;
    private readonly IntPtr _mainWindowHandle;
    private readonly AutomationElement _mainWindowElement;
    private bool _disposed;

    public string DataDirectory => _tempDataDir;
    public Process Process => _process;
    public IntPtr MainWindowHandle => _mainWindowHandle;
    public AutomationElement MainWindowElement => _mainWindowElement;

    private UiAppSession(string tempDataDir, Process process, IntPtr hwnd, AutomationElement element)
    {
        _tempDataDir = tempDataDir;
        _process = process;
        _mainWindowHandle = hwnd;
        _mainWindowElement = element;
    }

    public static UiAppSession Start(string theme = "Dark", bool emptyVault = false)
    {
        string repoRoot = FindRepoRoot();
        string appExePath = Path.Combine(repoRoot, "src", "App.Wpf", "bin", "Debug", "net10.0-windows", "RbxManager.App.exe");

        if (!File.Exists(appExePath))
        {
            throw new FileNotFoundException($"Debug app executable not found at '{appExePath}'. Run 'dotnet build -c Debug' first.", appExePath);
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "RbxUiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Pre-create settings.json to configure theme cleanly without master password
        string settingsJson = $$"""
            {
              "Theme": "{{theme}}",
              "Language": "vi-VN",
              "IsMasterPasswordEnabled": false,
              "MultiInstanceAcknowledged": false
            }
            """;
        File.WriteAllText(Path.Combine(tempDir, "settings.json"), settingsJson);

        ProcessStartInfo startInfo = new()
        {
            FileName = appExePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appExePath)!
        };

        startInfo.EnvironmentVariables["RBXMANAGER_DATA_DIR"] = tempDir;
        startInfo.EnvironmentVariables["RBXMANAGER_UITEST"] = "1";
        if (emptyVault)
        {
            startInfo.EnvironmentVariables["RBXMANAGER_EMPTY_VAULT"] = "1";
        }

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start application process for UI test.");

        IntPtr hwnd = IntPtr.Zero;
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow <= deadline)
        {
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"Application exited immediately with code {process.ExitCode}.");
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                hwnd = process.MainWindowHandle;
                break;
            }

            Thread.Sleep(200);
        }

        if (hwnd == IntPtr.Zero)
        {
            try { process.Kill(true); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
            throw_timeout();
        }

        // Restore and ensure window is visible
        NativeWindowCapture.ShowWindow(hwnd, 9); // SW_RESTORE
        NativeWindowCapture.SetForegroundWindow(hwnd);
        Thread.Sleep(500);

        AutomationElement rootElement = AutomationElement.FromHandle(hwnd)
            ?? throw new InvalidOperationException($"Unable to obtain AutomationElement from HWND {hwnd}.");

        return new UiAppSession(tempDir, process, hwnd, rootElement);

        static void throw_timeout() => throw new TimeoutException("Timed out waiting for main application window handle.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
            _process.Dispose();
        }
        catch
        {
        }

        try
        {
            if (Directory.Exists(_tempDataDir))
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.GetFiles(current, "*.slnx").Length > 0 || Directory.GetFiles(current, "*.sln").Length > 0)
            {
                return current;
            }
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }
            current = parent.FullName;
        }
        throw new InvalidOperationException("Could not find repository root containing .slnx or .sln.");
    }
}
