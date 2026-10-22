namespace RbxManager.Infrastructure.Tests.Backup;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using RbxManager.Core.Backup;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Backup;
using RbxManager.Infrastructure.Storage;
using RbxManager.Infrastructure.Tests.TestHelpers;

[SupportedOSPlatform("windows")]
public sealed class EncryptedBackupServiceTests
{
    private const string StrongPassword = "StrongBackupPassword123!";
    private const string WrongPassword = "WrongBackupPassword456!";
    private const string FakeCookie1 = "FAKE_COOKIE_ONE_12345";
    private const string FakeCookie2 = "FAKE_COOKIE_TWO_67890";

    [Fact]
    public async Task ExportAndImport_RoundTrip_RestoresAccounts()
    {
        using TempDirectory tempDir = new();
        string vault1Path = tempDir.CreateFilePath("vault1.dat");
        string vault2Path = tempDir.CreateFilePath("vault2.dat");
        string backupPath = tempDir.CreateFilePath("backup.rbxbackup");

        using (EncryptedFileVault vault1 = new(vault1Path))
        {
            using SecretValue c1 = SecretValue.FromString(FakeCookie1);
            using SecretValue c2 = SecretValue.FromString(FakeCookie2);

            await vault1.AddAsync(new Account(new AccountId(1), "AliasOne", "GroupA", "Note 1", c1, DateTimeOffset.UtcNow, Username: "RobloxUser1", UserId: 101, Status: AccountStatus.Valid));
            await vault1.AddAsync(new Account(new AccountId(2), "AliasTwo", "GroupB", "Note 2", c2, DateTimeOffset.UtcNow, Username: "RobloxUser2", UserId: 102, Status: AccountStatus.Expired));

            EncryptedBackupService backupService = new(vault1);
            BackupExportResult exportResult = await backupService.ExportAsync(backupPath, StrongPassword);

            Assert.Equal(2, exportResult.ExportedCount);
            Assert.True(File.Exists(backupPath));
        }

        using (EncryptedFileVault vault2 = new(vault2Path))
        {
            EncryptedBackupService importService = new(vault2);
            BackupImportResult importResult = await importService.ImportAsync(backupPath, StrongPassword);

            Assert.Equal(2, importResult.ImportedCount);
            Assert.Equal(0, importResult.SkippedCount);

            IReadOnlyList<Account> accounts = await vault2.ListAsync();
            Assert.Equal(2, accounts.Count);

            Account? a1 = accounts.FirstOrDefault(a => a.Alias == "AliasOne");
            Assert.NotNull(a1);
            Assert.Equal("GroupA", a1.Group);
            Assert.Equal("RobloxUser1", a1.Username);
            Assert.Equal(101, a1.UserId);
            Assert.Equal(AccountStatus.Valid, a1.Status);
            Assert.Equal(FakeCookie1, a1.Cookie.Reveal());

            Account? a2 = accounts.FirstOrDefault(a => a.Alias == "AliasTwo");
            Assert.NotNull(a2);
            Assert.Equal("GroupB", a2.Group);
            Assert.Equal(AccountStatus.Expired, a2.Status);
            Assert.Equal(FakeCookie2, a2.Cookie.Reveal());
        }
    }

    [Fact]
    public async Task WrongPassword_And_TamperedByte_ThrowSameGenericBackupException()
    {
        using TempDirectory tempDir = new();
        string vaultPath = tempDir.CreateFilePath("vault.dat");
        string backupPath = tempDir.CreateFilePath("backup.rbxbackup");
        string destVaultPath = tempDir.CreateFilePath("destVault.dat");

        using (EncryptedFileVault vault = new(vaultPath))
        {
            using SecretValue c1 = SecretValue.FromString(FakeCookie1);
            await vault.AddAsync(new Account(new AccountId(1), "AliasOne", null, null, c1, DateTimeOffset.UtcNow));

            EncryptedBackupService backupService = new(vault);
            await backupService.ExportAsync(backupPath, StrongPassword);
        }

        using (EncryptedFileVault destVault = new(destVaultPath))
        {
            EncryptedBackupService importService = new(destVault);

            // 1. Wrong password
            BackupException wrongPassEx = await Assert.ThrowsAsync<BackupException>(
                () => importService.ImportAsync(backupPath, WrongPassword));

            Assert.Equal(BackupException.DefaultErrorMessage, wrongPassEx.Message);

            // 2. Tampered byte in ciphertext
            byte[] bytes = await File.ReadAllBytesAsync(backupPath);
            bytes[^1] ^= 0xFF; // Flip last byte of ciphertext
            string tamperedPath = tempDir.CreateFilePath("tampered.rbxbackup");
            await File.WriteAllBytesAsync(tamperedPath, bytes);

            BackupException tamperedEx = await Assert.ThrowsAsync<BackupException>(
                () => importService.ImportAsync(tamperedPath, StrongPassword));

            Assert.Equal(wrongPassEx.Message, tamperedEx.Message);
        }
    }

    [Fact]
    public async Task TwoExportsOfSameData_ProduceDifferentBytes()
    {
        using TempDirectory tempDir = new();
        string vaultPath = tempDir.CreateFilePath("vault.dat");
        string backup1 = tempDir.CreateFilePath("backup1.rbxbackup");
        string backup2 = tempDir.CreateFilePath("backup2.rbxbackup");

        using (EncryptedFileVault vault = new(vaultPath))
        {
            using SecretValue c1 = SecretValue.FromString(FakeCookie1);
            await vault.AddAsync(new Account(new AccountId(1), "AliasOne", null, null, c1, DateTimeOffset.UtcNow));

            EncryptedBackupService service = new(vault);
            await service.ExportAsync(backup1, StrongPassword);
            await service.ExportAsync(backup2, StrongPassword);
        }

        byte[] b1 = await File.ReadAllBytesAsync(backup1);
        byte[] b2 = await File.ReadAllBytesAsync(backup2);

        Assert.False(b1.SequenceEqual(b2));
    }

    [Fact]
    public async Task CiphertextOnDisk_DoesNotContainPlaintextAliasOrCookie()
    {
        using TempDirectory tempDir = new();
        string vaultPath = tempDir.CreateFilePath("vault.dat");
        string backupPath = tempDir.CreateFilePath("backup.rbxbackup");

        using (EncryptedFileVault vault = new(vaultPath))
        {
            using SecretValue c1 = SecretValue.FromString(FakeCookie1);
            await vault.AddAsync(new Account(new AccountId(1), "SuperSecretAlias999", null, null, c1, DateTimeOffset.UtcNow));

            EncryptedBackupService service = new(vault);
            await service.ExportAsync(backupPath, StrongPassword);
        }

        byte[] backupBytes = await File.ReadAllBytesAsync(backupPath);
        string fileAsLatin1 = Encoding.Latin1.GetString(backupBytes);

        Assert.DoesNotContain("SuperSecretAlias999", fileAsLatin1, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeCookie1, fileAsLatin1, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_IterationsBelowMinimum_Rejected()
    {
        using TempDirectory tempDir = new();
        string vaultPath = tempDir.CreateFilePath("vault.dat");
        string backupPath = tempDir.CreateFilePath("backup.rbxbackup");

        using (EncryptedFileVault vault = new(vaultPath))
        {
            using SecretValue c1 = SecretValue.FromString(FakeCookie1);
            await vault.AddAsync(new Account(new AccountId(1), "AliasOne", null, null, c1, DateTimeOffset.UtcNow));

            EncryptedBackupService service = new(vault);
            await service.ExportAsync(backupPath, StrongPassword);
        }

        byte[] bytes = await File.ReadAllBytesAsync(backupPath);
        // Overwrite iterations (bytes 21..24) with 100,000 (< 600,000)
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21, 4), 100_000);

        string modifiedPath = tempDir.CreateFilePath("modified.rbxbackup");
        await File.WriteAllBytesAsync(modifiedPath, bytes);

        using (EncryptedFileVault destVault = new(tempDir.CreateFilePath("destVault.dat")))
        {
            EncryptedBackupService importService = new(destVault);
            await Assert.ThrowsAsync<BackupException>(() => importService.ImportAsync(modifiedPath, StrongPassword));
        }
    }

    [Fact]
    public async Task Import_UnknownMagicOrVersion_Rejected()
    {
        using TempDirectory tempDir = new();
        string vaultPath = tempDir.CreateFilePath("vault.dat");
        string backupPath = tempDir.CreateFilePath("backup.rbxbackup");

        using (EncryptedFileVault vault = new(vaultPath))
        {
            using SecretValue c1 = SecretValue.FromString(FakeCookie1);
            await vault.AddAsync(new Account(new AccountId(1), "AliasOne", null, null, c1, DateTimeOffset.UtcNow));

            EncryptedBackupService service = new(vault);
            await service.ExportAsync(backupPath, StrongPassword);
        }

        byte[] bytes = await File.ReadAllBytesAsync(backupPath);
        bytes[0] = (byte)'X'; // Corrupt magic

        string modifiedPath = tempDir.CreateFilePath("modified.rbxbackup");
        await File.WriteAllBytesAsync(modifiedPath, bytes);

        using (EncryptedFileVault destVault = new(tempDir.CreateFilePath("destVault.dat")))
        {
            EncryptedBackupService importService = new(destVault);
            await Assert.ThrowsAsync<BackupException>(() => importService.ImportAsync(modifiedPath, StrongPassword));
        }
    }

    [Fact]
    public async Task Import_MergeSkipsDuplicateAliasesWithoutOverwriting()
    {
        using TempDirectory tempDir = new();
        string vault1Path = tempDir.CreateFilePath("vault1.dat");
        string vault2Path = tempDir.CreateFilePath("vault2.dat");
        string backupPath = tempDir.CreateFilePath("backup.rbxbackup");

        // Backup has AliasOriginal and AliasNew
        using (EncryptedFileVault vault1 = new(vault1Path))
        {
            using SecretValue c1 = SecretValue.FromString(FakeCookie1);
            using SecretValue c2 = SecretValue.FromString(FakeCookie2);
            await vault1.AddAsync(new Account(new AccountId(1), "AliasOriginal", "GroupBackup", "Backup Note", c1, DateTimeOffset.UtcNow));
            await vault1.AddAsync(new Account(new AccountId(2), "AliasNew", "GroupNew", "New Note", c2, DateTimeOffset.UtcNow));

            EncryptedBackupService service = new(vault1);
            await service.ExportAsync(backupPath, StrongPassword);
        }

        // Vault 2 already has AliasOriginal with different note and cookie
        using (EncryptedFileVault vault2 = new(vault2Path))
        {
            using SecretValue cExisting = SecretValue.FromString("EXISTING_COOKIE_DO_NOT_OVERWRITE");
            await vault2.AddAsync(new Account(new AccountId(10), "aliasoriginal", "GroupExisting", "Existing Note", cExisting, DateTimeOffset.UtcNow));

            EncryptedBackupService importService = new(vault2);
            BackupImportResult result = await importService.ImportAsync(backupPath, StrongPassword);

            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(1, result.SkippedCount);

            IReadOnlyList<Account> accounts = await vault2.ListAsync();
            Assert.Equal(2, accounts.Count);

            Account existing = accounts.First(a => string.Equals(a.Alias, "AliasOriginal", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("GroupExisting", existing.Group);
            Assert.Equal("Existing Note", existing.Note);
            Assert.Equal("EXISTING_COOKIE_DO_NOT_OVERWRITE", existing.Cookie.Reveal());

            Account imported = accounts.First(a => a.Alias == "AliasNew");
            Assert.Equal("GroupNew", imported.Group);
            Assert.Equal(FakeCookie2, imported.Cookie.Reveal());
        }
    }
}
