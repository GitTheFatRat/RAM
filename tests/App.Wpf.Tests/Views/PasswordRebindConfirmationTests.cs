namespace RbxManager.App.Tests.Views;

using RbxManager.App.Views.Windows;
using RbxManager.Core.Models;
using Xunit;

/// <summary>M44 R2: an unprotected vault must never be bound to a single (possibly mistyped) password entry.</summary>
public sealed class PasswordRebindConfirmationTests
{
    [Fact]
    public void Confirm_RequiresTwoIdenticalEntries_AndRestartsOnMismatch()
    {
        PasswordRebindConfirmation confirmation = new();
        using SecretValue typo = SecretValue.FromString("Pasword123!");
        using SecretValue intended = SecretValue.FromString("Password123!");

        Assert.False(confirmation.Confirm(typo));
        Assert.False(confirmation.Confirm(intended));
        Assert.True(confirmation.Confirm(intended));
    }

    // M44 R2b real-data: an exclusive OS lock on vault.dat (AV/backup) must route to the confirmation path, not skip it.
    [Fact]
    public async System.Threading.Tasks.Task SafeIsPasswordProtected_WhenVaultHeaderIsLocked_TreatsVaultAsUnprotected()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RbxM44Lock_" + System.Guid.NewGuid().ToString("N"));
        string vaultPath = System.IO.Path.Combine(directory, "vault.dat");
        try
        {
            using Infrastructure.Storage.VaultKeyProvider keyProvider = new();
            keyProvider.SetKey(SecretValue.FromString("Password123!"));
            using Infrastructure.Storage.EncryptedFileVault vault = new(vaultPath, keyProvider);
            using (SecretValue cookie = SecretValue.FromString("m44-fake-cookie"))
            {
                Assert.True((await vault.AddAsync(new Account(new AccountId(1), "A", null, null, cookie, System.DateTimeOffset.UtcNow))).IsSuccess);
            }

            Assert.True(MasterPasswordWindow.SafeIsPasswordProtected(vault));
            using (System.IO.FileStream exclusive = new(vaultPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
            {
                Assert.False(MasterPasswordWindow.SafeIsPasswordProtected(vault));
            }
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
            catch (System.IO.IOException)
            {
            }
        }
    }

    [Fact]
    public void Confirm_KeepsItsOwnCopy_WhenCallerDisposesTheEntry()
    {
        PasswordRebindConfirmation confirmation = new();
        SecretValue first = SecretValue.FromString("Password123!");
        Assert.False(confirmation.Confirm(first));
        first.Dispose();

        using SecretValue second = SecretValue.FromString("Password123!");
        Assert.True(confirmation.Confirm(second));
    }
}
