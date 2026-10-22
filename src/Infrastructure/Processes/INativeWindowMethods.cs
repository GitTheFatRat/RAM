namespace RbxManager.Infrastructure.Processes;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

public interface INativeWindowMethods
{
    IntPtr GetForegroundWindow();
    uint GetWindowThreadProcessId(IntPtr hWnd);
    bool IsWindowVisible(IntPtr hWnd);
    bool ShowWindow(IntPtr hWnd, int nCmdShow);
    void EnumerateWindows(Func<IntPtr, bool> callback);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsNativeWindowMethods : INativeWindowMethods
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "EnumWindows")]
    private static extern bool EnumWindowsNative(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowThreadProcessIdNative(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowNative(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisibleNative(IntPtr hWnd);

    public IntPtr GetForegroundWindow() => GetForegroundWindowNative();

    public uint GetWindowThreadProcessId(IntPtr hWnd)
    {
        _ = GetWindowThreadProcessIdNative(hWnd, out uint pid);
        return pid;
    }

    public bool IsWindowVisible(IntPtr hWnd) => IsWindowVisibleNative(hWnd);

    public bool ShowWindow(IntPtr hWnd, int nCmdShow) => ShowWindowNative(hWnd, nCmdShow);

    public void EnumerateWindows(Func<IntPtr, bool> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        EnumWindowsNative((hWnd, _) => callback(hWnd), IntPtr.Zero);
    }
}
