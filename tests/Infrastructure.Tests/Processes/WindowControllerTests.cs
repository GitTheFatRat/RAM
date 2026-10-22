namespace RbxManager.Infrastructure.Tests.Processes;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Processes;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class WindowControllerTests
{
    private sealed class FakeProcessProvider : IProcessProvider
    {
        public List<ProcessSnapshot> Processes { get; set; } = new();

        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => Processes;

        public bool Kill(int pid, bool graceful) => true;
    }

    private sealed class FakeNativeWindowMethods : INativeWindowMethods
    {
        public IntPtr ForegroundHwnd { get; set; } = IntPtr.Zero;
        public Dictionary<IntPtr, uint> WindowPids { get; } = new();
        public List<(IntPtr Hwnd, int CmdShow)> ShowWindowCalls { get; } = new();

        public IntPtr GetForegroundWindow() => ForegroundHwnd;

        public uint GetWindowThreadProcessId(IntPtr hWnd) =>
            WindowPids.TryGetValue(hWnd, out uint pid) ? pid : 0;

        public bool IsWindowVisible(IntPtr hWnd) => true;

        public bool ShowWindow(IntPtr hWnd, int nCmdShow)
        {
            ShowWindowCalls.Add((hWnd, nCmdShow));
            return true;
        }

        public void EnumerateWindows(Func<IntPtr, bool> callback)
        {
            foreach (IntPtr hWnd in WindowPids.Keys)
            {
                if (!callback(hWnd))
                {
                    break;
                }
            }
        }
    }

    [Fact]
    public void ThrottleBackgroundProcesses_WhenNoRobloxProcesses_CompletesWithoutError()
    {
        FakeProcessProvider processProvider = new();
        WindowController controller = new(processProvider);

        // Should complete cleanly without throwing
        controller.ThrottleBackgroundProcesses();
    }

    [Fact]
    public void RestoreProcesses_WhenNoThrottledWindows_CompletesWithoutError()
    {
        FakeProcessProvider processProvider = new();
        WindowController controller = new(processProvider);

        controller.RestoreProcesses();
    }

    [Fact]
    public void ThrottleAndRestore_WithFakeProcesses_ExecutesCleanly()
    {
        FakeProcessProvider processProvider = new()
        {
            Processes = [new ProcessSnapshot(12345, DateTimeOffset.UtcNow, 1024L * 1024L)]
        };
        WindowController controller = new(processProvider);

        controller.ThrottleBackgroundProcesses();
        controller.RestoreProcesses();
    }

    [Fact]
    public void ThrottleBackgroundProcesses_SkipsForeground_MinimizesOnlyTrackedPids_AndRestores()
    {
        FakeNativeWindowMethods native = new()
        {
            ForegroundHwnd = (IntPtr)100
        };
        native.WindowPids[(IntPtr)100] = 1000; // Foreground PID
        native.WindowPids[(IntPtr)200] = 2000; // Background tracked PID
        native.WindowPids[(IntPtr)300] = 3000; // Background untracked PID

        WindowController controller = new(processProvider: null, nativeMethods: native);

        // Throttle tracked PIDs [1000, 2000]
        controller.ThrottleBackgroundProcesses([1000, 2000]);

        // Window 100 is foreground -> skipped
        // Window 200 is background tracked -> minimized (SW_SHOWMINNOACTIVE = 7)
        // Window 300 is untracked -> skipped
        Assert.Single(native.ShowWindowCalls);
        Assert.Equal(((IntPtr)200, 7), native.ShowWindowCalls[0]);
        Assert.Equal(1, controller.ThrottledWindowCount);

        // Now restore
        controller.RestoreProcesses();

        // Window 200 restored (SW_RESTORE = 9)
        Assert.Equal(2, native.ShowWindowCalls.Count);
        Assert.Equal(((IntPtr)200, 9), native.ShowWindowCalls[1]);
        Assert.Equal(0, controller.ThrottledWindowCount);
    }
}
