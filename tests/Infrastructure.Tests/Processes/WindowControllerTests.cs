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

    [Fact]
    public void M35_MoveResizeWindow_IncludesNoActivateAndNoZOrder_AndStripsMinimizeMaximizeBox()
    {
        IntPtr targetHwnd = (IntPtr)0x1234;
        int initialStyle = Native.User32.WS_POPUP
            | Native.User32.WS_VISIBLE
            | Native.User32.WS_MINIMIZE
            | Native.User32.WS_MAXIMIZE
            | Native.User32.WS_MINIMIZEBOX
            | Native.User32.WS_MAXIMIZEBOX;

        int? capturedNewStyle = null;
        (IntPtr Hwnd, int X, int Y, int Cx, int Cy, uint Flags)? capturedSetWindowPos = null;
        int showWindowRestoreCalls = 0;

        Native.User32.IsIconicOverride = _ => true;
        Native.User32.IsZoomedOverride = _ => false;
        Native.User32.ShowWindowOverride = (_, cmd) =>
        {
            if (cmd == Native.User32.SW_RESTORE)
            {
                showWindowRestoreCalls++;
            }
            return true;
        };
        Native.User32.GetWindowLongOverride = (_, _) => initialStyle;
        Native.User32.SetWindowLongOverride = (_, _, newStyle) =>
        {
            capturedNewStyle = newStyle;
            return initialStyle;
        };
        Native.User32.SetWindowPosOverride = (hWnd, _, x, y, cx, cy, flags) =>
        {
            capturedSetWindowPos = (hWnd, x, y, cx, cy, flags);
            return true;
        };

        try
        {
            bool applied = Native.User32.MoveResizeWindow(targetHwnd, 200, 0, 200, 400);

            Assert.True(applied);
            Assert.Equal(1, showWindowRestoreCalls);
            Assert.NotNull(capturedNewStyle);
            Assert.Equal(0, capturedNewStyle.Value & Native.User32.WS_MINIMIZEBOX);
            Assert.Equal(0, capturedNewStyle.Value & Native.User32.WS_MAXIMIZEBOX);
            Assert.Equal(0, capturedNewStyle.Value & Native.User32.WS_POPUP);

            Assert.NotNull(capturedSetWindowPos);
            uint flags = capturedSetWindowPos.Value.Flags;
            Assert.Equal(Native.User32.SWP_NOACTIVATE, flags & Native.User32.SWP_NOACTIVATE);
            Assert.Equal(Native.User32.SWP_NOZORDER, flags & Native.User32.SWP_NOZORDER);
            Assert.Equal(0u, flags & Native.User32.SWP_NOMOVE);
            Assert.Equal((200, 0, 200, 400), (capturedSetWindowPos.Value.X, capturedSetWindowPos.Value.Y, capturedSetWindowPos.Value.Cx, capturedSetWindowPos.Value.Cy));
        }
        finally
        {
            Native.User32.IsIconicOverride = null;
            Native.User32.IsZoomedOverride = null;
            Native.User32.ShowWindowOverride = null;
            Native.User32.GetWindowLongOverride = null;
            Native.User32.SetWindowLongOverride = null;
            Native.User32.SetWindowPosOverride = null;
        }
    }

    [Fact]
    public void M37_WindowSizeLock_OnWinEvent_MoveOnly_SkipsSetWindowPos()
    {
        IntPtr targetHwnd = (IntPtr)0x5678;
        int setWindowPosCalls = 0;

        Native.User32.IsIconicOverride = _ => false;
        Native.User32.IsZoomedOverride = _ => false;
        Native.User32.GetWindowLongOverride = (_, _) => Native.User32.WS_CAPTION | Native.User32.WS_VISIBLE;
        Native.User32.SetWindowLongOverride = (_, _, style) => style;
        Native.User32.SetWindowPosOverride = (_, _, _, _, _, _, _) =>
        {
            setWindowPosCalls++;
            return true;
        };

        try
        {
            // Window moved to (450, 220), but size is already 200x400 (Right=650, Bottom=620).
            using Native.WindowSizeLock sizeLock = new(
                targetHwnd,
                width: 200,
                height: 400,
                tilePosition: new GridSlot(0, 0),
                getBoundsOverride: () => (450, 220, 650, 620));

            sizeLock.OnWinEvent(
                IntPtr.Zero,
                Native.WindowSizeLock.EVENT_OBJECT_LOCATIONCHANGE,
                targetHwnd,
                Native.WindowSizeLock.OBJID_WINDOW,
                0,
                0,
                0);

            Assert.Equal(0, setWindowPosCalls);
        }
        finally
        {
            Native.User32.IsIconicOverride = null;
            Native.User32.IsZoomedOverride = null;
            Native.User32.GetWindowLongOverride = null;
            Native.User32.SetWindowLongOverride = null;
            Native.User32.SetWindowPosOverride = null;
        }
    }

    [Fact]
    public void M37_WindowSizeLock_OnWinEvent_Resize_AppliesSwpNoMoveFlag()
    {
        IntPtr targetHwnd = (IntPtr)0x9ABC;
        List<(int X, int Y, int Cx, int Cy, uint Flags)> setWindowPosCalls = new();

        Native.User32.IsIconicOverride = _ => false;
        Native.User32.IsZoomedOverride = _ => false;
        Native.User32.GetWindowLongOverride = (_, _) => Native.User32.WS_CAPTION | Native.User32.WS_VISIBLE;
        Native.User32.SetWindowLongOverride = (_, _, style) => style;
        Native.User32.SetWindowPosOverride = (_, _, x, y, cx, cy, flags) =>
        {
            setWindowPosCalls.Add((x, y, cx, cy, flags));
            return true;
        };

        try
        {
            // User dragged window to (450, 220) and Roblox forced 800x600 (Right=1250, Bottom=820).
            using Native.WindowSizeLock sizeLock = new(
                targetHwnd,
                width: 200,
                height: 400,
                tilePosition: new GridSlot(0, 0),
                getBoundsOverride: () => (450, 220, 1250, 820));

            sizeLock.OnWinEvent(
                IntPtr.Zero,
                Native.WindowSizeLock.EVENT_OBJECT_LOCATIONCHANGE,
                targetHwnd,
                Native.WindowSizeLock.OBJID_WINDOW,
                0,
                0,
                0);

            Assert.Single(setWindowPosCalls);
            (int _, int _, int cx, int cy, uint flags) = setWindowPosCalls[0];
            Assert.Equal(200, cx);
            Assert.Equal(400, cy);
            Assert.Equal(Native.User32.SWP_NOMOVE, flags & Native.User32.SWP_NOMOVE);
            Assert.Equal(Native.User32.SWP_NOACTIVATE, flags & Native.User32.SWP_NOACTIVATE);
            Assert.Equal(Native.User32.SWP_NOZORDER, flags & Native.User32.SWP_NOZORDER);
        }
        finally
        {
            Native.User32.IsIconicOverride = null;
            Native.User32.IsZoomedOverride = null;
            Native.User32.GetWindowLongOverride = null;
            Native.User32.SetWindowLongOverride = null;
            Native.User32.SetWindowPosOverride = null;
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task WindowSizeLock_WhenBurstLimitExceeded_SchedulesTrailingEnforcement()
    {
        IntPtr targetHwnd = (IntPtr)0xDEF0;
        int setWindowPosCalls = 0;
        System.Threading.Tasks.TaskCompletionSource<bool> trailingCall = new();

        Native.User32.IsIconicOverride = _ => false;
        Native.User32.IsZoomedOverride = _ => false;
        Native.User32.GetWindowLongOverride = (_, _) => Native.User32.WS_CAPTION | Native.User32.WS_VISIBLE;
        Native.User32.SetWindowLongOverride = (_, _, style) => style;
        Native.User32.SetWindowPosOverride = (_, _, _, _, _, _, _) =>
        {
            if (System.Threading.Interlocked.Increment(ref setWindowPosCalls) >= 9)
            {
                trailingCall.TrySetResult(true);
            }
            return true;
        };

        try
        {
            using Native.WindowSizeLock sizeLock = new(
                targetHwnd,
                width: 200,
                height: 400,
                tilePosition: null,
                getBoundsOverride: () => (0, 0, 800, 600));

            for (int i = 0; i < 10; i++)
            {
                sizeLock.OnWinEvent(
                    IntPtr.Zero,
                    Native.WindowSizeLock.EVENT_OBJECT_LOCATIONCHANGE,
                    targetHwnd,
                    Native.WindowSizeLock.OBJID_WINDOW,
                    0,
                    0,
                    0);
            }

            // First 8 calls happen synchronously; the 9th is deferred until the 1000ms burst window expires.
            Assert.Equal(8, setWindowPosCalls);

            System.Threading.Tasks.Task completed = await System.Threading.Tasks.Task.WhenAny(
                trailingCall.Task,
                System.Threading.Tasks.Task.Delay(2500));
            Assert.Same(trailingCall.Task, completed);
        }
        finally
        {
            Native.User32.IsIconicOverride = null;
            Native.User32.IsZoomedOverride = null;
            Native.User32.GetWindowLongOverride = null;
            Native.User32.SetWindowLongOverride = null;
            Native.User32.SetWindowPosOverride = null;
        }
    }
}
