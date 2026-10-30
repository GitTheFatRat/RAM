namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Native;

/// <remarks>
/// Only ever minimizes via <see cref="IWindowController"/> (SW_SHOWMINNOACTIVE) — never SW_HIDE, never off-screen moves —
/// and never touches a window the user activated or restored from the taskbar within <see cref="UserActivationGracePeriod"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class BackgroundWindowThrottler : BackgroundService
{
    // The 3s poll can land mid-restore, when the taskbar (explorer) still owns the foreground; without a grace
    // period that freshly clicked window would be minimized again and appear to "vanish".
    private static readonly TimeSpan UserActivationGracePeriod = TimeSpan.FromSeconds(15);

    private readonly ISettingsService _settingsService;
    private readonly IWindowController _windowController;
    private readonly ISessionTracker? _sessionTracker;
    private readonly INativeWindowMethods _nativeWindowMethods;
    private readonly ILogger<BackgroundWindowThrottler> _logger;
    private readonly Dictionary<int, DateTime> _lastUserActivationUtc = new();
    private HashSet<int> _minimizedAfterLastTick = new();
    private bool _wasThrottling;

    public BackgroundWindowThrottler(
        ISettingsService settingsService,
        IWindowController windowController,
        ISessionTracker? sessionTracker = null,
        ILogger<BackgroundWindowThrottler>? logger = null,
        INativeWindowMethods? nativeWindowMethods = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _windowController = windowController ?? throw new ArgumentNullException(nameof(windowController));
        _sessionTracker = sessionTracker;
        _nativeWindowMethods = nativeWindowMethods ?? new WindowsNativeWindowMethods();
        _logger = logger ?? NullLogger<BackgroundWindowThrottler>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool isThrottlingEnabled = _settingsService.CurrentSettings.ThrottleBackgroundRoblox &&
                                           _settingsService.CurrentSettings.ThrottleBackgroundAcknowledged &&
                                           !_settingsService.CurrentSettings.EnableAutoTile;
                if (isThrottlingEnabled && _sessionTracker != null)
                {
                    int[] trackedPids = _sessionTracker.ActiveSessions
                        .Select(s => s.ProcessId)
                        .ToArray();

                    if (trackedPids.Length > 0)
                    {
                        int[] throttleablePids = ExcludeRecentlyActivated(trackedPids);
                        if (throttleablePids.Length > 0)
                        {
                            _windowController.ThrottleBackgroundProcesses(throttleablePids);
                            _minimizedAfterLastTick.UnionWith(throttleablePids);
                        }

                        _wasThrottling = true;
                    }
                    else if (_wasThrottling)
                    {
                        _windowController.RestoreProcesses();
                        ResetActivationTracking();
                        _wasThrottling = false;
                    }
                }
                else if (_wasThrottling)
                {
                    _windowController.RestoreProcesses();
                    ResetActivationTracking();
                    _wasThrottling = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogThrottlerError(_logger, ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        if (_wasThrottling)
        {
            _windowController.RestoreProcesses();
        }
    }

    /// <summary>
    /// Stamps a PID as user-activated when it holds the foreground, or when a window we minimized last tick is now
    /// restored (the user clicked it on the taskbar), and returns only PIDs outside the grace period.
    /// </summary>
    private int[] ExcludeRecentlyActivated(int[] trackedPids)
    {
        DateTime now = DateTime.UtcNow;
        HashSet<int> tracked = new(trackedPids);
        HashSet<int> restoredPids = new();
        HashSet<int> minimizedPids = new();

        IntPtr foregroundHwnd = _nativeWindowMethods.GetForegroundWindow();
        int foregroundPid = foregroundHwnd != IntPtr.Zero ? (int)_nativeWindowMethods.GetWindowThreadProcessId(foregroundHwnd) : 0;

        _nativeWindowMethods.EnumerateWindows(hWnd =>
        {
            if (!_nativeWindowMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            int pid = (int)_nativeWindowMethods.GetWindowThreadProcessId(hWnd);
            if (tracked.Contains(pid))
            {
                bool isMinimized = User32.IsIconicOverride?.Invoke(hWnd) ?? User32.IsIconic(hWnd);
                (isMinimized ? minimizedPids : restoredPids).Add(pid);
            }

            return true;
        });

        if (tracked.Contains(foregroundPid))
        {
            _lastUserActivationUtc[foregroundPid] = now;
        }

        foreach (int pid in restoredPids.Where(_minimizedAfterLastTick.Contains))
        {
            _lastUserActivationUtc[pid] = now;
        }

        foreach (int stalePid in _lastUserActivationUtc.Keys.Where(pid => !tracked.Contains(pid)).ToList())
        {
            _lastUserActivationUtc.Remove(stalePid);
        }

        // Only remember PIDs whose windows are all minimized; a mixed state means the user restored one of them.
        minimizedPids.ExceptWith(restoredPids);
        _minimizedAfterLastTick = minimizedPids;

        return trackedPids
            .Where(pid => !_lastUserActivationUtc.TryGetValue(pid, out DateTime activatedAt) ||
                          now - activatedAt >= UserActivationGracePeriod)
            .ToArray();
    }

    private void ResetActivationTracking()
    {
        _lastUserActivationUtc.Clear();
        _minimizedAfterLastTick.Clear();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Background window throttler encountered an error.")]
    private static partial void LogThrottlerError(ILogger logger, Exception ex);
}
