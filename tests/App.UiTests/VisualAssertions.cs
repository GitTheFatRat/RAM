namespace RbxManager.App.UiTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using Xunit;

internal static class VisualAssertions
{
    private static readonly HashSet<string> InteractiveControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ControlType.Button",
        "ControlType.Edit",
        "ControlType.CheckBox",
        "ControlType.ComboBox",
        "ControlType.Slider",
        "ControlType.TabItem",
        "ControlType.MenuItem",
        "ControlType.DataGrid",
        "ControlType.RadioButton",
        "ControlType.Hyperlink"
    };

    public static void AssertProcessAliveAndHealthy(Process process)
    {
        Assert.False(process.HasExited, "Application process exited or terminated prematurely during UI scenario.");
    }

    public static void AssertNoUnnamedInteractiveElements(AutomationNode root)
    {
        List<AutomationNode> nodes = AutomationHelper.Flatten(root);
        List<string> violations = new();

        foreach (AutomationNode node in nodes)
        {
            if (node.IsOffscreen)
            {
                continue;
            }

            if (InteractiveControlTypes.Contains(node.ControlType))
            {
                if (string.IsNullOrWhiteSpace(node.Name) && string.IsNullOrWhiteSpace(node.AutomationId))
                {
                    violations.Add($"Control {node.ControlType} at ({node.BoundingRectangle.X}, {node.BoundingRectangle.Y}) has both Name and AutomationId empty.");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found unnamed interactive UI elements without stable AutomationId or Name:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    public static void AssertDarkThemeDataGridContrast(Bitmap screenshot, List<AutomationNode> allNodes, NativeWindowCapture.RECT windowRect)
    {
        List<AutomationNode> dataGrids = allNodes
            .Where(n => (n.ControlType.Contains("DataGrid", StringComparison.OrdinalIgnoreCase) ||
                         n.AutomationId.EndsWith("_DataGrid", StringComparison.OrdinalIgnoreCase)) &&
                        !n.IsOffscreen &&
                        n.BoundingRectangle.Width > 50 &&
                        n.BoundingRectangle.Height > 50)
            .ToList();

        foreach (AutomationNode grid in dataGrids)
        {
            int localX = Math.Max(0, (int)(grid.BoundingRectangle.X - windowRect.Left));
            int localY = Math.Max(0, (int)(grid.BoundingRectangle.Y - windowRect.Top));
            int localWidth = Math.Min((int)grid.BoundingRectangle.Width, screenshot.Width - localX);
            int localHeight = Math.Min((int)grid.BoundingRectangle.Height, screenshot.Height - localY);

            if (localWidth <= 10 || localHeight <= 10)
            {
                continue;
            }

            int totalSampled = 0;
            int whitePixels = 0;

            // Sample every 2nd pixel for high performance and thorough coverage
            for (int y = localY; y < localY + localHeight; y += 2)
            {
                for (int x = localX; x < localX + localWidth; x += 2)
                {
                    Color color = screenshot.GetPixel(x, y);
                    // Standard ITU-R BT.709 relative luminance
                    double luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

                    totalSampled++;
                    if (luminance > 0.85)
                    {
                        whitePixels++;
                    }
                }
            }

            if (totalSampled > 0)
            {
                double whiteRatio = (double)whitePixels / totalSampled;
                Assert.True(
                    whiteRatio <= 0.03,
                    $"DataGrid '{grid.AutomationId}' in Dark theme failed contrast check: {whiteRatio:P2} of pixels are near-white (luminance > 0.85), exceeding 3% limit. White row regression detected!");
            }
        }
    }

    public static void AssertBoundsWithinWindow(AutomationNode node, NativeWindowCapture.RECT windowRect)
    {
        List<AutomationNode> nodes = AutomationHelper.Flatten(node);
        double maxAllowedWidth = windowRect.Width + 50;
        double maxAllowedHeight = windowRect.Height + 50;

        foreach (AutomationNode item in nodes)
        {
            if (item.IsOffscreen)
            {
                continue;
            }

            // Scrollable container content panes (such as StackPanel inside ScrollViewer) naturally extend beyond viewport height
            if (string.Equals(item.ControlType, "ControlType.Pane", StringComparison.OrdinalIgnoreCase) &&
                item.Children.Count > 0 &&
                item.BoundingRectangle.Width <= maxAllowedWidth)
            {
                continue;
            }

            if (item.BoundingRectangle.Width > maxAllowedWidth || item.BoundingRectangle.Height > maxAllowedHeight)
            {
                Assert.Fail($"Control '{item.AutomationId}' ({item.ControlType}) reported bounds ({item.BoundingRectangle.Width}x{item.BoundingRectangle.Height}) larger than window ({windowRect.Width}x{windowRect.Height}).");
            }
        }
    }

    public static void AssertTitlesAndHeadersPresent(List<AutomationNode> nodes)
    {
        bool hasTitleOrHeader = nodes.Any(n =>
            (n.ControlType.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
             n.ControlType.Contains("Header", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(n.Name));

        Assert.True(hasTitleOrHeader, "Page does not report any non-empty titles or headers in UI Automation tree.");
    }
}
