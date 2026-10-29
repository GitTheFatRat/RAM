namespace RbxManager.Core.Integrations;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;

public interface IPotassiumService
{
    PotassiumScanResult? LastScanResult { get; }

    Task<PotassiumScanResult> ScanAsync(CancellationToken ct = default);

    Result Launch();
}
