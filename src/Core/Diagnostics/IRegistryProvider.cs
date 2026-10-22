namespace RbxManager.Core.Diagnostics;

using System;
using System.Collections.Generic;

public interface IRegistryProvider
{
    bool ProtocolHandlerExists(string protocol);

    string? GetProtocolHandlerCommand(string protocol);

    IReadOnlyList<(string HiveName, string? Command)> GetProtocolHandlerHives(string protocol) =>
        Array.Empty<(string, string?)>();

    bool StandardRobloxInstallFolderExists();

    string? GetWebView2Version();

    bool IsProcessElevated();
}
