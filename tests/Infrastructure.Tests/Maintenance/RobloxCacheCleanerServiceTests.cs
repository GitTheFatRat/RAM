namespace RbxManager.Infrastructure.Tests.Maintenance;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NSubstitute;
using RbxManager.Core.Maintenance;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Maintenance;

public sealed class RobloxCacheCleanerServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rbx-m34-" + Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _temp;
    private readonly IProcessProvider _processes = Substitute.For<IProcessProvider>();

    public RobloxCacheCleanerServiceTests()
    {
        _local = Path.Combine(_root, "LocalAppData", "Roblox");
        _temp = Path.Combine(_root, "Temp", "Roblox");
        _processes.GetRobloxProcesses().Returns(new List<ProcessSnapshot>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string CreateFile(string path, int bytes = 100, DateTime? createdUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (createdUtc.HasValue)
        {
            File.SetCreationTimeUtc(path, createdUtc.Value);
        }

        return path;
    }

    private RobloxCacheCleanerService CreateCleaner() => new(_processes, _local, _temp);

    [Fact]
    public void Clean_NoRobloxRunning_RemovesLogsDumpsAndCachesButKeepsNonTargets()
    {
        string log = CreateFile(Path.Combine(_local, "logs", "0.744.0_20261024T152340Z_Player_14A18_last.log"), 1000);
        string dump = CreateFile(Path.Combine(_local, "logs", "crashes", "2026-10-24.dmp"), 5000);
        string cert = CreateFile(Path.Combine(_local, "logs", "cacert.pem"));
        string crashSettings = CreateFile(Path.Combine(_local, "logs", "crashes", "settings.dat"));
        string legacyHttp = CreateFile(Path.Combine(_local, "http", "a1", "blob"));
        string tempHttp = CreateFile(Path.Combine(_temp, "http", "ab", "cd"), 2000);
        string staleProvider = CreateFile(Path.Combine(_temp, "ContentProvider_4242", "asset.bin"), 3000);
        string assetStore = CreateFile(Path.Combine(_local, "rbx-storage", "x", "asset"));

        CacheCleanResult result = CreateCleaner().Clean(CancellationToken.None);

        Assert.False(File.Exists(log));
        Assert.False(File.Exists(dump));
        Assert.False(File.Exists(legacyHttp));
        Assert.False(File.Exists(tempHttp));
        Assert.False(File.Exists(staleProvider));
        Assert.True(File.Exists(cert));
        Assert.True(File.Exists(crashSettings));
        Assert.True(File.Exists(assetStore));
        Assert.False(Directory.Exists(Path.Combine(_temp, "ContentProvider_4242")));
        Assert.True(Directory.Exists(_temp));
        Assert.Equal(5, result.FilesDeleted);
        Assert.Equal(1000 + 5000 + 100 + 2000 + 3000, result.BytesFreed);
        Assert.Equal(0, result.FilesSkipped);
    }

    [Fact]
    public void Clean_WithRunningInstance_KeepsItsLogAndContentProviderFolder()
    {
        DateTimeOffset liveStart = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        _processes.GetRobloxProcesses().Returns(new List<ProcessSnapshot> { new(9008, liveStart, 1) });
        string oldLog = CreateFile(Path.Combine(_local, "logs", "old_Player_last.log"), createdUtc: liveStart.UtcDateTime.AddHours(-2));
        string liveLog = CreateFile(Path.Combine(_local, "logs", "live_Player_last.log"), createdUtc: liveStart.UtcDateTime.AddSeconds(3));
        string liveProvider = CreateFile(Path.Combine(_temp, "ContentProvider_9008", "asset.bin"));
        string deadProvider = CreateFile(Path.Combine(_temp, "ContentProvider_1080", "asset.bin"));

        CacheCleanResult result = CreateCleaner().Clean(CancellationToken.None);

        Assert.False(File.Exists(oldLog));
        Assert.True(File.Exists(liveLog));
        Assert.True(File.Exists(liveProvider));
        Assert.False(File.Exists(deadProvider));
        Assert.Equal(2, result.FilesSkipped);
    }

    [Fact]
    public void Clean_LockedFile_IsSkippedWithoutThrowing()
    {
        string locked = CreateFile(Path.Combine(_temp, "http", "locked"));
        string free = CreateFile(Path.Combine(_temp, "http", "free"));

        CacheCleanResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = CreateCleaner().Clean(CancellationToken.None);
        }

        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
        Assert.Equal(1, result.FilesDeleted);
        Assert.Equal(1, result.FilesSkipped);
    }

    [Fact]
    public void Clean_MissingDirectories_ReturnsEmptyResult()
    {
        CacheCleanResult result = CreateCleaner().Clean(CancellationToken.None);

        Assert.Equal(new CacheCleanResult(0, 0, 0), result);
    }
}
