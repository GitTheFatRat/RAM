namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public sealed partial class WindowController : IWindowController
{
    private const int SwShowMinNoActive = 7;
    private const int SwRestore = 9;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly IProcessProvider _processProvider;
    private readonly ILogger<WindowController> _logger;
    private readonly HashSet<IntPtr> _throttledWindows = new();
    private readonly object _lock = new();

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public WindowController(IProcessProvider processProvider, ILogger<WindowController>? logger = null)
    {
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        _logger = logger ?? NullLogger<WindowController>.Instance;
    }

    public void ThrottleBackgroundProcesses()
    {
        try
        {
            IntPtr foregroundHwnd = GetForegroundWindow();
            uint foregroundPid = 0;
            if (foregroundHwnd != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(foregroundHwnd, out foregroundPid);
            }

            IReadOnlyList<ProcessSnapshot> snapshots = _processProvider.GetRobloxProcesses();
            if (snapshots.Count == 0)
            {
                return;
            }

            HashSet<uint> robloxPids = new(snapshots.Select(p => (uint)p.Pid));

            lock (_lock)
            {
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd))
                    {
                        return true;
                    }

                    _ = GetWindowThreadProcessId(hWnd, out uint pid);
                    if (robloxPids.Contains(pid) && pid != foregroundPid)
                    {
                        ShowWindow(hWnd, SwShowMinNoActive);
                        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
                        _throttledWindows.Add(hWnd);
                    }

                    return true;
                }, IntPtr.Zero);
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
                    ShowWindow(hWnd, SwRestore);
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
