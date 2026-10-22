namespace RbxManager.Infrastructure.Storage;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Logging;
using RbxManager.Core.Common;
using RbxManager.Core.Exceptions;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using RbxManager.Core.Validation;

[SupportedOSPlatform("windows")]
public sealed partial class EncryptedFileVault : IAccountStore, IDisposable
{
    private static readonly byte[] MagicBytes = [0x52, 0x42, 0x58, 0x56]; // "RBXV"
    private const byte Version1Byte = 0x01;
    private const byte Version2Byte = 0x02;
    private const byte KdfTypeDpapi = 0x00;
    private const byte KdfTypeArgon2idDpapi = 0x01;

    public const int Argon2MemorySizeKb = 65536; // 64 MB
    public const int Argon2Iterations = 3;
    public const int Argon2Parallelism = 4;
    public const int Argon2SaltLength = 16;
    public const int DerivedKeyLength = 32;

    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RbxManager",
        "vault.dat");

    private readonly string _filePath;
    private readonly IVaultKeyProvider? _keyProvider;
    private readonly ILogger<EncryptedFileVault>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public string VaultPath => _filePath;

    public EncryptedFileVault(
        string? filePath = null,
        IVaultKeyProvider? keyProvider = null,
        ILogger<EncryptedFileVault>? logger = null)
    {
        _filePath = filePath ?? DefaultPath;
        _keyProvider = keyProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<Account> accounts = await LoadInternalAsync(ct).ConfigureAwait(false);
            if (_logger != null)
            {
                LogListedAccounts(_logger, accounts.Count);
            }

            return accounts;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Result> AddAsync(Account account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        List<Account>? loadedAccounts = null;
        try
        {
            loadedAccounts = await LoadInternalAsync(ct).ConfigureAwait(false);

            Result validationResult = AccountValidator.Validate(account, loadedAccounts);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }

            if (loadedAccounts.Any(existing => existing.Id == account.Id))
            {
                return Result.Failure($"Account with ID '{account.Id}' already exists.");
            }

            List<Account> toSave = new(loadedAccounts.Count + 1);
            toSave.AddRange(loadedAccounts);
            toSave.Add(account);
            await SaveInternalAsync(toSave, ct).ConfigureAwait(false);

            if (_logger != null)
            {
                LogAddedAccount(_logger, account.Id.Value, toSave.Count);
            }

            return Result.Success();
        }
        finally
        {
            if (loadedAccounts != null)
            {
                foreach (Account loaded in loadedAccounts)
                {
                    if (!ReferenceEquals(loaded.Cookie, account.Cookie))
                    {
                        loaded.Cookie.Dispose();
                    }

                    if (loaded.Proxy?.Password != null && !ReferenceEquals(loaded.Proxy.Password, account.Proxy?.Password))
                    {
                        loaded.Proxy.Dispose();
                    }
                }
            }
            _semaphore.Release();
        }
    }

    public async Task<Result> UpdateAsync(Account account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        List<Account>? loadedAccounts = null;
        try
        {
            loadedAccounts = await LoadInternalAsync(ct).ConfigureAwait(false);

            Result validationResult = AccountValidator.Validate(account, loadedAccounts);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }

            int index = loadedAccounts.FindIndex(existing => existing.Id == account.Id);
            if (index == -1)
            {
                return Result.Failure($"Account with ID '{account.Id}' was not found.");
            }

            List<Account> toSave = new(loadedAccounts);
            toSave[index] = account;
            await SaveInternalAsync(toSave, ct).ConfigureAwait(false);

            if (_logger != null)
            {
                LogUpdatedAccount(_logger, account.Id.Value);
            }

            return Result.Success();
        }
        finally
        {
            if (loadedAccounts != null)
            {
                foreach (Account loaded in loadedAccounts)
                {
                    if (!ReferenceEquals(loaded.Cookie, account.Cookie))
                    {
                        loaded.Cookie.Dispose();
                    }

                    if (loaded.Proxy?.Password != null && !ReferenceEquals(loaded.Proxy.Password, account.Proxy?.Password))
                    {
                        loaded.Proxy.Dispose();
                    }
                }
            }
            _semaphore.Release();
        }
    }

    public async Task RemoveAsync(AccountId id, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        List<Account>? loadedAccounts = null;
        try
        {
            loadedAccounts = await LoadInternalAsync(ct).ConfigureAwait(false);
            List<Account> toSave = loadedAccounts.Where(existing => existing.Id != id).ToList();

            if (toSave.Count != loadedAccounts.Count)
            {
                await SaveInternalAsync(toSave, ct).ConfigureAwait(false);
                if (_logger != null)
                {
                    LogRemovedAccount(_logger, id.Value, toSave.Count);
                }
            }
        }
        finally
        {
            if (loadedAccounts != null)
            {
                foreach (Account loaded in loadedAccounts)
                {
                    loaded.Cookie.Dispose();
                    loaded.Proxy?.Dispose();
                }
            }
            _semaphore.Release();
        }
    }

    public static byte[] DeriveKey(SecretValue password, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        string revealed = password.Reveal();
        byte[] passwordBytes = Encoding.UTF8.GetBytes(revealed);
        try
        {
            using Argon2id argon2 = new(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = Argon2Parallelism,
                MemorySize = Argon2MemorySizeKb,
                Iterations = Argon2Iterations
            };
            return argon2.GetBytes(DerivedKeyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private async Task<List<Account>> LoadInternalAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return new List<Account>();
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(_filePath, ct).ConfigureAwait(false);

        if (fileBytes.Length < 7)
        {
            throw new VaultCorruptedException("Vault file is too short to contain a valid header.");
        }

        if (fileBytes[0] != MagicBytes[0] || fileBytes[1] != MagicBytes[1] ||
            fileBytes[2] != MagicBytes[2] || fileBytes[3] != MagicBytes[3])
        {
            throw new VaultCorruptedException("Vault header magic mismatch.");
        }

        byte version = fileBytes[4];
        if (version != Version1Byte && version != Version2Byte)
        {
            throw new VaultCorruptedException($"Unsupported vault version '{version}'.");
        }

        byte[] plaintextBytes;
        if (version == Version1Byte)
        {
            plaintextBytes = LoadVersion1(fileBytes);
        }
        else
        {
            plaintextBytes = LoadVersion2(fileBytes);
        }

        List<AccountDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<AccountDto>>(plaintextBytes);
        }
        catch (JsonException ex)
        {
            throw new VaultCorruptedException("Vault accounts payload could not be parsed as valid JSON.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }

        List<Account> accounts = new();
        if (dtos != null)
        {
            foreach (AccountDto dto in dtos)
            {
                AccountStatus status = Enum.IsDefined(typeof(AccountStatus), dto.Status)
                    ? (AccountStatus)dto.Status
                    : AccountStatus.Unknown;

                ProxyConfig? proxy = null;
                if (dto.Proxy != null)
                {
                    ProxyType proxyType = Enum.IsDefined(typeof(ProxyType), dto.Proxy.Type)
                        ? (ProxyType)dto.Proxy.Type
                        : ProxyType.Http;

                    proxy = new ProxyConfig(
                        Host: dto.Proxy.Host,
                        Port: dto.Proxy.Port,
                        Username: dto.Proxy.Username,
                        Password: dto.Proxy.Password != null ? SecretValue.FromString(dto.Proxy.Password) : null,
                        Type: proxyType);
                }

                accounts.Add(new Account(
                    Id: new AccountId(dto.Id),
                    Alias: dto.Alias,
                    Group: dto.Group,
                    Note: dto.Note,
                    Cookie: SecretValue.FromString(dto.Cookie),
                    CreatedAt: dto.CreatedAt,
                    Username: dto.Username,
                    UserId: dto.UserId,
                    Status: status,
                    LastCheckedAt: dto.LastCheckedAt,
                    Proxy: proxy));
            }
        }

        return accounts;
    }

    private static byte[] LoadVersion1(byte[] fileBytes)
    {
        ushort wrappedKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.AsSpan(5, 2));
        if (7 + wrappedKeyLength + 12 + 16 > fileBytes.Length)
        {
            throw new VaultCorruptedException("Vault file is truncated or corrupted.");
        }

        int offset = 7;
        byte[] wrappedKey = fileBytes.AsSpan(offset, wrappedKeyLength).ToArray();
        offset += wrappedKeyLength;

        ReadOnlySpan<byte> nonce = fileBytes.AsSpan(offset, 12);
        offset += 12;

        ReadOnlySpan<byte> tag = fileBytes.AsSpan(offset, 16);
        offset += 16;

        ReadOnlySpan<byte> ciphertext = fileBytes.AsSpan(offset);
        byte[] aad = [0x52, 0x42, 0x58, 0x56, 0x01];

        byte[] dataKey;
        try
        {
            dataKey = ProtectedData.Unprotect(wrappedKey, null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new VaultCorruptedException("Failed to unwrap encryption key via DPAPI.", ex);
        }

        byte[] plaintextBytes = new byte[ciphertext.Length];
        try
        {
            using AesGcm aesGcm = new(dataKey, 16);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes, aad);
            return plaintextBytes;
        }
        catch (CryptographicException ex)
        {
            throw new VaultCorruptedException("Vault authentication tag verification failed. Data is corrupted or tampered.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private byte[] LoadVersion2(byte[] fileBytes)
    {
        if (fileBytes.Length < 8)
        {
            throw new VaultCorruptedException("Vault file is too short to contain a valid header.");
        }

        byte kdfType = fileBytes[5];
        if (kdfType == KdfTypeDpapi)
        {
            ushort wrappedKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.AsSpan(6, 2));
            if (8 + wrappedKeyLength + 12 + 16 > fileBytes.Length)
            {
                throw new VaultCorruptedException("Vault file is truncated or corrupted.");
            }

            int offset = 8;
            byte[] wrappedKey = fileBytes.AsSpan(offset, wrappedKeyLength).ToArray();
            offset += wrappedKeyLength;

            ReadOnlySpan<byte> nonce = fileBytes.AsSpan(offset, 12);
            offset += 12;

            ReadOnlySpan<byte> tag = fileBytes.AsSpan(offset, 16);
            offset += 16;

            ReadOnlySpan<byte> ciphertext = fileBytes.AsSpan(offset);
            byte[] aad = [0x52, 0x42, 0x58, 0x56, 0x02, 0x00];

            byte[] dataKey;
            try
            {
                dataKey = ProtectedData.Unprotect(wrappedKey, null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                throw new VaultCorruptedException("Failed to unwrap encryption key via DPAPI.", ex);
            }

            byte[] plaintextBytes = new byte[ciphertext.Length];
            try
            {
                using AesGcm aesGcm = new(dataKey, 16);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes, aad);
                return plaintextBytes;
            }
            catch (CryptographicException ex)
            {
                throw new VaultCorruptedException("Vault authentication tag verification failed. Data is corrupted or tampered.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        else if (kdfType == KdfTypeArgon2idDpapi)
        {
            if (fileBytes.Length < 24)
            {
                throw new VaultCorruptedException("Vault file is too short to contain a valid header.");
            }

            byte[] argonSalt = fileBytes.AsSpan(6, Argon2SaltLength).ToArray();
            ushort wrappedKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.AsSpan(22, 2));
            if (24 + wrappedKeyLength + 12 + 16 > fileBytes.Length)
            {
                throw new VaultCorruptedException("Vault file is truncated or corrupted.");
            }

            int offset = 24;
            byte[] wrappedKey = fileBytes.AsSpan(offset, wrappedKeyLength).ToArray();
            offset += wrappedKeyLength;

            ReadOnlySpan<byte> nonce = fileBytes.AsSpan(offset, 12);
            offset += 12;

            ReadOnlySpan<byte> tag = fileBytes.AsSpan(offset, 16);
            offset += 16;

            ReadOnlySpan<byte> ciphertext = fileBytes.AsSpan(offset);
            byte[] aad = fileBytes.AsSpan(0, 22).ToArray();

            if (_keyProvider == null || !_keyProvider.HasKey)
            {
                throw new VaultCorruptedException("Master password is required to unlock this vault.");
            }

            SecretValue? masterPassword = _keyProvider.GetKey();
            if (masterPassword == null || !masterPassword.HasContent)
            {
                throw new VaultCorruptedException("Master password is required to unlock this vault.");
            }

            byte[] argonKey = DeriveKey(masterPassword, argonSalt);
            byte[] dpapiUnwrapped;
            try
            {
                dpapiUnwrapped = ProtectedData.Unprotect(wrappedKey, null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                CryptographicOperations.ZeroMemory(argonKey);
                throw new VaultCorruptedException("Failed to unwrap encryption key via DPAPI.", ex);
            }

            if (!CryptographicOperations.FixedTimeEquals(dpapiUnwrapped, argonKey))
            {
                CryptographicOperations.ZeroMemory(dpapiUnwrapped);
                CryptographicOperations.ZeroMemory(argonKey);
                throw new VaultCorruptedException("Invalid master password.");
            }

            byte[] plaintextBytes = new byte[ciphertext.Length];
            try
            {
                using AesGcm aesGcm = new(argonKey, 16);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes, aad);
                return plaintextBytes;
            }
            catch (CryptographicException ex)
            {
                throw new VaultCorruptedException("Vault authentication tag verification failed. Data is corrupted or tampered.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dpapiUnwrapped);
                CryptographicOperations.ZeroMemory(argonKey);
            }
        }
        else
        {
            throw new VaultCorruptedException($"Unsupported vault KDF type '{kdfType}'.");
        }
    }

    private async Task SaveInternalAsync(List<Account> accounts, CancellationToken ct = default)
    {
        List<AccountDto> dtos = new(accounts.Count);
        foreach (Account account in accounts)
        {
            ProxyDto? proxyDto = null;
            if (account.Proxy != null)
            {
                proxyDto = new ProxyDto
                {
                    Host = account.Proxy.Host,
                    Port = account.Proxy.Port,
                    Username = account.Proxy.Username,
                    Password = account.Proxy.Password?.Reveal(),
                    Type = (int)account.Proxy.Type
                };
            }

            dtos.Add(new AccountDto
            {
                Id = account.Id.Value,
                Alias = account.Alias,
                Group = account.Group,
                Note = account.Note,
                Cookie = account.Cookie.Reveal(),
                CreatedAt = account.CreatedAt,
                Username = account.Username,
                UserId = account.UserId,
                Status = (int)account.Status,
                LastCheckedAt = account.LastCheckedAt,
                Proxy = proxyDto
            });
        }

        byte[] plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(dtos);

        bool useArgon2 = _keyProvider != null && _keyProvider.HasKey;
        byte kdfType = useArgon2 ? KdfTypeArgon2idDpapi : KdfTypeDpapi;

        byte[] dataKey;
        byte[]? argonSalt = null;
        byte[] aad;

        if (useArgon2)
        {
            argonSalt = RandomNumberGenerator.GetBytes(Argon2SaltLength);
            SecretValue secret = _keyProvider!.GetKey()!;
            dataKey = DeriveKey(secret, argonSalt);

            aad = new byte[22];
            MagicBytes.CopyTo(aad.AsSpan(0, 4));
            aad[4] = Version2Byte;
            aad[5] = kdfType;
            argonSalt.CopyTo(aad.AsSpan(6, Argon2SaltLength));
        }
        else
        {
            dataKey = RandomNumberGenerator.GetBytes(32);
            aad = [MagicBytes[0], MagicBytes[1], MagicBytes[2], MagicBytes[3], Version2Byte, kdfType];
        }

        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[plaintextBytes.Length];

        try
        {
            using AesGcm aesGcm = new(dataKey, 16);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }

        byte[] wrappedKey = ProtectedData.Protect(dataKey, null, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(dataKey);

        if (wrappedKey.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("Wrapped key size exceeds maximum supported length.");
        }

        ushort wrappedKeyLength = (ushort)wrappedKey.Length;
        int headerPrefixLength = useArgon2 ? 24 : 8; // (4B magic + 1B ver + 1B kdf + [16B salt] + 2B keyLen)
        int totalLength = headerPrefixLength + wrappedKey.Length + 12 + 16 + ciphertext.Length;
        byte[] outputBuffer = new byte[totalLength];

        MagicBytes.CopyTo(outputBuffer.AsSpan(0, 4));
        outputBuffer[4] = Version2Byte;
        outputBuffer[5] = kdfType;

        int currentOffset = 6;
        if (useArgon2 && argonSalt != null)
        {
            argonSalt.CopyTo(outputBuffer.AsSpan(currentOffset, Argon2SaltLength));
            currentOffset += Argon2SaltLength;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(outputBuffer.AsSpan(currentOffset, 2), wrappedKeyLength);
        currentOffset += 2;

        wrappedKey.CopyTo(outputBuffer.AsSpan(currentOffset, wrappedKey.Length));
        currentOffset += wrappedKey.Length;

        nonce.CopyTo(outputBuffer.AsSpan(currentOffset, 12));
        currentOffset += 12;

        tag.CopyTo(outputBuffer.AsSpan(currentOffset, 16));
        currentOffset += 16;

        ciphertext.CopyTo(outputBuffer.AsSpan(currentOffset, ciphertext.Length));

        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tmpPath = _filePath + ".tmp";
        string bakPath = _filePath + ".bak";

        await using (FileStream fs = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await fs.WriteAsync(outputBuffer, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
            fs.Flush(true);
        }

        if (File.Exists(_filePath))
        {
            File.Replace(tmpPath, _filePath, bakPath);
        }
        else
        {
            File.Move(tmpPath, _filePath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _semaphore.Dispose();
        _disposed = true;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Listed {Count} accounts from vault.")]
    private static partial void LogListedAccounts(ILogger logger, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Added account with ID {AccountId}. Total accounts: {Count}.")]
    private static partial void LogAddedAccount(ILogger logger, long accountId, int count);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Updated account with ID {AccountId}.")]
    private static partial void LogUpdatedAccount(ILogger logger, long accountId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Removed account with ID {AccountId}. Remaining accounts: {Count}.")]
    private static partial void LogRemovedAccount(ILogger logger, long accountId, int count);
}
