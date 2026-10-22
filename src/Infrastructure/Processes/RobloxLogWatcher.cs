namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;

public sealed partial class RobloxLogWatcher : IRobloxLogWatcher
{
    public static readonly string[] DefaultKeywords =
    [
        "Connection lost",
        "DisconnectReason",
        "Error 277",
        "Error 268"
    ];

    private static readonly string DefaultLogsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox",
        "logs");

    private readonly string _logsDirectory;
    private readonly IReadOnlyList<string> _keywords;
    private readonly ILogger<RobloxLogWatcher> _logger;
    private readonly ConcurrentDictionary<int, WatcherContext> _activeWatchers = new();
    private bool _disposed;

    public event Action<int, string>? GhostDisconnectDetected;

    public RobloxLogWatcher(
        string? logsDirectory = null,
        IReadOnlyList<string>? keywords = null,
        ILogger<RobloxLogWatcher>? logger = null)
    {
        _logsDirectory = logsDirectory ?? DefaultLogsDirectory;
        _keywords = keywords ?? DefaultKeywords;
        _logger = logger ?? NullLogger<RobloxLogWatcher>.Instance;
    }

    public void StartWatching(int pid, DateTimeOffset startTime, string? explicitLogPath = null)
    {
        if (_disposed)
        {
            return;
        }

        string? logPath = explicitLogPath ?? ResolveLogFile(pid, startTime);
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
        {
            LogLogFileNotFound(_logger, pid);
            return;
        }

        CancellationTokenSource cts = new();
        WatcherContext context = new(logPath, cts);

        if (_activeWatchers.TryAdd(pid, context))
        {
            LogStartedWatching(_logger, pid, logPath);
            context.Task = Task.Run(() => TailLogAsync(pid, context, cts.Token), cts.Token);
        }
        else
        {
            cts.Dispose();
        }
    }

    public void StopWatching(int pid)
    {
        if (_activeWatchers.TryRemove(pid, out WatcherContext? context))
        {
            context.Dispose();
            LogStoppedWatching(_logger, pid);
        }
    }

    private string? ResolveLogFile(int pid, DateTimeOffset startTime)
    {
        try
        {
            if (!Directory.Exists(_logsDirectory))
            {
                return null;
            }

            DirectoryInfo dir = new(_logsDirectory);
            FileInfo[] files = dir.GetFiles("*Player*.log")
                .Where(f => !f.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();

            if (files.Length == 0)
            {
                return null;
            }

            // 1. Check if any file created within 2 minutes of startTime matches
            FileInfo? closest = files
                .Where(f => Math.Abs((f.CreationTimeUtc - startTime.UtcDateTime).TotalSeconds) < 120)
                .MinBy(f => Math.Abs((f.CreationTimeUtc - startTime.UtcDateTime).TotalSeconds));

            if (closest != null)
            {
                return closest.FullName;
            }

            // 2. Fall back to the most recently modified player log
            return files[0].FullName;
        }
        catch (Exception ex)
        {
            LogResolveError(_logger, pid, ex.Message);
            return null;
        }
    }

    private async Task TailLogAsync(int pid, WatcherContext context, CancellationToken ct)
    {
        try
        {
            using FileStream stream = new(
                context.LogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using StreamReader reader = new(stream, Encoding.UTF8);

            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line != null)
                {
                    foreach (string keyword in _keywords)
                    {
                        if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            LogDisconnectDetected(_logger, pid, keyword);
                            GhostDisconnectDetected?.Invoke(pid, keyword);
                            return;
                        }
                    }
                }
                else
                {
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
        catch (Exception ex)
        {
            LogTailError(_logger, pid, ex.Message);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            foreach (int pid in _activeWatchers.Keys.ToList())
            {
                StopWatching(pid);
            }
        }
    }

    private sealed class WatcherContext : IDisposable
    {
        public string LogPath { get; }
        public CancellationTokenSource Cts { get; }
        public Task? Task { get; set; }

        public WatcherContext(string logPath, CancellationTokenSource cts)
        {
            LogPath = logPath;
            Cts = cts;
        }

        public void Dispose()
        {
            try
            {
                Cts.Cancel();
            }
            catch
            {
            }
            Cts.Dispose();
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Started tailing Roblox log for PID {Pid} at {LogPath}.")]
    private static partial void LogStartedWatching(ILogger logger, int pid, string logPath);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Stopped tailing Roblox log for PID {Pid}.")]
    private static partial void LogStoppedWatching(ILogger logger, int pid);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Ghost disconnect keyword '{Keyword}' detected in log for Roblox PID {Pid}.")]
    private static partial void LogDisconnectDetected(ILogger logger, int pid, string keyword);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Could not find matching Roblox log file for PID {Pid}.")]
    private static partial void LogLogFileNotFound(ILogger logger, int pid);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Error resolving log file for PID {Pid}: {Message}.")]
    private static partial void LogResolveError(ILogger logger, int pid, string message);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Error tailing log file for PID {Pid}: {Message}.")]
    private static partial void LogTailError(ILogger logger, int pid, string message);
}
