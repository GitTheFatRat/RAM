namespace RbxManager.Core.Servers;

using System.Collections.Generic;

public sealed record ServerPage(
    IReadOnlyList<ServerInfo> Servers,
    string? NextCursor);
