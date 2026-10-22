# UI Verification Agent Workflow

## Purpose
Enables autonomous agents to inspect, verify, and catch visual regressions in the RbxManager WPF application within a fully isolated sandbox using window-only screenshots and UI Automation dumps.

## Trigger
Run this workflow after any milestone or change modifying:
- XAML views or controls (`src/App.Wpf/Views/`)
- Application themes or design tokens (`src/App.Wpf/Themes/`)
- ViewModels and display data structures (`src/App.Wpf/ViewModels/`)

## Execution Steps
1. **Run the Test Harness**:
   Execute `tools/run-ui-tests.ps1` in PowerShell. This builds the application in `Debug` configuration and executes all scenarios under the `UI` category.
2. **Review Output Artifacts**:
   - Screenshots: `artifacts/ui/<timestamp>/<scenario>-<theme>.png` (captured via Win32 `PrintWindow` with `PW_RENDERFULLCONTENT`).
   - UI Automation tree dumps: `artifacts/ui/<timestamp>/<scenario>-<theme>.uia.json`.
   - Run summary: `artifacts/ui/<timestamp>/summary.md`.
3. **Inspect and Report**:
   - Inspect every generated PNG image. Check for layout overflow, text clipping/truncation, low contrast, element misalignment, or incorrect localization text.
   - If image viewing capability is unavailable in the agent environment, rely on the UIA JSON tree dump and automatic harness assertions and note this in the milestone report.
   - Record all identified visual defects and corresponding fixes in `docs/reports/<milestone>.md`.

## Safety Invariants
- The harness only touches the test process it spawned; no full-screen capture or desktop hijack is ever performed.
- Operates strictly with fake accounts, fake network responses, and an isolated temporary data directory (`RBXMANAGER_DATA_DIR`). Real data in `%LOCALAPPDATA%\RbxManager` is never touched.
