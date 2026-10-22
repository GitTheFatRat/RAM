namespace RbxManager.Infrastructure.Backup;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Backup;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;

public sealed partial class EncryptedBackupService : IBackupService
{
    public const int MinimumPasswordLength = 12;
    public const uint MinimumIterations = 600_000;
    public const uint DefaultIterations = 600_000;

    private static readonly byte[] MagicBytes = [0x52, 0x42, 0x58, 0x42]; // "RBXB"
    private const byte VersionByte = 0x01;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 4 + 1 + SaltLength + 4 + NonceLength + TagLength; // 53 bytes

    private readonly IAccountStore _accountStore;
    private readonly ILogger<EncryptedBackupService> _logger;

    public EncryptedBackupService(IAccountStore accountStore, ILogger<EncryptedBackupService>? logger = null)
    {
        _accountStore = accountStore;
        _logger = logger ?? NullLogger<EncryptedBackupService>.Instance;
    }

    public async Task<BackupExportResult> ExportAsync(
        string destinationFilePath,
        string password,
        CancellationToken ct = default)
    {
        if (password == null || password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException($"Password must be at least {MinimumPasswordLength} characters.", nameof(password));
        }

        IReadOnlyList<Account> accounts = await _accountStore.ListAsync(ct).ConfigureAwait(false);
        if (accounts.Count == 0)
        {
            throw new InvalidOperationException("Cannot export an empty vault.");
        }

        LogExporting(accounts.Count);

        List<BackupAccountDto> dtos = new(accounts.Count);
        foreach (Account account in accounts)
        {
            dtos.Add(new BackupAccountDto
            {
                Id = account.Id.Value,
                Alias = account.Alias,
                Group = account.Group,
                Note = account.Note,
                Cookie = account.Cookie.Reveal(),
                CreatedAt = account.CreatedAt,
                UserId = account.UserId,
                Username = account.Username,
                Status = (int)account.Status,
                LastCheckedAt = account.LastCheckedAt
            });
        }

        byte[] plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(dtos);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLength);
        byte[] tag = new byte[TagLength];
        byte[] ciphertext = new byte[plaintextBytes.Length];

        byte[] aad = new byte[4 + 1 + SaltLength + 4];
        MagicBytes.CopyTo(aad, 0);
        aad[4] = VersionByte;
        salt.CopyTo(aad, 5);
        BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(21, 4), DefaultIterations);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, (int)DefaultIterations, HashAlgorithmName.SHA512, 32);

        try
        {
            using (AesGcm aesGcm = new(key, TagLength))
            {
                aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
            }

            string? directory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = destinationFilePath + ".tmp";
            using (FileStream fs = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fs.WriteAsync(MagicBytes, ct).ConfigureAwait(false);
                fs.WriteByte(VersionByte);
                await fs.WriteAsync(salt, ct).ConfigureAwait(false);
                byte[] iterationsBytes = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(iterationsBytes, DefaultIterations);
                await fs.WriteAsync(iterationsBytes, ct).ConfigureAwait(false);
                await fs.WriteAsync(nonce, ct).ConfigureAwait(false);
                await fs.WriteAsync(tag, ct).ConfigureAwait(false);
                await fs.WriteAsync(ciphertext, ct).ConfigureAwait(false);
            }

            if (File.Exists(destinationFilePath))
            {
                File.Replace(tempPath, destinationFilePath, null);
            }
            else
            {
                File.Move(tempPath, destinationFilePath);
            }

            LogExportSuccess(accounts.Count);
            return new BackupExportResult(accounts.Count, destinationFilePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public async Task<BackupImportResult> ImportAsync(
        string sourceFilePath,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new BackupException("Password cannot be empty.");
        }

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Backup file not found.", sourceFilePath);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(sourceFilePath, ct).ConfigureAwait(false);
        if (fileBytes.Length < HeaderLength)
        {
            throw new BackupException("Invalid backup file: file header too short.");
        }

        ReadOnlySpan<byte> fileSpan = fileBytes.AsSpan();

        if (!fileSpan[..4].SequenceEqual(MagicBytes))
        {
            throw new BackupException("Invalid backup file: unrecognized magic bytes.");
        }

        if (fileSpan[4] != VersionByte)
        {
            throw new BackupException($"Unsupported backup version: {fileSpan[4]}.");
        }

        byte[] salt = fileSpan.Slice(5, SaltLength).ToArray();
        uint iterations = BinaryPrimitives.ReadUInt32LittleEndian(fileSpan.Slice(21, 4));

        if (iterations < MinimumIterations)
        {
            throw new BackupException($"Backup iterations ({iterations}) below minimum required ({MinimumIterations}).");
        }

        byte[] nonce = fileSpan.Slice(25, NonceLength).ToArray();
        byte[] tag = fileSpan.Slice(37, TagLength).ToArray();
        byte[] ciphertext = fileSpan[HeaderLength..].ToArray();

        byte[] aad = new byte[4 + 1 + SaltLength + 4];
        MagicBytes.CopyTo(aad, 0);
        aad[4] = VersionByte;
        salt.CopyTo(aad, 5);
        BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(21, 4), iterations);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, (int)iterations, HashAlgorithmName.SHA512, 32);
        byte[] plaintextBytes = new byte[ciphertext.Length];

        List<BackupAccountDto>? dtos;
        try
        {
            using (AesGcm aesGcm = new(key, TagLength))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes, aad);
            }

            dtos = JsonSerializer.Deserialize<List<BackupAccountDto>>(plaintextBytes);
        }
        catch (Exception ex) when (ex is AuthenticationTagMismatchException or CryptographicException or JsonException)
        {
            throw new BackupException(BackupException.DefaultErrorMessage, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }

        if (dtos == null || dtos.Count == 0)
        {
            return new BackupImportResult(0, 0);
        }

        IReadOnlyList<Account> existingAccounts = await _accountStore.ListAsync(ct).ConfigureAwait(false);
        HashSet<string> existingAliases = existingAccounts.Select(a => a.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int importedCount = 0;
        int skippedCount = 0;
        long nextId = existingAccounts.Count > 0 ? existingAccounts.Max(a => a.Id.Value) + 1 : 1;

        foreach (BackupAccountDto dto in dtos)
        {
            if (existingAliases.Contains(dto.Alias))
            {
                skippedCount++;
                continue;
            }

            using SecretValue cookie = SecretValue.FromString(dto.Cookie);
            AccountStatus status = Enum.IsDefined(typeof(AccountStatus), dto.Status)
                ? (AccountStatus)dto.Status
                : AccountStatus.Unknown;

            Account account = new(
                Id: new AccountId(nextId++),
                Alias: dto.Alias,
                Group: dto.Group,
                Note: dto.Note,
                Cookie: cookie,
                CreatedAt: dto.CreatedAt,
                UserId: dto.UserId,
                Username: dto.Username,
                Status: status,
                LastCheckedAt: dto.LastCheckedAt);

            Result addResult = await _accountStore.AddAsync(account, ct).ConfigureAwait(false);
            if (addResult.IsSuccess)
            {
                existingAliases.Add(dto.Alias);
                importedCount++;
            }
            else
            {
                skippedCount++;
            }
        }

        LogImportSuccess(importedCount, skippedCount);
        return new BackupImportResult(importedCount, skippedCount);
    }

    [LoggerMessage(EventId = 601, Level = LogLevel.Information, Message = "Exporting encrypted backup of {Count} accounts.")]
    private partial void LogExporting(int count);

    [LoggerMessage(EventId = 602, Level = LogLevel.Information, Message = "Successfully exported {Count} accounts to backup file.")]
    private partial void LogExportSuccess(int count);

    [LoggerMessage(EventId = 603, Level = LogLevel.Information, Message = "Imported {Imported} accounts from backup ({Skipped} duplicates skipped).")]
    private partial void LogImportSuccess(int imported, int skipped);

    internal sealed class BackupAccountDto
    {
        public long Id { get; set; }
        public string Alias { get; set; } = string.Empty;
        public string? Group { get; set; }
        public string? Note { get; set; }
        public string Cookie { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public long? UserId { get; set; }
        public string? Username { get; set; }
        public int Status { get; set; }
        public DateTimeOffset? LastCheckedAt { get; set; }
    }
}
