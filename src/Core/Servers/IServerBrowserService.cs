namespace RbxManager.Core.Servers;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;

public interface IServerBrowserService
{
    Task<Result<ServerPage>> GetServersAsync(
        long placeId,
        ServerSort sort = ServerSort.Descending,
        string? cursor = null,
        CancellationToken ct = default);
}
