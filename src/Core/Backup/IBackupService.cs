namespace RbxManager.Core.Backup;

using System.Threading;
using System.Threading.Tasks;

public interface IBackupService
{
    Task<BackupExportResult> ExportAsync(string destinationFilePath, string password, CancellationToken ct = default);
    Task<BackupImportResult> ImportAsync(string sourceFilePath, string password, CancellationToken ct = default);
}
