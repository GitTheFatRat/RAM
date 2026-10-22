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

        // 5. AccountsContextMenu
        RunScenario(session, "AccountsContextMenu", theme, artifactsDir, results, () =>
        {
            AutomationElement? moreBtn = AutomationHelper.FindByAutomationId(root, "Accounts_MoreActionsButton", 3000);
            if (moreBtn != null)
            {
                AutomationHelper.TryInvoke(moreBtn);
                Thread.Sleep(300);
            }
        }, postAction: () =>
        {
            SendEscape(session.MainWindowHandle);
            Thread.Sleep(200);
        });

        // 6. AccountsAddCookieDialog
        RunScenario(session, "AccountsAddCookieDialog", theme, artifactsDir, results, () =>
        {
            AutomationElement? addSplit = AutomationHelper.FindByAutomationId(root, "Accounts_AddSplitButton", 3000);
            if (addSplit != null)
            {
                AutomationHelper.TryInvoke(addSplit);
                Thread.Sleep(200);

                AutomationElement? cookieItem = AutomationHelper.FindByAutomationId(root, "Accounts_AddViaCookieMenuItem", 2000);
                if (cookieItem != null)
                {
                    AutomationHelper.TryInvoke(cookieItem);
                    Thread.Sleep(400);

                    AutomationElement? aliasBox = AutomationHelper.FindByAutomationId(root, "AccountEditor_AliasBox", 2000);
                    if (aliasBox != null)
                    {
                        AutomationHelper.TrySetValue(aliasBox, "NewFakeAccount");
                    }
                }
            }
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
        });

        // 7. AccountsEditDialog
        RunScenario(session, "AccountsEditDialog", theme, artifactsDir, results, () =>
        {
            AutomationElement? moreBtn = AutomationHelper.FindByAutomationId(root, "Accounts_MoreActionsButton", 3000);
            if (moreBtn != null)
            {
                AutomationHelper.TryInvoke(moreBtn);
                Thread.Sleep(200);

                AutomationElement? editItem = AutomationHelper.FindByAutomationId(root, "Accounts_EditMenuItem", 2000);
                if (editItem != null)
                {
                    AutomationHelper.TryInvoke(editItem);
                    Thread.Sleep(400);
                }
            }
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
        });

        // 8. AccountsDeleteConfirmation
        RunScenario(session, "AccountsDeleteConfirmation", theme, artifactsDir, results, () =>
        {
            AutomationElement? moreBtn = AutomationHelper.FindByAutomationId(root, "Accounts_MoreActionsButton", 3000);
            if (moreBtn != null)
            {
                AutomationHelper.TryInvoke(moreBtn);
                Thread.Sleep(200);

                AutomationElement? delItem = AutomationHelper.FindByAutomationId(root, "Accounts_DeleteMenuItem", 2000);
                if (delItem != null)
                {
                    AutomationHelper.TryInvoke(delItem);
                    Thread.Sleep(400);
                }
            }
        }, postAction: () =>
        {
            DismissOpenContentDialog(root, session.MainWindowHandle);
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

        // 10. ServersPage (Canned List)
        RunScenario(session, "ServersPage", theme, artifactsDir, results, () =>
        {
            NavigateTo(session.MainWindowHandle, root, "NavItem_Servers");
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

        // 18. LoginWindow
        RunScenario(session, "LoginWindow", theme, artifactsDir, results, () =>
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
            return WaitForLoginWindowHwnd(s.Process.Id, s.MainWindowHandle);
        }, postAction: () =>
        {
            IntPtr loginHwnd = FindLoginWindowHwnd(session.Process.Id, session.MainWindowHandle);
            if (loginHwnd != IntPtr.Zero)
            {
                SendEscape(loginHwnd);
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
        Action? postAction = null)
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
            NativeWindowCapture.SaveBitmap(screenshot, imagePath);

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

    private static IntPtr WaitForLoginWindowHwnd(int processId, IntPtr excludeHwnd, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            IntPtr hwnd = FindLoginWindowHwnd(processId, excludeHwnd);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }
            Thread.Sleep(100);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindLoginWindowHwnd(int processId, IntPtr excludeHwnd)
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
