namespace RbxManager.Infrastructure.Tests.Processes;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Processes;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryOptimizerTests
{
    private sealed class FakeProcessProvider : IProcessProvider
    {
        public List<ProcessSnapshot> Processes { get; set; } = new();

        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => Processes;

        public bool Kill(int pid, bool graceful) => true;
    }

    private sealed class FakeNativeProcessMemoryMethods : INativeProcessMemoryMethods
    {
        public List<(uint DesiredAccess, int Pid)> OpenProcessCalls { get; } = new();
        public HashSet<int> AccessDeniedPids { get; } = new();
        public HashSet<int> FailingPids { get; } = new();
        private int _lastError;

        public SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId)
        {
            OpenProcessCalls.Add((dwDesiredAccess, dwProcessId));
            if (AccessDeniedPids.Contains(dwProcessId))
            {
                _lastError = 5; // ERROR_ACCESS_DENIED
                return new SafeProcessHandle(IntPtr.Zero, true);
            }
            if (FailingPids.Contains(dwProcessId))
            {
                _lastError = 87; // ERROR_INVALID_PARAMETER
                return new SafeProcessHandle(IntPtr.Zero, true);
            }
            // Return pseudo valid handle (non-zero)
            return new SafeProcessHandle((IntPtr)0x1234, false);
        }

        public bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize) => true;

        public int GetLastWin32Error() => _lastError;
    }

    private sealed class FakeNativeWindowMethods : INativeWindowMethods
    {
        public IntPtr ForegroundHwnd { get; set; } = IntPtr.Zero;
        public Dictionary<IntPtr, uint> WindowPids { get; } = new();

        public IntPtr GetForegroundWindow() => ForegroundHwnd;

        public uint GetWindowThreadProcessId(IntPtr hWnd) =>
            WindowPids.TryGetValue(hWnd, out uint pid) ? pid : 0;

        public bool IsWindowVisible(IntPtr hWnd) => true;
        public bool ShowWindow(IntPtr hWnd, int nCmdShow) => true;
        public void EnumerateWindows(Func<IntPtr, bool> callback) { }
    }

    [Fact]
    public async Task TrimMemoryAsync_WhenNoProcesses_CompletesAndRecordsZero()
    {
        FakeProcessProvider provider = new();
        WindowsMemoryOptimizer optimizer = new(provider);

        await optimizer.TrimMemoryAsync();

        Assert.Equal(0, optimizer.LastTrimmedCount);
        Assert.Equal(0, optimizer.LastAccessDeniedCount);
        Assert.Equal(0, optimizer.LastFailedCount);
    }

    [Fact]
    public async Task TrimMemoryAsync_WithCustomDelegate_TrimsAllPids()
    {
        List<int> trimmedPids = new();
        FakeProcessProvider provider = new()
        {
            Processes =
            [
                new ProcessSnapshot(1001, DateTimeOffset.UtcNow, 256 * 1024 * 1024),
                new ProcessSnapshot(1002, DateTimeOffset.UtcNow, 512 * 1024 * 1024)
            ]
        };

        WindowsMemoryOptimizer optimizer = new(
            provider,
            nativeMemoryMethods: null,
            nativeWindowMethods: null,
            customTrimProcess: pid =>
            {
                trimmedPids.Add(pid);
                return true;
            });

        await optimizer.TrimMemoryAsync();

        Assert.Equal(2, optimizer.LastTrimmedCount);
        Assert.Equal([1001, 1002], trimmedPids);
    }

    [Fact]
    public async Task TrimMemoryAsync_TracksMinimalAccessAndCountsAccurately()
    {
        FakeNativeProcessMemoryMethods nativeMem = new();
        nativeMem.AccessDeniedPids.Add(1002);
        nativeMem.FailingPids.Add(1003);

        FakeNativeWindowMethods nativeWin = new()
        {
            ForegroundHwnd = (IntPtr)999
        };
        nativeWin.WindowPids[(IntPtr)999] = 1000; // Foreground PID

        WindowsMemoryOptimizer optimizer = new(
            processProvider: null,
            nativeMemoryMethods: nativeMem,
            nativeWindowMethods: nativeWin);

        // Trim PIDs: 1000 (foreground, should skip), 1001 (success), 1002 (access denied), 1003 (fail)
        await optimizer.TrimMemoryAsync([1000, 1001, 1002, 1003]);

        Assert.Equal(1, optimizer.LastTrimmedCount);
        Assert.Equal(1, optimizer.LastAccessDeniedCount);
        Assert.Equal(1, optimizer.LastFailedCount);

        // Verify PID 1000 was skipped and not opened
        Assert.DoesNotContain(nativeMem.OpenProcessCalls, c => c.Pid == 1000);

        // Verify minimal privileges (0x0100 PROCESS_SET_QUOTA only)
        Assert.All(nativeMem.OpenProcessCalls, call =>
        {
            Assert.Equal(0x0100u, call.DesiredAccess);
        });
    }

    [Fact]
    public async Task TrimMemoryAsync_WhenAlreadyCancelled_ReturnsCanceledTask()
    {
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(1001, DateTimeOffset.UtcNow, 100)]
        };

        WindowsMemoryOptimizer optimizer = new(provider);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => optimizer.TrimMemoryAsync(null, cts.Token));
    }

    [Fact]
    public async Task TrimMemoryAsync_DryRunWithNonExistentPid_DoesNotThrow()
    {
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(9999999, DateTimeOffset.UtcNow, 100)]
        };

        WindowsMemoryOptimizer optimizer = new(provider);

        await optimizer.TrimMemoryAsync();

        Assert.Equal(0, optimizer.LastTrimmedCount);
    }

    [Fact]
    public async Task TrimMemoryAsync_WhenCustomDelegateThrows_DoesNotCrash()
    {
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(1001, DateTimeOffset.UtcNow, 100)]
        };

        WindowsMemoryOptimizer optimizer = new(
            provider,
            nativeMemoryMethods: null,
            nativeWindowMethods: null,
            customTrimProcess: pid => throw new InvalidOperationException("Simulated failure"));

        await optimizer.TrimMemoryAsync();

        Assert.Equal(0, optimizer.LastTrimmedCount);
    }
}
