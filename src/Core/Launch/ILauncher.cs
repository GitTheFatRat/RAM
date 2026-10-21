namespace RbxManager.Core.Launch;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public interface ILauncher
{
    Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default);
}
