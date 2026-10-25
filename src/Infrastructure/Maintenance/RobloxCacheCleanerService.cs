namespace RbxManager.Infrastructure.Maintenance;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Maintenance;
using RbxManager.Core.Processes;

/// <summary>
/// Deletes Roblox disk clutter. Targets (verified on Roblox 0.744, Windows 10 22H2):
/// <c>%LOCALAPPDATA%\Roblox\logs</c> (*.log, *.dmp only; cacert.pem and crashes\settings.dat are kept),
/// <c>%LOCALAPPDATA%\Roblox\http</c> (legacy location, absent on current builds) and <c>%TEMP%\Roblox</c>
/// (current HTTP cache plus one ContentProvider_&lt;pid&gt; folder per instance ever started).
/// The asset store <c>rbx-storage</c> is deliberately untouched: wiping it forces every next launch to
/// re-download assets, the opposite of faster launches.
/// </summary>
public sealed partial class RobloxCacheCleanerService : IRobloxCacheCleaner
{
    private const string ContentProviderPrefix = "ContentProvider_";

    // A Player log is created a few seconds after its process starts; anything created after the oldest live
    // process (minus this margin) may belong to a running instance whose disconnect detector tails it.
    private static readonly TimeSpan LiveLogMargin = TimeSpan.FromSeconds(30);

    private readonly IProcessProvider _processProvider;
    private readonly string _robloxLocalDirectory;
    private readonly string _robloxTempDirectory;
    private readonly ILogger<RobloxCacheCleanerService> _logger;

    public RobloxCacheCleanerService(
        IProcessProvider processProvider,
        string? robloxLocalDirectory = null,
        string? robloxTempDirectory = null,
        ILogger<RobloxCacheCleanerService>? logger = null)
    {
        _processProvider = processProvider;
        _robloxLocalDirectory = robloxLocalDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
        _robloxTempDirectory = robloxTempDirectory ?? Path.Combine(Path.GetTempPath(), "Roblox");
        _logger = logger ?? NullLogger<RobloxCacheCleanerService>.Instance;
    }

    public Task<CacheCleanResult> CleanAsync(CancellationToken ct = default) => Task.Run(() => Clean(ct), ct);

    internal CacheCleanResult Clean(CancellationToken ct)
    {
        IReadOnlyList<ProcessSnapshot> running = _processProvider.GetRobloxProcesses();
        HashSet<string> liveContentFolders = running
            .Select(p => ContentProviderPrefix + p.Pid.ToString(CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DateTime? liveLogCutoffUtc = running.Count == 0 ? null : running.Min(p => p.StartTime).UtcDateTime - LiveLogMargin;

        Tally tally = new();
        string logsDirectory = Path.Combine(_robloxLocalDirectory, "logs");
        foreach (FileInfo file in EnumerateFiles(logsDirectory, "*.log").Concat(EnumerateFiles(logsDirectory, "*.dmp")))
        {
            ct.ThrowIfCancellationRequested();
            if (liveLogCutoffUtc is DateTime cutoff && file.CreationTimeUtc >= cutoff)
            {
                tally.Skipped++;
                continue;
            }

            TryDelete(file, tally);
        }

        foreach (string cacheRoot in new[] { Path.Combine(_robloxLocalDirectory, "http"), _robloxTempDirectory })
        {
            foreach (FileInfo file in EnumerateFiles(cacheRoot, "*"))
            {
                ct.ThrowIfCancellationRequested();
                if (IsInsideLiveContentFolder(file, cacheRoot, liveContentFolders))
                {
                    tally.Skipped++;
                    continue;
                }

                TryDelete(file, tally);
            }

            RemoveEmptySubdirectories(cacheRoot);
        }

        CacheCleanResult result = new(tally.Deleted, tally.Bytes, tally.Skipped);
        LogCleaned(result.FilesDeleted, result.BytesFreed / (1024.0 * 1024.0), result.FilesSkipped);
        return result;
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string directory, string pattern) =>
        Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateFiles(pattern, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            : [];

    private static bool IsInsideLiveContentFolder(FileInfo file, string cacheRoot, HashSet<string> liveFolders)
    {
        string relative = Path.GetRelativePath(cacheRoot, file.FullName);
        int separator = relative.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return separator > 0 && liveFolders.Contains(relative[..separator]);
    }

    private static void TryDelete(FileInfo file, Tally tally)
    {
        try
        {
            long length = file.Length;
            file.Delete();
            tally.Deleted++;
            tally.Bytes += length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by a running Roblox instance (or read-only): leave it for the next pass.
            tally.Skipped++;
        }
    }

    private static void RemoveEmptySubdirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        // Deepest first so parents become empty after their children are removed; the root itself is kept.
        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [LoggerMessage(EventId = 701, Level = LogLevel.Information, Message = "Roblox cache cleaned: {Deleted} files, {MegabytesFreed:F1} MB freed, {Skipped} skipped (in use).")]
    private partial void LogCleaned(int deleted, double megabytesFreed, int skipped);

    private sealed class Tally
    {
        public int Deleted;
        public long Bytes;
        public int Skipped;
    }
}
