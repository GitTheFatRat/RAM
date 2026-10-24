namespace RbxManager.Infrastructure.Native;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches Win32 API constant names")]
[SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public Win32 helper per M28 specification")]
public static class User32
{
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSENDCHANGING = 0x0400;

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

    /// <summary>Waits for the process's main window, resizes it once, and returns its handle (Zero on failure).</summary>
    public static async Task<IntPtr> ResizeProcessMainWindowAsync(
        int processId,
        int width,
        int height,
        CancellationToken ct = default)
    {
        if (processId <= 0 || width <= 0 || height <= 0)
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

            // Wait for the Roblox window and D3D11SwapChain to finish initial creation before resizing once.
            await Task.Delay(1500, ct).ConfigureAwait(false);
            process.Refresh();

            if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr hWnd = process.MainWindowHandle;
            bool resized = ResizeWindow(hWnd, width, height);
            int win32Error = resized ? 0 : Marshal.GetLastWin32Error();
            Console.WriteLine($"[User32] SetWindowPos(PID={processId}, hWnd=0x{hWnd:X}, {width}x{height}) => {resized} (err={win32Error})");
            return resized ? hWnd : IntPtr.Zero;
        }
        catch (OperationCanceledException)
        {
            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[User32] Failed to resize window for PID {processId}: {ex.Message}");
            return IntPtr.Zero;
        }
    }

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
}
