# M35-M37 AUDIT: UI Responsiveness & Window Interaction Verification

## PROBLEM
We have implemented critical UI and Win32 interaction fixes (M35, M36, M37). Before considering the RbxManager client stable, we must strictly verify these changes against the WPF UI tree and the Win32 API logic to ensure they function as intended without breaking the existing Auto-Tile or Size Lock mechanisms.

## TASKS

1. **Verify UI Constraints (M36):**
   - Run a test against the WPF UI tree (or parse the XAML) for `MainWindow`. Assert that `MinWidth` is set to at least 900 and `MinHeight` to at least 600.
   - Assert that the columns in the Accounts DataGrid/ListView have explicit `MinWidth` properties defined (not just `*` or `Auto`).

2. **Verify Anti-Minimize & Focus Flags (M35):**
   - Write a unit test for the window sizing logic checking the flags passed to `SetWindowPos`.
   - Assert that `SWP_NOACTIVATE` (0x0010) and `SWP_NOZORDER` (0x0004) are included when applying the grid layout.
   - Assert that the window style modification logic strips `WS_MINIMIZEBOX` and `WS_MAXIMIZEBOX` to prevent Windows from auto-minimizing borderless focus-lost windows.

3. **Verify Draggable Windows & Size Lock (M37):**
   - Test the `WindowSizeLock.OnWinEvent` logic. 
   - **Scenario A (Move Only):** Simulate an event where the window's X/Y changes, but the Width/Height matches the target (e.g., 200x400). Assert that `SetWindowPos` is **skipped** (early return).
   - **Scenario B (Resize):** Simulate an event where the Width/Height changes (e.g., Roblox forces 800x600). Assert that the correction call to `SetWindowPos` strictly includes the `SWP_NOMOVE` (0x0002) flag so it does not snap back to the old grid coordinates.

4. **Generate Factual Audit Report:**
   - Create the file `docs/reports/M35_M37_Audit_Report.md`.
   - Document the exact results of the tests above.
   - Confirm whether the UI constraints and Win32 flag assertions passed.

## FINISH
Run `dotnet build` and execute the tests.
Commit message: "test: verify M35-M37 UI constraints and Win32 window interaction fixes".