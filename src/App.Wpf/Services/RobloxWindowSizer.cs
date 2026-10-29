namespace RbxManager.App.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    private readonly record struct ProcessOverride(int Width, int Height, DateTime ProcessStartTime);

    private readonly record struct AccountOverride(int Width, int Height, DateTime ExpiresAtUtc);

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

        if (User32.IsIconic(hWnd))
        {
            _ = User32.ShowWindow(hWnd, User32.SW_RESTORE);
        }

        User32.ForceWindowedMode(hWnd);

        GridSlot? tile = enableAutoTile ? ReserveTile(processId, width, height) : null;
        bool applied = tile is GridSlot slot
            ? User32.MoveResizeWindow(hWnd, slot.X, slot.Y, width, height)
            : User32.ResizeWindow(hWnd, width, height);
        bool locked = applied && AttachOnUiThread(hWnd, width, height, tile);
        Serilog.Log.Information(
            "Custom window resolution {Width}x{Height} for PID {ProcessId}: applied={Applied}, locked={Locked}, tile={Tile}",
            width,
            height,
            processId,
            applied,
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
                bool passApplied = tile is GridSlot currentSlot
                    ? User32.MoveResizeWindow(currentHwnd, currentSlot.X, currentSlot.Y, width, height)
                    : User32.ResizeWindow(currentHwnd, width, height);

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

                if (User32.IsIconic(hWnd))
                {
                    _ = User32.ShowWindow(hWnd, User32.SW_RESTORE);
                }

                User32.ForceWindowedMode(hWnd);
                GridSlot? tile = enableAutoTile ? ReserveTile(proc.Id, width, height) : null;
                bool applied = tile is GridSlot slot
                    ? User32.MoveResizeWindow(hWnd, slot.X, slot.Y, width, height)
                    : User32.ResizeWindow(hWnd, width, height);
                if (applied)
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
}
