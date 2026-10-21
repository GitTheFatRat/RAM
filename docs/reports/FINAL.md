# RbxManager - Final Audit Report

This report summarizes the final engineering state, test coverage, dependency security audit, known limitations, and security allowlist for the RbxManager project.

---

## 1. Milestone Status Table

| Milestone | Status | Commit | Description | Blocked Sub-Features |
| :--- | :--- | :--- | :--- | :--- |
| **M0** | Done | `3857e86` | Solution scaffold, C# 14 / .NET 10, WPF-UI Dark Theme, i18n (`vi-VN` default, `en-US`), Serilog file sink | None |
| **M1** | Done | `08b5140` | Encrypted vault v1 (DPAPI + AES-256-GCM), Account list MVVM UI, Add/Edit/Delete dialogs | None |
| **M1.1** | Done | `8bcd3cb` | Zero-I/O test isolation preventing `%LOCALAPPDATA%\RbxManager` pollution | None |
| **M2** | Done | `11467e2` | WebView2 auth, cookie validation, URI protocol game launcher (`roblox-player:`), Polly resilience, architecture & log leak guards | Private Server launch URL syntax (unconfirmed from public documentation) |
| **M3** | Done | `34c71ba` | Public server browser (`/v1/games/{placeId}/servers/Public`), process monitor, hosted session tracking, auto-rejoin with exponential backoff & circuit breaker | None |
| **M4** | Done | `ca4d92a` | `AppSettings` & `SettingsService` (`settings.json`), group filter chips, editable group combo, bulk launch with cancellation, encrypted backup format v1 (PBKDF2-HMAC-SHA512 at 600,000 iters + AES-256-GCM) | Multi-instance real gate (undocumented Roblox mutex; gate safely abstracted behind interface, toggle disabled in production) |
| **M5** | Done | `PENDING` | Global exception handling, app single-instance mutex, log redaction enricher, keyboard shortcuts, i18n & XAML literal & secret quality guards, packaging (`tools/publish.ps1`), documentation (`README.vi.md`, `docs/MANUAL_TEST_CHECKLIST.md`, `docs/BEFORE_PUBLIC_RELEASE.md`) | None |

---

## 2. BLOCKED & UNVERIFIED Lists

### BLOCKED Items (Excluded from UI)
1. **Private Server Launch (M2)**:
   - *Reason*: Roblox documentation does not publicly publish the exact URI and PlaceLauncher query parameter contract for VIP / Private Server launch links (`accessCode` vs `linkCode`). Excluded from launch dialog to prevent failed launches or account flagging.
2. **Multi-Instance Real Gate (M4)**:
   - *Reason*: The exact Windows named mutex used by `RobloxPlayerBeta.exe` is undocumented and subject to client anti-cheat revisions. The application provides `INamedMutexInstanceGate` and `NamedMutexInstanceGate` with process checks, but keeps the live multi-instance toggle safely disabled with clear risk disclosures in Settings.

### UNVERIFIED Items (from `docs/api-notes.md`)
- `GET https://users.roblox.com/v1/users/authenticated`: Verified by mock handlers in tests; requires live throwaway account confirmation.
- `POST https://auth.roblox.com/v1/authentication-ticket`: Verified by mock handlers in tests; requires live throwaway account confirmation.
- `roblox-player:` launch URI protocol and `PlaceLauncher.ashx` query parameter format: Verified by mock handlers and architecture tests; requires live execution verification.

*(Note: `GET https://games.roblox.com/v1/games/{placeId}/servers/Public` was live-verified against public Place ID 1818).*

---

## 3. Test Totals per Project

- **`tests/Core.Tests`**: 42 passed, 0 failed, 0 skipped.
- **`tests/Infrastructure.Tests`**: 73 passed, 0 failed, 0 skipped.
- **Total Test Suite**: **115 tests passing** (0 failures, 0 warnings).
- **Test Isolation**: Verified `%LOCALAPPDATA%\RbxManager\vault.dat` timestamp remained untouched throughout all test executions.

---

## 4. Package List & Exact Versions

### Production Packages
- **`App.Wpf`** (`net10.0-windows`):
  - `CommunityToolkit.Mvvm`: `8.4.2`
  - `Microsoft.Extensions.DependencyInjection`: `10.0.13`
  - `Microsoft.Extensions.Hosting`: `10.0.13`
  - `Microsoft.Web.WebView2`: `1.0.4258.31`
  - `Serilog`: `4.4.0`
  - `Serilog.Sinks.File`: `7.0.0`
  - `WPF-UI`: `4.3.0`
- **`Infrastructure`** (`net10.0`):
  - `Microsoft.Extensions.Hosting`: `10.0.13`
  - `Microsoft.Extensions.Http`: `10.0.13`
  - `Microsoft.Extensions.Logging.Abstractions`: `10.0.13`
  - `Polly`: `8.8.0`
  - `Serilog`: `4.4.0`
  - `System.Security.Cryptography.ProtectedData`: `10.0.13`
- **`Core`** (`net10.0`):
  - Zero external package dependencies (pure .NET standard library).

### Test Packages
- `Microsoft.NET.Test.Sdk`: `17.14.1`
- `NSubstitute`: `6.2.0`
- `xunit`: `2.9.3`
- `xunit.runner.visualstudio`: `3.1.4`
- `coverlet.collector`: `6.0.4`

---

## 5. Vulnerability & Deprecation Audit
- `dotnet list package --vulnerable --include-transitive`: **0 vulnerabilities found**.
- `dotnet list package --deprecated`: **0 production packages deprecated**. (Legacy `xunit 2.9.3` in test projects flagged for upcoming v3 release, test-only).

---

## 6. Deviations from Specifications
- None. All architectural constraints, security boundaries, and milestone features conform strictly to `RUNBOOK.md` and `M0.md`-`M5.md`.

---

## 7. Known Limitations
1. **Cookie Rotation**: Roblox occasionally rotates `.ROBLOSECURITY` cookies upon browser navigation. RbxManager does not hook in-session cookie refresh tokens; if invalidated by user logout on web, the user must update the cookie or log in again via WebView2.
2. **Disconnect Detection**: Disconnections are detected purely via process exit of `RobloxPlayerBeta.exe` (clean, crash, or kill). In-game network freezes where the process remains open without network traffic are not detected.
3. **DPAPI Security Scope**: Windows DPAPI `CurrentUser` scope encrypts data per Windows account. It does not defend against malware or processes executing with the same user privileges. A Master Password is planned for public release.
4. **Roblox Policy & Multi-Instance**: Running multiple clients concurrently or holding multiple alt accounts may conflict with Roblox Terms of Use. The software provides explicit disclaimers and defaults multi-instance to off.
5. **Live Roblox Verification**: Production APIs are verified with comprehensive mock fixtures. End-to-end game launch should be verified with throwaway accounts using `docs/MANUAL_TEST_CHECKLIST.md`.

---

## 8. Allowlist of `SecretValue.Reveal()` Callers

Enforced continuously by `RbxManager.Core.Tests.Architecture.ArchitectureTests`:
1. `SecretValue.cs`: Core implementation of secret unwrapping.
2. `AccountValidator.cs`: Validation that cookie string is non-empty after trimming.
3. `EncryptedFileVault.cs`: Serialization of encrypted vault payload into AES-256-GCM ciphertext.
4. `RobloxAuthService.cs`: Construction of authenticated `.ROBLOSECURITY` cookie header for official Roblox HTTPS endpoints.
5. `RobloxLauncher.cs`: Inclusion of temporary single-use auth ticket in `roblox-player:` launch URI.
6. `AccountsViewModel.cs`: Account edit cloning.
7. `EncryptedBackupService.cs`: Serialization of backup export payload into PBKDF2/AES-GCM ciphertext.
