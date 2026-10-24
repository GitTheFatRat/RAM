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

    public static async Task<bool> ResizeProcessMainWindowAsync(
        int processId,
        int width,
        int height,
        CancellationToken ct = default)
    {
        if (processId <= 0 || width <= 0 || height <= 0)
        {
            return false;
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
                    return false;
                }

                await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
                waitedMs += pollIntervalMs;
                process.Refresh();
            }

            if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            // Wait 1 second after MainWindowHandle becomes non-zero so the Roblox client finishes initializing its window.
            await Task.Delay(1000, ct).ConfigureAwait(false);
            process.Refresh();

            if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            uint flags = SWP_NOZORDER | SWP_NOMOVE | SWP_NOSENDCHANGING;
            bool resized = SetWindowPos(
                process.MainWindowHandle,
                IntPtr.Zero,
                0,
                0,
                width,
                height,
                flags);

            int win32Error = resized ? 0 : Marshal.GetLastWin32Error();
            Console.WriteLine($"[User32] SetWindowPos(PID={processId}, hWnd=0x{process.MainWindowHandle:X}, {width}x{height}) => {resized} (err={win32Error})");

            // Re-apply once more after 1.5s in case the Roblox client overrides window bounds during place load.
            await Task.Delay(1500, ct).ConfigureAwait(false);
            process.Refresh();
            if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
            {
                resized = SetWindowPos(
                    process.MainWindowHandle,
                    IntPtr.Zero,
                    0,
                    0,
                    width,
                    height,
                    flags);
            }

            return resized;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[User32] Failed to resize window for PID {processId}: {ex.Message}");
            return false;
        }
    }
}
