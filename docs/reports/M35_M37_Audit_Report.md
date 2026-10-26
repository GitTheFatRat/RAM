# M35-M37 Audit Report: UI Responsiveness & Window Interaction Verification

- **Date:** 2026-10-26 (UTC+7)
- **Scope:** Verification of M35 (Anti-Minimize & Focus Flags), M36 (Responsive UI & Window Minimum Resolution), and M37 (Draggable Auto-Tiled Windows / Size Lock Decoupling)

## Summary

| Check | Test / Target | Result |
|-------|---------------|--------|
| Full test suite | `dotnet test "RAM CLONE.slnx" --filter "Category!=UI&Category!=Live"` | **343 passed, 0 failed** (Core: 93, App.Wpf: 54, Infrastructure: 196) |
| M36 UI Constraints (WPF UI Tree) | `XamlSmokeTests.M36_MainWindow_And_AccountsDataGrid_EnforceMinimumSizeConstraints` | **PASS** (64 ms) |
| M36 UI Constraints (XAML Scan) | `XamlScanTests.M36_MainWindowAndAccountsColumns_HaveExplicitMinWidthAndMinHeightInXaml` | **PASS** (31 ms) |
| M35 Anti-Minimize & Focus Flags | `WindowControllerTests.M35_MoveResizeWindow_IncludesNoActivateAndNoZOrder_AndStripsMinimizeMaximizeBox` | **PASS** (6 ms) |
| M37 Draggable Windows — Move Only | `WindowControllerTests.M37_WindowSizeLock_OnWinEvent_MoveOnly_SkipsSetWindowPos` | **PASS** (1 ms) |
| M37 Draggable Windows — Resize | `WindowControllerTests.M37_WindowSizeLock_OnWinEvent_Resize_AppliesSwpNoMoveFlag` | **PASS** (53 ms) |

---

## 1. UI Constraints Verification (M36)

### Tests Executed
- `RbxManager.App.Tests.Views.XamlSmokeTests.M36_MainWindow_And_AccountsDataGrid_EnforceMinimumSizeConstraints` (instantiates live `MainWindow` and `AccountsPage` on STA thread)
- `RbxManager.Infrastructure.Tests.QualityGuards.XamlScanTests.M36_MainWindowAndAccountsColumns_HaveExplicitMinWidthAndMinHeightInXaml` (parses `MainWindow.xaml` and `AccountsPage.xaml`)

### Verified Properties
- **`MainWindow.xaml`:**
  - `MinWidth="900"` (`window.MinWidth == 900 >= 900`) — **PASS**
  - `MinHeight="600"` (`window.MinHeight == 600 >= 600`) — **PASS**
- **`AccountsPage.xaml` (`AccountsDataGrid` columns):**
  - `ScrollViewer.HorizontalScrollBarVisibility="Auto"` is enabled on the `DataGrid`.
  - All 7 columns define explicit numeric `MinWidth` values alongside proportional/fixed widths:
    1. Selection Checkbox (`DataGridTemplateColumn`): `Width="44"`, `MinWidth="44"`
    2. Alias (`DataGridTextColumn`): `Width="2*"`, `MinWidth="120"`
    3. Group (`DataGridTextColumn`): `Width="1.2*"`, `MinWidth="80"`
    4. Username (`DataGridTextColumn`): `Width="1.8*"`, `MinWidth="110"`
    5. Status (`DataGridTemplateColumn`): `Width="130"`, `MinWidth="130"`
    6. Last Checked (`DataGridTemplateColumn`): `Width="1.6*"`, `MinWidth="100"`
    7. Created (`DataGridTextColumn`): `Width="1.4*"`, `MinWidth="90"`
    8. Action (`DataGridTemplateColumn`): `Width="170"`, `MinWidth="170"` (buttons wrapped in horizontal `StackPanel` with explicit spacing)

---

## 2. Anti-Minimize & Focus Flags Verification (M35)

### Test Executed
- `RbxManager.Infrastructure.Tests.Processes.WindowControllerTests.M35_MoveResizeWindow_IncludesNoActivateAndNoZOrder_AndStripsMinimizeMaximizeBox`

### Verified Assertions
1. **Minimize Restoration (`ShowWindow`):**
   - When `IsIconic(hWnd)` is `true`, `User32.ForceWindowedMode` invokes `ShowWindow(hWnd, SW_RESTORE)` (`SW_RESTORE = 9`) before applying style or position changes — **PASS**.
2. **Window Style Modification (`GWL_STYLE`):**
   - Given a window with `WS_POPUP | WS_VISIBLE | WS_MINIMIZE | WS_MAXIMIZE | WS_MINIMIZEBOX | WS_MAXIMIZEBOX`, `User32.ForceWindowedMode` strips `WS_MINIMIZEBOX` (`0x00020000`), `WS_MAXIMIZEBOX` (`0x00010000`), `WS_POPUP` (`0x80000000`), `WS_MINIMIZE`, and `WS_MAXIMIZE`, and applies `WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_VISIBLE` via `SetWindowLong(hWnd, GWL_STYLE, newStyle)`:
     - `(newStyle & WS_MINIMIZEBOX) == 0` — **PASS**
     - `(newStyle & WS_MAXIMIZEBOX) == 0` — **PASS**
     - `(newStyle & WS_POPUP) == 0` — **PASS**
3. **Grid Placement Flags (`User32.MoveResizeWindow`):**
   - `SetWindowPos` is called with `uFlags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOSENDCHANGING` (`0x0434`):
     - `(flags & SWP_NOACTIVATE) == 0x0010` — **PASS**
     - `(flags & SWP_NOZORDER) == 0x0004` — **PASS**
     - `(flags & SWP_NOMOVE) == 0` (allows initial grid slot placement at `(200, 0)`) — **PASS**

---

## 3. Draggable Windows & Size Lock Verification (M37)

### Tests Executed
- **Scenario A (Move Only):** `RbxManager.Infrastructure.Tests.Processes.WindowControllerTests.M37_WindowSizeLock_OnWinEvent_MoveOnly_SkipsSetWindowPos`
- **Scenario B (Resize):** `RbxManager.Infrastructure.Tests.Processes.WindowControllerTests.M37_WindowSizeLock_OnWinEvent_Resize_AppliesSwpNoMoveFlag`

### Verified Assertions
- **Scenario A (Move Only):**
  - Window initialized with target size `200x400` and tile slot `(0, 0)`.
  - Simulated `EVENT_OBJECT_LOCATIONCHANGE` (`0x800B`) with new window bounds `(Left: 450, Top: 220, Right: 650, Bottom: 620)` (moved to `X=450, Y=220`, width `200`, height `400`).
  - `WindowSizeLock.EnforceSize()` detected `bounds.Right - bounds.Left == 200` and `bounds.Bottom - bounds.Top == 400` and returned early.
  - Total `SetWindowPos` invocations: **`0`** — **PASS**.
- **Scenario B (Resize):**
  - Window initialized with target size `200x400` and tile slot `(0, 0)`.
  - Simulated `EVENT_OBJECT_LOCATIONCHANGE` (`0x800B`) with bounds `(Left: 450, Top: 220, Right: 1250, Bottom: 820)` (Roblox forced `800x600` at dragged position `X=450, Y=220`).
  - `WindowSizeLock.EnforceSize()` invoked `User32.ResizeWindow(hWnd, 200, 400)`.
  - `SetWindowPos` was called **1 time** with `cx = 200`, `cy = 400` and `uFlags = 0x0436`:
    - `(flags & SWP_NOMOVE) == 0x0002` (preserves dragged `(450, 220)` coordinates instead of snapping back to `(0, 0)`) — **PASS**
    - `(flags & SWP_NOACTIVATE) == 0x0010` — **PASS**
    - `(flags & SWP_NOZORDER) == 0x0004` — **PASS**
