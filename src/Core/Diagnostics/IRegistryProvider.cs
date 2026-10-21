namespace RbxManager.Core.Diagnostics;

public interface IRegistryProvider
{
    bool ProtocolHandlerExists(string protocol);

    string? GetProtocolHandlerCommand(string protocol);

    bool StandardRobloxInstallFolderExists();

    string? GetWebView2Version();

    bool IsProcessElevated();
}
