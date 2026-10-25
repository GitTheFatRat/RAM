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
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public const int GWL_STYLE = -16;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_MINIMIZE = 0x20000000;
    public const int WS_MAXIMIZE = 0x01000000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_SYSMENU = 0x00080000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int SW_RESTORE = 9;
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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

            while (!process.HasExited)
            {
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

                if (process.HasExited)
                {
                    return IntPtr.Zero;
                }

                // Wait for the Roblox window and D3D11SwapChain to finish initial creation before touching it.
                await Task.Delay(1500, ct).ConfigureAwait(false);
                waitedMs += 1500;
                process.Refresh();

                if (process.HasExited)
                {
                    return IntPtr.Zero;
                }

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }

            return IntPtr.Zero;
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
    /// Restores minimized/maximized state and strips fullscreen/minimize/maximize styles so clicking
    /// another window does not trigger Windows' fullscreen focus-loss auto-minimization.
    /// </summary>
    public static void ForceWindowedMode(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(hWnd) || IsZoomed(hWnd))
        {
            _ = ShowWindow(hWnd, SW_RESTORE);
        }

        int style = GetWindowLong(hWnd, GWL_STYLE);
        if (style == 0)
        {
            return;
        }

        int windowedStyle = (style & ~(WS_POPUP | WS_MAXIMIZE | WS_MINIMIZE | WS_MAXIMIZEBOX | WS_MINIMIZEBOX))
            | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_VISIBLE;

        if (windowedStyle != style)
        {
            _ = SetWindowLong(hWnd, GWL_STYLE, windowedStyle);
        }
    }

    /// <summary>
    /// Size-only change. SWP_NOSENDCHANGING is required: without it Roblox's WM_GETMINMAXINFO
    /// (ptMinTrackSize = 800x600) clamps any request below that floor.
    /// </summary>
    public static bool ResizeWindow(IntPtr hWnd, int width, int height)
    {
        ForceWindowedMode(hWnd);
        return SetWindowPos(
            hWnd,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOSENDCHANGING);
    }

    /// <summary>Position and size in one call, for initial tile placement: <see cref="ResizeWindow"/> without SWP_NOMOVE.</summary>
    public static bool MoveResizeWindow(IntPtr hWnd, int x, int y, int width, int height)
    {
        ForceWindowedMode(hWnd);
        return SetWindowPos(
            hWnd,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOSENDCHANGING);
    }
}
