namespace RbxManager.Infrastructure.Sessions;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Servers;
using RbxManager.Core.Sessions;
using RbxManager.Core.Storage;

public sealed partial class SessionTracker : BackgroundService, ISessionTracker
{
    private readonly IProcessProvider _processProvider;
    private readonly IAccountStore _accountStore;
    private readonly IRobloxAuthService _authService;
    private readonly ILauncher _launcher;
    private readonly IServerBrowserService? _serverBrowserService;
    private readonly Core.Settings.ISettingsService? _settingsService;
    private readonly ILogger<SessionTracker> _logger;

    private readonly ConcurrentDictionary<int, RunningSession> _activeSessions = new();
    private readonly ConcurrentDictionary<long, bool> _autoRejoinEnabled = new();
    private readonly ConcurrentDictionary<long, RejoinPolicy> _rejoinPolicies = new();
    private readonly ConcurrentDictionary<int, bool> _intentionalCloses = new();
    private readonly ConcurrentDictionary<long, bool> _gaveUpAccounts = new();

    private readonly Func<RejoinPolicy> _policyFactory;

    public event Action<RunningSession>? SessionStarted;
    public event Action<RunningSession, bool>? SessionEnded;

    public IReadOnlyList<RunningSession> ActiveSessions => _activeSessions.Values.ToList();

    public SessionTracker(
        IProcessProvider processProvider,
        IAccountStore accountStore,
        IRobloxAuthService authService,
        ILauncher launcher,
        ILogger<SessionTracker>? logger = null,
        Func<RejoinPolicy>? policyFactory = null,
        Core.Settings.ISettingsService? settingsService = null)
        : this(processProvider, accountStore, authService, launcher, null, settingsService, logger, policyFactory)
    {
    }

    public SessionTracker(
        IProcessProvider processProvider,
        IAccountStore accountStore,
        IRobloxAuthService authService,
        ILauncher launcher,
        IServerBrowserService? serverBrowserService,
        Core.Settings.ISettingsService? settingsService,
        ILogger<SessionTracker>? logger = null,
        Func<RejoinPolicy>? policyFactory = null)
    {
        _processProvider = processProvider;
        _accountStore = accountStore;
        _authService = authService;
        _launcher = launcher;
        _serverBrowserService = serverBrowserService;
        _settingsService = settingsService;
        _logger = logger ?? NullLogger<SessionTracker>.Instance;
        _policyFactory = policyFactory ?? (() =>
        {
            if (settingsService != null)
            {
                Core.Settings.AppSettings s = settingsService.CurrentSettings;
                return new RejoinPolicy(
                    initialDelay: TimeSpan.FromSeconds(s.RejoinDelaySeconds),
                    maxAttempts: s.RejoinMaxAttempts);
            }
            return new RejoinPolicy();
        });
    }

    public bool GetAutoRejoin(AccountId accountId)
    {
        return _autoRejoinEnabled.TryGetValue(accountId.Value, out bool enabled) && enabled;
    }

    public void SetAutoRejoin(AccountId accountId, bool enabled)
    {
        _autoRejoinEnabled[accountId.Value] = enabled;
        if (!enabled)
        {
            _gaveUpAccounts.TryRemove(accountId.Value, out _);
        }
    }

    public bool IsRejoinGaveUp(AccountId accountId)
    {
        return _gaveUpAccounts.TryGetValue(accountId.Value, out bool gaveUp) && gaveUp;
    }

    public async Task<bool> CloseSessionAsync(int pid, bool graceful = true)
    {
        _intentionalCloses[pid] = true;
        bool killed = await Task.Run(() => _processProvider.Kill(pid, graceful)).ConfigureAwait(false);
        return killed;
    }

    public async Task CloseAllSessionsAsync(bool graceful = true)
    {
        foreach (int pid in _activeSessions.Keys.ToList())
        {
            await CloseSessionAsync(pid, graceful).ConfigureAwait(false);
        }
    }

    internal void RegisterSession(RunningSession session)
    {
        _activeSessions[session.ProcessId] = session;
    }

    public async Task<RunningSession?> TrackLaunchAsync(
        AccountId accountId,
        LaunchRequest request,
        Func<Task<Result>> launchAction,
        CancellationToken ct = default)
    {
        IReadOnlyList<ProcessSnapshot> baseline = _processProvider.GetRobloxProcesses();
        HashSet<int> baselinePids = baseline.Select(p => p.Pid).ToHashSet();

        Result launchResult = await launchAction().ConfigureAwait(false);
        if (!launchResult.IsSuccess)
        {
            return null;
        }

        DateTimeOffset timeout = DateTimeOffset.UtcNow.AddSeconds(60);
        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < timeout)
        {
            IReadOnlyList<ProcessSnapshot> current = _processProvider.GetRobloxProcesses();
            List<ProcessSnapshot> newProcesses = current.Where(p => !baselinePids.Contains(p.Pid)).ToList();

            if (newProcesses.Count == 1)
            {
                ProcessSnapshot snapshot = newProcesses[0];
                RunningSession session = new(accountId, request, snapshot.Pid, snapshot.StartTime);

                _activeSessions[snapshot.Pid] = session;
                LogSessionBound(accountId.Value, snapshot.Pid);

                SessionStarted?.Invoke(session);
                return session;
            }

            if (newProcesses.Count > 1)
            {
                LogMultipleNewPids(accountId.Value, newProcesses.Count);
                return null;
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        LogBindingTimeout(accountId.Value);
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, stoppingToken).ConfigureAwait(false);
                await PollProcessesAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogPollError(ex.GetType().Name);
            }
        }
    }

    internal async Task PollProcessesAsync()
    {
        IReadOnlyList<ProcessSnapshot> currentProcesses = _processProvider.GetRobloxProcesses();
        HashSet<int> activePids = currentProcesses.Select(p => p.Pid).ToHashSet();

        foreach (KeyValuePair<int, RunningSession> kvp in _activeSessions.ToList())
        {
            int pid = kvp.Key;
            RunningSession session = kvp.Value;

            if (!activePids.Contains(pid))
            {
                _activeSessions.TryRemove(pid, out _);
                bool intentional = _intentionalCloses.TryRemove(pid, out _);

                LogSessionEnded(session.AccountId.Value, pid, intentional);
                SessionEnded?.Invoke(session, intentional);

                if (!intentional && GetAutoRejoin(session.AccountId))
                {
                    await HandleAutoRejoinAsync(session).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task HandleAutoRejoinAsync(RunningSession session)
    {
        RejoinPolicy policy = _rejoinPolicies.GetOrAdd(session.AccountId.Value, _ => _policyFactory());
        RejoinDecision decision = policy.Evaluate(isIntentionalExit: false);

        if (decision.GaveUp)
        {
            _gaveUpAccounts[session.AccountId.Value] = true;
            LogRejoinGaveUp(session.AccountId.Value);
            return;
        }

        if (!decision.ShouldRejoin)
        {
            return;
        }

        LogRejoining(session.AccountId.Value, (int)decision.Delay.TotalSeconds);

        if (decision.Delay > TimeSpan.Zero)
        {
            await Task.Delay(decision.Delay).ConfigureAwait(false);
        }

        IReadOnlyList<Account> accounts = await _accountStore.ListAsync().ConfigureAwait(false);
        Account? account = accounts.FirstOrDefault(a => a.Id == session.AccountId);
        if (account == null)
        {
            return;
        }

        Result<RobloxUser> validation = await _authService.ValidateCookieAsync(account.Cookie).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            if (string.Equals(validation.ErrorCode, AuthErrorCodes.Expired, StringComparison.OrdinalIgnoreCase))
            {
                Account updated = account with
                {
                    Status = AccountStatus.Expired,
                    LastCheckedAt = DateTimeOffset.UtcNow
                };
                await _accountStore.UpdateAsync(updated).ConfigureAwait(false);
                LogRejoinStoppedExpired(account.Id.Value);
            }
            return;
        }

        LaunchRequest launchRequest = session.Request;
        bool smartHopping = _settingsService?.CurrentSettings.EnableSmartServerHopping ?? true;
        if (smartHopping && _serverBrowserService != null)
        {
            try
            {
                Result<ServerPage> serverResult = await _serverBrowserService
                    .GetServersAsync(session.Request.PlaceId, ServerSort.Ascending, null, CancellationToken.None)
                    .ConfigureAwait(false);

                if (serverResult.IsSuccess && serverResult.Value != null)
                {
                    ServerInfo? candidate = serverResult.Value.Servers
                        .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Id) && s.Playing < s.MaxPlayers);

                    if (candidate != null)
                    {
                        launchRequest = session.Request with { JobId = candidate.Id };
                        LogServerHopped(session.AccountId.Value, candidate.Id);
                    }
                    else
                    {
                        launchRequest = session.Request;
                        LogServerHopFallback(session.AccountId.Value);
                    }
                }
                else
                {
                    launchRequest = session.Request;
                    LogServerHopFallback(session.AccountId.Value);
                }
            }
            catch (Exception ex)
            {
                LogServerHopError(session.AccountId.Value, ex.Message);
                launchRequest = session.Request;
            }
        }

        await TrackLaunchAsync(session.AccountId, launchRequest, () => _launcher.LaunchAsync(launchRequest)).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 501, Level = LogLevel.Information, Message = "Bound account {AccountId} to new Roblox PID {Pid}.")]
    private partial void LogSessionBound(long accountId, int pid);

    [LoggerMessage(EventId = 502, Level = LogLevel.Warning, Message = "Multiple new Roblox PIDs detected ({Count}) during launch of account {AccountId}. Leaving unbound.")]
    private partial void LogMultipleNewPids(long accountId, int count);

    [LoggerMessage(EventId = 503, Level = LogLevel.Warning, Message = "Timed out waiting for new Roblox PID for account {AccountId}.")]
    private partial void LogBindingTimeout(long accountId);

    [LoggerMessage(EventId = 504, Level = LogLevel.Information, Message = "Roblox session for account {AccountId} (PID {Pid}) ended. Intentional: {Intentional}.")]
    private partial void LogSessionEnded(long accountId, int pid, bool intentional);

    [LoggerMessage(EventId = 505, Level = LogLevel.Information, Message = "Initiating auto-rejoin for account {AccountId} in {DelaySeconds}s.")]
    private partial void LogRejoining(long accountId, int delaySeconds);

    [LoggerMessage(EventId = 506, Level = LogLevel.Warning, Message = "Auto-rejoin circuit breaker tripped for account {AccountId}. Gave up rejoining.")]
    private partial void LogRejoinGaveUp(long accountId);

    [LoggerMessage(EventId = 507, Level = LogLevel.Warning, Message = "Auto-rejoin cancelled for account {AccountId}: account cookie has expired.")]
    private partial void LogRejoinStoppedExpired(long accountId);

    [LoggerMessage(EventId = 508, Level = LogLevel.Error, Message = "Exception in process monitor loop: {ExceptionType}.")]
    private partial void LogPollError(string exceptionType);

    [LoggerMessage(EventId = 509, Level = LogLevel.Information, Message = "Smart server hop for account {AccountId}: selected server {JobId}.")]
    private partial void LogServerHopped(long accountId, string jobId);

    [LoggerMessage(EventId = 510, Level = LogLevel.Warning, Message = "Smart server hop for account {AccountId} found no available server; falling back to original request.")]
    private partial void LogServerHopFallback(long accountId);

    [LoggerMessage(EventId = 511, Level = LogLevel.Warning, Message = "Smart server hop for account {AccountId} failed ({Error}); falling back to original request.")]
    private partial void LogServerHopError(long accountId, string error);
}
