namespace RbxManager.Core.Maintenance;

using System.Threading;
using System.Threading.Tasks;

public sealed record CacheCleanResult(int FilesDeleted, long BytesFreed, int FilesSkipped);

public interface IRobloxCacheCleaner
{
    /// <summary>
    /// Deletes Roblox logs, crash dumps and temp/HTTP caches. Files of running instances are skipped,
    /// never deleted, so live sessions keep their log (disconnect detection) and content cache.
    /// </summary>
    Task<CacheCleanResult> CleanAsync(CancellationToken ct = default);
}
