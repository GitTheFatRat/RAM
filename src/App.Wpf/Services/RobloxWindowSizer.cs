namespace RbxManager.App.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Native;

/// <summary>
/// Applies the user's custom Roblox window size, keeps it there across drags, and (with Auto-Tile)
/// places each new window in the next free grid slot of the primary work area.
/// </summary>
internal static class RobloxWindowSizer
{
    public const int RecoveryWidth = 800;
    public const int RecoveryHeight = 600;

    // An account override waits for that account's next Roblox window; if the relaunch never happens it
    // must not ambush a launch hours later, so it expires (recovery browser itself times out at 15 min).
    private static readonly TimeSpan AccountOverrideLifetime = TimeSpan.FromMinutes(20);

    private static readonly Dictionary<int, int> TileSlotsByPid = new();
    private static readonly ConcurrentDictionary<int, ProcessOverride> ProcessOverrides = new();
    private static readonly ConcurrentDictionary<AccountId, AccountOverride> AccountSessionOverrides = new();
    private static readonly object TileLock = new();

    // Minimized windows sit at (-32000, -32000); anything this far out is parked or has a corrupted restore rect.
    private const int ParkedCoordinateThreshold = -10000;
    private const int OffScreenThreshold = -1000;
    private const int SW_SHOWMINNOACTIVE = 7;
    private const int WPF_ASYNCWINDOWPLACEMENT = 0x0004;
    private const uint MONITOR_DEFAULTTONULL = 0x00000000;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;

    private static readonly ConcurrentDictionary<int, ManagedWindow> ManagedWindows = new();
    private static readonly WinEventProc RestoreWatcherCallback = OnRestoreOrActivate;
    private static IntPtr foregroundHook;
    private static IntPtr minimizeEndHook;

    private readonly record struct ProcessOverride(int Width, int Height, DateTime ProcessStartTime);

    private readonly record struct AccountOverride(int Width, int Height, DateTime ExpiresAtUtc);

    /// <param name="AwaitingRestore">Geometry was staged while minimized; apply live + attach the lock on restore.</param>
    private readonly record struct ManagedWindow(int Width, int Height, GridSlot? Tile, DateTime? ProcessStartTime, bool AwaitingRestore);

    private enum GeometryOutcome
    {
        Applied,
        Failed,
        DeferredWhileMinimized,
    }

    public static void SetSessionOverride(AccountId accountId, int width = RecoveryWidth, int height = RecoveryHeight) =>
        AccountSessionOverrides[accountId] = new AccountOverride(width, height, DateTime.UtcNow + AccountOverrideLifetime);

    public static void SetProcessOverride(int processId, int width = RecoveryWidth, int height = RecoveryHeight)
    {
        if (TryGetProcessStartTime(processId) is DateTime startTime)
        {
            ProcessOverrides[processId] = new ProcessOverride(width, height, startTime);
        }
    }

    public static void CancelSessionOverride(AccountId accountId) =>
        AccountSessionOverrides.TryRemove(accountId, out _);

    public static void ClearOverrides()
    {
        ProcessOverrides.Clear();
        AccountSessionOverrides.Clear();
    }

    internal static (bool Enabled, int Width, int Height, bool EnableAutoTile) ResolveTargetResolution(
        ISettingsService? settingsService,
        int processId,
        AccountId? accountId = null)
    {
        if (accountId.HasValue &&
            AccountSessionOverrides.TryRemove(accountId.Value, out AccountOverride accountOverride) &&
            accountOverride.ExpiresAtUtc > DateTime.UtcNow)
        {
            // Pin to this PID for later re-applies (settings changes); the consumed size applies right now even if
            // the process is already gone or inaccessible.
            SetProcessOverride(processId, accountOverride.Width, accountOverride.Height);
            bool overrideAutoTile = settingsService?.CurrentSettings is { EnableCustomResolution: true, EnableAutoTile: true };
            return (true, accountOverride.Width, accountOverride.Height, overrideAutoTile);
        }

        if (TryGetLiveProcessOverride(processId) is ProcessOverride overrideSize)
        {
            bool autoTile = settingsService?.CurrentSettings is { EnableCustomResolution: true, EnableAutoTile: true };
            return (true, overrideSize.Width, overrideSize.Height, autoTile);
        }

        if (settingsService?.CurrentSettings is { EnableCustomResolution: true } settings)
        {
            return (true, settings.ResolutionWidth, settings.ResolutionHeight, settings.EnableAutoTile);
        }

        return (false, 0, 0, false);
    }

    // PIDs are recycled by Windows: an override only applies to the exact process instance it was set for.
    private static ProcessOverride? TryGetLiveProcessOverride(int processId)
    {
        if (!ProcessOverrides.TryGetValue(processId, out ProcessOverride entry))
        {
            return null;
        }

        if (TryGetProcessStartTime(processId) == entry.ProcessStartTime)
        {
            return entry;
        }

        ProcessOverrides.TryRemove(new KeyValuePair<int, ProcessOverride>(processId, entry));
        return null;
    }

    private static DateTime? TryGetProcessStartTime(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.HasExited ? null : process.StartTime;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static async Task ApplyIfEnabledAsync(
        ISettingsService? settingsService,
        int processId,
        CancellationToken ct,
        AccountId? accountId = null)
    {
        (bool enabled, int width, int height, bool enableAutoTile) = ResolveTargetResolution(settingsService, processId, accountId);
        if (!enabled)
        {
            return;
        }

        IntPtr hWnd = await User32.WaitForMainWindowAsync(processId, ct).ConfigureAwait(false);
        if (hWnd == IntPtr.Zero)
        {
            Serilog.Log.Information("Custom window resolution for PID {ProcessId}: main window not found", processId);
            return;
        }

        EnsureRestoreWatcherOnUiThread();

        GridSlot? tile = enableAutoTile ? ReserveTile(processId, width, height) : null;
        GeometryOutcome outcome = ApplyGeometry(processId, hWnd, width, height, tile);
        bool locked = outcome == GeometryOutcome.Applied && AttachOnUiThread(hWnd, width, height, tile);
        Serilog.Log.Information(
            "Custom window resolution {Width}x{Height} for PID {ProcessId}: outcome={Outcome}, locked={Locked}, tile={Tile}",
            width,
            height,
            processId,
            outcome,
            locked,
            tile?.ToString() ?? "-");

        // Roblox re-centers/recreates its window when the 3D scene initializes ~2-15s after startup; re-apply to lock the grid slot.
        for (int pass = 0; pass < 6 && !ct.IsCancellationRequested; pass++)
        {
            try
            {
                await Task.Delay(2500, ct).ConfigureAwait(false);
                using Process proc = Process.GetProcessById(processId);
                if (proc.HasExited)
                {
                    break;
                }

                proc.Refresh();
                IntPtr currentHwnd = proc.MainWindowHandle;
                if (currentHwnd == IntPtr.Zero)
                {
                    continue;
                }

                bool handleChanged = currentHwnd != hWnd;
                hWnd = currentHwnd;
                bool passApplied = ApplyGeometry(processId, currentHwnd, width, height, tile) == GeometryOutcome.Applied;

                if (passApplied && (handleChanged || !locked))
                {
                    locked = AttachOnUiThread(hWnd, width, height, tile);
                }
            }
            catch
            {
                break;
            }
        }
    }

    public static void ApplyToRunningProcesses(ISettingsService? settingsService)
    {
        bool globalCustomEnabled = settingsService?.CurrentSettings is { EnableCustomResolution: true };
        if (!globalCustomEnabled && ProcessOverrides.IsEmpty)
        {
            return;
        }

        foreach (int pid in ProcessOverrides.Keys.ToList())
        {
            _ = TryGetLiveProcessOverride(pid);
        }

        foreach (Process proc in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try
            {
                (bool enabled, int width, int height, bool enableAutoTile) = ResolveTargetResolution(
                    settingsService,
                    proc.Id);
                if (!enabled)
                {
                    continue;
                }

                proc.Refresh();
                IntPtr hWnd = proc.MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                {
                    continue;
                }

                EnsureRestoreWatcherOnUiThread();
                GridSlot? tile = enableAutoTile ? ReserveTile(proc.Id, width, height) : null;
                if (ApplyGeometry(proc.Id, hWnd, width, height, tile) == GeometryOutcome.Applied)
                {
                    _ = AttachOnUiThread(hWnd, width, height, tile);
                }
            }
            catch
            {
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private static GridSlot ReserveTile(int processId, int width, int height)
    {
        ScreenArea workArea = User32.GetPrimaryWorkArea();
        lock (TileLock)
        {
            foreach (int exitedPid in TileSlotsByPid.Keys.Where(pid => !IsAlive(pid)).ToList())
            {
                TileSlotsByPid.Remove(exitedPid);
            }

            if (!TileSlotsByPid.TryGetValue(processId, out int slotIndex))
            {
                slotIndex = WindowGridLayout.NextFreeSlot(TileSlotsByPid.Values.ToList());
                TileSlotsByPid[processId] = slotIndex;
            }

            return WindowGridLayout.GetSlot(slotIndex, workArea, width, height);
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // WinEvent callbacks are pumped by the installing thread, so the hook must live on the UI dispatcher.
    private static bool AttachOnUiThread(IntPtr hWnd, int width, int height, GridSlot? tile)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess()
            ? WindowSizeLock.Attach(hWnd, width, height, tile)
            : dispatcher.Invoke(() => WindowSizeLock.Attach(hWnd, width, height, tile));
    }

    /// <summary>
    /// Single gate for every size/tile write. A minimized window lives at (-32000, -32000): SetWindowPos on it
    /// (or the SW_RESTORE that ForceWindowedMode/WindowSizeLock would issue) fights the taskbar restore and can
    /// persist the parked coordinates as the restore rect. So while iconic we only stage rcNormalPosition.
    /// </summary>
    private static GeometryOutcome ApplyGeometry(int processId, IntPtr hWnd, int width, int height, GridSlot? tile)
    {
        DateTime? startTime = TryGetProcessStartTime(processId);
        if (User32.IsIconic(hWnd))
        {
            StageNormalPlacement(hWnd, width, height, tile, SW_SHOWMINNOACTIVE);
            ManagedWindows[processId] = new ManagedWindow(width, height, tile, startTime, AwaitingRestore: true);
            return GeometryOutcome.DeferredWhileMinimized;
        }

        ManagedWindows[processId] = new ManagedWindow(width, height, tile, startTime, AwaitingRestore: false);
        User32.ForceWindowedMode(hWnd);
        GridSlot? position = tile ?? (IsOffScreen(hWnd) ? CenteredSlot(width, height, User32.GetPrimaryWorkArea()) : null);
        bool applied = position is GridSlot target
            ? User32.MoveResizeWindow(hWnd, target.X, target.Y, width, height)
            : User32.ResizeWindow(hWnd, width, height);
        return applied ? GeometryOutcome.Applied : GeometryOutcome.Failed;
    }

    /// <param name="onlyIfCorrupted">Leave a sane restore rect alone (e.g. on a secondary monitor) and only repair a parked one.</param>
    private static void StageNormalPlacement(IntPtr hWnd, int width, int height, GridSlot? tile, int showCommand, bool onlyIfCorrupted = false)
    {
        WindowPlacement placement = new() { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hWnd, ref placement))
        {
            return;
        }

        // rcNormalPosition is in workspace coordinates (origin = work-area top-left), not screen coordinates.
        ScreenArea workArea = User32.GetPrimaryWorkArea();
        int restoreX = placement.NormalPosition.Left + workArea.Left;
        int restoreY = placement.NormalPosition.Top + workArea.Top;
        bool restoreRectCorrupted = IsOffScreenRect(restoreX, restoreY, width, height);
        if (onlyIfCorrupted && !restoreRectCorrupted)
        {
            return;
        }

        // A restore rect that already sits on some monitor (incl. one left of/above the primary) is kept as-is;
        // only tile slots and parked/corrupted rects are placed inside the primary work area.
        GridSlot target = tile is GridSlot tileSlot
            ? ClampToWorkArea(tileSlot, width, height, workArea)
            : restoreRectCorrupted
                ? ClampToWorkArea(CenteredSlot(width, height, workArea), width, height, workArea)
                : new GridSlot(restoreX, restoreY);

        placement.NormalPosition = new NativeRect
        {
            Left = target.X - workArea.Left,
            Top = target.Y - workArea.Top,
            Right = target.X - workArea.Left + width,
            Bottom = target.Y - workArea.Top + height,
        };
        placement.ShowCommand = showCommand;
        // Drop WPF_RESTORETOMAXIMIZED (we force windowed mode); async so a hung Roblox can't block our thread.
        placement.Flags = WPF_ASYNCWINDOWPLACEMENT;
        _ = SetWindowPlacement(hWnd, ref placement);
    }

    private static GridSlot CenteredSlot(int width, int height, ScreenArea area) =>
        new(area.Left + Math.Max(0, (area.Width - width) / 2), area.Top + Math.Max(0, (area.Height - height) / 2));

    private static GridSlot ClampToWorkArea(GridSlot slot, int width, int height, ScreenArea area) =>
        new(
            Math.Clamp(slot.X, area.Left, area.Left + Math.Max(0, area.Width - width)),
            Math.Clamp(slot.Y, area.Top, area.Top + Math.Max(0, area.Height - height)));

    // A raw "X < -1000" test would yank windows off a monitor placed left of/above the primary, so a window
    // only counts as off-screen when it is parked/corrupted or intersects no display at all.
    private static bool IsOffScreen(IntPtr hWnd) =>
        GetWindowRect(hWnd, out NativeRect bounds) &&
        IsOffScreenRect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);

    internal static bool IsOffScreenRect(int left, int top, int width, int height)
    {
        if (left <= ParkedCoordinateThreshold || top <= ParkedCoordinateThreshold)
        {
            return true;
        }

        if (left >= OffScreenThreshold && top >= OffScreenThreshold)
        {
            return false;
        }

        NativeRect rect = new() { Left = left, Top = top, Right = left + Math.Max(1, width), Bottom = top + Math.Max(1, height) };
        return MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) == IntPtr.Zero;
    }

    private static void EnsureRestoreWatcherOnUiThread()
    {
        // Without a message-pumping dispatcher the out-of-context callbacks would never be delivered.
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            InstallRestoreWatcher();
        }
        else
        {
            dispatcher.Invoke(InstallRestoreWatcher);
        }
    }

    // Global (all-process) hooks: Roblox recreates its main window, so filtering is done per event by PID.
    private static void InstallRestoreWatcher()
    {
        if (foregroundHook == IntPtr.Zero)
        {
            foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, RestoreWatcherCallback, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        if (minimizeEndHook == IntPtr.Zero)
        {
            minimizeEndHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZEEND, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, RestoreWatcherCallback, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
    }

    private static void OnRestoreOrActivate(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != OBJID_WINDOW || hWnd == IntPtr.Zero ||
            GetWindowThreadProcessId(hWnd, out uint rawPid) == 0 ||
            !ManagedWindows.TryGetValue((int)rawPid, out ManagedWindow managed))
        {
            return;
        }

        int processId = (int)rawPid;
        if (managed.ProcessStartTime is DateTime expectedStart && TryGetProcessStartTime(processId) != expectedStart)
        {
            // PID was recycled by an unrelated process.
            ManagedWindows.TryRemove(new KeyValuePair<int, ManagedWindow>(processId, managed));
            return;
        }

        try
        {
            if (User32.IsIconic(hWnd))
            {
                // Activated from the taskbar but its restore rect is parked off-screen: repair it and restore in one
                // atomic call. A healthy rect is left to Windows; MINIMIZEEND follows and completes the live apply below.
                if (eventType == EVENT_SYSTEM_FOREGROUND)
                {
                    StageNormalPlacement(hWnd, managed.Width, managed.Height, managed.Tile, User32.SW_RESTORE, onlyIfCorrupted: true);
                }

                return;
            }

            bool wasOffScreen = IsOffScreen(hWnd);
            if (!managed.AwaitingRestore && !wasOffScreen)
            {
                return;
            }

            GeometryOutcome outcome = ApplyGeometry(processId, hWnd, managed.Width, managed.Height, managed.Tile);
            if (outcome == GeometryOutcome.Applied && managed.AwaitingRestore)
            {
                _ = WindowSizeLock.Attach(hWnd, managed.Width, managed.Height, managed.Tile);
            }

            if (wasOffScreen)
            {
                _ = User32.ShowWindow(hWnd, User32.SW_RESTORE);
            }

            Serilog.Log.Information(
                "Restored Roblox window for PID {ProcessId}: offScreen={OffScreen}, deferredApply={Deferred}, outcome={Outcome}, tile={Tile}",
                processId,
                wasOffScreen,
                managed.AwaitingRestore,
                outcome,
                managed.Tile?.ToString() ?? "center");
        }
        catch (Exception ex)
        {
            // Native callback: an escaping exception would tear down the dispatcher.
            Serilog.Log.Warning(ex, "Failed to recover restored Roblox window for PID {ProcessId}", processId);
        }
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc callback, uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
}
