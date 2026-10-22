namespace RbxManager.Infrastructure.Tests.Processes;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using RbxManager.Infrastructure.Processes;
using Xunit;

public sealed class RobloxLogWatcherTests : IDisposable
{
    private readonly string _tempDir;

    public RobloxLogWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rbx_logwatcher_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task WatchLog_NormalLines_DoesNotRaiseEvent()
    {
        string logPath = Path.Combine(_tempDir, "0.1_Player_test_last.log");
        await File.WriteAllTextAsync(logPath, "Initial line 1\nInitial line 2\n");

        using RobloxLogWatcher watcher = new(_tempDir);
        bool eventRaised = false;
        watcher.GhostDisconnectDetected += (pid, reason) => eventRaised = true;

        watcher.StartWatching(1001, DateTimeOffset.UtcNow, logPath);

        await File.AppendAllTextAsync(logPath, "Normal gameplay log entry\n");
        await Task.Delay(350);

        Assert.False(eventRaised);
    }

    [Fact]
    public async Task WatchLog_ConnectionLost_RaisesDisconnectEvent()
    {
        string logPath = Path.Combine(_tempDir, "0.1_Player_test_last.log");
        await File.WriteAllTextAsync(logPath, "Initial line\n");

        using RobloxLogWatcher watcher = new(_tempDir);
        int detectedPid = 0;
        string detectedKeyword = string.Empty;
        TaskCompletionSource<bool> tcs = new();

        watcher.GhostDisconnectDetected += (pid, keyword) =>
        {
            detectedPid = pid;
            detectedKeyword = keyword;
            tcs.TrySetResult(true);
        };

        watcher.StartWatching(2002, DateTimeOffset.UtcNow, logPath);

        await File.AppendAllTextAsync(logPath, "Error: Connection lost to server.\n");

        Task completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completedTask);
        Assert.Equal(2002, detectedPid);
        Assert.Equal("Connection lost", detectedKeyword);
    }

    [Theory]
    [InlineData("Network disconnect: Connection lost to host", "Connection lost")]
    [InlineData("Critical failure: Error 277 occurred", "Error 277")]
    [InlineData("Security kick: Error 268 client behavior", "Error 268")]
    public async Task WatchLog_KnownKeywords_RaiseDisconnectEvents(string logLine, string expectedKeyword)
    {
        string logPath = Path.Combine(_tempDir, $"0.1_Player_{Guid.NewGuid():N}_last.log");
        await File.WriteAllTextAsync(logPath, "Starting game...\n");

        using RobloxLogWatcher watcher = new(_tempDir);
        int detectedPid = 0;
        string detectedKeyword = string.Empty;
        TaskCompletionSource<bool> tcs = new();

        watcher.GhostDisconnectDetected += (pid, keyword) =>
        {
            detectedPid = pid;
            detectedKeyword = keyword;
            tcs.TrySetResult(true);
        };

        watcher.StartWatching(3003, DateTimeOffset.UtcNow, logPath);

        await File.AppendAllTextAsync(logPath, logLine + "\n");

        Task completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completedTask);
        Assert.Equal(3003, detectedPid);
        Assert.Equal(expectedKeyword, detectedKeyword);
    }

    [Fact]
    public async Task StopWatching_ReleasesFileLockCleanly()
    {
        string logPath = Path.Combine(_tempDir, "0.1_Player_lock_last.log");
        await File.WriteAllTextAsync(logPath, "Starting...\n");

        RobloxLogWatcher watcher = new(_tempDir);
        watcher.StartWatching(4004, DateTimeOffset.UtcNow, logPath);
        await Task.Delay(100);

        watcher.StopWatching(4004);
        watcher.Dispose();

        // If lock was not released, File.Delete would throw IOException
        File.Delete(logPath);
        Assert.False(File.Exists(logPath));
    }

    [Fact]
    public async Task ResolveLogFile_WhenMultipleCandidatesExist_SkipsMonitoringDueToAmbiguity()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string logPath1 = Path.Combine(_tempDir, "0.1_Player_ambig1.log");
        string logPath2 = Path.Combine(_tempDir, "0.1_Player_ambig2.log");
        await File.WriteAllTextAsync(logPath1, "Log 1 start\n");
        await File.WriteAllTextAsync(logPath2, "Log 2 start\n");
        File.SetCreationTimeUtc(logPath1, now.UtcDateTime);
        File.SetCreationTimeUtc(logPath2, now.UtcDateTime);

        using RobloxLogWatcher watcher = new(_tempDir);
        bool eventRaised = false;
        watcher.GhostDisconnectDetected += (pid, reason) => eventRaised = true;

        watcher.StartWatching(5005, now);

        await File.AppendAllTextAsync(logPath1, "Connection lost\n");
        await File.AppendAllTextAsync(logPath2, "Connection lost\n");
        await Task.Delay(350);

        Assert.False(eventRaised);
    }

    [Fact]
    public async Task ResolveLogFile_WhenSingleCandidateExists_BindsAndDetectsDisconnect()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string logPath = Path.Combine(_tempDir, "0.1_Player_single.log");
        await File.WriteAllTextAsync(logPath, "Log single start\n");
        File.SetCreationTimeUtc(logPath, now.UtcDateTime);

        using RobloxLogWatcher watcher = new(_tempDir);
        TaskCompletionSource<int> tcs = new();
        watcher.GhostDisconnectDetected += (pid, reason) => tcs.TrySetResult(pid);

        watcher.StartWatching(6006, now);

        await File.AppendAllTextAsync(logPath, "Connection lost\n");

        Task completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completedTask);
        Assert.Equal(6006, await tcs.Task);
    }
}
