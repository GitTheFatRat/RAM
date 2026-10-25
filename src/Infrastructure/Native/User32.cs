namespace RbxManager.Infrastructure.Native;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Processes;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches Win32 API constant names")]
[SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public Win32 helper per M28 specification")]
public static class User32
{
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    private const uint SPI_GETWORKAREA = 0x0030;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out NativeRect pvParam, uint fWinIni);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    /// <summary>Waits until the process's main window exists and has settled; returns Zero on timeout or exit.</summary>
    public static async Task<IntPtr> WaitForMainWindowAsync(int processId, CancellationToken ct = default)
    {
        if (processId <= 0)
        {
            return IntPtr.Zero;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            const int pollIntervalMs = 200;
            const int maxWaitMs = 30000;
            int waitedMs = 0;

            while (process.MainWindowHandle == IntPtr.Zero && !process.HasExited)
            {
                if (waitedMs >= maxWaitMs)
                {
                    Console.WriteLine($"[User32] Timed out waiting for MainWindowHandle on PID {processId}.");
                    return IntPtr.Zero;
                }

                await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
                waitedMs += pollIntervalMs;
                process.Refresh();
            }

            if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // Wait for the Roblox window and D3D11SwapChain to finish initial creation before touching it.
            await Task.Delay(1500, ct).ConfigureAwait(false);
            process.Refresh();

            return process.HasExited ? IntPtr.Zero : process.MainWindowHandle;
        }
        catch (OperationCanceledException)
        {
            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[User32] Failed to find main window for PID {processId}: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Primary monitor work area (excludes the taskbar) in physical pixels, the unit SetWindowPos uses.
    /// WPF's SystemParameters.WorkArea is in DIPs and would under-fill the grid at display scaling above 100%.
    /// </summary>
    public static ScreenArea GetPrimaryWorkArea() =>
        SystemParametersInfo(SPI_GETWORKAREA, 0, out NativeRect rect, 0)
            ? new ScreenArea(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : default;

    /// <summary>
    /// Size-only change. SWP_NOSENDCHANGING is required: without it Roblox's WM_GETMINMAXINFO
    /// (ptMinTrackSize = 800x600) clamps any request below that floor.
    /// </summary>
    public static bool ResizeWindow(IntPtr hWnd, int width, int height) =>
        SetWindowPos(
            hWnd,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);

    /// <summary>Position and size in one call, for initial tile placement: <see cref="ResizeWindow"/> without SWP_NOMOVE.</summary>
    public static bool MoveResizeWindow(IntPtr hWnd, int x, int y, int width, int height) =>
        SetWindowPos(
            hWnd,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
}
