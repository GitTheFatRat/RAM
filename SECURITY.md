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

## Backup and Recovery (Format v1)

The live vault is bound to the current Windows user via Windows DPAPI (`DataProtectionScope.CurrentUser`). If the Windows user profile is lost, corrupted, reinstalled, or the user account is recreated, `vault.dat` cannot be decrypted and recovery is cryptographically impossible.

To eliminate this single point of failure without exposing plaintext credentials, RbxManager provides an encrypted export and backup mechanism (`.rbxbackup` format v1).

### Binary Layout (Format v1)
- `magic` (4 bytes ASCII: `RBXB`)
- `version` (1 byte: `0x01`)
- `salt` (16 bytes): cryptographically secure random bytes
- `iterations` (4 bytes `UInt32` Little-Endian): minimum 600,000 iterations (downgrade protected)
- `nonce` (12 bytes): cryptographically secure random bytes
- `tag` (16 bytes): AES-GCM authentication tag
- `ciphertext` (remaining bytes): AES-256-GCM encrypted UTF-8 JSON array of internal account DTOs
- Associated Authenticated Data (AAD): 25 bytes (`RBXB\x01` + 16-byte salt + 4-byte iterations)

### Cryptographic Parameters & Key Derivation
- **KDF**: `PBKDF2-HMAC-SHA512` via `Rfc2898DeriveBytes.Pbkdf2`.
- **Iteration Count**: Minimum and default 600,000 iterations.
- **Key Size**: 256-bit (32 bytes).
- **Cipher**: `AES-256-GCM` with an explicit 128-bit (16 bytes) authentication tag.
- **Password Enforcement**: Minimum 12 characters, entered twice upon export, buffers zeroed after derivation (`CryptographicOperations.ZeroMemory`).

### Restore Guidance & Downgrade Defense
- Tampered ciphertexts and invalid passwords both yield the identical typed generic `BackupException` to avoid oracle leakages.
- Iteration counts below 600,000 are rejected immediately upon import to protect against low-iteration downgrade attacks.
- Account merge policy skips existing aliases without overwriting existing vault accounts.

---

## Authentication and Launch Security (M2)

1. **Roblox Player Protocol Ticket Visibility**:
   - The game launch mechanism passes the single-use authentication ticket via the custom URI protocol handler (`roblox-player:...gameinfo:{ticket}...`).
   - Consequently, the ticket briefly appears in the process start arguments and command line of the invoked application, making it visible to other local processes running under the same user account with query privileges. This is an inherent design constraint of the official Roblox client URI launch protocol.

2. **Isolated WebView2 Login Profiles**:
   - When authenticating via the embedded browser, an ephemeral user data directory (`%TEMP%\RbxManager\login-{guid}`) is allocated.
   - Upon capturing the authentication cookie or dismissing the window, the temporary profile directory is recursively deleted to ensure session cookies and cached credentials do not persist unencrypted on disk.

3. **Cookie Rotation Handling**:
   - `Set-Cookie` headers returned by Roblox API endpoints are ignored (`UseCookies = false`). Handling automatic cookie rotation is intentionally deferred as a documented limitation.

---

## Server Browser and Process Monitoring Security (M3)

1. **Unauthenticated Public Server Queries**:
   - The server browser (`ServerBrowserService`) queries Roblox game server listings via unauthenticated GET requests to `https://games.roblox.com`. No cookies, authentication headers, or user secrets are ever attached to these requests.

2. **Process Management and Safe Rejoin**:
   - Process enumeration (`SystemProcessProvider`) inspects local running Roblox player processes using standard Windows process APIs.
   - Auto-rejoin re-validates the account's cookie prior to requesting an authentication ticket. If the cookie is expired, rejoining terminates immediately and the account status is updated to `Expired`.
   - Rejoin attempts are strictly bounded by `RejoinPolicy` with exponential backoff and a 5-attempt circuit breaker across a 30-minute window to avoid infinite crash loops or spamming Roblox endpoints.

---

## Hardening, Redaction & Quality Guards (M5)

1. **Global Exception Sanitization**:
   - `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, and `AppDomain.UnhandledException` log only the exception's type name and stack frame without raw message strings or request URLs, preventing secret leakage during runtime failures.
   - Non-fatal UI dialogs prevent recursive unhandled exception crash loops.

2. **Log Redaction Enricher**:
   - A custom Serilog enricher (`RobloxSecretRedactionEnricher`) inspects all scalar log properties.
   - Any property value containing `.ROBLOSECURITY=` or `_|WARNING:-DO-NOT-SHARE-THIS` is automatically masked to `[REDACTED]`.
   - Daily log file sink retains logs for at most 14 days.

3. **Application Single-Instance Mutex**:
   - RbxManager uses a named mutex (`Local\RbxManager_Application_SingleInstance_Mutex`) to ensure only one instance of the manager runs at a time.
   - Subsequent launches activate the existing window via Win32 `SetForegroundWindow` and terminate gracefully, preventing database race conditions.

4. **Automated Quality Guards**:
   - `SecretScanTests`: Scans all tracked source code and documentation to ensure no live Roblox secrets or warning prefixes with payloads exist in the codebase.
   - `I18nParityTests`: Validates 100% key and value parity across `Strings.resx` and `Strings.vi.resx`.
   - `XamlLiteralTests`: Prevents untranslated raw string literals in XAML presentation files.

---

## Multi-Instance Gate & Secret Lifecycle (M6)

1. **Multi-Instance Gate Boundaries**:
   - The multi-instance gate (`NamedMutexInstanceGate`) creates named synchronization objects (`ROBLOX_singletonMutex` and `ROBLOX_singletonEvent`) inside **its own process** on a dedicated background worker thread with proper thread affinity.
   - **What it does NOT do**: It does NOT inject into, patch, hook, or inspect the memory of Roblox client processes, nor does it close or duplicate handles belonging to `RobloxPlayerBeta.exe`.
   - It only operates when no Roblox processes are currently running. If named objects already exist, it aborts without holding partial handles.

2. **SecretValue Lifecycle & Transient Plaintext**:
   - In WPF user interfaces, capturing user input from a `PasswordBox` or retrieving a session cookie from WebView2 momentarily creates a transient managed `string` in memory.
   - The application minimizes this exposure window: the string is immediately wrapped into a `SecretValue` via `SecretValue.FromString(secret)`, and the UI input control is cleared immediately (`ClearPassword()`). The plaintext string is never stored in any ViewModel field, property, collection, or state object.
   - Calling `SecretValue.Reveal()` is prohibited in UI ViewModels and Validation layers (enforced by `ArchitectureTests`). Duplication is performed via memory-isolated `SecretValue.Clone()`, and empty checks are performed directly on byte buffers via `SecretValue.HasContent`.

---

## Diagnostics Report Sanitization (M7)

1. **Redacted by Design**:
   - The diagnostics report begins with the mandatory line: `Safe to share: redacted. No cookies or tickets.`
   - No cookies (`.ROBLOSECURITY`), auth tickets, or credential values are ever printed or captured in the report.
   - HTTP response bodies are never logged or exported verbatim; only HTTP status codes, exception types, and structured Roblox JSON error codes/messages (truncated to 120 characters) are included.
   - HTTP header values are completely excluded; only header names are reported.
   - Account launch verification renders the launch URI using the literal placeholder `<ticket>`, matching the Dry Run pattern.
   - The entire generated report is passed through `RobloxSecretRedactor` prior to UI display or clipboard copy.

2. **Network Diagnostics**:
   - Probing of public endpoints (`roblox.com`, `users`, `auth`, `games`, `presence`, `apis`) executes unauthenticated GET requests without cookies or authentication headers.

