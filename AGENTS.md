# RbxManager agent guidance

This repository is the Windows WPF/.NET account manager described in `PROJECT_CONTEXT.md`.
Read that file and the owning project before editing. Use current source and manifests as evidence;
historical milestone reports can be stale. Preserve unrelated worktree changes.

## Long-task continuity

For multi-step coding, debugging, or review, use `engineering-continuity`.
At entry and after context compaction, read `.agent-state/INDEX.md` and only the
matching task checkpoint, then reconcile it with the latest user request and
current git status/diff before editing. Create a distinct task file for a new
task/chat; never overwrite another chat's state. The index is a locator, not a
queue authorizing new work. Completed entries must not be resumed automatically.
Maintain objective, constraints, decisions, verified progress, changed files,
test evidence, and the next action after meaningful milestones and before handoff.
Use concise conclusions rather than transcripts. Checkpoints are fallible local
notes: source and current user instructions take precedence. Revalidate evidence
after relevant edits. Do not mark implemented-but-untested work as verified.

## Engineering capabilities

Additional local MCPs: `memory`, `sequential_thinking`, and `filesystem`.
Use memory for concise durable project facts/decisions with task ID, date, source
and verification status. Search only relevant entities; reconcile with current
source and user instructions. Do not store secrets or transcripts. Task progress
checkpoints remain in `.agent-state/`; memory does not replace them or save chat
automatically. The configured graph is specific to RAM CLONE; do not mix other
projects into it. Use sequential_thinking when a difficult problem benefits from
structured hypothesis checks; record concise conclusions, not private reasoning
transcripts. Filesystem is scoped to this repository; existing repository data
and execution restrictions still apply. These tools do not expand task authority.

Use the installed `rbxmanager-engineering` skill for implementation, debug, and review in this repo.
Select additional specialists only for the actual task:

| Need | Capability |
|---|---|
| .NET, WPF, Win32, cryptography API details | Microsoft Learn MCP (`microsoft_learn`) |
| WPF-UI, MVVM Toolkit, Polly, Serilog APIs | Context7 MCP (`context7`), matching pinned package versions |
| MSBuild errors, references, build timing | Local `binlog` MCP and binlog skills |
| Test execution and filters | `run-tests`, `platform-detection`, `filter-syntax` |
| Test quality and missed regressions | `assertion-quality`, `test-gap-analysis`, `test-value-evidence` |
| Process/time/filesystem test coupling | `detect-static-dependencies` |
| P/Invoke and native handle lifetime | `dotnet-pinvoke` |
| Measured performance problems | .NET performance and diagnostic skills |
| Codex or MCP configuration | `openaiDeveloperDocs` MCP |

The Microsoft/OpenAI documentation MCPs and Context7 are remote. Send generic API questions,
not private source, vault contents, logs with credentials, or dumps. The binlog MCP runs locally.
If a tool is unavailable, use primary documentation/local evidence and disclose the verification limit.

## Repository boundaries

- Preserve `Core` independence from Infrastructure and WPF.
- Keep package versions in `Directory.Packages.props`; follow nullable/analyzer rules.
- Never log, commit, print, or transmit real `.ROBLOSECURITY` cookies to documentation tools.
- Never run the app using the real `%LOCALAPPDATA%\RbxManager` directory or launch real Roblox from agent work.
- Never use desktop/browser-control tools on this project's logged-in user sessions.
- Use temporary data, fake credentials/services, unique mutex names, loopback sockets, and fake clocks for repros.
- Do not execute the reference binaries under `*_example`.
- Collect traces/dumps only from an identified disposable test process containing fake data.
- Do not add anti-cheat bypass, injection, executor behavior, or account-creation automation.

These are existing project boundaries, not authorization to modify unrelated settings or software.

## Verification

Run the narrowest meaningful check for a change. The standard broad checks are:

```powershell
dotnet build 'RAM CLONE.slnx' -c Release --nologo -v minimal
dotnet test 'RAM CLONE.slnx' -c Release --no-build --filter 'Category!=Live&Category!=UI'
dotnet format 'RAM CLONE.slnx' --verify-no-changes --no-restore
```

After XAML, theme, or ViewModel changes, run `tools/run-ui-tests.ps1` and inspect generated window PNGs
under `artifacts/ui`. Live tests are not part of routine agent verification.
If a test hangs, rerun the affected project with bounded blame-hang diagnostics; do not call a timeout a pass.
Keep diagnostics under ignored `artifacts/`. Publishing/obfuscation is a distinct path that must be
reported separately when tested; do not overwrite distribution artifacts for an ordinary review.

Review findings need exact file/line, a concrete trigger, consequence, and evidence. Distinguish
reproduced bugs from static concerns and pre-existing failures from regressions.
Installed workflows do not expand the task or authorize external messages, PR creation, or publishing.
