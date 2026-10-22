namespace RbxManager.Core.Processes;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IMemoryOptimizer
{
    int LastTrimmedCount { get; }
    int LastAccessDeniedCount { get; }
    int LastFailedCount { get; }

    Task TrimMemoryAsync(IReadOnlyCollection<int>? trackedPids = null, CancellationToken ct = default);
}
