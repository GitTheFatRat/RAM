namespace RbxManager.Infrastructure.Storage;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    private const byte VersionByte = 0x01;
    private static readonly byte[] AadBytes = [0x52, 0x42, 0x58, 0x56, 0x01];

    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RbxManager",
        "vault.dat");

    private readonly string _filePath;
    private readonly ILogger<EncryptedFileVault>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public string VaultPath => _filePath;

    public EncryptedFileVault(string? filePath = null, ILogger<EncryptedFileVault>? logger = null)
    {
        _filePath = filePath ?? DefaultPath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Account>> ListAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            List<Account> accounts = await LoadInternalAsync().ConfigureAwait(false);
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

    public async Task<Result> AddAsync(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            List<Account> accounts = await LoadInternalAsync().ConfigureAwait(false);

            Result validationResult = AccountValidator.Validate(account, accounts);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }

            if (accounts.Any(existing => existing.Id == account.Id))
            {
                return Result.Failure($"Account with ID '{account.Id}' already exists.");
            }

            accounts.Add(account);
            await SaveInternalAsync(accounts).ConfigureAwait(false);

            if (_logger != null)
            {
                LogAddedAccount(_logger, account.Id.Value, accounts.Count);
            }

            return Result.Success();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Result> UpdateAsync(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            List<Account> accounts = await LoadInternalAsync().ConfigureAwait(false);

            Result validationResult = AccountValidator.Validate(account, accounts);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }

            int index = accounts.FindIndex(existing => existing.Id == account.Id);
            if (index == -1)
            {
                return Result.Failure($"Account with ID '{account.Id}' was not found.");
            }

            accounts[index] = account;
            await SaveInternalAsync(accounts).ConfigureAwait(false);

            if (_logger != null)
            {
                LogUpdatedAccount(_logger, account.Id.Value);
            }

            return Result.Success();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RemoveAsync(AccountId id)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            List<Account> accounts = await LoadInternalAsync().ConfigureAwait(false);
            int removedCount = accounts.RemoveAll(existing => existing.Id == id);

            if (removedCount > 0)
            {
                await SaveInternalAsync(accounts).ConfigureAwait(false);
                if (_logger != null)
                {
                    LogRemovedAccount(_logger, id.Value, accounts.Count);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<List<Account>> LoadInternalAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new List<Account>();
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);

        if (fileBytes.Length < 7)
        {
            throw new VaultCorruptedException("Vault file is too short to contain a valid header.");
        }

        if (fileBytes[0] != MagicBytes[0] || fileBytes[1] != MagicBytes[1] ||
            fileBytes[2] != MagicBytes[2] || fileBytes[3] != MagicBytes[3])
        {
            throw new VaultCorruptedException("Vault header magic mismatch.");
        }

        if (fileBytes[4] != VersionByte)
        {
            throw new VaultCorruptedException($"Unsupported vault version '{fileBytes[4]}'.");
        }

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
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes, AadBytes);
        }
        catch (CryptographicException ex)
        {
            throw new VaultCorruptedException("Vault authentication tag verification failed. Data is corrupted or tampered.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
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
                    LastCheckedAt: dto.LastCheckedAt));
            }
        }

        return accounts;
    }

    private async Task SaveInternalAsync(List<Account> accounts)
    {
        List<AccountDto> dtos = new(accounts.Count);
        foreach (Account account in accounts)
        {
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
                LastCheckedAt = account.LastCheckedAt
            });
        }

        byte[] plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(dtos);

        byte[] dataKey = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[plaintextBytes.Length];

        try
        {
            using AesGcm aesGcm = new(dataKey, 16);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, AadBytes);
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
        int totalLength = 4 + 1 + 2 + wrappedKey.Length + 12 + 16 + ciphertext.Length;
        byte[] outputBuffer = new byte[totalLength];

        MagicBytes.CopyTo(outputBuffer.AsSpan(0, 4));
        outputBuffer[4] = VersionByte;
        BinaryPrimitives.WriteUInt16LittleEndian(outputBuffer.AsSpan(5, 2), wrappedKeyLength);
        wrappedKey.CopyTo(outputBuffer.AsSpan(7, wrappedKey.Length));

        int currentOffset = 7 + wrappedKey.Length;
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

        await File.WriteAllBytesAsync(tmpPath, outputBuffer).ConfigureAwait(false);

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
