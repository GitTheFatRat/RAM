namespace RbxManager.Infrastructure.Tests.Storage;

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using RbxManager.Core.Exceptions;
using RbxManager.Core.Models;
using RbxManager.Infrastructure.Storage;
using RbxManager.Infrastructure.Tests.TestHelpers;
using Xunit;

/// <summary>
/// M44 Flow 2 real-data: real vault files on disk, real DPAPI + Argon2, and a real OS file lock to make the
/// atomic replace fail. Guards against the key provider drifting away from what is actually on disk.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VaultSaveFailureRealDataTests
{
    [Fact]
    public async Task ReencryptAsync_WhenAtomicReplaceFails_KeepsOldKeyUsable_AndLeavesNoTmp()
    {
        using TempDirectory temp = new();
        string vaultPath = temp.CreateFilePath("vault.dat");
        using VaultKeyProvider keyProvider = CreateProvider("OldPassword123!");
        await SeedTwoAccountsAsync(vaultPath, keyProvider);

        using (EncryptedFileVault vault = new(vaultPath, keyProvider))
        using (FileStream lockedBackup = new(vaultPath + ".bak", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (SecretValue newPassword = SecretValue.FromString("NewPassword123!"))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => vault.ReencryptAsync(newPassword));
        }

        Assert.False(File.Exists(vaultPath + ".tmp"), "A failed save stranded vault.dat.tmp on disk.");

        // Same provider instance (what the running app keeps) must still decrypt the untouched on-disk vault.
        using (EncryptedFileVault reopened = new(vaultPath, keyProvider))
        {
            Assert.Equal(2, (await reopened.ListAsync()).Count);
        }
    }

    [Fact]
    public async Task ReencryptAsync_ChangePassword_OldPasswordRejected_NewAccepted_NoBackupsLeft()
    {
        using TempDirectory temp = new();
        string vaultPath = temp.CreateFilePath("vault.dat");
        using VaultKeyProvider keyProvider = CreateProvider("OldPassword123!");
        await SeedTwoAccountsAsync(vaultPath, keyProvider);

        using (EncryptedFileVault vault = new(vaultPath, keyProvider))
        using (SecretValue newPassword = SecretValue.FromString("NewPassword123!"))
        {
            await vault.ReencryptAsync(newPassword);
        }

        Assert.Empty(Directory.GetFiles(temp.Path, "*.bak"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));

        using (VaultKeyProvider oldProvider = CreateProvider("OldPassword123!"))
        using (EncryptedFileVault withOld = new(vaultPath, oldProvider))
        {
            await Assert.ThrowsAsync<VaultCorruptedException>(() => withOld.ListAsync());
        }

        using (VaultKeyProvider newProvider = CreateProvider("NewPassword123!"))
        using (EncryptedFileVault withNew = new(vaultPath, newProvider))
        {
            Assert.Equal(2, (await withNew.ListAsync()).Count);
        }
    }

    // M44 B12: a normal save that upgrades protection must not leave the weaker previous primary as .bak.
    [Fact]
    public async Task NormalSave_UpgradingDpapiOnlyVaultToPassword_LeavesNoDpapiOnlyBackup()
    {
        using TempDirectory temp = new();
        string vaultPath = temp.CreateFilePath("vault.dat");
        using (VaultKeyProvider noPassword = new())
        {
            await SeedTwoAccountsAsync(vaultPath, noPassword);
        }

        using (VaultKeyProvider withPassword = CreateProvider("NewPassword123!"))
        using (EncryptedFileVault vault = new(vaultPath, withPassword))
        {
            Account first = (await vault.ListAsync())[0];
            Assert.True((await vault.UpdateAsync(first with { Note = "upgraded" })).IsSuccess);
        }

        Assert.Equal(0x02, (await File.ReadAllBytesAsync(vaultPath))[5]);
        Assert.False(File.Exists(vaultPath + ".bak"), "DPAPI-only backup survived next to a password-protected vault.");

        // The next save at the same protection level rotates a .bak again (recovery copy is kept when it is not weaker).
        using (VaultKeyProvider withPassword = CreateProvider("NewPassword123!"))
        using (EncryptedFileVault vault = new(vaultPath, withPassword))
        {
            Account first = (await vault.ListAsync())[0];
            Assert.True((await vault.UpdateAsync(first with { Note = "again" })).IsSuccess);
        }

        Assert.True(File.Exists(vaultPath + ".bak"));
        Assert.Equal(0x02, (await File.ReadAllBytesAsync(vaultPath + ".bak"))[5]);
    }

    // M44 B13: a backup locked by AV/indexer during re-encryption must not be silently forgotten.
    [Fact]
    public async Task ReencryptAsync_WhenStaleBackupIsLocked_RetriesAndDeletesItOnNextLoad()
    {
        using TempDirectory temp = new();
        string vaultPath = temp.CreateFilePath("vault.dat");
        using VaultKeyProvider keyProvider = new();
        await SeedTwoAccountsAsync(vaultPath, keyProvider);
        string staleBackup = vaultPath + ".v1.bak";
        await File.WriteAllBytesAsync(staleBackup, [1, 2, 3, 4]);

        using EncryptedFileVault vault = new(vaultPath, keyProvider);
        using (FileStream scannerHandle = new(staleBackup, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (SecretValue password = SecretValue.FromString("NewPassword123!"))
        {
            await vault.ReencryptAsync(password);
            Assert.True(File.Exists(staleBackup));
        }

        Assert.Equal(2, (await vault.ListAsync()).Count);
        Assert.False(File.Exists(staleBackup), "Stale DPAPI-era backup was never retried after the lock was released.");
    }

    // M44 B14: settings say "password on" but the vault on disk is DPAPI-only (crash between the two writes).
    [Fact]
    public async Task EnsurePasswordProtection_OnDpapiOnlyVault_BindsTypedPassword_OtherPasswordsRejected()
    {
        using TempDirectory temp = new();
        string vaultPath = temp.CreateFilePath("vault.dat");
        using (VaultKeyProvider noPassword = new())
        {
            await SeedTwoAccountsAsync(vaultPath, noPassword);
        }

        using (VaultKeyProvider typed = CreateProvider("TypedPassword123!"))
        using (EncryptedFileVault vault = new(vaultPath, typed))
        using (SecretValue password = SecretValue.FromString("TypedPassword123!"))
        {
            Assert.False(vault.IsPasswordProtectedOnDisk());
            Assert.Equal(2, (await vault.ListAsync()).Count);
            Assert.True(await vault.EnsurePasswordProtectionAsync(password));
            Assert.True(vault.IsPasswordProtectedOnDisk());
            Assert.False(await vault.EnsurePasswordProtectionAsync(password));
        }

        Assert.Empty(Directory.GetFiles(temp.Path, "*.bak"));
        using (VaultKeyProvider other = CreateProvider("SomethingElse123!"))
        using (EncryptedFileVault withOther = new(vaultPath, other))
        {
            await Assert.ThrowsAsync<VaultCorruptedException>(() => withOther.ListAsync());
        }
    }

    // M44 R4: a queued wipe of an old .bak must not destroy the fresh .bak produced by a later successful save.
    [Fact]
    public async Task PendingBackupWipe_IsCancelled_WhenLaterSaveRotatesAFreshBackup()
    {
        using TempDirectory temp = new();
        string vaultPath = temp.CreateFilePath("vault.dat");
        using VaultKeyProvider keyProvider = CreateProvider("Password123!");
        await SeedTwoAccountsAsync(vaultPath, keyProvider);

        using EncryptedFileVault vault = new(vaultPath, keyProvider);
        // Simulate an earlier wipe that failed while the old .bak was locked (a real lock would also block File.Replace).
        var pending = (System.Collections.Generic.HashSet<string>)typeof(EncryptedFileVault)
            .GetField("_pendingSecureDeletes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vault)!;
        pending.Add(vaultPath + ".bak");

        Account first = (await vault.ListAsync())[0];
        Assert.True((await vault.UpdateAsync(first with { Note = "fresh" })).IsSuccess);
        Assert.Equal(2, (await vault.ListAsync()).Count);

        Assert.True(File.Exists(vaultPath + ".bak"), "Fresh recovery backup was wiped by a stale pending delete.");
        Assert.Equal(0x02, (await File.ReadAllBytesAsync(vaultPath + ".bak"))[5]);
    }

    private static VaultKeyProvider CreateProvider(string password)
    {
        VaultKeyProvider provider = new();
        provider.SetKey(SecretValue.FromString(password));
        return provider;
    }

    private static async Task SeedTwoAccountsAsync(string vaultPath, VaultKeyProvider keyProvider)
    {
        using EncryptedFileVault vault = new(vaultPath, keyProvider);
        using SecretValue first = SecretValue.FromString("m44-fake-cookie-value-one");
        using SecretValue second = SecretValue.FromString("m44-fake-cookie-value-two");
        await vault.AddAsync(new Account(new AccountId(1), "First", null, null, first, DateTimeOffset.UtcNow));
        await vault.AddAsync(new Account(new AccountId(2), "Second", null, null, second, DateTimeOffset.UtcNow));
        Assert.True(File.Exists(vaultPath + ".bak"));
    }
}
