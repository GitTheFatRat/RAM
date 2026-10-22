namespace RbxManager.Core.Processes;

public interface IWindowController
{
    void ThrottleBackgroundProcesses();

    void RestoreProcesses();
}
