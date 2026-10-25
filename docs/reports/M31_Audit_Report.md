# M31 Audit Report: End-to-End System Audit & Real Data Verification

- **Date:** 2026-10-25 (UTC+7)
- **Commit audited:** `f49c5c6` (M30) plus this milestone's live-test additions
- **Host:** Windows 10 22H2 (`10.0.19045.0`), x64, process elevated (Administrator token)
- **Roblox state during audit:** 0 `RobloxPlayerBeta` processes running

## Summary

| # | Check | Result |
|---|-------|--------|
| 1 | `dotnet test` on the full solution | **PASS**: 313 passed, 0 failed, 0 skipped |
| 2a | Read a saved account from the real local vault | **NOT RUN**: blocked by standing project rule (see below) |
| 2b | Live API, anonymous (real network) | **PASS**: real status codes captured, all as expected |
| 2c | Live API, authenticated (cookie) | **NOT RUN**: no `RBX_LIVE_TEST_COOKIE` supplied; suite extended so it covers M31 when run |
| 3a | `NamedMutexInstanceGate` claims `Global\ROBLOX_singletonEvent` with WorldSid ACE | **PASS**: no `UnauthorizedAccessException` |
| 3b | `WindowSizeLock` WinEvent hooks on this Windows build | **PASS**: hooks installed, callback fired, size restored |

## 1. Full Test Suite

Command: `dotnet test "RAM CLONE.slnx"` (Debug). Raw console output:

```
Passed!  - Failed: 0, Passed:  88, Skipped: 0, Total:  88 - RbxManager.Core.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  53, Skipped: 0, Total:  53 - RbxManager.App.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 172, Skipped: 0, Total: 172 - RbxManager.Infrastructure.Tests.dll (net10.0)
No test matches the given testcase filter `Category!=UI`   in ...\App.UiTests.dll
No test matches the given testcase filter `Category!=Live` in ...\RbxManager.Infrastructure.LiveTests.dll
```

**Total: 313 passed / 0 failed.** `App.UiTests` and `Infrastructure.LiveTests` are excluded from the default run by design (opt-in categories).

Separately run during M30/M31:

- UI harness (`tools/run-ui-tests.ps1`, sandboxed fake data): **all scenarios PASS** in Dark and Light themes (artifacts: `artifacts/ui/20261025_104425`).
- Live suite (`--filter Category=Live`): `Passed: 4`. **These 4 passes are no-ops.** Every live test returns early when `RBX_LIVE_TEST_COOKIE` is unset, so they did not exercise Roblox. This is recorded so the number is not mistaken for live verification.

## 2. Live Data & API Verification

### 2a. Reading a saved account from the real vault: not executed

M31 asked to read the local data store and use a saved account's cookie (e.g. `RobloxACCOUNTTES11`). This was **intentionally not done**:

- `PROJECT_CONTEXT.md` standing rule: *"Never run the app against the real data directory (`%LOCALAPPDATA%\RbxManager`) from an agent session."*
- Security rule 1: `.ROBLOSECURITY` cookies must never be printed or logged. Decrypting `vault.dat` (DPAPI + AES-GCM, not a plain `database.json` / `settings.json`) inside an agent session would put a full-control credential into the agent's context and logs.

The sanctioned path for authenticated live checks is `tools/run-live-tests.ps1`, where **the user** supplies the cookie through `-Cookie` or `RBX_LIVE_TEST_COOKIE`. See section 2c.

### 2b. Anonymous live calls (real network, production handler chain)

Probe: a temporary console app in the agent scratch directory (not committed). It uses `RobloxHttpClientFactory.CreateClient()`, which is the same `HostGuard -> CSRF -> Retry` chain the app uses. No cookie was sent.

| Request | Status | Latency | Real response body |
|---------|--------|---------|--------------------|
| `GET users.roblox.com/v1/users/authenticated` | **401 Unauthorized** | 496 ms | `{"errors":[{"code":9002,"subcode":0,"message":"Authentication token is missing"}]}` |
| `POST auth.roblox.com/v1/authentication-ticket` | **401 Unauthorized** | 421 ms | `{"errors":[{"code":9002,"subcode":0,"message":"Authentication token is missing"}]}` |
| `POST users.roblox.com/v1/usernames/users` (`Roblox`) | **200 OK** | 262 ms | `{"data":[{"requestedUsername":"Roblox","hasVerifiedBadge":true,"id":1,"name":"Roblox","displayName":"Roblox"}]}` |
| `POST presence.roblox.com/v1/presence/users` (`userIds:[1]`) | **200 OK** | 398 ms | `{"userPresences":[{"userPresenceType":0,"lastLocation":"Website","placeId":null,"rootPlaceId":null,"gameId":null,"universeId":null,"userId":1}]}` |

What this shows:

- **Network path is healthy.** There were no timeouts, DNS or TLS failures, and no SNI blocking on this host at audit time. The latency range was 262 to 496 ms.
- **The cookie checks fail correctly without a cookie.** Without a cookie, both cookie validation and the auth-ticket request get `401` (code 9002) directly. Roblox did not issue a CSRF challenge (`csrfRetry=False`). `RobloxAuthService` maps 401 to `AuthErrorCodes.Expired`, so the app's handling of this case is consistent.
- **The M30 username lookup matches the live API.** The real response shape matches the `UsernamesResponse` DTO (`data[].id`).
- **The M30 presence lookup matches the live API.** The real response shape matches the `PresenceResponse` DTO (`userPresences[].placeId` / `gameId`). When the target is offline (`userPresenceType: 0`), `placeId` and `gameId` are `null`. `PresenceService` maps that to `PresenceErrorCodes.ServerHidden`, which the UI shows as "Không tìm thấy máy chủ. Mục tiêu đang offline hoặc tắt quyền Joins."
- **Presence answers anonymous requests.** Without a cookie the request still returns 200, but `gameId` is only filled in for viewers the target lets join. That is why M30 sends the following account's cookie.

### 2c. Authenticated live calls: pending user run

`tests/Infrastructure.LiveTests/RobloxLiveIntegrationTests.cs` now covers all of M31 task 2:

| Test | Covers |
|------|--------|
| `ValidateCookieAsync_WithRealCookie_ReturnsAuthenticatedRobloxUser` | Cookie validation via `users.roblox.com` (existing) |
| `GetAuthTicketAsync_WithRealCookie_ReturnsTicket` | **new**: CSRF handshake plus `rbx-authentication-ticket`. On failure it reports the HTTP status and whether a CSRF token was obtained |
| `FindUserServerAsync_WithOwnUsername_ReturnsServerOrHiddenNotTransportError` | **new**: username lookup plus authenticated presence for the cookie owner. Accepts a server result or `ServerHidden`, and fails on Network, Expired or Unexpected errors |
| `GetServersAsync_WithBloxFruitsPlaceId_...` | Public server browser (existing) |

To run it (the cookie stays in your own shell and never reaches the agent):

```powershell
.\tools\run-live-tests.ps1 -Cookie '<your test account cookie>'
```

## 3. System Environment Hook Check

### 3a. Instance gate: `Global\ROBLOX_singletonEvent`

```
Direct claim Global\ROBLOX_singletonEvent: OK (createdNew=True, WorldSid FullControl ACE present=True)
    SDDL: D:(A;;0x1f0001;;;WD)(A;;0x1f0001;;;SY)(A;;0x1f0001;;;BA)(A;;0x120001;;;S-1-5-5-0-333234)
Production gate TryEnable: IsSuccess=True Error=- IsMultiInstanceEnabled=True
Production gate Disable: IsMultiInstanceEnabled=False
```

- `NamedMutexInstanceGate.CreateLenientMutex` created the object in the `Global\` namespace. **No `UnauthorizedAccessException`** was thrown.
- The ACL check is independent of the gate code, which swallows ACL errors on purpose. It confirms that `WD` (Everyone / WorldSid) is granted `0x1F0001` (`MutexRights.FullControl`).
- The full production gate (`new NamedMutexInstanceGate(new SystemProcessProvider())`) enabled and disabled cleanly.
- **Caveat:** this host runs elevated, so `SeCreateGlobalPrivilege` is present. On a standard, non-elevated user, creating `Global\` objects may fail. The gate would then return `MutexError`; it does not crash. This audit did not verify the non-elevated case.

### 3b. `WindowSizeLock`: WinEvent hooks

Target: an off-screen WPF window owned by the probe process. No Roblox or other user window was touched.

```
Attach returned True; size after attach: 640x480 (expected 640x480)
Size immediately after external SetWindowPos(300x300): 300x300
Size after hook callback pump: 640x480 (expected 640x480)
```

- `SetWinEventHook` (out-of-context) succeeded on Windows 10 19045, so `Attach` returned `true`.
- The initial `EnforceSize` applied 640x480.
- An external resize was reverted by the `EVENT_OBJECT_LOCATIONCHANGE` callback. This proves the callbacks are delivered through the installing thread's message pump, as the class remarks require.
- **Real error found in the probe itself, not the product:** the first probe run threw `InvalidOperationException: The calling thread must be STA` at `System.Windows.Window..ctor()`. Cause: `async Task Main` resumes on a thread-pool (MTA) thread after `await`. Running the network part synchronously fixed it. The app is not affected, because `RobloxWindowSizer` attaches on the WPF UI thread. This does confirm the documented constraint that `Attach` needs a message-pumping STA thread.

## Findings & Recommendations

1. **No product defects found** in the audited modules.
2. **Authenticated live coverage is still open.** Run `tools/run-live-tests.ps1` with a test-account cookie to close task 2c.
3. **Live tests report "Passed" when skipped.** The early `return` makes a run without a cookie look green. Consider failing loudly or using `Assert.Skip` (xUnit v3), so CI and reports cannot mistake skipped for verified.
4. **Global namespace dependency.** Behaviour on a non-elevated account is unverified. If public release targets standard users, test the gate there; it falls back to `MutexError`, not a crash.
