namespace RbxManager.Core.Diagnostics;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Models;

public interface IDiagnosticsService
{
    Task<string> RunDiagnosticsAsync(Account? targetAccount = null, CancellationToken ct = default);
}
