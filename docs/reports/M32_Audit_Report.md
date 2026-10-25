# M32 / M32.1 Audit Report: Auto-Tile, Disconnection Recovery & Real-Environment Verification

- **Date:** 2026-10-25 (UTC+7)
- **Host:** Windows 10 22H2 (`10.0.19045.0`), x64, primary monitor 1920x1080 (work area 1920x1040)
- **Roblox state during audit:** 0 `RobloxPlayerBeta` processes (no live Roblox launch, per project rules)
- **Scope:** M32.1 supersedes M32 (same tasks plus the audit), so both are delivered in a single commit.

## Summary

| Check | Method | Result |
|-------|--------|--------|
| Full test suite | `dotnet test "RAM CLONE.slnx"` | **326 passed, 0 failed** (Core 93, App 53, Infrastructure 180) |
| Grid maths | `WindowGridLayoutTests` (5 tests) | **PASS**: no overlap across all 18 slots of a 200x400 grid |
| Real Win32 tile placement | Probe: 2 x `notepad.exe` moved by `User32.MoveResizeWindow` | **PASS**: exact grid coordinates, no overlap, at 200x400 and 400x300 |
| Log parsing on real logs | Production `RobloxLogWatcher` vs 10 real Roblox Player logs | **PASS**: 1 real disconnect detected, 9 clean logs silent, 10 of 10 agree with an independent scan |
| Disconnect leads to kill and relaunch | `RealLogWatcher_DisconnectLineInPidLog_WithAutoReconnect_KillsAndRelaunches` | **PASS** |
| Hang watchdog | `InstanceWatchdogServiceTests` (4 tests) | **PASS** |
| Settings UI | Sandboxed UI harness (`tools/run-ui-tests.ps1`) | **PASS** in Dark and Light; both new toggles appear in the UIA tree |

## 1. What was built

### Auto-Tile (`EnableAutoTile`, Settings → Performance card)

- **Grid maths:** `Core/Processes/WindowGridLayout` is pure and unit-tested. It computes a row-major grid from the saved custom width and height. `NextFreeSlot` reuses the lowest free slot, so closing a window frees its tile.
- **Placement:** `RobloxWindowSizer` assigns the next free slot and places the window with `User32.MoveResizeWindow`. This is the same `SetWindowPos` flags as the size lock, without `SWP_NOMOVE`.
- **Window recreation:** `WindowSizeLock` now remembers the tile. When Roblox recreates its main window (app shell to game, or a teleport), the new window is moved back to its tile instead of reappearing in the centre.
- **Screen bounds:** these come from `SPI_GETWORKAREA` (physical pixels), not `SystemParameters.WorkArea` (DIPs). `SetWindowPos` works in physical pixels, so DIPs would leave part of the grid unused at display scaling above 100%.
- **Depends on custom resolution:** the toggle is disabled unless "Custom window resolution" is on, because the grid needs a fixed window size.
- **More windows than slots:** the grid wraps to slot 0, so extra windows stack on existing tiles rather than going off-screen.

### Auto-Reconnect (`EnableAutoReconnect`, Settings → Auto-rejoin card)

| Signal | Detector | Interval |
|--------|----------|----------|
| Crash (`HasExited`) | `SessionTracker` poll (existing) | 2 s |
| Engine hang (`!Process.Responding`) | **new** `InstanceWatchdogService` | 15 s; recovers after 2 consecutive misses (~30 s); ignores the first 60 s of a session |
| Disconnect signature (`Connection lost`, `Error 277`, `Error 268`) | `RobloxLogWatcher` tailing the PID's own log (existing, M13) | live |

When a hang or a disconnect is detected, `ISessionTracker.RecoverSession` kills the process. The session is then relaunched after a cooldown of at least **20 s**, using the same account and the same `LaunchRequest` (PlaceId/JobId). The relaunch does not depend on the per-account auto-rejoin toggle. The existing circuit breaker (attempt limit within a 30-minute rolling window) still applies, so a permanently broken server cannot cause an endless relaunch loop.

Deliberate design decisions:

1. **No duplicate detection.** Crash detection and log tailing already existed (M12/M13). The new service only adds the missing hang detector, and all three signals feed one recovery path in `SessionTracker`. Two independent services killing and relaunching the same PID would race each other.
2. **An ordinary process exit is not treated as a network failure.** If you close a Roblox window yourself, that is indistinguishable from a crash, and Auto-Reconnect would otherwise bring it straight back. Plain exits therefore still follow the per-account auto-rejoin toggle. Auto-Reconnect relaunches only sessions it killed after a confirmed hang or disconnect. This is covered by `PlainExit_WithAutoReconnectButNoPerAccountToggle_DoesNotRelaunch`.
3. **Auto-Reconnect overrides "NotifyOnly".** With Auto-Reconnect on, a detected disconnect signature is treated as kill-and-relaunch, whatever the ghost-disconnect mode is set to.

### Pre-existing gap found and fixed

Launches from the **Accounts tab** (the main launch path) were never registered with `SessionTracker`. They had no crash, log or rejoin monitoring at all; only Servers-tab joins were tracked. `AccountsViewModel` now passes the matched PID to the new `ISessionTracker.AttachSession`. As a result:

- Accounts-tab sessions are now monitored by Auto-Reconnect.
- They now appear on the Running page.
- They are now visible to the background throttler and RAM cleaner, which work from the active session list.

Window sizing and tiling are now driven from a single `SessionStarted` subscription in `App.xaml.cs`. This covers manual launches, Servers-tab joins and auto-reconnect relaunches. Previously, relaunched windows were not resized at all.

## 2. Tile verification (real Win32, real processes)

Probe: two `notepad.exe` processes were started and their main windows retrieved through the production `User32.WaitForMainWindowAsync`. Each was assigned a slot with `WindowGridLayout.NextFreeSlot` / `GetSlot`, placed with `User32.MoveResizeWindow`, and read back with `GetWindowRect`. Both windows were closed afterwards. Raw output:

```
Primary work area (physical px): left=0 top=0 1920x1040; grid 9 cols x 2 rows
PID 16492: spawned at (299,100) 1436x759 -> slot 0 expected (0,0); SetWindowPos=True err=0; actual (0,0) 200x400
PID 22264: spawned at (299,100) 1436x759 -> slot 1 expected (200,0); SetWindowPos=True err=0; actual (200,0) 200x400
RESULT: overlap=False positionsMatchGrid=True sizesMatch=True

Primary work area (physical px): left=0 top=0 1920x1040; grid 4 cols x 3 rows
PID 9132: spawned at (299,100) 1436x759 -> slot 0 expected (0,0); SetWindowPos=True err=0; actual (0,0) 400x300
PID 21224: spawned at (299,100) 1436x759 -> slot 1 expected (400,0); SetWindowPos=True err=0; actual (400,0) 400x300
RESULT: overlap=False positionsMatchGrid=True sizesMatch=True
```

What this shows:

- **The problem is real.** Both windows spawned at the same position, (299,100), which is the "everything opens in the same spot" problem from the M32 brief.
- **The fix is pixel-exact.** After placement, the actual window rectangles match the computed grid exactly in position and size, with no overlap.
- **Cross-process `SetWindowPos` works on this Windows build.** It returned `True` with `err=0`.

The work area of 1920x1040 confirms that the 40 px taskbar is excluded.

**Not verified here:** Roblox itself. Roblox enforces an 800x600 minimum window size, which `SWP_NOSENDCHANGING` already bypasses (proven in M28.7). Notepad does not enforce that minimum, so this probe proves the placement and maths, not the Roblox-specific clamp.

## 3. Log parsing verification (real historical Roblox logs)

The production `RobloxLogWatcher` (default keywords) was pointed at every Player log in `%LOCALAPPDATA%\Roblox\logs`. An independent line scan counted the same keywords for comparison. Only file names, sizes and keyword counts were recorded; no log content was copied, apart from the redacted classification below.

```
Player log files: 10 (of 20 *.log total). Keywords: Connection lost | Error 277 | Error 268
..._20261024T152340Z_Player_14A18_last.log ( 6 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261024T153640Z_Player_E85E7_last.log (54 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261024T154324Z_Player_20B2A_last.log ( 1 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261024T154353Z_Player_D4F10_last.log (39 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261024T154459Z_Player_F0711_last.log (40 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261024T154622Z_Player_BCC64_last.log (37 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261024T154159Z_Player_2FF43_last.log ( 6 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261024T155940Z_Player_D9BA3_last.log (45 KB): watcher=TRIGGERED 'Connection lost'; scan=Connection lost:3 [agree]
..._20261024T160042Z_Player_3AFE5_last.log (24 KB): watcher=no trigger; scan=0/0/0 [agree]
..._20261025T102333Z_Player_DC8A1_last.log ( 5 KB): watcher=no trigger; scan=0/0/0 [agree]
RESULT: 1 files triggered recovery flag; independent scan found signatures in 1 files.
```

**The trigger is a true positive.** The three matching lines in `D9BA3` (digits masked) are at the end of the session, lines 361 to 363 of 370:

```
[FLog::Network] Connection lost
[FLog::Network] Connection lost: connectMode: Disconnect ASAP, timeMS:#####, ...
[FLog::Network] Connection lost: AckTimeout #, IsOutgoingDataWaiting #
```

`AckTimeout` is a genuine network drop: the server stopped acknowledging packets. This is exactly the packet-loss case M32 targets. The 9 clean logs produced no false triggers.

**Observation:** none of the 10 real logs contain the literal string `Error 277`. In this real sample, the network-level disconnect was logged only as `Connection lost: AckTimeout`. The `Connection lost` keyword is therefore the one doing the work, and removing it from the advanced keyword list would leave this case undetected.

**End-to-end flag test:** `RealLogWatcher_DisconnectLineInPidLog_WithAutoReconnect_KillsAndRelaunches` covers the full path:

1. A realistic Roblox log (real filename pattern and FLog line format, with an Error 277 / Connection lost tail) is written to a temporary logs folder.
2. The real `RobloxLogWatcher` binds it to the PID through the production creation-time match.
3. Ghost mode is set to `NotifyOnly`, with Auto-Reconnect on.
4. The test asserts that the watcher fires, that the PID is killed, and that exactly one relaunch is issued with the original request.

## 4. Test inventory added

| File | Tests |
|------|-------|
| `tests/Core.Tests/Processes/WindowGridLayoutTests.cs` | 5: non-overlap across all slots, wrap past capacity, work-area offset, window larger than screen, free-slot reuse |
| `tests/Infrastructure.Tests/Sessions/InstanceWatchdogServiceTests.cs` | 4: two strikes trigger recovery, a single hitch resets, startup grace, feature off means no probing |
| `tests/Infrastructure.Tests/Sessions/SessionTrackerTests.cs` | 4: recovery relaunches the same place and job without the per-account toggle, untracked PID is a no-op, a plain exit is not relaunched, real log watcher drives kill and relaunch |

## 5. Known limitations

- **Primary monitor only.** Auto-Tile uses the primary monitor's work area.
- **Tile slots are lost on app restart.** They are held in memory only. After a restart, already-running windows keep their positions, but new windows start again from slot 0.
- **The recovery relaunch blocks the session poll.** It runs inside `SessionTracker`'s poll loop (an existing design), so while one account waits out its 20 s cooldown, other exits are detected afterwards. When a network drop hits several accounts at once, they relaunch one after another, which also spaces out the launches.
- **The hang watchdog was not tested against a real frozen Roblox.** It relies on `Process.Responding` (whether the window's message pump responds); that call was exercised through the production `SystemProcessProvider`, but only unit-tested with a substitute for the frozen state.
