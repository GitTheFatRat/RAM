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
}
