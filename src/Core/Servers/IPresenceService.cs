namespace RbxManager.Core.Servers;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public sealed record TargetServer(long UserId, long PlaceId, string JobId);

public static class PresenceErrorCodes
{
    public const string UserNotFound = "UserNotFound";
    public const string ServerHidden = "ServerHidden";
}

public interface IPresenceService
{
    /// <summary>
    /// Resolves <paramref name="targetUsername"/> and returns the server it is currently playing in.
    /// Presence only exposes the JobId to viewers the target allows to join, so the lookup is made
    /// with <paramref name="viewerCookie"/> - the account that is about to follow.
    /// </summary>
    Task<Result<TargetServer>> FindUserServerAsync(
        string targetUsername,
        SecretValue viewerCookie,
        CancellationToken ct = default);
}
