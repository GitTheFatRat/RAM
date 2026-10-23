namespace RbxManager.Core.Network;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;

public static class DpiErrorCodes
{
    public const string UacDenied = "UacDenied";
    public const string DownloadFailed = "DownloadFailed";
    public const string LaunchFailed = "LaunchFailed";
}

public interface IDpiToolService
{
    bool IsRunning();

    Task<Result> EnsureInstalledAndStartAsync(CancellationToken ct = default);
}
