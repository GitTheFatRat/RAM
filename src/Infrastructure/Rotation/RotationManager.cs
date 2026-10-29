namespace RbxManager.Infrastructure.Rotation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Maintenance;
using RbxManager.Core.Models;
using RbxManager.Core.Rotation;
using RbxManager.Core.Sessions;
using RbxManager.Core.Storage;

/// <summary>
/// Farms account groups in turn: launch group, farm for the configured duration, kill every tracked session,
/// let processes exit, wipe disk cache, move to the next group; after the last group it starts over.
/// </summary>
public sealed partial class RotationManager : IRotationManager, IDisposable
{
    internal static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(10);

    private readonly IAccountStore _accountStore;
    private readonly ILauncher _launcher;
    private readonly ISessionTracker _sessionTracker;
    private readonly IRobloxCacheCleaner _cacheCleaner;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RotationManager> _logger;
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task _loop = Task.CompletedTask;
    private RotationStatus _status = RotationStatus.Idle;

    public Func<Account, LaunchRequest, CancellationToken, Task<Result>>? ModerationRecoveryHandler { get; set; }

    public RotationManager(
        IAccountStore accountStore,
        ILauncher launcher,
        ISessionTracker sessionTracker,
        IRobloxCacheCleaner cacheCleaner,
        TimeProvider? timeProvider = null,
        ILogger<RotationManager>? logger = null)
    {
        _accountStore = accountStore;
        _launcher = launcher;
        _sessionTracker = sessionTracker;
        _cacheCleaner = cacheCleaner;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<RotationManager>.Instance;
    }

    public event Action<RotationStatus>? StatusChanged;

    public RotationStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public Result Start(RotationPlan plan)
    {
        Result validation = plan.Validate();
        if (!validation.IsSuccess)
        {
            return validation;
        }

        lock (_sync)
        {
            if (_cts != null)
            {
                return Result.Failure("Rotation is already running.", "AlreadyRunning");
            }

            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            _loop = Task.Run(() => RunAsync(plan, ct), CancellationToken.None);
        }

        return Result.Success();
    }

    /// <summary>Stops scheduling. Sessions already running are left alone; the user decides whether to close them.</summary>
    public async Task StopAsync()
    {
        Task loop;
        lock (_sync)
        {
            _cts?.Cancel();
            loop = _loop;
        }

        await loop.ConfigureAwait(false);
    }

    internal Task Completion
    {
        get
        {
            lock (_sync)
            {
                return _loop;
            }
        }
    }

    private async Task RunAsync(RotationPlan plan, CancellationToken ct)
    {
        try
        {
            for (int index = 0; ; index = (index + 1) % plan.Groups.Count)
            {
                string group = plan.Groups[index];
                await RunStepSafelyAsync("launch", group, () => LaunchGroupAsync(group, plan.PlaceId, ct)).ConfigureAwait(false);

                DateTimeOffset switchAt = _timeProvider.GetUtcNow() + plan.GroupDuration;
                SetStatus(new RotationStatus(RotationPhase.Farming, group, switchAt));
                await Task.Delay(plan.GroupDuration, _timeProvider, ct).ConfigureAwait(false);

                SetStatus(new RotationStatus(RotationPhase.Stopping, group, null));
                LogSwitching(group, _sessionTracker.ActiveSessions.Count);
                await RunStepSafelyAsync("close", group, () => _sessionTracker.CloseAllSessionsAsync(graceful: false)).ConfigureAwait(false);
                await Task.Delay(ExitGracePeriod, _timeProvider, ct).ConfigureAwait(false);

                SetStatus(new RotationStatus(RotationPhase.Cleaning, group, null));
                await RunStepSafelyAsync("clean", group, () => _cacheCleaner.CleanAsync(ct)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogLoopFailed(ex.GetType().Name);
        }
        finally
        {
            lock (_sync)
            {
                _cts?.Dispose();
                _cts = null;
            }

            SetStatus(RotationStatus.Idle);
        }
    }

    private async Task LaunchGroupAsync(string group, long placeId, CancellationToken ct)
    {
        SetStatus(new RotationStatus(RotationPhase.Launching, group, null));
        IReadOnlyList<Account> accounts = await _accountStore.ListAsync(ct).ConfigureAwait(false);
        try
        {
            List<Account> members = accounts
                .Where(a => string.Equals(a.Group, group, StringComparison.OrdinalIgnoreCase) && a.Status != AccountStatus.Expired)
                .ToList();
            LogLaunchingGroup(group, members.Count);

            // RobloxLauncher serialises launches and enforces the configured spacing, so no extra pacing here.
            int consecutiveRateLimits = 0;
            for (int i = 0; i < members.Count; i++)
            {
                Account account = members[i];
                ct.ThrowIfCancellationRequested();
                Result? launchResult;
                try
                {
                    launchResult = await LaunchMemberAsync(account, group, placeId, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One account's vault/recovery failure must not end the whole rotation.
                    LogAccountFailed(account.Id.Value, group, ex.GetType().Name);
                    continue;
                }

                if (launchResult is { IsSuccess: true })
                {
                    consecutiveRateLimits = 0;
                }
                else if (string.Equals(launchResult?.ErrorCode, AuthErrorCodes.RateLimited, StringComparison.OrdinalIgnoreCase))
                {
                    consecutiveRateLimits = LaunchBackoffPolicy.NextStrikeCount(consecutiveRateLimits);
                    if (i + 1 < members.Count)
                    {
                        int backoffSeconds = LaunchBackoffPolicy.ComputeBackoffSeconds(consecutiveRateLimits);
                        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), _timeProvider, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            foreach (Account acc in accounts)
            {
                acc.Cookie.Dispose();
            }
        }
    }

    private async Task<Result?> LaunchMemberAsync(Account account, string group, long placeId, CancellationToken ct)
    {
        LaunchRequest request = new(account.Id, placeId);
        Result? launchResult = await TrackedLaunchAsync(account, request, ct).ConfigureAwait(false);
        if (!string.Equals(launchResult?.ErrorCode, AuthErrorCodes.Moderated, StringComparison.OrdinalIgnoreCase))
        {
            return launchResult;
        }

        SetStatus(new RotationStatus(RotationPhase.WaitingForRecovery, group, null));
        try
        {
            return ModerationRecoveryHandler != null
                ? await ModerationRecoveryHandler(account, request, ct).ConfigureAwait(false)
                : await TrackedLaunchAsync(account, request, ct).ConfigureAwait(false);
        }
        finally
        {
            SetStatus(new RotationStatus(RotationPhase.Launching, group, null));
        }
    }

    private async Task<Result?> TrackedLaunchAsync(Account account, LaunchRequest request, CancellationToken ct)
    {
        Result? launchResult = null;
        await _sessionTracker.TrackLaunchAsync(
            account.Id,
            request,
            async () =>
            {
                launchResult = await _launcher.LaunchAsync(request, ct).ConfigureAwait(false);
                return launchResult;
            },
            ct).ConfigureAwait(false);
        return launchResult;
    }

    // Close/clean are housekeeping between groups: a locked cache file or a stuck process must not stop farming.
    private async Task RunStepSafelyAsync(string step, string group, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStepFailed(step, group, ex.GetType().Name);
        }
    }

    private void SetStatus(RotationStatus status)
    {
        lock (_sync)
        {
            _status = status;
        }

        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _cts?.Cancel();
        }
    }

    [LoggerMessage(EventId = 801, Level = LogLevel.Information, Message = "Rotation: launching group '{Group}' ({Count} accounts).")]
    private partial void LogLaunchingGroup(string group, int count);

    [LoggerMessage(EventId = 802, Level = LogLevel.Information, Message = "Rotation: group '{Group}' finished; closing {Count} sessions.")]
    private partial void LogSwitching(string group, int count);

    [LoggerMessage(EventId = 803, Level = LogLevel.Error, Message = "Rotation loop stopped by {ExceptionType}.")]
    private partial void LogLoopFailed(string exceptionType);

    [LoggerMessage(EventId = 804, Level = LogLevel.Warning, Message = "Rotation: account {AccountId} in group '{Group}' failed with {ExceptionType}; continuing.")]
    private partial void LogAccountFailed(long accountId, string group, string exceptionType);

    [LoggerMessage(EventId = 805, Level = LogLevel.Warning, Message = "Rotation: step '{Step}' for group '{Group}' failed with {ExceptionType}; rotation continues.")]
    private partial void LogStepFailed(string step, string group, string exceptionType);
}
