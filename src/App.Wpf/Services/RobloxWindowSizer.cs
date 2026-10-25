namespace RbxManager.App.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Native;

/// <summary>
/// Applies the user's custom Roblox window size, keeps it there across drags, and (with Auto-Tile)
/// places each new window in the next free grid slot of the primary work area.
/// </summary>
internal static class RobloxWindowSizer
{
    private static readonly Dictionary<int, int> TileSlotsByPid = new();
    private static readonly object TileLock = new();

    public static async Task ApplyIfEnabledAsync(ISettingsService? settingsService, int processId, CancellationToken ct)
    {
        if (settingsService?.CurrentSettings is not { EnableCustomResolution: true } settings)
        {
            return;
        }

        int width = settings.ResolutionWidth;
        int height = settings.ResolutionHeight;
        IntPtr hWnd = await User32.WaitForMainWindowAsync(processId, ct).ConfigureAwait(false);
        if (hWnd == IntPtr.Zero)
        {
            Serilog.Log.Information("Custom window resolution for PID {ProcessId}: main window not found", processId);
            return;
        }

        GridSlot? tile = settings.EnableAutoTile ? ReserveTile(processId, width, height) : null;
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
