namespace RbxManager.Infrastructure.Tests.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Exceptions;
using RbxManager.Core.Models;
using RbxManager.Infrastructure.Storage;

[SupportedOSPlatform("windows")]
public sealed class EncryptedFileVaultTests : IDisposable
{
    private const string FakeCookie1 = "FAKE_COOKIE_FOR_TESTS_123";
    private const string FakeCookie2 = "FAKE_COOKIE_FOR_TESTS_456_UPDATED";

    private readonly string _tempDirectory;
    private readonly string _vaultFilePath;

    public EncryptedFileVaultTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "RbxManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _vaultFilePath = Path.Combine(_tempDirectory, "vault.dat");
    }

    [Fact]
    public async Task Add_LoadsSameDataFromNewInstance()
    {
        using (EncryptedFileVault initialVault = new(_vaultFilePath))
        {
            using SecretValue cookie = SecretValue.FromString(FakeCookie1);
            Account account = new(new AccountId(101), "AlphaUser", "MainGroup", "Some note", cookie, DateTimeOffset.UtcNow);
            Result result = await initialVault.AddAsync(account);
            Assert.True(result.IsSuccess);
        }

        using (EncryptedFileVault newVault = new(_vaultFilePath))
        {
            IReadOnlyList<Account> accounts = await newVault.ListAsync();
            Assert.Single(accounts);
            Assert.Equal(101, accounts[0].Id.Value);
            Assert.Equal("AlphaUser", accounts[0].Alias);
            Assert.Equal("MainGroup", accounts[0].Group);
            Assert.Equal("Some note", accounts[0].Note);
            Assert.Equal(FakeCookie1, accounts[0].Cookie.Reveal());
        }
    }

    [Fact]
    public async Task Update_ModifiesExistingEntrySuccessfully()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue initialCookie = SecretValue.FromString(FakeCookie1);
        Account original = new(new AccountId(202), "InitialAlias", "OldGroup", "Old note", initialCookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(original);

        using SecretValue updatedCookie = SecretValue.FromString(FakeCookie2);
        Account updated = new(new AccountId(202), "UpdatedAlias", "NewGroup", "New note", updatedCookie, original.CreatedAt);
        Result updateResult = await vault.UpdateAsync(updated);

        Assert.True(updateResult.IsSuccess);
        IReadOnlyList<Account> accounts = await vault.ListAsync();
        Assert.Single(accounts);
        Assert.Equal("UpdatedAlias", accounts[0].Alias);
        Assert.Equal("NewGroup", accounts[0].Group);
        Assert.Equal("New note", accounts[0].Note);
        Assert.Equal(FakeCookie2, accounts[0].Cookie.Reveal());
    }

    [Fact]
    public async Task Remove_DeletesEntryFromVault()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(303), "ToRemove", null, null, cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        await vault.RemoveAsync(account.Id);

        IReadOnlyList<Account> accounts = await vault.ListAsync();
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task CiphertextOnDisk_DoesNotContainPlaintextAliasOrCookie()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(404), "SecretUniqueAlias_999", "GroupSecret", "TopSecretNote", cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] rawBytes = await File.ReadAllBytesAsync(_vaultFilePath);
        string rawString = Encoding.UTF8.GetString(rawBytes);

        Assert.DoesNotContain(FakeCookie1, rawString, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretUniqueAlias_999", rawString, StringComparison.Ordinal);
        Assert.DoesNotContain("TopSecretNote", rawString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlippingOneByteInCiphertext_ThrowsVaultCorruptedException()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(505), "CorruptTest", null, null, cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] bytes = await File.ReadAllBytesAsync(_vaultFilePath);
        bytes[^1] ^= 0xFF; // Flip last byte of ciphertext
        await File.WriteAllBytesAsync(_vaultFilePath, bytes);

        using EncryptedFileVault testVault = new(_vaultFilePath);
        await Assert.ThrowsAsync<VaultCorruptedException>(() => testVault.ListAsync());
    }

    [Fact]
    public async Task FlippingOneByteInTag_ThrowsVaultCorruptedException()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(606), "TagTamper", null, null, cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] bytes = await File.ReadAllBytesAsync(_vaultFilePath);
        ushort wrappedKeyLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5, 2));
        int tagOffset = 7 + wrappedKeyLength + 12; // Tag is right after wrappedKey and 12-byte nonce
        bytes[tagOffset] ^= 0xFF; // Corrupt tag
        await File.WriteAllBytesAsync(_vaultFilePath, bytes);

        using EncryptedFileVault testVault = new(_vaultFilePath);
        await Assert.ThrowsAsync<VaultCorruptedException>(() => testVault.ListAsync());
    }

    [Fact]
    public async Task WrongMagicHeader_ThrowsVaultCorruptedException()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(707), "MagicTest", null, null, cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] bytes = await File.ReadAllBytesAsync(_vaultFilePath);
        bytes[0] = (byte)'X'; // Corrupt magic "RBXV" -> "XBXV"
        await File.WriteAllBytesAsync(_vaultFilePath, bytes);

        using EncryptedFileVault testVault = new(_vaultFilePath);
        await Assert.ThrowsAsync<VaultCorruptedException>(() => testVault.ListAsync());
    }

    [Fact]
    public async Task SaveTwice_GeneratesDifferentCiphertextForIdenticalData()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(808), "FreshKeyTest", null, null, cookie, DateTimeOffset.UtcNow);

        await vault.AddAsync(account);
        byte[] firstSaveBytes = await File.ReadAllBytesAsync(_vaultFilePath);

        // Update with identical data to trigger a second save
        await vault.UpdateAsync(account);
        byte[] secondSaveBytes = await File.ReadAllBytesAsync(_vaultFilePath);

        Assert.False(firstSaveBytes.SequenceEqual(secondSaveBytes));
    }

    [Fact]
    public async Task BakFile_IsCreatedOnSecondSave()
    {
        using EncryptedFileVault vault = new(_vaultFilePath);
        using SecretValue cookie1 = SecretValue.FromString(FakeCookie1);
        using SecretValue cookie2 = SecretValue.FromString(FakeCookie2);
        string bakPath = _vaultFilePath + ".bak";

        Assert.False(File.Exists(bakPath));

        Account account1 = new(new AccountId(901), "FirstSave", null, null, cookie1, DateTimeOffset.UtcNow);
        await vault.AddAsync(account1);
        Assert.False(File.Exists(bakPath)); // First save moves file, no backup yet

        Account account2 = new(new AccountId(902), "SecondSave", null, null, cookie2, DateTimeOffset.UtcNow);
        await vault.AddAsync(account2);
        Assert.True(File.Exists(bakPath)); // Second save replaces and generates .bak
    }

    [Fact]
    public async Task DefaultVaultPath_CanSaveAndPersistAccount()
    {
        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RbxManager", "vault.dat");
        using EncryptedFileVault vault = new();
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(999999), "ManualTestAccount", "AlphaGroup", "Test note for M1 verification", cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        Assert.True(File.Exists(defaultPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }

        GC.SuppressFinalize(this);
    }
}
