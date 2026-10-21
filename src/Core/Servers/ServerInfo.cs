namespace RbxManager.Core.Servers;

public sealed record ServerInfo(
    string Id,
    int Playing,
    int MaxPlayers,
    double? Fps,
    int? Ping);
