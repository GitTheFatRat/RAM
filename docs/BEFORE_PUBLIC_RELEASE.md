# Pre-Release Checklist & Roadmap (BEFORE_PUBLIC_RELEASE.md)

This document establishes the mandatory security, legal, and engineering requirements that must be met before distributing RbxManager to the public.

---

## 1. Code Signing & Authenticode Certificate
- **Current State**: Binaries in `artifacts/RbxManager-win-x64.zip` are unsigned. Running on fresh Windows installations triggers Microsoft SmartScreen warnings.
- **Release Requirement**:
  - Acquire an EV (Extended Validation) or OV Code Signing Certificate from a reputable CA (DigiCert, Sectigo, etc.).
  - Integrate signing into the CI pipeline via Azure Key Vault or hardware security key (HSM).
  - Sign both `RbxManager.exe` and any embedded DLLs with valid timestamps (`http://timestamp.digicert.com`).

---

## 2. Windows Installer Architecture
- **Current State**: Single-file portable deployment inside a `.zip` archive.
- **Release Requirement**:
  - Provide an official MSIX package (preferred for Windows 10/11 sandbox security and auto-updates) or a clean WiX v4/v5 based installer.
  - Support Per-User installation (non-administrative) into `%LOCALAPPDATA%\Programs\RbxManager`.
  - Provide a clean uninstaller that prompts whether to retain or purge `%LOCALAPPDATA%\RbxManager` vaults and settings.

---

## 3. Antivirus & SmartScreen False-Positive Mitigation Plan
- **Risk Analysis**: Utilities interacting with games, managing cookies, or using named mutexes frequently encounter heuristics-based false positive detections from AV vendors (Windows Defender, Malwarebytes, CrowdStrike, etc.).
- **Action Plan**:
  - Code signing with EV certificate to establish immediate SmartScreen reputation.
  - Submit early release candidates to Microsoft Security Intelligence (WDSI) and VirusTotal for false-positive whitelisting prior to public release.
  - Maintain a transparent open-source build trail with reproducible GitHub Actions artifacts.

---

## 4. Vault Encryption: Master Password Integration
- **Current State**: `vault.dat` uses Windows DPAPI (`DataProtectionScope.CurrentUser`) wrapping an AES-256-GCM data key.
  - *Limitation*: Any process executed under the same Windows user session can call `CryptUnprotectData` to unwrap the key.
- **Release Requirement**:
  - Implement a mandatory or optional Master Password for opening the primary vault.
  - Key derivation: Utilize **Argon2id** (or PBKDF2-HMAC-SHA512 with >= 600,000 iterations, as already implemented in `EncryptedBackupService`).
  - Zero memory of master password buffers immediately after deriving the vault key.

---

## 5. Trademark, Name & Roblox Terms of Service Review
- **Name Assessment**: "RbxManager" uses the prefix "Rbx", which is widely recognized as referring to Roblox.
  - *Risk*: Roblox Corporation has strict trademark policies regarding the use of "Roblox" and derivatives in third-party products.
  - *Action*: Conduct a formal trademark assessment and consider neutral branding if necessary.
- **Roblox Terms of Service**:
  - Confirm that reading session cookies with user consent for launcher assistance remains compliant.
  - Ensure the application strictly adheres to the scope boundary: NO automation of gameplay, NO botting, NO anti-cheat bypass, NO modification of client memory.

---

## 6. Open Source Software (OSS) License Audit
- Verify licenses of all direct and transitive dependencies:
  - `WPF-UI` (Lepoco): MIT License.
  - `CommunityToolkit.Mvvm`: MIT License.
  - `Microsoft.Extensions.*`: MIT License.
  - `Serilog`, `Serilog.Sinks.File`: Apache 2.0.
  - `Microsoft.Web.WebView2`: Microsoft Software License / Redistributable.
  - `Polly`, `Polly.Core`: BSD-3-Clause / Apache 2.0.
- Ensure all third-party license notices are packaged alongside the release executable in a `THIRD_PARTY_LICENSES.txt` file.

---

## 7. Privacy Statement & Telemetry Policy
- Maintain a public, immutable Privacy Statement stating:
  - Zero telemetry, analytics, or background tracking.
  - Zero remote server connections other than direct HTTPS requests to official `*.roblox.com` endpoints.
  - Clear explanation of local credential storage under Windows DPAPI.

---

## 8. Third-Party Independent Security Audit
- Before wide public distribution, engage an independent application security auditor or conduct a peer-reviewed code audit focused on:
  - Memory residency of decrypted `SecretValue` strings.
  - Process isolation and handle leaking in `ProcessProvider`.
  - Resilience against malicious local file replacements in `%LOCALAPPDATA%`.
