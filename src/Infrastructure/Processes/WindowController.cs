namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public sealed partial class WindowController : IWindowController
{
    private const int SwShowMinNoActive = 7;
    private const int SwRestore = 9;

    private readonly INativeWindowMethods _nativeMethods;
    private readonly ILogger<WindowController> _logger;
    private readonly HashSet<IntPtr> _throttledWindows = new();
    private readonly object _lock = new();

    public int ThrottledWindowCount
    {
        get
        {
            lock (_lock)
            {
                return _throttledWindows.Count;
            }
        }
    }

    public WindowController(
        IProcessProvider? processProvider = null,
        INativeWindowMethods? nativeMethods = null,
        ILogger<WindowController>? logger = null)
    {
        _nativeMethods = nativeMethods ?? new WindowsNativeWindowMethods();
        _logger = logger ?? NullLogger<WindowController>.Instance;
    }

    public void ThrottleBackgroundProcesses(IReadOnlyCollection<int>? trackedPids = null)
    {
        if (trackedPids == null || trackedPids.Count == 0)
        {
            return;
        }

        try
        {
            IntPtr foregroundHwnd = _nativeMethods.GetForegroundWindow();
            uint foregroundPid = foregroundHwnd != IntPtr.Zero
                ? _nativeMethods.GetWindowThreadProcessId(foregroundHwnd)
                : 0;

            HashSet<uint> targetPids = new(trackedPids.Select(p => (uint)p));

            lock (_lock)
            {
                _nativeMethods.EnumerateWindows(hWnd =>
                {
                    if (!_nativeMethods.IsWindowVisible(hWnd))
                    {
                        return true;
                    }

                    uint pid = _nativeMethods.GetWindowThreadProcessId(hWnd);
                    if (targetPids.Contains(pid) && pid != foregroundPid)
                    {
                        // Minimize without activating; never move off-screen or send synthetic input
                        _nativeMethods.ShowWindow(hWnd, SwShowMinNoActive);
                        _throttledWindows.Add(hWnd);
                    }

                    return true;
                });
            }
        }
        catch (Exception ex)
        {
            LogThrottleWarning(_logger, ex);
        }
    }

    public void RestoreProcesses()
    {
        try
        {
            lock (_lock)
            {
                foreach (IntPtr hWnd in _throttledWindows)
                {
                    _nativeMethods.ShowWindow(hWnd, SwRestore);
                }
                _throttledWindows.Clear();
            }
        }
        catch (Exception ex)
        {
            LogRestoreWarning(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Error throttling background Roblox processes.")]
    private static partial void LogThrottleWarning(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Error restoring throttled Roblox windows.")]
    private static partial void LogRestoreWarning(ILogger logger, Exception ex);
}
