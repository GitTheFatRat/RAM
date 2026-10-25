namespace RbxManager.Core.Sessions;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public interface ISessionTracker
{
    event Action<RunningSession>? SessionStarted;
    event Action<RunningSession, bool>? SessionEnded;
    event Action<RunningSession, string>? GhostDisconnectOccurred;

    IReadOnlyList<RunningSession> ActiveSessions { get; }

    bool GetAutoRejoin(AccountId accountId);
    void SetAutoRejoin(AccountId accountId, bool enabled);

    bool IsRejoinGaveUp(AccountId accountId);

    Task<bool> CloseSessionAsync(int pid, bool graceful = true);
    Task CloseAllSessionsAsync(bool graceful = true);

    Task<RunningSession?> TrackLaunchAsync(
        AccountId accountId,
        LaunchRequest request,
        Func<Task<Result>> launchAction,
        CancellationToken ct = default);

    /// <summary>Registers a process that was launched and PID-matched outside <see cref="TrackLaunchAsync"/>.</summary>
    RunningSession? AttachSession(AccountId accountId, LaunchRequest request, int pid);

    /// <summary>
    /// Kills a broken session (hang / disconnect) and relaunches it after the auto-reconnect cooldown,
    /// independent of the per-account auto-rejoin toggle. Returns false if the PID is not tracked.
    /// </summary>
    bool RecoverSession(int pid, string reason);
}
