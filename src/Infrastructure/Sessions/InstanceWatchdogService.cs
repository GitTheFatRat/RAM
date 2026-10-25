namespace RbxManager.Infrastructure.Sessions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;

/// <summary>
/// Auto-reconnect hang detector. The other two failure signals are already covered elsewhere and are not
/// re-polled here: process exit by <see cref="SessionTracker"/> (2 s poll), and disconnect signatures such as
/// "Error 277" by <see cref="Processes.RobloxLogWatcher"/> (live tail of the PID's own log file).
/// </summary>
public sealed partial class InstanceWatchdogService : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    // Roblox briefly stops pumping messages while loading a place or teleporting; two consecutive
    // misses (~30 s frozen) separate a real engine hang from a loading hitch.
    internal const int HangStrikesBeforeRecovery = 2;
    internal static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(60);

    private readonly ISessionTracker _sessionTracker;
    private readonly IProcessProvider _processProvider;
    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InstanceWatchdogService> _logger;
    private readonly Dictionary<int, int> _hangStrikes = new();

    public InstanceWatchdogService(
        ISessionTracker sessionTracker,
        IProcessProvider processProvider,
        ISettingsService settingsService,
        TimeProvider? timeProvider = null,
        ILogger<InstanceWatchdogService>? logger = null)
    {
        _sessionTracker = sessionTracker;
        _processProvider = processProvider;
        _settingsService = settingsService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<InstanceWatchdogService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    CheckSessions();
                }
                catch (Exception ex)
                {
                    LogCheckError(ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal void CheckSessions()
    {
        if (!_settingsService.CurrentSettings.EnableAutoReconnect)
        {
            _hangStrikes.Clear();
            return;
        }

        IReadOnlyList<RunningSession> sessions = _sessionTracker.ActiveSessions;
        HashSet<int> activePids = sessions.Select(s => s.ProcessId).ToHashSet();
        foreach (int stalePid in _hangStrikes.Keys.Where(pid => !activePids.Contains(pid)).ToList())
        {
            _hangStrikes.Remove(stalePid);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (RunningSession session in sessions)
        {
            int pid = session.ProcessId;
            if (now - session.StartedAt < StartupGrace || _processProvider.IsResponding(pid))
            {
                _hangStrikes.Remove(pid);
                continue;
            }

            int strikes = _hangStrikes.GetValueOrDefault(pid) + 1;
            _hangStrikes[pid] = strikes;
            LogNotResponding(pid, strikes);
            if (strikes >= HangStrikesBeforeRecovery)
            {
                _hangStrikes.Remove(pid);
                _sessionTracker.RecoverSession(pid, "NotResponding");
            }
        }
    }

    [LoggerMessage(EventId = 601, Level = LogLevel.Warning, Message = "Roblox PID {Pid} is not responding (strike {Strikes}).")]
    private partial void LogNotResponding(int pid, int strikes);

    [LoggerMessage(EventId = 602, Level = LogLevel.Error, Message = "Instance watchdog check failed: {ExceptionType}.")]
    private partial void LogCheckError(string exceptionType);
}
