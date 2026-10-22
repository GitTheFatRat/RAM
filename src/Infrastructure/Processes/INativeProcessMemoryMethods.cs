namespace RbxManager.Infrastructure.Processes;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

public interface INativeProcessMemoryMethods
{
    SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
    bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);
    int GetLastWin32Error();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsNativeProcessMemoryMethods : INativeProcessMemoryMethods
{
    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcessNative(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSizeNative(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

    public SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId) =>
        OpenProcessNative(dwDesiredAccess, bInheritHandle, dwProcessId);

    public bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize) =>
        SetProcessWorkingSetSizeNative(hProcess, dwMinimumWorkingSetSize, dwMaximumWorkingSetSize);

    public int GetLastWin32Error() => Marshal.GetLastWin32Error();
}
