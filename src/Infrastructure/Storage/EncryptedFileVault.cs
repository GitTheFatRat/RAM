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
using RbxManager.Infrastructure.Serialization;

[SupportedOSPlatform("windows")]
public sealed partial class EncryptedFileVault : IAccountStore, IDisposable
{
    private static readonly byte[] MagicBytes = [0x52, 0x42, 0x58, 0x56]; // "RBXV"
    private const byte Version1Byte = 0x01;
    private const byte Version2Byte = 0x02;
    private const byte KdfTypeDpapi = 0x00;
    // Legacy: the Argon2 output was the data key and was itself DPAPI-wrapped (password bypassable). Load-only.
    private const byte KdfTypeArgon2idDpapi = 0x01;
    private const byte KdfTypeArgon2idKeyWrap = 0x02;
    private const int Argon2HeaderLength = 34;
    private const int KeyEnvelopeLength = 12 + 16 + DerivedKeyLength;
    private const string CredentialFailureDataKey = "RbxManager.Vault.CredentialFailure";

    public const int Argon2MemorySizeKb = 65536; // 64 MB
    public const int Argon2Iterations = 3;
    public const int Argon2Parallelism = 4;
    public const int Argon2SaltLength = 16;
    public const int DerivedKeyLength = 32;

    public const int MinimumArgon2MemorySizeKb = 65536; // 64 MiB
    public const int MaximumArgon2MemorySizeKb = 1_048_576; // 1 GiB
    public const int MinimumArgon2Iterations = 3;
    public const int MaximumArgon2Iterations = 100;
    public const int MinimumArgon2Parallelism = 1;
    public const int MaximumArgon2Parallelism = 64;

    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RbxManager",
        "vault.dat");

    private readonly string _filePath;
    private readonly IVaultKeyProvider? _keyProvider;
    private readonly ILogger<EncryptedFileVault>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;
    private bool _recoveredFromBackup;
    private bool _legacyBackupsPurged;

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
                }
            }
            _semaphore.Release();
        }
    }

    public async Task ReencryptAsync(SecretValue? newKey, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        List<Account>? loadedAccounts = null;
        try
        {
            loadedAccounts = await LoadInternalAsync(ct).ConfigureAwait(false);
            if (_keyProvider != null)
            {
                if (newKey != null && newKey.HasContent)
                {
                    _keyProvider.SetKey(newKey.Clone());
                }
                else
                {
                    _keyProvider.Clear();
                }
            }

            await SaveInternalAsync(loadedAccounts, ct).ConfigureAwait(false);

            // Backups still carry the previous protection level (e.g. no password / old password).
            List<string> staleBackups = GetLegacyV1BackupPaths();
            staleBackups.Add(_filePath + ".bak");
            SecureDeleteFiles(staleBackups);
        }
        finally
        {
            if (loadedAccounts != null)
            {
                foreach (Account loaded in loadedAccounts)
                {
                    loaded.Cookie.Dispose();
                }
            }
            _semaphore.Release();
        }
    }

    public static byte[] DeriveKey(
        SecretValue password,
        byte[] salt,
        int memorySizeKb = Argon2MemorySizeKb,
        int iterations = Argon2Iterations,
        int parallelism = Argon2Parallelism)
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
                DegreeOfParallelism = parallelism,
                MemorySize = memorySizeKb,
                Iterations = iterations
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

        List<Account> accounts;
        try
        {
            accounts = DecodeVault(fileBytes);
        }
        catch (VaultCorruptedException primaryFailure) when (!IsCredentialFailure(primaryFailure))
        {
            List<Account>? recovered = await TryLoadBackupAsync(ct).ConfigureAwait(false);
            if (recovered == null)
            {
                throw;
            }

            _recoveredFromBackup = true;
            if (_logger != null)
            {
                LogRecoveredFromBackup(_logger, primaryFailure.Message);
            }

            return recovered;
        }

        _recoveredFromBackup = false;

        // v1 upgrade backups are kept only until the upgraded (v2+) primary has been proven readable.
        if (fileBytes[4] != Version1Byte && !_legacyBackupsPurged)
        {
            SecureDeleteFiles(GetLegacyV1BackupPaths());
            _legacyBackupsPurged = true;
        }

        return accounts;
    }

    private async Task<List<Account>?> TryLoadBackupAsync(CancellationToken ct)
    {
        string bakPath = _filePath + ".bak";
        if (!File.Exists(bakPath))
        {
            return null;
        }

        try
        {
            byte[] backupBytes = await File.ReadAllBytesAsync(bakPath, ct).ConfigureAwait(false);
            return DecodeVault(backupBytes);
        }
        catch (VaultCorruptedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private List<Account> DecodeVault(byte[] fileBytes)
    {
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
            dtos = JsonSerializer.Deserialize(plaintextBytes, RbxManagerJsonContext.Default.ListAccountDto);
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
        else if (kdfType == KdfTypeArgon2idDpapi || kdfType == KdfTypeArgon2idKeyWrap)
        {
            // Magic (4) + Version (1) + KdfType (1) + Memory (4) + Iterations (4) + Parallelism (4) + Salt (16) = 34 bytes
            // + WrappedKeyLength (2 bytes) = 36 bytes minimum before wrapped key
            if (fileBytes.Length < 36)
            {
                throw new VaultCorruptedException("Vault file is too short to contain a valid Argon2 header.");
            }

            uint memorySizeKb = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.AsSpan(6, 4));
            uint iterations = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.AsSpan(10, 4));
            uint parallelism = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.AsSpan(14, 4));

            if (memorySizeKb < MinimumArgon2MemorySizeKb)
            {
                throw new VaultCorruptedException($"Vault Argon2 memory size {memorySizeKb} KB is below minimum {MinimumArgon2MemorySizeKb} KB.");
            }

            if (memorySizeKb > MaximumArgon2MemorySizeKb)
            {
                throw new VaultCorruptedException($"Vault Argon2 memory size {memorySizeKb} KB exceeds maximum {MaximumArgon2MemorySizeKb} KB.");
            }

            if (iterations < MinimumArgon2Iterations)
            {
                throw new VaultCorruptedException($"Vault Argon2 iterations {iterations} is below minimum {MinimumArgon2Iterations}.");
            }

            if (iterations > MaximumArgon2Iterations)
            {
                throw new VaultCorruptedException($"Vault Argon2 iterations {iterations} exceeds maximum {MaximumArgon2Iterations}.");
            }

            if (parallelism < MinimumArgon2Parallelism)
            {
                throw new VaultCorruptedException($"Vault Argon2 parallelism {parallelism} is below minimum {MinimumArgon2Parallelism}.");
            }

            if (parallelism > MaximumArgon2Parallelism)
            {
                throw new VaultCorruptedException($"Vault Argon2 parallelism {parallelism} exceeds maximum {MaximumArgon2Parallelism}.");
            }

            byte[] argonSalt = fileBytes.AsSpan(18, Argon2SaltLength).ToArray();
            ushort wrappedKeyLength = BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.AsSpan(34, 2));

            if (36 + wrappedKeyLength + 12 + 16 > fileBytes.Length)
            {
                throw new VaultCorruptedException("Vault file is truncated or corrupted.");
            }

            int offset = 36;
            byte[] wrappedKey = fileBytes.AsSpan(offset, wrappedKeyLength).ToArray();
            offset += wrappedKeyLength;

            ReadOnlySpan<byte> nonce = fileBytes.AsSpan(offset, 12);
            offset += 12;

            ReadOnlySpan<byte> tag = fileBytes.AsSpan(offset, 16);
            offset += 16;

            ReadOnlySpan<byte> ciphertext = fileBytes.AsSpan(offset);
            byte[] aad = fileBytes.AsSpan(0, Argon2HeaderLength).ToArray(); // Authenticate all 34 header prefix bytes

            if (_keyProvider == null || !_keyProvider.HasKey)
            {
                throw CredentialFailure("Master password is required to unlock this vault.");
            }

            SecretValue? masterPassword = _keyProvider.GetKey();
            if (masterPassword == null || !masterPassword.HasContent)
            {
                throw CredentialFailure("Master password is required to unlock this vault.");
            }

            byte[] dpapiUnwrapped;
            try
            {
                dpapiUnwrapped = ProtectedData.Unprotect(wrappedKey, null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                throw new VaultCorruptedException("Failed to unwrap encryption key via DPAPI.", ex);
            }

            byte[] passwordKey = DeriveKey(masterPassword, argonSalt, (int)memorySizeKb, (int)iterations, (int)parallelism);
            byte[] dataKey;
            try
            {
                dataKey = kdfType == KdfTypeArgon2idKeyWrap
                    ? UnwrapDataKey(dpapiUnwrapped, passwordKey, aad)
                    : VerifyLegacyArgonKey(dpapiUnwrapped, passwordKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dpapiUnwrapped);
                CryptographicOperations.ZeroMemory(passwordKey);
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
        else
        {
            throw new VaultCorruptedException($"Unsupported vault KDF type '{kdfType}'.");
        }
    }

    private static byte[] UnwrapDataKey(byte[] keyEnvelope, byte[] passwordKey, byte[] aad)
    {
        if (keyEnvelope.Length != KeyEnvelopeLength)
        {
            throw new VaultCorruptedException("Vault key envelope has an invalid length.");
        }

        ReadOnlySpan<byte> wrapNonce = keyEnvelope.AsSpan(0, 12);
        ReadOnlySpan<byte> wrapTag = keyEnvelope.AsSpan(12, 16);
        ReadOnlySpan<byte> wrappedDataKey = keyEnvelope.AsSpan(28, DerivedKeyLength);
        byte[] dataKey = new byte[DerivedKeyLength];
        try
        {
            using AesGcm keyWrap = new(passwordKey, 16);
            keyWrap.Decrypt(wrapNonce, wrappedDataKey, wrapTag, dataKey, aad);
            return dataKey;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw CredentialFailure("Invalid master password.");
        }
    }

    private static byte[] VerifyLegacyArgonKey(byte[] dpapiUnwrapped, byte[] passwordKey)
    {
        if (!CryptographicOperations.FixedTimeEquals(dpapiUnwrapped, passwordKey))
        {
            throw CredentialFailure("Invalid master password.");
        }

        return (byte[])passwordKey.Clone();
    }

    private static VaultCorruptedException CredentialFailure(string message)
    {
        VaultCorruptedException exception = new(message);
        exception.Data[CredentialFailureDataKey] = true;
        return exception;
    }

    internal static bool IsCredentialFailure(VaultCorruptedException exception) =>
        exception.Data[CredentialFailureDataKey] is true;

    private async Task SaveInternalAsync(List<Account> accounts, CancellationToken ct = default)
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

        byte[] plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(dtos, RbxManagerJsonContext.Default.ListAccountDto);

        bool useArgon2 = _keyProvider != null && _keyProvider.HasKey;
        byte kdfType = useArgon2 ? KdfTypeArgon2idKeyWrap : KdfTypeDpapi;

        byte[] dataKey = RandomNumberGenerator.GetBytes(DerivedKeyLength);
        byte[] aad;
        byte[] keyMaterialToProtect;

        if (useArgon2)
        {
            byte[] argonSalt = RandomNumberGenerator.GetBytes(Argon2SaltLength);
            SecretValue secret = _keyProvider!.GetKey()!;

            // AAD authenticates Magic (4) + Version (1) + KdfType (1) + Memory (4) + Iterations (4) + Parallelism (4) + Salt (16) = 34 bytes
            aad = new byte[Argon2HeaderLength];
            MagicBytes.CopyTo(aad.AsSpan(0, 4));
            aad[4] = Version2Byte;
            aad[5] = kdfType;
            BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(6, 4), (uint)Argon2MemorySizeKb);
            BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(10, 4), (uint)Argon2Iterations);
            BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(14, 4), (uint)Argon2Parallelism);
            argonSalt.CopyTo(aad.AsSpan(18, Argon2SaltLength));

            byte[] passwordKey = DeriveKey(secret, argonSalt, Argon2MemorySizeKb, Argon2Iterations, Argon2Parallelism);
            try
            {
                keyMaterialToProtect = WrapDataKey(dataKey, passwordKey, aad);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordKey);
            }
        }
        else
        {
            aad = [MagicBytes[0], MagicBytes[1], MagicBytes[2], MagicBytes[3], Version2Byte, kdfType];
            keyMaterialToProtect = dataKey;
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

        // In Argon2 mode DPAPI only adds user binding on top of the password-wrapped envelope.
        byte[] wrappedKey = ProtectedData.Protect(keyMaterialToProtect, null, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(keyMaterialToProtect);
        CryptographicOperations.ZeroMemory(dataKey);

        if (wrappedKey.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("Wrapped key size exceeds maximum supported length.");
        }

        ushort wrappedKeyLength = (ushort)wrappedKey.Length;
        int headerPrefixLength = aad.Length;
        int totalLength = headerPrefixLength + 2 + wrappedKey.Length + 12 + 16 + ciphertext.Length;
        byte[] outputBuffer = new byte[totalLength];

        aad.CopyTo(outputBuffer.AsSpan(0, headerPrefixLength));
        int currentOffset = headerPrefixLength;

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
            if (_recoveredFromBackup)
            {
                // The primary is known-corrupted: replace it without rotating it over the good .bak.
                File.Move(tmpPath, _filePath, overwrite: true);
            }
            else
            {
                if (BackupV1VaultIfUpgrading(_filePath))
                {
                    _legacyBackupsPurged = false;
                }

                File.Replace(tmpPath, _filePath, bakPath);
            }
        }
        else
        {
            File.Move(tmpPath, _filePath);
        }

        _recoveredFromBackup = false;
    }

    private static byte[] WrapDataKey(byte[] dataKey, byte[] passwordKey, byte[] aad)
    {
        byte[] keyEnvelope = new byte[KeyEnvelopeLength];
        RandomNumberGenerator.Fill(keyEnvelope.AsSpan(0, 12));
        using AesGcm keyWrap = new(passwordKey, 16);
        keyWrap.Encrypt(
            keyEnvelope.AsSpan(0, 12),
            dataKey,
            keyEnvelope.AsSpan(28, DerivedKeyLength),
            keyEnvelope.AsSpan(12, 16),
            aad);
        return keyEnvelope;
    }

    private List<string> GetLegacyV1BackupPaths()
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return new List<string>();
        }

        string fileName = Path.GetFileName(_filePath);
        return Directory.GetFiles(directory, fileName + ".v1*.bak").ToList();
    }

    private void SecureDeleteFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using (FileStream fs = new(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    byte[] zeros = new byte[fs.Length];
                    fs.Write(zeros, 0, zeros.Length);
                    fs.Flush(true);
                }

                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_logger != null)
                {
                    LogBackupDeletionFailed(_logger, Path.GetFileName(path), ex.GetType().Name);
                }
            }
        }
    }

    private static bool BackupV1VaultIfUpgrading(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            byte[] header = new byte[5];
            using (FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read = fs.Read(header, 0, header.Length);
                if (read < 5)
                {
                    return false;
                }
            }

            if (header[0] == MagicBytes[0] && header[1] == MagicBytes[1] &&
                header[2] == MagicBytes[2] && header[3] == MagicBytes[3] &&
                header[4] == Version1Byte)
            {
                string v1Bak = filePath + ".v1.bak";
                if (!File.Exists(v1Bak))
                {
                    File.Copy(filePath, v1Bak, overwrite: false);
                }
                else
                {
                    string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
                    string tsBak = $"{filePath}.v1.{timestamp}.bak";
                    File.Copy(filePath, tsBak, overwrite: false);
                }

                return true;
            }

            return false;
        }
        catch
        {
            // Do not fail upgrade if backup operation cannot read existing file
            return false;
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

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Primary vault file could not be read ({Reason}); recovered accounts from backup. The next save rewrites the primary file.")]
    private static partial void LogRecoveredFromBackup(ILogger logger, string reason);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Could not securely delete stale vault backup {FileName} ({ErrorType}).")]
    private static partial void LogBackupDeletionFailed(ILogger logger, string fileName, string errorType);
}
