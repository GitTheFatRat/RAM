# Project Context: RbxManager

## Purpose
A Windows desktop application designed to manage multiple Roblox accounts with a default Vietnamese user interface (`vi-VN`). Built for personal use initially with a path to public release.

## Milestone Status
- **M0**: Scaffold, project structure, build pipeline & analyzers. (Completed)
- **M1**: Encrypted account vault (`vault.dat` v1) & account list UI with in-dialog editing/validation. (Completed)
- **M1.1**: Isolated tests from real `%LOCALAPPDATA%` vault path. (Completed)
- **M2**: Roblox authentication, WebView2 login, cookie validation, game launcher, security guard tests. (Completed)
- **M3**: Server browser, process monitor, running sessions, auto-rejoin. (Completed)
- **M4**: Settings, multi-instance gate, group management, encrypted backup. (Completed)
- **M5**: Hardening, packaging, and final documentation. (Completed)
- **M6**: Multi-instance real gate, Reveal() surface reduction, report reconciliation, review bundle. (Completed)
- **M7**: Diagnostics page, redacted report generator, customizable launch templates, code health pass. (Completed)

## Target & Stack
- **Runtime / Language**: C# 14 / .NET 10 (LTS).
- **Target Frameworks**:
  - `net10.0-windows` for `src/App.Wpf`.
  - `net10.0` for `src/Core`, `src/Infrastructure`, `tests/Core.Tests`, and `tests/Infrastructure.Tests`.
- **Platform**: Windows 10/11 x64 only.
- **UI & Pattern**: WPF, MVVM (via `CommunityToolkit.Mvvm`), Fluent Dark UI (via `WPF-UI`).
- **Dependency Injection & Hosting**: `Microsoft.Extensions.Hosting` (Generic Host).
- **Logging**: Serilog file logger rolling daily to `logs/app-.log`.
- **Package Management**: Central Package Management (`Directory.Packages.props`).

## Solution Layout (Root Namespace: `RbxManager`)
- **`src/Core/`**: Domain models (`AccountId`, `SecretValue`, `Account`), `AccountValidator`, `IAccountStore`, and `VaultCorruptedException`. Zero external dependencies.
- **`src/Infrastructure/`**: `EncryptedFileVault` persistence using DPAPI and AES-256-GCM. References `Core`.
- **`src/App.Wpf/`**: WPF shell, Fluent UI views, navigation, ViewModels, and DI container bootstrap. References `Core` and `Infrastructure`.
- **`tests/Core.Tests/`**: Unit tests verifying Core models and validation logic. References `Core`.
- **`tests/Infrastructure.Tests/`**: Integration tests verifying DPAPI wrapping, AES-GCM encryption round-trip, backup generation, and tamper detection. References `Core` and `Infrastructure`.

## Core Rules

### Security
1. `.ROBLOSECURITY` cookies grant full account control and must never be logged, printed, stored in plaintext, committed to Git, or sent to non-Roblox hosts.
2. No telemetry, cloud sync, or remote executable downloaders.
3. Anti-cheat bypass, executors, process injection, and bulk account creation are strictly out of scope.

### Code Quality
1. **KISS & YAGNI**: No speculative abstractions, dead code, or placeholder/TODO comments.
2. **Strict Nullable**: Nullable reference types enabled with warnings treated as errors.
3. **Clean Code**: Descriptive naming, file-scoped namespaces, and 4-space indentation.
