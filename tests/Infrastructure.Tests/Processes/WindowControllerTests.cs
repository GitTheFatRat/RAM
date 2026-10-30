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

    // M44 real-data: genuine top-level Win32 windows minimized by the OS, no User32 overrides involved.
    [Fact]
    public void RealWindow_WhileMinimized_AttachAndEveryLockEvent_NeverRestoresOrResizes()
    {
        using RealTopLevelWindow window = RealTopLevelWindow.Create(120, 140, 640, 480);
        window.MinimizeWithoutActivating();
        Assert.True(Native.User32.IsIconic(window.Handle));

        Assert.True(Native.WindowSizeLock.Attach(window.Handle, 200, 400));
        Assert.True(Native.User32.IsIconic(window.Handle));

        using Native.WindowSizeLock sizeLock = new(window.Handle, 200, 400, tilePosition: new GridSlot(0, 0));
        uint[] lockEvents =
        [
            Native.WindowSizeLock.EVENT_SYSTEM_MOVESIZEEND,
            Native.WindowSizeLock.EVENT_SYSTEM_MINIMIZESTART,
            Native.WindowSizeLock.EVENT_SYSTEM_MINIMIZEEND,
            Native.WindowSizeLock.EVENT_OBJECT_LOCATIONCHANGE,
        ];
        foreach (uint eventType in lockEvents)
        {
            sizeLock.OnWinEvent(IntPtr.Zero, eventType, window.Handle, Native.WindowSizeLock.OBJID_WINDOW, 0, 0, 0);
            Assert.True(Native.User32.IsIconic(window.Handle), $"Event 0x{eventType:X} restored a minimized window.");
        }

        // The restore rect must still be the original 640x480 at (120, 140), not the lock size or a parked coordinate.
        window.Restore();
        (int left, int top, int width, int height) = window.Bounds;
        Assert.Equal((120, 140, 640, 480), (left, top, width, height));
    }

    [Fact]
    public void RealWindow_RecreatedWhileMinimized_IsAdoptedWithoutRestore_AndTiledOnUserRestore()
    {
        using RealTopLevelWindow original = RealTopLevelWindow.Create(300, 300, 640, 480);
        using RealTopLevelWindow recreated = RealTopLevelWindow.Create(400, 400, 640, 480);
        recreated.MinimizeWithoutActivating();

        using Native.WindowSizeLock sizeLock = new(original.Handle, 200, 400, tilePosition: new GridSlot(50, 60));
        sizeLock.OnWinEvent(IntPtr.Zero, Native.WindowSizeLock.EVENT_OBJECT_SHOW, recreated.Handle, Native.WindowSizeLock.OBJID_WINDOW, 0, 0, 0);
        Assert.True(Native.User32.IsIconic(recreated.Handle));

        recreated.Restore();
        sizeLock.OnWinEvent(IntPtr.Zero, Native.WindowSizeLock.EVENT_SYSTEM_MINIMIZEEND, recreated.Handle, Native.WindowSizeLock.OBJID_WINDOW, 0, 0, 0);

        Assert.False(Native.User32.IsIconic(recreated.Handle));
        Assert.Equal((50, 60, 200, 400), recreated.Bounds);
    }

    private sealed class RealTopLevelWindow : IDisposable
    {
        private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int SW_SHOWMINNOACTIVE = 7;
        private const int SW_SHOWNOACTIVATE = 4;

        private RealTopLevelWindow(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }

        public (int Left, int Top, int Width, int Height) Bounds =>
            GetWindowRect(Handle, out NativeRect rect)
                ? (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
                : throw new InvalidOperationException("GetWindowRect failed.");

        public static RealTopLevelWindow Create(int x, int y, int width, int height)
        {
            IntPtr handle = CreateWindowEx(WS_EX_NOACTIVATE, "STATIC", "M44 real-data window", WS_OVERLAPPEDWINDOW | WS_VISIBLE, x, y, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, handle);
            return new RealTopLevelWindow(handle);
        }

        public void MinimizeWithoutActivating() => _ = ShowWindow(Handle, SW_SHOWMINNOACTIVE);

        public void Restore() => _ = ShowWindow(Handle, SW_SHOWNOACTIVATE);

        public void Dispose() => _ = DestroyWindow(Handle);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
    }
}
