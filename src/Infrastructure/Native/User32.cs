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

            // Wait for the Roblox window and D3D11SwapChain to finish initial creation before resizing once.
            await Task.Delay(1500, ct).ConfigureAwait(false);
            process.Refresh();

            if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr hWnd = process.MainWindowHandle;

            // One-shot initial sizing only: drag/resize persistence comes from the injected FastFlags
            // (RobloxFastFlagInjector), so the window keeps its native frame and normal Windows messages.
            uint flags = SWP_NOMOVE | SWP_NOZORDER;
            bool resized = SetWindowPos(
                hWnd,
                IntPtr.Zero,
                0,
                0,
                width,
                height,
                flags);

            int win32Error = resized ? 0 : Marshal.GetLastWin32Error();
            Console.WriteLine($"[User32] SetWindowPos(PID={processId}, hWnd=0x{hWnd:X}, {width}x{height}) => {resized} (err={win32Error})");
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
