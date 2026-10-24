namespace RbxManager.App.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Native;

/// <summary>Applies the user's custom Roblox window size and keeps it there across drags.</summary>
internal static class RobloxWindowSizer
{
    public static async Task ApplyIfEnabledAsync(ISettingsService? settingsService, int processId, CancellationToken ct)
    {
        if (settingsService?.CurrentSettings is not { EnableCustomResolution: true } settings)
        {
            return;
        }

        int width = settings.ResolutionWidth;
        int height = settings.ResolutionHeight;
        IntPtr hWnd = await User32.ResizeProcessMainWindowAsync(processId, width, height, ct).ConfigureAwait(false);
        bool locked = hWnd != IntPtr.Zero && AttachOnUiThread(hWnd, width, height);
        Serilog.Log.Information(
            "Custom window resolution {Width}x{Height} for PID {ProcessId}: resized={Resized}, locked={Locked}",
            width,
            height,
            processId,
            hWnd != IntPtr.Zero,
            locked);
    }

    // WinEvent callbacks are pumped by the installing thread, so the hook must live on the UI dispatcher.
    private static bool AttachOnUiThread(IntPtr hWnd, int width, int height)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess()
            ? WindowSizeLock.Attach(hWnd, width, height)
            : dispatcher.Invoke(() => WindowSizeLock.Attach(hWnd, width, height));
    }
}
