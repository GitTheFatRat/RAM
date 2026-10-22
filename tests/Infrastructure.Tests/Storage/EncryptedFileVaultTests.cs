namespace RbxManager.Infrastructure.Tests.Storage;

using System;
using System.Buffers.Binary;
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
using RbxManager.Infrastructure.Tests.TestHelpers;

[SupportedOSPlatform("windows")]
public sealed class EncryptedFileVaultTests
{
    private const string FakeCookie1 = "FAKE_COOKIE_FOR_TESTS_123";
    private const string FakeCookie2 = "FAKE_COOKIE_FOR_TESTS_456_UPDATED";

    [Fact]
    public void DefaultPath_MatchesExpectedLocalApplicationDataPath()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RbxManager",
            "vault.dat");

        Assert.Equal(expected, EncryptedFileVault.DefaultPath);
    }

    [Fact]
    public async Task ConstructWithInjectedPath_NeverCreatesFilesOutsideInjectedDirectory()
    {
        using TempDirectory tempDir = new();
        string injectedPath = tempDir.CreateFilePath("guard_vault.dat");

        using (EncryptedFileVault vault = new(injectedPath))
        {
            Assert.False(File.Exists(injectedPath));

            using SecretValue cookie = SecretValue.FromString(FakeCookie1);
            Account account = new(new AccountId(1111), "GuardAccount", null, null, cookie, DateTimeOffset.UtcNow);
            Result result = await vault.AddAsync(account);
            Assert.True(result.IsSuccess);
        }

        Assert.True(File.Exists(injectedPath));
        string[] createdFiles = Directory.GetFiles(tempDir.Path);
        Assert.All(createdFiles, file => Assert.StartsWith(tempDir.Path, file, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OldPayloadWithoutNewFields_LoadsWithTolerantDefaults()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        // Simulate an M1 JSON payload that does NOT have UserId, Status, or LastCheckedAt
        string legacyJson = """[{"Id":10,"Alias":"LegacyAccount","Group":"LegacyGroup","Note":"LegacyNote","Cookie":"FAKE_COOKIE_FOR_TESTS_123","CreatedAt":"2026-01-01T00:00:00+00:00","Username":null}]""";
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(legacyJson);

        byte[] dataKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        byte[] nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] aad = [0x52, 0x42, 0x58, 0x56, 0x01];

        using (System.Security.Cryptography.AesGcm aes = new(dataKey, 16))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }

        byte[] wrappedKey = System.Security.Cryptography.ProtectedData.Protect(dataKey, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        ushort wrappedKeyLength = checked((ushort)wrappedKey.Length);

        using (FileStream fs = File.Create(vaultFilePath))
        {
            fs.Write([0x52, 0x42, 0x58, 0x56, 0x01]);
            byte[] lenBytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(lenBytes, wrappedKeyLength);
            fs.Write(lenBytes);
            fs.Write(wrappedKey);
            fs.Write(nonce);
            fs.Write(tag);
            fs.Write(ciphertext);
        }

        using (EncryptedFileVault vault = new(vaultFilePath))
        {
            IReadOnlyList<Account> accounts = await vault.ListAsync();
            Assert.Single(accounts);
            Account account = accounts[0];
            Assert.Equal(10, account.Id.Value);
            Assert.Equal("LegacyAccount", account.Alias);
            Assert.Equal(AccountStatus.Unknown, account.Status);
            Assert.Null(account.UserId);
            Assert.Null(account.LastCheckedAt);
            Assert.Equal(FakeCookie1, account.Cookie.Reveal());
        }
    }

    [Fact]
    public async Task Add_LoadsSameDataFromNewInstance()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using (EncryptedFileVault initialVault = new(vaultFilePath))
        {
            using SecretValue cookie = SecretValue.FromString(FakeCookie1);
            Account account = new(new AccountId(101), "AlphaUser", "MainGroup", "Some note", cookie, DateTimeOffset.UtcNow);
            Result result = await initialVault.AddAsync(account);
            Assert.True(result.IsSuccess);
        }

        using (EncryptedFileVault newVault = new(vaultFilePath))
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
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using EncryptedFileVault vault = new(vaultFilePath);
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
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using EncryptedFileVault vault = new(vaultFilePath);
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
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using EncryptedFileVault vault = new(vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(404), "SecretUniqueAlias_999", "GroupSecret", "TopSecretNote", cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] rawBytes = await File.ReadAllBytesAsync(vaultFilePath);
        string rawString = Encoding.UTF8.GetString(rawBytes);

        Assert.DoesNotContain(FakeCookie1, rawString, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretUniqueAlias_999", rawString, StringComparison.Ordinal);
        Assert.DoesNotContain("TopSecretNote", rawString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlippingOneByteInCiphertext_ThrowsVaultCorruptedException()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using EncryptedFileVault vault = new(vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(505), "CorruptTest", null, null, cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] bytes = await File.ReadAllBytesAsync(vaultFilePath);
        bytes[^1] ^= 0xFF; // Flip last byte of ciphertext
        await File.WriteAllBytesAsync(vaultFilePath, bytes);

        using EncryptedFileVault testVault = new(vaultFilePath);
        await Assert.ThrowsAsync<VaultCorruptedException>(() => testVault.ListAsync());
    }

    [Fact]
    public async Task FlippingOneByteInTag_ThrowsVaultCorruptedException()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using EncryptedFileVault vault = new(vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(606), "TagTamper", null, null, cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] bytes = await File.ReadAllBytesAsync(vaultFilePath);
        ushort wrappedKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
        int tagOffset = 8 + wrappedKeyLength + 12; // Tag is right after wrappedKey and 12-byte nonce
        bytes[tagOffset] ^= 0xFF; // Corrupt tag
        await File.WriteAllBytesAsync(vaultFilePath, bytes);

        using EncryptedFileVault testVault = new(vaultFilePath);
        await Assert.ThrowsAsync<VaultCorruptedException>(() => testVault.ListAsync());
    }

    [Fact]
    public async Task WrongMagicHeader_ThrowsVaultCorruptedException()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using EncryptedFileVault vault = new(vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(707), "MagicTest", null, null, cookie, DateTimeOffset.UtcNow);
        await vault.AddAsync(account);

        byte[] bytes = await File.ReadAllBytesAsync(vaultFilePath);
        bytes[0] = (byte)'X'; // Corrupt magic "RBXV" -> "XBXV"
        await File.WriteAllBytesAsync(vaultFilePath, bytes);

        using EncryptedFileVault testVault = new(vaultFilePath);
        await Assert.ThrowsAsync<VaultCorruptedException>(() => testVault.ListAsync());
    }

    [Fact]
    public async Task SaveTwice_GeneratesDifferentCiphertextForIdenticalData()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using EncryptedFileVault vault = new(vaultFilePath);
        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        Account account = new(new AccountId(808), "FreshKeyTest", null, null, cookie, DateTimeOffset.UtcNow);

        await vault.AddAsync(account);
        byte[] firstSaveBytes = await File.ReadAllBytesAsync(vaultFilePath);

        // Update with identical data to trigger a second save
        await vault.UpdateAsync(account);
        byte[] secondSaveBytes = await File.ReadAllBytesAsync(vaultFilePath);

        Assert.False(firstSaveBytes.SequenceEqual(secondSaveBytes));
    }

    [Fact]
    public async Task BakFile_IsCreatedOnSecondSave()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();
        string bakPath = vaultFilePath + ".bak";

        using EncryptedFileVault vault = new(vaultFilePath);
        using SecretValue cookie1 = SecretValue.FromString(FakeCookie1);
        using SecretValue cookie2 = SecretValue.FromString(FakeCookie2);

        Assert.False(File.Exists(bakPath));

        Account account1 = new(new AccountId(901), "FirstSave", null, null, cookie1, DateTimeOffset.UtcNow);
        await vault.AddAsync(account1);
        Assert.False(File.Exists(bakPath)); // First save moves file, no backup yet

        Account account2 = new(new AccountId(902), "SecondSave", null, null, cookie2, DateTimeOffset.UtcNow);
        await vault.AddAsync(account2);
        Assert.True(File.Exists(bakPath)); // Second save replaces and generates .bak
    }

    [Fact]
    public void Argon2id_DeriveKey_ProducesExpected32ByteLength()
    {
        using SecretValue password = SecretValue.FromString("MasterPasswordTest123!");
        byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);

        byte[] key1 = EncryptedFileVault.DeriveKey(password, salt);
        byte[] key2 = EncryptedFileVault.DeriveKey(password, salt);

        Assert.Equal(32, key1.Length);
        Assert.Equal(32, key2.Length);
        Assert.Equal(key1, key2); // Deterministic for same password and salt
    }

    [Fact]
    public async Task FormatV1_LoadsCorrectly_AndUpgradesToFormatV2OnSave()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        // 1. Manually craft a Format v1 vault
        string json = """[{"Id":100,"Alias":"V1Account","Group":"V1Group","Note":"V1Note","Cookie":"FAKE_COOKIE_FOR_TESTS_123","CreatedAt":"2026-01-01T00:00:00+00:00","Username":null}]""";
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(json);

        byte[] dataKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        byte[] nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] aad = [0x52, 0x42, 0x58, 0x56, 0x01];

        using (System.Security.Cryptography.AesGcm aesGcm = new(dataKey, 16))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }

        byte[] wrappedKey = System.Security.Cryptography.ProtectedData.Protect(dataKey, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        ushort wrappedKeyLength = (ushort)wrappedKey.Length;

        byte[] v1FileBytes = new byte[7 + wrappedKey.Length + 12 + 16 + ciphertext.Length];
        new byte[] { 0x52, 0x42, 0x58, 0x56, 0x01 }.CopyTo(v1FileBytes.AsSpan(0, 5));
        BinaryPrimitives.WriteUInt16LittleEndian(v1FileBytes.AsSpan(5, 2), wrappedKeyLength);
        wrappedKey.CopyTo(v1FileBytes.AsSpan(7, wrappedKey.Length));
        nonce.CopyTo(v1FileBytes.AsSpan(7 + wrappedKey.Length, 12));
        tag.CopyTo(v1FileBytes.AsSpan(19 + wrappedKey.Length, 16));
        ciphertext.CopyTo(v1FileBytes.AsSpan(35 + wrappedKey.Length));

        await File.WriteAllBytesAsync(vaultFilePath, v1FileBytes);

        // 2. Load with EncryptedFileVault (v1 fallback)
        using (EncryptedFileVault vault = new(vaultFilePath))
        {
            IReadOnlyList<Account> loaded = await vault.ListAsync();
            Assert.Single(loaded);
            Assert.Equal("V1Account", loaded[0].Alias);

            // 3. Update account to trigger a save
            await vault.UpdateAsync(loaded[0]);
        }

        // 4. Assert saved file is now Format v2 (version byte at offset 4 is 0x02)
        byte[] savedBytes = await File.ReadAllBytesAsync(vaultFilePath);
        Assert.Equal(0x02, savedBytes[4]);
    }

    [Fact]
    public async Task FormatV2_WithArgon2id_RoundTrip_SavesAndLoadsAccountsAndProxy()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using VaultKeyProvider keyProvider = new();
        using SecretValue masterPassword = SecretValue.FromString("MyMasterSecretPassword2026!");
        keyProvider.SetKey(masterPassword.Clone());

        using SecretValue cookie = SecretValue.FromString(FakeCookie1);
        using SecretValue proxyPass = SecretValue.FromString("ProxySecret456");
        using ProxyConfig proxy = new("proxy.network.local", 1080, "puser", proxyPass, ProxyType.Socks5);
        Account account = new(new AccountId(555), "ProxyAccount", "PGroup", "PNote", cookie, DateTimeOffset.UtcNow, Proxy: proxy);

        // 1. Save with Argon2id + DPAPI
        using (EncryptedFileVault vault = new(vaultFilePath, keyProvider))
        {
            Result addResult = await vault.AddAsync(account);
            Assert.True(addResult.IsSuccess);
        }

        // 2. Inspect disk bytes
        byte[] fileBytes = await File.ReadAllBytesAsync(vaultFilePath);
        Assert.Equal(0x02, fileBytes[4]); // version 2
        Assert.Equal(0x01, fileBytes[5]); // kdfType 1 (Argon2id + DPAPI)

        // 3. Reopen with new vault instance and same key
        using (EncryptedFileVault vault = new(vaultFilePath, keyProvider))
        {
            IReadOnlyList<Account> loaded = await vault.ListAsync();
            Assert.Single(loaded);
            Account loadedAcc = loaded[0];
            Assert.Equal("ProxyAccount", loadedAcc.Alias);
            Assert.NotNull(loadedAcc.Proxy);
            Assert.Equal("proxy.network.local", loadedAcc.Proxy.Host);
            Assert.Equal(1080, loadedAcc.Proxy.Port);
            Assert.Equal("puser", loadedAcc.Proxy.Username);
            Assert.Equal(ProxyType.Socks5, loadedAcc.Proxy.Type);
            Assert.NotNull(loadedAcc.Proxy.Password);
            Assert.True(loadedAcc.Proxy.Password.HasContent);
        }
    }

    [Fact]
    public async Task FormatV2_WithArgon2id_InvalidPassword_ThrowsVaultCorruptedException()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using VaultKeyProvider correctKeyProvider = new();
        using SecretValue correctPassword = SecretValue.FromString("CorrectPassword123!");
        correctKeyProvider.SetKey(correctPassword.Clone());

        using (EncryptedFileVault vault = new(vaultFilePath, correctKeyProvider))
        {
            using SecretValue cookie = SecretValue.FromString(FakeCookie1);
            Account account = new(new AccountId(1), "SecretAcc", null, null, cookie, DateTimeOffset.UtcNow);
            await vault.AddAsync(account);
        }

        // Reopen with wrong master password
        using VaultKeyProvider wrongKeyProvider = new();
        using SecretValue wrongPassword = SecretValue.FromString("WrongPassword12345!");
        wrongKeyProvider.SetKey(wrongPassword.Clone());

        using (EncryptedFileVault vault = new(vaultFilePath, wrongKeyProvider))
        {
            VaultCorruptedException ex = await Assert.ThrowsAsync<VaultCorruptedException>(() => vault.ListAsync());
            Assert.Contains("Invalid master password", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task FormatV2_WithArgon2id_MissingPassword_ThrowsVaultCorruptedException()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using VaultKeyProvider keyProvider = new();
        using SecretValue masterPassword = SecretValue.FromString("CorrectPassword123!");
        keyProvider.SetKey(masterPassword.Clone());

        using (EncryptedFileVault vault = new(vaultFilePath, keyProvider))
        {
            using SecretValue cookie = SecretValue.FromString(FakeCookie1);
            Account account = new(new AccountId(1), "SecretAcc", null, null, cookie, DateTimeOffset.UtcNow);
            await vault.AddAsync(account);
        }

        // Try to open without key provider
        using (EncryptedFileVault vault = new(vaultFilePath, keyProvider: null))
        {
            VaultCorruptedException ex = await Assert.ThrowsAsync<VaultCorruptedException>(() => vault.ListAsync());
            Assert.Contains("Master password is required", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task FormatV2_WithArgon2id_FlippedSaltByte_ThrowsVaultCorruptedException()
    {
        using TempDirectory tempDir = new();
        string vaultFilePath = tempDir.CreateFilePath();

        using VaultKeyProvider keyProvider = new();
        using SecretValue masterPassword = SecretValue.FromString("CorrectPassword123!");
        keyProvider.SetKey(masterPassword.Clone());

        using (EncryptedFileVault vault = new(vaultFilePath, keyProvider))
        {
            using SecretValue cookie = SecretValue.FromString(FakeCookie1);
            Account account = new(new AccountId(1), "SecretAcc", null, null, cookie, DateTimeOffset.UtcNow);
            await vault.AddAsync(account);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(vaultFilePath);
        fileBytes[10] ^= 0xFF; // Flip byte in salt
        await File.WriteAllBytesAsync(vaultFilePath, fileBytes);

        using (EncryptedFileVault vault = new(vaultFilePath, keyProvider))
        {
            await Assert.ThrowsAsync<VaultCorruptedException>(() => vault.ListAsync());
        }
    }
}
