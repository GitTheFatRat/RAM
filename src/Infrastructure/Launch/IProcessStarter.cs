namespace RbxManager.Infrastructure.Launch;

public interface IProcessStarter
{
    bool IsRobloxPlayerInstalled();
    bool Start(string uri);
}
