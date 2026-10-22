namespace RbxManager.Core.Processes;

using System.Threading;
using System.Threading.Tasks;

public interface IMemoryOptimizer
{
    Task TrimMemoryAsync(CancellationToken ct = default);
}
