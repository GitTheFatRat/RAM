namespace RbxManager.Core.Processes;

using System.Collections.Generic;

public interface IProcessProvider
{
    IReadOnlyList<ProcessSnapshot> GetRobloxProcesses();
    bool Kill(int pid, bool graceful);
    bool IsProcessRunning(string processName) => false;

    /// <summary>False when the process's UI thread has stopped pumping messages (engine hang).</summary>
    bool IsResponding(int pid) => true;
}
