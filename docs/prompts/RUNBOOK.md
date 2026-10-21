# RUNBOOK: finish the project end to end

ROLE: Implementation engineer. The Tech Lead (Claude) wrote these specs and will review ONLY after the whole project is done. There is no review between milestones, so the gates in each spec are the safety net. Do not skip or weaken them.

## ORDER
M1.1 -> M2 -> M3 -> M4 -> M5. Specs live in docs/prompts/. Read PROJECT_CONTEXT.md, ARCHITECTURE.md and SECURITY.md before starting.

## RESUME PROTOCOL
- State file: docs/STATE.md. Create it if missing: one line per milestone with status (pending | done | partial | blocked), commit hash, and the list of BLOCKED sub-features.
- At start: read STATE.md, skip milestones marked done, continue with the first one that is not.
- After each milestone: update STATE.md, commit.
- If your context or token budget is nearly exhausted: finish the current task, commit, update STATE.md, and stop. The user will rerun the same one-line command.

## AUTONOMY
- Do not ask the user anything. Do not wait for confirmation. Do not stop between milestones.
- If a sub-feature is blocked (cannot be verified, package incompatible, API behavior unknown): implement everything else, mark the sub-feature BLOCKED in STATE.md with the reason, continue.
- Hard stop only if a security rule below would be violated, or the working tree contains changes you did not make.

## TOKEN BUDGET
- No per-step narration. Do not print file contents. Do not re-read files you just wrote.
- Console output: at most 10 lines per milestone, at most 20 lines at the very end.
- Per-milestone report: docs/reports/<milestone>.md, concise (gate results, deviations, blocked items). No raw dumps unless a gate failed.

## GLOBAL RULES
- Stack: C# / .NET 10, WPF + WPF-UI, MVVM (CommunityToolkit.Mvvm), DI via Microsoft.Extensions.Hosting. Dependency direction: App.Wpf -> Infrastructure -> Core, and App.Wpf -> Core. Core never references WPF or Infrastructure.
- KISS/YAGNI. No TODO or placeholder comments, no dead code, no unused usings. Strict nullable. TreatWarningsAsErrors stays on. Central package management. Verify each new package supports net10.0 / net10.0-windows and record its version in the report.
- All user-visible text comes from Strings.resx / Strings.vi.resx (vi default). Add every new key to both files.
- Git: one commit per milestone, message "feat: <milestone> <summary>". Never commit secrets, vault files, logs, bin or obj. Never run anything as Administrator.
- Tests: never use a real cookie or ticket, only obviously fake strings. Tests never touch %LOCALAPPDATA%\RbxManager or any real user path; use unique temp directories that are deleted afterwards. No test makes a network call unless a spec explicitly allows it.

## SECURITY RULES (non-negotiable)
1. A Roblox cookie (.ROBLOSECURITY) and an auth ticket grant account access. Never log them, print them, persist them in plaintext, put them in exception messages, or include them in any URI that is logged.
2. SecretValue.Reveal() may be called only by the classes on the allowlist kept in ArchitectureTests. A new caller needs an allowlist entry and a one-line justification in the report.
3. Cookies are sent only over HTTPS to roblox.com or *.roblox.com. Enforced by a handler and covered by tests.
4. No telemetry, no cloud sync, no auto-update.
5. OUT OF SCOPE FOREVER: anti-cheat bypass; injecting into, patching, hooking, or duplicating handles in Roblox processes; executors; sharing rbx-player links; bulk account creation; captcha solving; automating in-game gameplay.

## ROBLOX FACTS POLICY
Endpoints, headers and URI formats in the specs are HYPOTHESES from the Tech Lead's memory and may be outdated. Verify them with fakes in tests and, only where a spec allows it, with ONE unauthenticated public request. Never guess silently: record every item you could not verify as UNVERIFIED in docs/api-notes.md. The human will verify those with a throwaway account.

## GATES (every milestone, before the commit)
1. dotnet build -c Release: 0 Warning(s), 0 Error(s).
2. dotnet test -c Release: all pass. Run it twice and confirm %LOCALAPPDATA%\RbxManager was not created or modified by the runs (compare timestamps).
3. From M2 on: ArchitectureTests and the log-leak tests pass.
4. Docs updated: PROJECT_CONTEXT.md (status), plus ARCHITECTURE.md and SECURITY.md when affected.
5. docs/reports/<milestone>.md written, docs/STATE.md updated, then commit.
