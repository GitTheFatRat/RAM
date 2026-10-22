namespace RbxManager.Infrastructure.Tests.Processes;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public async Task TrimMemoryAsync_WhenNoProcesses_CompletesAndRecordsZero()
    {
        FakeProcessProvider provider = new();
        WindowsMemoryOptimizer optimizer = new(provider);

        await optimizer.TrimMemoryAsync();

        Assert.Equal(0, optimizer.LastTrimmedCount);
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

        WindowsMemoryOptimizer optimizer = new(provider, pid =>
        {
            trimmedPids.Add(pid);
            return true;
        });

        await optimizer.TrimMemoryAsync();

        Assert.Equal(2, optimizer.LastTrimmedCount);
        Assert.Equal([1001, 1002], trimmedPids);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => optimizer.TrimMemoryAsync(cts.Token));
    }

    [Fact]
    public async Task TrimMemoryAsync_DryRunWithNonExistentPid_DoesNotThrow()
    {
        // PID 9999999 is guaranteed invalid or inaccessible; native OpenProcess will fail gracefully
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

        WindowsMemoryOptimizer optimizer = new(provider, pid => throw new InvalidOperationException("Simulated failure"));

        await optimizer.TrimMemoryAsync();

        Assert.Equal(0, optimizer.LastTrimmedCount);
    }
}
