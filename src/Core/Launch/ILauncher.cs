namespace RbxManager.Core.Launch;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public interface ILauncher
{
    Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default);
}

public static class LaunchBackoffPolicy
{
    public const int BaseBackoffSeconds = 30;
    public const int MaxBackoffSeconds = 300;
    public const int MaxStrikes = 5;

    public static int NextStrikeCount(int currentStrikes) =>
        System.Math.Min(System.Math.Max(0, currentStrikes) + 1, MaxStrikes);

    public static int ComputeBackoffSeconds(int strikeCount)
    {
        int clamped = System.Math.Clamp(strikeCount, 1, MaxStrikes);
        return System.Math.Min(MaxBackoffSeconds, BaseBackoffSeconds * (1 << (clamped - 1)));
    }
}
