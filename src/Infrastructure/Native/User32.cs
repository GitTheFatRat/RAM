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

        const int maxAttempts = 20; // 20 * 500ms = 10 seconds max
        for (int attempt = 0; attempt < maxAttempts && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return false;
                }

                process.Refresh();
                IntPtr mainWindowHandle = process.MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    return SetWindowPos(
                        mainWindowHandle,
                        IntPtr.Zero,
                        0,
                        0,
                        width,
                        height,
                        SWP_NOMOVE | SWP_NOZORDER);
                }
            }
            catch (ArgumentException)
            {
                // Process already exited.
                return false;
            }
            catch
            {
                // Transient process inspection error; continue polling.
            }

            try
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }
}
