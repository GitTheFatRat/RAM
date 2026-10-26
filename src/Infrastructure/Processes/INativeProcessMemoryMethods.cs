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
    public SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId) =>
        new(IntPtr.Zero, true);

    public bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize) =>
        false;

    public int GetLastWin32Error() => 87;
}
