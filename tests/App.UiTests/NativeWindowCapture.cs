namespace RbxManager.App.UiTests;

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class NativeWindowCapture
{
    private const uint PW_CLIENTONLY = 1;
    private const uint PW_RENDERFULLCONTENT = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Math.Max(0, Right - Left);
        public int Height => Math.Max(0, Bottom - Top);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    public static Bitmap CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle cannot be zero.", nameof(hwnd));
        }

        if (!GetWindowRect(hwnd, out RECT rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            throw new InvalidOperationException($"Unable to obtain valid window dimensions for HWND {hwnd}.");
        }

        int width = rect.Width;
        int height = rect.Height;

        ShowWindow(hwnd, 5); // SW_SHOW
        SetForegroundWindow(hwnd);

        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = g.GetHdc();
            try
            {
                // Win32 PrintWindow with PW_RENDERFULLCONTENT (0x02) for WPF/DirectX DWM-composed windows
                bool success = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                if (!success)
                {
                    // Fallback to PW_CLIENTONLY or 0 if render-full-content fails
                    PrintWindow(hwnd, hdc, PW_CLIENTONLY);
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        return bitmap;
    }

    public static void SaveBitmap(Bitmap bitmap, string outputPath)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    /// <summary>
    /// WPF ContextMenu/DropDown content lives in separate WS_POPUP HWNDs that PrintWindow(main) never includes, so a
    /// screenshot alone could not prove a menu rendered. Returns a new image (grown to the union of the window and its
    /// visible caption-less popups) with every popup painted at its real on-screen offset.
    /// </summary>
    public static Bitmap ComposeWithProcessPopups(Bitmap capture, IntPtr targetHwnd, int processId, out int popupCount)
    {
        popupCount = 0;
        if (!GetWindowRect(targetHwnd, out RECT target))
        {
            return new Bitmap(capture);
        }

        System.Collections.Generic.List<(IntPtr Hwnd, RECT Rect)> popups = new();
        EnumWindows((hwnd, _) =>
        {
            if (hwnd != targetHwnd && IsWindowVisible(hwnd) &&
                GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid == processId &&
                GetWindowRect(hwnd, out RECT rect) && rect.Width > 0 && rect.Height > 0 &&
                rect.Right > target.Left && rect.Left < target.Right && rect.Bottom > target.Top && rect.Top < target.Bottom)
            {
                long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                if ((style & WS_POPUP) != 0 && (style & WS_CAPTION) != WS_CAPTION)
                {
                    popups.Add((hwnd, rect));
                }
            }

            return true;
        }, IntPtr.Zero);

        int left = target.Left, top = target.Top, right = target.Right, bottom = target.Bottom;
        foreach ((_, RECT rect) in popups)
        {
            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }

        Bitmap composed = new(right - left, bottom - top, PixelFormat.Format32bppArgb);
        using Graphics canvas = Graphics.FromImage(composed);
        canvas.Clear(Color.Transparent);
        canvas.DrawImageUnscaled(capture, target.Left - left, target.Top - top);

        // EnumWindows lists top-most first; paint bottom-up so z-order is preserved.
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            (IntPtr hwnd, RECT rect) = popups[i];
            using Bitmap popupImage = new(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(popupImage))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    _ = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            // The popup's transparent drop-shadow margin comes back as opaque pure black from PrintWindow.
            popupImage.MakeTransparent(Color.Black);
            canvas.DrawImageUnscaled(popupImage, rect.Left - left, rect.Top - top);
            popupCount++;
        }

        return composed;
    }

    private const int GWL_STYLE = -16;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_CAPTION = 0x00C00000L;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
}
