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
   - Account creation: RbxManager does not create Roblox accounts, does not schedule account creation, and will never incorporate features to create accounts or bypass account creation limits.
   - Anti-cheat bypasses or tampering.
   - Process injection or memory modification.
   - Script executors.
   - Roblox player deep link manipulation / link sharing.
   - Automated bulk account creation.

---

## Vault Storage Security (Format v2 & Format v1 Fallback)

### Binary Layout (Format v2)
- `magic` (4 bytes ASCII: `RBXV`)
- `version` (1 byte: `0x02`)
- `kdfType` (1 byte: `0x00` for DPAPI only, `0x01` for Argon2id + DPAPI)
- If `kdfType == 0x01`:
  - `memorySizeKb` (4 bytes `UInt32` Little-Endian): default 65,536 KB, minimum 65,536 KB (64 MiB) enforced on load
  - `iterations` (4 bytes `UInt32` Little-Endian): default 3, minimum 3 enforced on load
  - `parallelism` (4 bytes `UInt32` Little-Endian): default 4, minimum 1 enforced on load
  - `argonSalt` (16 bytes): cryptographically secure random bytes
  - Associated Authenticated Data (AAD): all 34 bytes of the prefix header (`RBXV\x02\x01` + memorySizeKb + iterations + parallelism + salt)
- If `kdfType == 0x00`:
  - Associated Authenticated Data (AAD): 6 bytes (`RBXV\x02\x00`)
- `wrappedKeyLength` (2 bytes unsigned integer, Little-Endian)
- `wrappedKey` (`wrappedKeyLength` bytes): 32-byte AES key wrapped with Windows DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`)
- `nonce` (12 bytes): cryptographically secure random bytes (fresh per save)
- `tag` (16 bytes): AES-GCM authentication tag
- `ciphertext` (remaining bytes): AES-256-GCM encrypted JSON payload of internal account DTOs

### Legacy Fallback & Automatic Migration Backup (Format v1)
- `magic` (`RBXV`) | `version` (`0x01`) | `wrappedKeyLength` (2B) | `wrappedKey` | `nonce` (12B) | `tag` (16B) | `ciphertext`.
- Loading Format v1 remains fully supported; any write or update automatically upgrades the storage layout to Format v2.
- **Automatic Migration Backup**: Prior to overwriting a Format v1 vault file with Format v2, the application automatically creates a backup copy at `vault.dat.v1.bak`. If that backup already exists, a timestamped copy (`vault.dat.v1.yyyyMMddHHmmssfff.bak`) is created so the user can always recover the pre-upgrade vault if needed.

### Cryptographic Parameters & Key Derivation (Argon2id)
- **KDF**: `Argon2id` via `Konscious.Security.Cryptography.Argon2` (version 1.3.1).
- **Memory Size**: 65,536 KB (64 MB). Parameter validation rejects any stored parameters below 65,536 KB.
- **Iterations**: 3. Parameter validation rejects any count below 3.
- **Degree of Parallelism**: 4 lanes / threads. Parameter validation rejects any count below 1.
- **Salt Length**: 16 bytes (fresh per save).
- **Output Key Size**: 256-bit (32 bytes).
- **Double-Layer Wrapping**: When master password protection is enabled, the 256-bit AES data key is derived from the master password and salt via Argon2id, and is additionally wrapped with Windows DPAPI (`CurrentUser`). Decryption requires both the correct master password and the matching Windows user account context. Wrong password attempts leave the vault and backup files completely untouched.
- **Irreversible Risk Acknowledgment**: Enabling a master password requires explicit user acknowledgment via a warning checkbox that lost master passwords cannot be recovered or reset by anyone.

### Threat Model & Defense-in-Depth
- **Protected Against**:
  - Offline extraction of storage drives / unauthenticated physical disk access.
  - Access by other user accounts on the same Windows machine.
  - Malware running as the same Windows user (when Master Password defense-in-depth is enabled via Argon2id).
  - Plaintext data leakage in logs or crash dumps via immediate zero-memory buffer wiping (`CryptographicOperations.ZeroMemory`).
  - Tampering or bit-flipping via AES-GCM AEAD authentication tag verification.

### Network Security & No Proxy Support
- RbxManager does not support custom proxy configurations per account or within the application.
- The Roblox game client connects directly over the host operating system network.
- The primary HTTP handler in RbxManager uses the default .NET behavior, which delegates to the Windows system proxy configuration if present.

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

## Hardening, Redaction & Quality Guards (M5, M9)

1. **Global Exception Sanitization**:
   - `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, and `AppDomain.UnhandledException` log only the exception's type name and stack frame without raw message strings or request URLs, preventing secret leakage during runtime failures.
   - Non-fatal UI dialogs prevent recursive unhandled exception crash loops.

2. **HTTP Hardening & Redirect Prevention**:
   - The primary `SocketsHttpHandler` enforces `AllowAutoRedirect = false` and `UseCookies = false` via `CreatePrimarySocketsHttpHandler()`. This guarantees that redirects cannot bypass the host guard handler, and sensitive `Cookie` headers are never automatically forwarded across cross-domain or unexpected redirects. Any 3xx response is treated as an unexpected status with its HTTP status code recorded.
   - Intermediate responses in retry loops (`RobloxRetryHandler`) are disposed before each retry attempt.

3. **Log Redaction Enricher & Recursive Sanitization**:
   - A custom Serilog enricher (`RobloxSecretRedactionEnricher`) inspects all log properties recursively, including scalar values, structured properties (`StructureValue`), sequence collections (`SequenceValue`), and dictionary entries (`DictionaryValue`).
   - Redaction patterns cover whole tokens: `_\|WARNING:-DO-NOT-SHARE-THIS\S*`, `\.ROBLOSECURITY=\S*`, and standalone secret tails `\|_[A-Za-z0-9]{16,}`. Any matching property is masked to `[REDACTED]`.
   - Daily log file sink retains logs for at most 14 days.

4. **Cookie Memory Hygiene**:
   - `EncryptedFileVault` operations (`AddAsync`, `UpdateAsync`, `RemoveAsync`) dispose every loaded account's cookie in `finally` blocks, preserving only caller-owned instances.
   - `RobloxLauncher` and store-querying services wrap account lists in `try ... finally` and dispose all loaded `SecretValue` instances immediately after obtaining single-use tickets, minimizing credential residency in process memory.

5. **Application Single-Instance Mutex**:
   - RbxManager uses a named mutex (`Local\RbxManager_Application_SingleInstance_Mutex`) to ensure only one instance of the manager runs at a time.
   - Subsequent launches activate the existing window via Win32 `SetForegroundWindow` and terminate gracefully, preventing database race conditions.

6. **Automated Quality Guards**:
   - `SecretScanTests`: Scans all tracked source code and documentation to ensure no live Roblox secrets or warning prefixes with payloads exist in the codebase.
   - `NoHardCodedVietnameseInCoreOrInfraTests`: Verifies that Core and Infrastructure layers return error codes only and contain zero Vietnamese UI language literals.
   - `IgnoredSourceFilesTests`: Verifies that git does not ignore any required source, markup, or test files.
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

## Diagnostics Report Sanitization (M7, M9)

1. **Redacted by Design**:
   - The diagnostics report begins with the mandatory line: `Safe to share: redacted. No cookies or tickets.`
   - No cookies (`.ROBLOSECURITY`), auth tickets, or credential values are ever printed or captured in the report.
   - Comprehensive pattern redaction covers the entire credential token (`_\|WARNING:-DO-NOT-SHARE-THIS\S*`, `\.ROBLOSECURITY=\S*`, and `\|_[A-Za-z0-9]{16,}`).
   - Local Windows usernames in filesystem paths are automatically masked to `C:\Users\<user>\...` across all report sections and protocol handler commands.
   - HTTP response bodies are never logged or exported verbatim; only HTTP status codes, exception types, and structured Roblox JSON error codes/messages (truncated to 120 characters and sanitized to single-line text) are included.
   - Detailed failure diagnosis records exact structured diagnostic fields: `Stage` ("Validate" or "Ticket"), `HttpStatus`, `RobloxErrorCodes`, truncated `RobloxMessage`, `CsrfTokenObtained`, `TicketHeaderPresent`, and `ResponseHeaderNames`.
   - HTTP header values are completely excluded; only header names are reported.
   - Account launch verification renders the launch URI using the literal placeholder `<ticket>`, matching the Dry Run pattern.
   - The entire generated report is sanitized via `SanitizeReport` prior to UI display or clipboard copy.

2. **Network Diagnostics**:
   - Probing of public endpoints (`roblox.com`, `users`, `auth`, `games`, `presence`, `apis`) executes unauthenticated GET requests without cookies or authentication headers.

---

## UI Test Sandbox Security (M16)

1. **Strict Process & Isolation Boundaries**:
   - The UI automation test harness (`App.UiTests`) interacts exclusively with the specific application instance process it launches.
   - Screen capture is strictly confined to the application window using the Win32 `PrintWindow` API with `PW_RENDERFULLCONTENT` (0x02). The harness never captures the full desktop or any other application window, ensuring no ambient user data (passwords, personal workspaces, browser sessions) is ever captured or exposed.
   - UI Automation tree inspection dumps omit text content for sensitive fields (`ui:PasswordBox` and text inputs), recording only presence (`hasTextValue: true`) and structural metadata.

2. **Throwaway Sandbox Directory & Environment Isolation**:
   - In Debug builds, `RBXMANAGER_DATA_DIR` allows pointing storage paths (vault, settings, logs) to a unique temporary directory created and destroyed per test run.
   - The application enforces a strict safety check: if `RBXMANAGER_UITEST=1` is provided, the application verifies `RBXMANAGER_DATA_DIR` is set and does **NOT** resolve within the real user directory `%LOCALAPPDATA%\RbxManager`. Any violation immediately aborts execution.
   - A dedicated test mutex (`Local\RbxManager_Application_UiTest_Mutex`) prevents collision with any real running RbxManager instance.

3. **Offline Mock Pipeline**:
   - When running under `RBXMANAGER_UITEST=1`, all real networking and execution services are replaced with offline DI fakes (`FakeRobloxAuthService`, `FakeServerBrowserService`, `FakeLauncher`, `FakeProcessProvider`, `FakeDiagnosticsService`, `FakeInstanceGate`).
   - Canned test data uses synthetic cookie tokens (`FAKE_UI_COOKIE_*`) that do not trigger secret scanners or leak credentials.
   - Zero real network calls to Roblox servers or local process launches (`RobloxPlayerBeta.exe`) can occur during UI tests.

4. **Release Build Safety Enforcement**:
   - All test hooks, fake services, and environment variable overrides (`RBXMANAGER_DATA_DIR`, `RBXMANAGER_UITEST`) are wrapped strictly within `#if DEBUG` preprocessor directives.
   - In Release builds, these environment variables and hooks are completely omitted by the compiler.
   - Verified by automated reflection tests (`ReleaseConfigurationSafetyTests`) that assert Release binaries contain zero test hook types or interfaces.

---

## UAT Live Trace Telemetry & Secret Masking (M19)

1. **Opt-In Local Trace Logging (`EnableUatTelemetry`)**:
   - Disabled by default (`EnableUatTelemetry = false`).
   - When explicitly enabled by the user in Settings -> Advanced (`"Bật chế độ ghi log thực chiến ra Desktop"`), `UatLogger` writes timestamped execution traces to `Desktop\RbxManager_UAT_Trace.txt` for local manual testing verification.
2. **Mandatory Ticket & Cookie Masking**:
   - Before writing any trace line (`WindowsProcessStarter.Start`, `WindowController`, `WindowsMemoryOptimizer`, `SessionTracker`), `UatLogger.MaskSecrets` scrubs all `.ROBLOSECURITY` cookies and single-use `gameinfo:` authentication tickets with `***`, ensuring credentials never appear in `RbxManager_UAT_Trace.txt`.

---

## External GoodbyeDPI Orchestrator Safety (M21)

1. **Zero Bundled Kernel Drivers**:
   - To avoid Hyperion anti-cheat bans and false-positive antivirus detections, RbxManager never bundles `WinDivert` or `GoodbyeDPI` inside its application binaries.
2. **User-Initiated External Tool Provisioning**:
   - Only when the user explicitly clicks `"Cài đặt & Bật GoodbyeDPI"` in `AddAccountWindow`, `DpiToolService` downloads the official `goodbyedpi-0.2.3.zip` archive from GitHub (`https://github.com/ValdikSS/GoodbyeDPI/releases/download/0.2.3/goodbyedpi-0.2.3.zip`) into `%LOCALAPPDATA%\RbxManager\Tools\GoodbyeDPI` and invokes `1_russia_blacklist.cmd` externally via standard Windows UAC (`UseShellExecute = true`, `Verb = "runas"`).





