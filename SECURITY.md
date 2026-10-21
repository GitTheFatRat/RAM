# Security Policy

## Core Security Rules

1. **Roblox Cookie Protection**
   - A Roblox security cookie (`.ROBLOSECURITY`) grants complete account access.
   - It must **NEVER** be logged, printed to standard output/debugger, serialized in plaintext, committed to source control, or transmitted to any host other than official Roblox endpoints.

2. **Privacy and Telemetry**
   - No telemetry or tracking.
   - No cloud synchronization.
   - No automatic updates that download and execute arbitrary binaries.

3. **Strict Out-of-Scope Activities (Permanent)**
   - Anti-cheat bypasses or tampering.
   - Process injection or memory modification.
   - Script executors.
   - Roblox player deep link manipulation / link sharing.
   - Automated bulk account creation.

---

## Vault Storage Security (Format v1)

### Binary Layout
- `magic` (4 bytes ASCII: `RBXV`)
- `version` (1 byte: `0x01`)
- `wrappedKeyLength` (2 bytes unsigned integer, Little-Endian)
- `wrappedKey` (`wrappedKeyLength` bytes): 32-byte AES key wrapped with Windows DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`)
- `nonce` (12 bytes): cryptographically secure random bytes
- `tag` (16 bytes): AES-GCM authentication tag
- `ciphertext` (remaining bytes): AES-256-GCM encrypted JSON payload of internal account DTOs
- Associated Authenticated Data (AAD): 5 bytes (`RBXV\x01`)

### Threat Model
- **Protected Against**:
  - Offline extraction of storage drives / unauthenticated physical disk access.
  - Access by other user accounts on the same Windows machine.
  - Plaintext data leakage in logs or crash dumps via immediate zero-memory buffer wiping (`CryptographicOperations.ZeroMemory`).
  - Tampering or bit-flipping via AES-GCM AEAD authentication tag verification.

- **Explicit Limitation**:
  - Windows DPAPI (`DataProtectionScope.CurrentUser`) decrypts credentials using keys tied to the current Windows user login. It does **NOT** protect against malware, info-stealers, or malicious scripts executing within the same logged-in Windows user context.

- **Planned Roadmap**:
  - A user-supplied Master Password with modern memory-hard key derivation (Argon2id) is scheduled for the public release to provide defense-in-depth against same-user malware.

---

## Backup and Recovery

The vault is bound to the current Windows user via Windows DPAPI (`DataProtectionScope.CurrentUser`). If the Windows user profile is lost, corrupted, reinstalled, or the user account is recreated, `vault.dat` cannot be decrypted and recovery is cryptographically impossible.

An encrypted export and backup mechanism (protected by a master password) is required before relying on the vault for critical accounts or releasing the application publicly. This functionality is planned for a subsequent milestone and is not yet implemented.
