# Codex tooling setup — 2026-10-30

## Additional requested MCP servers

Installed official packages from https://github.com/modelcontextprotocol/servers
under `C:\Users\Administrator\.codex\tools\engineering-mcp`, with exact versions
and an npm lockfile. They run directly with the existing Node 24 executable.

| MCP | Package version | Validation |
|---|---|---|
| memory | @modelcontextprotocol/server-memory 2.0.1 | 9 tools; synthetic entity survived server restart, then was deleted |
| sequential_thinking | @modelcontextprotocol/server-sequential-thinking 2.0.0 | 1 tool; synthetic call succeeded |
| filesystem | @modelcontextprotocol/server-filesystem 2.1.0 | 14 tools; allowed directory and AGENTS.md read verified |

Memory file: `C:\Users\Administrator\.codex\memory\ram-clone\memory.jsonl`.
Filesystem allowed directory: `C:\Users\Administrator\Desktop\RAM CLONE`.
Sequential Thinking has `DISABLE_THOUGHT_LOGGING=true`.
All three are enabled in shared Codex config, bringing this setup to seven MCPs.
Configuration backup is under `.codex/backups/mcp-additions`.
Validation evidence: `artifacts/mcp-additions/validation.json`.

Open a new CLI session to load them. Memory persists explicit tool writes; it
does not automatically capture conversations. Sequential Thinking structures
problem-solving tool interactions; it does not increase model capacity or context.
No application behavior was changed by this installation.

## Additional continuity setup

Added `engineering-continuity` after the baseline below (25 total skill packages).
It adapts [Antigravity CLI artifacts](https://antigravity.google/docs/cli/artifacts/)
into short per-task checkpoints, evidence-led debugging, and verification before
completion. Root AGENTS.md directs long tasks to read `.agent-state/INDEX.md`,
recover the matching checkpoint, and compare it against current source before edits.
The state directory is local and excluded through `.git/info/exclude`; it is not
shared across worktrees or machines. Keep it if cleaning local workspace files.

The skill passed the structural validator. Invoke `$engineering-continuity` in a
new Codex session when needed. This adds no MCP or automatic lifecycle hook and
does not change the model or context-window size. Checkpoint updates depend on
the agent following the installed instructions; perfect recall is not guaranteed.

## Original tooling baseline

Installed for coding, debugging, and reviewing RbxManager (Windows WPF/.NET).
The machine now has four additional MCP servers, 23 upstream .NET skill packages,
and one project-specific engineering skill. Three upstream packages are reference
catalogs with automatic invocation disabled. No application source was changed.

## MCP servers

| Server | Purpose | Verified |
|---|---|---|
| `microsoft_learn` | Official .NET, WPF, Win32 documentation | Actual WPF Dispatcher search |
| `context7` | Library documentation, including MVVM Toolkit | Library resolution and documentation query |
| `openaiDeveloperDocs` | Codex, MCP, and OpenAI documentation | Actual documentation search |
| `binlog` | Local MSBuild diagnosis | Read this project's Core build binlog successfully |

Configuration: `C:\Users\Administrator\.codex\config.toml`.
Endpoints: `https://learn.microsoft.com/api/mcp`, `https://mcp.context7.com/mcp`,
and `https://developers.openai.com/mcp`. The binlog server runs locally from
`C:\Users\Administrator\.codex\tools\dotnet\binlog-mcp.exe`.

Context7 worked without an API key during verification; service availability and
anonymous usage limits can change. Remote documentation tools receive generic
API queries. Build logs and runtime diagnostics remain local.

## Installed skills

Installed under `C:\Users\Administrator\.codex\skills` from the official
[dotnet/skills repository](https://github.com/dotnet/skills), pinned to commit
`95e7d0cb7994d47b5789baa68e13f7a2f348b956`:

- Interop: `dotnet-pinvoke`.
- Performance: `analyzing-dotnet-performance`, `dotnet-trace-collect`,
  `dump-collect`, `microbenchmarking`.
- Build: `binlog-generation`, `binlog-failure-analysis`,
  `troubleshoot-binlog-mcp`, `msbuild-antipatterns`,
  `resolve-project-references`, `check-bin-obj-clash`.
- Tests and review: `run-tests`, `platform-detection`, `assertion-quality`,
  `test-anti-patterns`, `test-gap-analysis`, `coverage-analysis`,
  `detect-static-dependencies`, `code-testing`, `test-value-evidence`.
- Reference catalogs: `filter-syntax`, `test-analysis-extensions`,
  `code-testing-extensions`.

The custom `rbxmanager-engineering` skill connects those capabilities to this
repository's MVVM architecture, vault, process lifecycle, browser extension,
Win32 integration, and isolated test harness. Root `AGENTS.md` supplies the
repository-specific routing and verification instructions.

Compatibility adaptations: the three reference catalogs' unsupported upstream
frontmatter flags were moved to string metadata, and their Codex interface files
disable implicit invocation. One relative documentation link was corrected in
the microbenchmarking references. Original files are backed up.

## Installed diagnostic executables

All tools use the isolated directory
`C:\Users\Administrator\.codex\tools\dotnet`; the global PATH was not changed.

| Package | Version |
|---|---|
| Microsoft.AITools.BinlogMcp | 3.0.4 |
| dotnet-dump | 10.0.750501 |
| dotnet-trace | 10.0.750501 |
| dotnet-counters | 10.0.750501 |

## Validation and activation

- All 24 skills passed the skill-creator structural validator.
- A fresh standalone Codex app-server discovered all 24 skills as enabled,
  with no skill-loading errors. No model request or new chat was created.
- All four MCP servers completed initialization and tool discovery; representative
  tool calls succeeded. The binlog server exposes 47 tools.
- `dotnet build src/Core/Core.csproj -c Release --no-restore` succeeded with
  zero warnings and errors. Its binlog was then parsed by the local MCP.
- Diagnostic tool version checks succeeded. Codex doctor reported no failures;
  existing environment notes are unrelated to these additions.

Evidence is under ignored `artifacts/codex-setup-2026-10-30`, including
`skill-validation.json`, `codex-skill-discovery.json`, `mcp-validation.json`,
`binlog-validation.json`, and `core.binlog`. This setup did not rerun the full
application test suite; previous project findings remain in the project scan.

Start a new Codex CLI session to load the complete setup. Newly installed skills
can become available on the next desktop turn; reopen the chat/session if its MCP
tool inventory has not refreshed. Use `$rbxmanager-engineering` to select the
project workflow explicitly, or request a concrete implementation/debug/review.

GitHub account integration is optional and is not counted among these installed
MCP servers. No GitHub connection, language-server integration, or cloud security
service is claimed as installed by this setup.

## Backup and maintenance

Original configuration:
`C:\Users\Administrator\.codex\backups\engineering-setup-20261030\config-211237.toml`.
Original adapted skill files are in the sibling `skill-adaptations` directory.

To remove this setup, remove only its four named MCP sections, its installed
skill directories, and its isolated diagnostic packages. Preserve unrelated
settings and existing MCP entries. Do not restore the whole backup blindly after
later configuration changes. The upstream commit and package versions above
provide a reproducible baseline for future updates.

Official service references:
[Microsoft Learn MCP](https://learn.microsoft.com/en-us/training/support/mcp),
[Context7 for Codex](https://context7.com/docs/clients/codex), and
[OpenAI Docs MCP](https://developers.openai.com/resources/docs-mcp).
