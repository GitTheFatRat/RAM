# Project Context: RbxManager

## Purpose
A Windows desktop application designed to manage multiple Roblox accounts with a default Vietnamese user interface (`vi-VN`). Built for personal use initially with a path to public release.

## Target & Stack
- **Runtime / Language**: C# 14 / .NET 10 (LTS).
- **Target Frameworks**:
  - `net10.0-windows` for `src/App.Wpf`.
  - `net10.0` for `src/Core`, `src/Infrastructure`, and `tests/Core.Tests`.
- **Platform**: Windows 10/11 x64 only.
- **UI & Pattern**: WPF, MVVM (via `CommunityToolkit.Mvvm`), Fluent Dark UI (via `WPF-UI`).
- **Dependency Injection & Hosting**: `Microsoft.Extensions.Hosting` (Generic Host).
- **Logging**: Serilog file logger rolling daily to `logs/app-.log`.
- **Package Management**: Central Package Management (`Directory.Packages.props`).

## Solution Layout (Root Namespace: `RbxManager`)
- **`src/Core/`**: Domain models, value objects, and service contracts. Zero dependencies on UI or Infrastructure.
- **`src/Infrastructure/`**: Data persistence, file operations, secure credential stores. References `Core`.
- **`src/App.Wpf/`**: WPF shell, Fluent UI views, navigation, ViewModels, and DI container bootstrap. References `Core` and `Infrastructure`.
- **`tests/Core.Tests/`**: Unit tests verifying Core models and logic via xUnit and NSubstitute. References `Core`.

## Core Rules

### Security
1. `.ROBLOSECURITY` cookies grant full account control and must never be logged, printed, stored in plaintext, committed to Git, or sent to non-Roblox hosts.
2. No telemetry, cloud sync, or remote executable downloaders.
3. Anti-cheat bypass, executors, process injection, and bulk account creation are strictly out of scope.

### Code Quality
1. **KISS & YAGNI**: No speculative abstractions, dead code, or placeholder/TODO comments.
2. **Strict Nullable**: Nullable reference types enabled with warnings treated as errors.
3. **Clean Code**: Descriptive naming, file-scoped namespaces, and 4-space indentation.
