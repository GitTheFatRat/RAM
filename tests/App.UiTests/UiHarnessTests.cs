namespace RbxManager.App.UiTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using Xunit;

public sealed class UiHarnessTests
{
    private sealed record ScenarioRecord(
        string Name,
        string Theme,
        bool Passed,
        string ImagePath,
        string JsonPath,
        string? ErrorMessage = null);

    [Fact]
    [Trait("Category", "UI")]
    public void RunAllUiScenarios_DarkAndLightThemes()
    {
        if (Environment.GetEnvironmentVariable("RBXMANAGER_RUN_UI_TESTS") != "1")
        {
            // UI tests require interactive desktop and Debug configuration.
            // Excluded from default dotnet test run. Run via tools/run-ui-tests.ps1.
            return;
        }

        string repoRoot = FindRepoRoot();
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string artifactsUiDir = Path.Combine(repoRoot, "artifacts", "ui", timestamp);
        Directory.CreateDirectory(artifactsUiDir);

        List<ScenarioRecord> results = new();
        string[] themes = { "Dark", "Light" };

        foreach (string theme in themes)
        {
            ExecuteThemeScenarios(theme, artifactsUiDir, results);
            ExecuteEmptyVaultScenario(theme, artifactsUiDir, results);
        }

        WriteSummary(artifactsUiDir, timestamp, results);

        int failedCount = results.Count(r => !r.Passed);
        Assert.True(
            failedCount == 0,
            $"{failedCount} of {results.Count} UI scenarios failed. See summary report in {artifactsUiDir}/summary.md");
    }

    private static void ExecuteThemeScenarios(string theme, string artifactsDir, List<ScenarioRecord> results)
    {
        using UiAppSession session = UiAppSession.Start(theme: theme, emptyVault: false);
        AutomationElement root = session.MainWindowElement;

        // 1. AccountsPage (Default State)
        RunScenario(session, "AccountsPage", theme, artifactsDir, results, () =>
        {
            AutomationHelper.FindByAutomationId(root, "Accounts_DataGrid", 5000);
            Thread.Sleep(300);
        });

        // 2. AccountsSearch
        RunScenario(session, "AccountsSearch", theme, artifactsDir, results, () =>
        {
            AutomationElement? searchBox = AutomationHelper.FindByAutomationId(root, "Accounts_SearchBox", 3000);
            if (searchBox != null)
            {
                AutomationHelper.TrySetValue(searchBox, "AltFarmer");
                Thread.Sleep(300);
            }
        });

        // 3. AccountsSort
        RunScenario(session, "AccountsSort", theme, artifactsDir, results, () =>
        {
            AutomationElement? grid = AutomationHelper.FindByAutomationId(root, "Accounts_DataGrid", 3000);
            if (grid != null)
            {
                AutomationElementCollection headers = grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.HeaderItem));
                if (headers.Count > 1)
                {
                    AutomationHelper.TryInvoke(headers[1]); // Click Alias column header
                    Thread.Sleep(300);
                }
            }
        });

        // 4. AccountsGroupFilter
        RunScenario(session, "AccountsGroupFilter", theme, artifactsDir, results, () =>
        {
            // Clear search first
            AutomationElement? searchBox = AutomationHelper.FindByAutomationId(root, "Accounts_SearchBox", 3000);
            if (searchBox != null)
            {
                AutomationHelper.TrySetValue(searchBox, string.Empty);
                Thread.Sleep(200);
            }

            AutomationElement? groupBtn = AutomationHelper.FindByName(root, "Main", 2000);
            if (groupBtn != null)
            {
                AutomationHelper.TryInvoke(groupBtn);
                Thread.Sleep(300);
            }
        });

        // 5. AccountsContextMenu — the "More" menu must actually open (it lives in a separate popup HWND).
        RunScenario(session, "AccountsContextMenu", theme, artifactsDir, results, () =>
        {
            AutomationElement moreBtn = AutomationHelper.Require(
                AutomationHelper.FindByAutomationId(root, "Accounts_MoreActionsButton", 3000),
                "Accounts_MoreActionsButton exists");
            AutomationHelper.RequireThat(AutomationHelper.TryInvoke(moreBtn), "More button is invokable");
            AutomationHelper.Require(
                AutomationHelper.FindInProcessWindows(session.Process.Id, "Accounts_EditMenuItem", 3000),
                "More menu opened and shows the Edit item");
            AutomationHelper.Require(
                AutomationHelper.FindInProcessWindows(session.Process.Id, "Accounts_DeleteMenuItem", 1000),
                "More menu shows the Delete item");
        }, postAction: () =>
        {
            SendEscape(session.MainWindowHandle);
            Thread.Sleep(200);
        }, capturePopups: true);

        // 6. AccountsAddCookieDialog — the split-button menu opens and the editor dialog appears and accepts input.
        RunScenario(session, "AccountsAddCookieDialog", theme, artifactsDir, results, () =>
        {
            AutomationElement addSplit = AutomationHelper.Require(
                AutomationHelper.FindByAutomationId(root, "Accounts_AddSplitButton", 3000),
                "Accounts_AddSplitButton exists");
            AutomationHelper.RequireThat(AutomationHelper.TryInvoke(addSplit), "Add split button is invokable");

            AutomationElement cookieItem = AutomationHelper.Require(
                AutomationHelper.FindInProcessWindows(session.Process.Id, "Accounts_AddViaCookieMenuItem", 3000),
                "Add menu opened and shows 'Add via cookie'");
            AutomationHelper.RequireThat(AutomationHelper.TryInvoke(cookieItem), "'Add via cookie' item is invokable");

            AutomationElement aliasBox = AutomationHelper.Require(
                AutomationHelper.FindByAutomationId(root, "AccountEditor_AliasBox", 3000),
                "Add-account dialog opened with the alias editor");
            AutomationHelper.RequireThat(AutomationHelper.TrySetValue(aliasBox, "NewFakeAccount"), "Alias box accepts input");
            AutomationHelper.RequireThat(
                AutomationHelper.WaitUntil(() => AutomationHelper.TryGetValue(aliasBox) == "NewFakeAccount", 1500),
                "Alias box reflects the typed value");
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
        });

        // 7. AccountsEditDialog — select a real row, then Edit must open the editor prefilled with that account.
        RunScenario(session, "AccountsEditDialog", theme, artifactsDir, results, () =>
        {
            SelectFirstAccountRow(root);
            OpenMoreMenuItem(session, root, "Accounts_EditMenuItem");

            AutomationElement aliasBox = AutomationHelper.Require(
                AutomationHelper.FindByAutomationId(root, "AccountEditor_AliasBox", 3000),
                "Edit dialog opened with the alias editor");
            AutomationHelper.RequireThat(
                AutomationHelper.WaitUntil(() => !string.IsNullOrWhiteSpace(AutomationHelper.TryGetValue(aliasBox)), 1500),
                "Edit dialog is prefilled with the selected account's alias");
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
        });

        // 8. AccountsDeleteConfirmation — Delete must raise the confirmation dialog and leave the vault untouched on cancel.
        int rowsBeforeDelete = 0;
        RunScenario(session, "AccountsDeleteConfirmation", theme, artifactsDir, results, () =>
        {
            AutomationElement grid = SelectFirstAccountRow(root);
            rowsBeforeDelete = AutomationHelper.CountDataItems(grid);
            OpenMoreMenuItem(session, root, "Accounts_DeleteMenuItem");

            AutomationHelper.Require(
                AutomationHelper.FindByName(root, "Xác nhận xóa", 3000) ?? AutomationHelper.FindByName(root, "Confirm Delete", 500),
                "Delete confirmation dialog is shown");
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
            AutomationElement? grid = AutomationHelper.FindByAutomationId(root, "Accounts_DataGrid", 2000);
            if (grid != null && rowsBeforeDelete > 0 &&
                !AutomationHelper.WaitUntil(() => AutomationHelper.CountDataItems(grid) == rowsBeforeDelete, 2000))
            {
                results.Add(new ScenarioRecord(
                    "AccountsDeleteCancelKeepsRow",
                    theme,
                    false,
                    string.Empty,
                    string.Empty,
                    "Cancelling the delete dialog removed an account row"));
            }
        });

        // 9. AccountsEmptySearch
        RunScenario(session, "AccountsEmptySearch", theme, artifactsDir, results, () =>
        {
            AutomationElement? searchBox = AutomationHelper.FindByAutomationId(root, "Accounts_SearchBox", 3000);
            if (searchBox != null)
            {
                AutomationHelper.TrySetValue(searchBox, "NoSuchAccountXYZ999");
                Thread.Sleep(300);
            }
        }, postAction: () =>
        {
            AutomationElement? searchBox = AutomationHelper.FindByAutomationId(root, "Accounts_SearchBox", 2000);
            if (searchBox != null)
            {
                AutomationHelper.TrySetValue(searchBox, string.Empty);
                Thread.Sleep(200);
            }
        });

        // 10. ServersPage (Canned List) — refresh a normal place ID and require the fake service's 3 servers as rows.
        RunScenario(session, "ServersPage", theme, artifactsDir, results, () =>
        {
            NavigateTo(session.MainWindowHandle, root, "NavItem_Servers");

            AutomationElement placeBox = AutomationHelper.Require(
                AutomationHelper.FindByAutomationId(root, "Servers_PlaceIdBox", 3000),
                "Servers_PlaceIdBox exists");
            AutomationElement refreshBtn = AutomationHelper.Require(
                AutomationHelper.FindByAutomationId(root, "Servers_RefreshButton", 3000),
                "Servers_RefreshButton exists");
            AutomationHelper.RequireThat(AutomationHelper.TrySetValue(placeBox, "1818"), "Place ID box accepts input");
            AutomationHelper.RequireThat(AutomationHelper.TryInvoke(refreshBtn), "Refresh button is invokable");

            AutomationElement grid = AutomationHelper.Require(
                AutomationHelper.FindByAutomationId(root, "Servers_DataGrid", 3000),
                "Servers_DataGrid exists");
            AutomationHelper.RequireThat(
                AutomationHelper.WaitUntil(() => AutomationHelper.CountDataItems(grid) >= 3, 4000),
                "Canned server list renders 3 rows");
        });

        // 11. ServersPageEmpty (Fixed fake place ID 999999)
        RunScenario(session, "ServersPageEmpty", theme, artifactsDir, results, () =>
        {
            AutomationElement? placeBox = AutomationHelper.FindByAutomationId(root, "Servers_PlaceIdBox", 3000);
            AutomationElement? refreshBtn = AutomationHelper.FindByAutomationId(root, "Servers_RefreshButton", 3000);
            if (placeBox != null && refreshBtn != null)
            {
                AutomationHelper.TrySetValue(placeBox, "999999");
                AutomationHelper.TryInvoke(refreshBtn);
                Thread.Sleep(500);
            }
        });

        // 12. ServersPageFailing (Fixed fake place ID 666666)
        RunScenario(session, "ServersPageFailing", theme, artifactsDir, results, () =>
        {
            AutomationElement? placeBox = AutomationHelper.FindByAutomationId(root, "Servers_PlaceIdBox", 3000);
            AutomationElement? refreshBtn = AutomationHelper.FindByAutomationId(root, "Servers_RefreshButton", 3000);
            if (placeBox != null && refreshBtn != null)
            {
                AutomationHelper.TrySetValue(placeBox, "666666");
                AutomationHelper.TryInvoke(refreshBtn);
                Thread.Sleep(500);
            }
        });

        // 13. RunningPage
        RunScenario(session, "RunningPage", theme, artifactsDir, results, () =>
        {
            NavigateTo(session.MainWindowHandle, root, "NavItem_Running");
        });

        // 14. DiagnosticsPage
        RunScenario(session, "DiagnosticsPage", theme, artifactsDir, results, () =>
        {
            NavigateTo(session.MainWindowHandle, root, "NavItem_Diagnostics");

            AutomationElement? runBtn = AutomationHelper.FindByAutomationId(root, "Diagnostics_RunButton", 3000);
            if (runBtn != null)
            {
                AutomationHelper.TryInvoke(runBtn);
                Thread.Sleep(1000);
            }
        });

        // 15. SettingsPage
        RunScenario(session, "SettingsPage", theme, artifactsDir, results, () =>
        {
            NavigateTo(session.MainWindowHandle, root, "NavItem_Settings");
        });

        // 16. SettingsRiskDialog
        RunScenario(session, "SettingsRiskDialog", theme, artifactsDir, results, () =>
        {
            AutomationElement? multiToggle = AutomationHelper.FindByAutomationId(root, "Settings_MultiInstanceToggle", 3000);
            if (multiToggle != null)
            {
                AutomationHelper.TryInvoke(multiToggle);
                Thread.Sleep(400);
            }
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
        });

        // 17. LaunchDialog (Dry Run)
        RunScenario(session, "LaunchDialog", theme, artifactsDir, results, () =>
        {
            NavigateTo(session.MainWindowHandle, root, "NavItem_Accounts");

            AutomationElement? searchBox = AutomationHelper.FindByAutomationId(root, "Accounts_SearchBox", 2000);
            if (searchBox != null)
            {
                AutomationHelper.TrySetValue(searchBox, string.Empty);
                Thread.Sleep(200);
            }

            AutomationElement? dataGrid = AutomationHelper.FindByAutomationId(root, "Accounts_DataGrid", 3000);
            if (dataGrid != null)
            {
                AutomationElement? firstRow = dataGrid.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
                if (firstRow != null)
                {
                    AutomationHelper.TrySelect(firstRow);
                    Thread.Sleep(200);
                }
            }

            AutomationElement? launchBtn = AutomationHelper.FindByAutomationId(root, "Accounts_LaunchButton", 3000);
            if (launchBtn != null && launchBtn.Current.IsEnabled)
            {
                AutomationHelper.TryInvoke(launchBtn);
                Thread.Sleep(400);

                AutomationElement? dryRunBtn = AutomationHelper.FindByAutomationId(root, "LaunchDialog_DryRunButton", 3000);
                if (dryRunBtn != null)
                {
                    AutomationHelper.TryInvoke(dryRunBtn);
                    Thread.Sleep(200);
                }
            }
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
        });

        // 18. AddAccountWindow
        RunScenario(session, "AddAccountWindow", theme, artifactsDir, results, () =>
        {
            NavigateTo(session.MainWindowHandle, root, "NavItem_Accounts");

            AutomationElement? addLogin = AutomationHelper.FindByAutomationId(root, "Accounts_AddViaLoginButton", 3000);
            if (addLogin != null)
            {
                AutomationHelper.TryInvoke(addLogin);
                Thread.Sleep(600);
            }
        }, targetWindowFinder: (s) =>
        {
            return WaitForSecondaryWindowHwnd(s.Process.Id, s.MainWindowHandle);
        }, postAction: () =>
        {
            IntPtr secondaryHwnd = FindSecondaryWindowHwnd(session.Process.Id, session.MainWindowHandle);
            if (secondaryHwnd != IntPtr.Zero)
            {
                SendEscape(secondaryHwnd);
                Thread.Sleep(300);
            }
        });
    }

    private static void ExecuteEmptyVaultScenario(string theme, string artifactsDir, List<ScenarioRecord> results)
    {
        using UiAppSession session = UiAppSession.Start(theme: theme, emptyVault: true);

        // 19. AccountsEmptyVault
        RunScenario(session, "AccountsEmptyVault", theme, artifactsDir, results, () =>
        {
            Thread.Sleep(600);
        });
    }

    private static void RunScenario(
        UiAppSession session,
        string scenarioName,
        string theme,
        string artifactsDir,
        List<ScenarioRecord> results,
        Action action,
        Func<UiAppSession, IntPtr>? targetWindowFinder = null,
        Action? postAction = null,
        bool capturePopups = false)
    {
        string imagePath = Path.Combine(artifactsDir, $"{scenarioName}-{theme}.png");
        string jsonPath = Path.Combine(artifactsDir, $"{scenarioName}-{theme}.uia.json");

        try
        {
            action();

            IntPtr targetHwnd = targetWindowFinder != null
                ? targetWindowFinder(session)
                : session.MainWindowHandle;

            if (targetHwnd == IntPtr.Zero)
            {
                targetHwnd = session.MainWindowHandle;
            }

            // Window-only capture via Win32 PrintWindow with render-full-content flag
            using Bitmap screenshot = NativeWindowCapture.CaptureWindow(targetHwnd);
            if (capturePopups)
            {
                // Assertions below keep using the raw window capture; the saved PNG must show the real popup menu.
                using Bitmap withPopups = NativeWindowCapture.ComposeWithProcessPopups(screenshot, targetHwnd, session.Process.Id, out int popupCount);
                AutomationHelper.RequireThat(popupCount > 0, "Popup menu window was rendered into the screenshot");
                NativeWindowCapture.SaveBitmap(withPopups, imagePath);
            }
            else
            {
                NativeWindowCapture.SaveBitmap(screenshot, imagePath);
            }

            // Dump UI tree via UI Automation
            AutomationElement targetElement = AutomationElement.FromHandle(targetHwnd);
            AutomationNode treeNode = AutomationHelper.DumpTree(targetElement);
            string jsonContent = AutomationSerializer.ToJson(treeNode);
            File.WriteAllText(jsonPath, jsonContent);

            // Automatic assertions
            VisualAssertions.AssertProcessAliveAndHealthy(session.Process);
            VisualAssertions.AssertNoUnnamedInteractiveElements(treeNode);

            NativeWindowCapture.GetWindowRect(targetHwnd, out NativeWindowCapture.RECT windowRect);
            VisualAssertions.AssertBoundsWithinWindow(treeNode, windowRect);

            if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase))
            {
                VisualAssertions.AssertDarkThemeDataGridContrast(screenshot, AutomationHelper.Flatten(treeNode), windowRect);
            }

            VisualAssertions.AssertTitlesAndHeadersPresent(AutomationHelper.Flatten(treeNode));

            results.Add(new ScenarioRecord(scenarioName, theme, true, imagePath, jsonPath));
        }
        catch (Exception ex)
        {
            results.Add(new ScenarioRecord(scenarioName, theme, false, imagePath, jsonPath, ex.Message));
            throw;
        }
        finally
        {
            postAction?.Invoke();
        }
    }

    private static AutomationElement SelectFirstAccountRow(AutomationElement root)
    {
        AutomationElement grid = AutomationHelper.Require(
            AutomationHelper.FindByAutomationId(root, "Accounts_DataGrid", 3000),
            "Accounts_DataGrid exists");
        AutomationElement firstRow = AutomationHelper.Require(
            grid.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem)),
            "Accounts grid has at least one row");
        AutomationHelper.RequireThat(AutomationHelper.TrySelect(firstRow), "First account row is selectable");
        Thread.Sleep(200);
        return grid;
    }

    private static void OpenMoreMenuItem(UiAppSession session, AutomationElement root, string menuItemAutomationId)
    {
        AutomationElement moreBtn = AutomationHelper.Require(
            AutomationHelper.FindByAutomationId(root, "Accounts_MoreActionsButton", 3000),
            "Accounts_MoreActionsButton exists");
        AutomationHelper.RequireThat(AutomationHelper.TryInvoke(moreBtn), "More button is invokable");

        AutomationElement item = AutomationHelper.Require(
            AutomationHelper.FindInProcessWindows(session.Process.Id, menuItemAutomationId, 3000),
            $"More menu shows {menuItemAutomationId}");
        AutomationHelper.RequireThat(item.Current.IsEnabled, $"{menuItemAutomationId} is enabled once a row is selected");
        AutomationHelper.RequireThat(AutomationHelper.TryInvoke(item), $"{menuItemAutomationId} is invokable");
        Thread.Sleep(400);
    }

    private static void DismissOpenContentDialog(AutomationElement root, IntPtr hwnd)
    {
        try
        {
            // Find and invoke the cancel/close button of any active ContentDialog
            AutomationElement? cancelBtn = AutomationHelper.FindByName(root, "Hủy", 1500)
                ?? AutomationHelper.FindByName(root, "Cancel", 500);

            if (cancelBtn != null)
            {
                AutomationHelper.TryInvoke(cancelBtn);
            }
            else
            {
                SendEscape(hwnd);
            }
            Thread.Sleep(300);
        }
        catch
        {
        }
    }

    private static IntPtr WaitForSecondaryWindowHwnd(int processId, IntPtr excludeHwnd, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            IntPtr hwnd = FindSecondaryWindowHwnd(processId, excludeHwnd);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }
            Thread.Sleep(100);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindSecondaryWindowHwnd(int processId, IntPtr excludeHwnd)
    {
        IntPtr found = IntPtr.Zero;
        EnumThreadWindowsCallback callback = (hwnd, lParam) =>
        {
            if (hwnd == excludeHwnd)
            {
                return true;
            }

            if (!NativeWindowCapture.IsWindowVisible(hwnd))
            {
                return true;
            }

            NativeWindowCapture.GetWindowRect(hwnd, out NativeWindowCapture.RECT rect);
            if (rect.Width > 200 && rect.Height > 200)
            {
                found = hwnd;
                return false;
            }
            return true;
        };

        Process[] processes = Process.GetProcesses();
        Process? targetProc = processes.FirstOrDefault(p => p.Id == processId);
        if (targetProc != null)
        {
            foreach (ProcessThread thread in targetProc.Threads)
            {
                EnumThreadWindows((uint)thread.Id, callback, IntPtr.Zero);
                if (found != IntPtr.Zero)
                {
                    break;
                }
            }
        }

        return found;
    }

    private delegate bool EnumThreadWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadWindowsCallback lpfn, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;

    private static void ClickElement(IntPtr hwnd, AutomationElement element)
    {
        System.Windows.Rect rect = element.Current.BoundingRectangle;
        POINT pt = new()
        {
            X = (int)(rect.X + rect.Width / 2),
            Y = (int)(rect.Y + rect.Height / 2)
        };
        ScreenToClient(hwnd, ref pt);
        IntPtr lParam = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));
        PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
        PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F1 = 0x70;
    private const int VK_F2 = 0x71;
    private const int VK_F3 = 0x72;
    private const int VK_F4 = 0x73;
    private const int VK_F6 = 0x75;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static void SendEscape(IntPtr hwnd)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != hwnd)
            {
                PostMessage(fg, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                PostMessage(fg, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
            }

            if (hwnd != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
            }
        }
        catch
        {
        }
    }

    private static void NavigateTo(IntPtr hwnd, AutomationElement root, string automationId)
    {
        int vk = automationId switch
        {
            "NavItem_Accounts" => VK_F1,
            "NavItem_Servers" => VK_F2,
            "NavItem_Running" => VK_F3,
            "NavItem_Diagnostics" => VK_F4,
            "NavItem_Settings" => VK_F6,
            _ => 0
        };

        if (vk != 0 && hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, IntPtr.Zero);
            Thread.Sleep(500);
            return;
        }

        AutomationElement? item = AutomationHelper.FindByAutomationId(root, automationId, 3000);
        if (item != null)
        {
            if (!AutomationHelper.TryInvoke(item))
            {
                ClickElement(hwnd, item);
            }
            Thread.Sleep(500);
        }
    }

    private static void WriteSummary(string artifactsDir, string timestamp, List<ScenarioRecord> results)
    {
        StringBuilder sb = new();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"# UI Automation Test Harness Summary ({timestamp})");
        sb.AppendLine();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Total Scenarios: {results.Count}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Passed: {results.Count(r => r.Passed)}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Failed: {results.Count(r => !r.Passed)}");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Theme | Status | Image Artifact | UIA JSON Dump |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (ScenarioRecord record in results)
        {
            string status = record.Passed ? "PASS" : $"FAIL ({record.ErrorMessage})";
            string relImg = Path.GetFileName(record.ImagePath);
            string relJson = Path.GetFileName(record.JsonPath);
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"| {record.Name} | {record.Theme} | {status} | [{relImg}](./{relImg}) | [{relJson}](./{relJson}) |");
        }

        string summaryPath = Path.Combine(artifactsDir, "summary.md");
        File.WriteAllText(summaryPath, sb.ToString());
    }

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.GetFiles(current, "*.slnx").Length > 0 || Directory.GetFiles(current, "*.sln").Length > 0)
            {
                return current;
            }
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }
            current = parent.FullName;
        }
        throw new InvalidOperationException("Could not find repository root containing .slnx or .sln.");
    }
}
