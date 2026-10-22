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
}
