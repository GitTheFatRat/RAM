namespace RbxManager.Core.Processes;

using System;

public interface IRobloxLogWatcher : IDisposable
{
    event Action<int, string>? GhostDisconnectDetected;

    void StartWatching(int pid, DateTimeOffset startTime, string? explicitLogPath = null);

    void StopWatching(int pid);
}
