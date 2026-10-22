namespace RbxManager.Core.Processes;

using System.Collections.Generic;

public interface IWindowController
{
    void ThrottleBackgroundProcesses(IReadOnlyCollection<int>? trackedPids = null);

    void RestoreProcesses();
}
