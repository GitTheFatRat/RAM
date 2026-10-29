namespace RbxManager.App.UiTests;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;

public static class AutomationHelper
{
    public static AutomationNode DumpTree(AutomationElement element)
    {
        AutomationNode node = new();
        try
        {
            node.ControlType = element.Current.ControlType?.ProgrammaticName ?? "Unknown";
            node.AutomationId = element.Current.AutomationId ?? string.Empty;
            node.Name = element.Current.Name ?? string.Empty;
            node.IsEnabled = element.Current.IsEnabled;
            node.IsOffscreen = element.Current.IsOffscreen;

            Rect rect = element.Current.BoundingRectangle;
            if (!rect.IsEmpty)
            {
                node.BoundingRectangle = new BoundingBox
                {
                    X = rect.X,
                    Y = rect.Y,
                    Width = rect.Width,
                    Height = rect.Height
                };
            }

            // Check if element has text/password value capability without reading the secret value
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _) ||
                element.Current.ControlType == ControlType.Edit)
            {
                node.HasTextValue = true;
            }
        }
        catch
        {
            // Transient element during UI updates
        }

        try
        {
            AutomationElementCollection children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                node.Children.Add(DumpTree(child));
            }
        }
        catch
        {
        }

        return node;
    }

    public static List<AutomationNode> Flatten(AutomationNode root)
    {
        List<AutomationNode> list = new() { root };
        foreach (AutomationNode child in root.Children)
        {
            list.AddRange(Flatten(child));
        }
        return list;
    }

    public static AutomationElement? FindByAutomationId(AutomationElement root, string automationId, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        PropertyCondition condition = new(AutomationElement.AutomationIdProperty, automationId);

        while (DateTime.UtcNow <= deadline)
        {
            try
            {
                AutomationElement? element = root.FindFirst(TreeScope.Descendants, condition);
                if (element != null)
                {
                    return element;
                }
            }
            catch
            {
            }
            System.Threading.Thread.Sleep(50);
        }

        return null;
    }

    public static AutomationElement? FindByName(AutomationElement root, string name, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        PropertyCondition condition = new(AutomationElement.NameProperty, name);

        while (DateTime.UtcNow <= deadline)
        {
            try
            {
                AutomationElement? element = root.FindFirst(TreeScope.Descendants, condition);
                if (element != null)
                {
                    return element;
                }
            }
            catch
            {
            }
            System.Threading.Thread.Sleep(50);
        }

        return null;
    }

    public static AutomationElement? FindByControlType(AutomationElement root, ControlType controlType, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        PropertyCondition condition = new(AutomationElement.ControlTypeProperty, controlType);

        while (DateTime.UtcNow <= deadline)
        {
            try
            {
                AutomationElement? element = root.FindFirst(TreeScope.Descendants, condition);
                if (element != null)
                {
                    return element;
                }
            }
            catch
            {
            }
            System.Threading.Thread.Sleep(50);
        }

        return null;
    }

    public static bool TryInvoke(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern) && pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out object? togglePattern) && togglePattern is TogglePattern toggle)
            {
                toggle.Toggle();
                return true;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selPattern) && selPattern is SelectionItemPattern sel)
            {
                sel.Select();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool TrySetValue(AutomationElement element, string value)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern) && pattern is ValuePattern val)
            {
                val.SetValue(value);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool TrySelect(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern) && pattern is SelectionItemPattern sel)
            {
                sel.Select();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool TryExpand(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? pattern) && pattern is ExpandCollapsePattern exp)
            {
                exp.Expand();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// WPF ContextMenus and Popups are separate top-level HWNDs, so they are never descendants of the
    /// main window element. This searches every top-level window owned by the process.
    /// </summary>
    public static AutomationElement? FindInProcessWindows(int processId, string automationId, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        PropertyCondition ownedByProcess = new(AutomationElement.ProcessIdProperty, processId);
        PropertyCondition byId = new(AutomationElement.AutomationIdProperty, automationId);

        while (DateTime.UtcNow <= deadline)
        {
            try
            {
                foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, ownedByProcess))
                {
                    AutomationElement? match = window.FindFirst(TreeScope.Descendants, byId);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            catch
            {
            }

            System.Threading.Thread.Sleep(50);
        }

        return null;
    }

    public static int CountDataItems(AutomationElement container) =>
        container.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem)).Count;

    public static string? TryGetValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern) && pattern is ValuePattern val
                ? val.Current.Value
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Fails the scenario instead of silently skipping when an expected element is missing.</summary>
    public static AutomationElement Require(AutomationElement? element, string expectation) =>
        element ?? throw new InvalidOperationException($"UI assertion failed: {expectation}");

    public static void RequireThat(bool condition, string expectation)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"UI assertion failed: {expectation}");
        }
    }

    public static bool WaitUntil(Func<bool> predicate, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow <= deadline)
        {
            try
            {
                if (predicate())
                {
                    return true;
                }
            }
            catch
            {
            }

            System.Threading.Thread.Sleep(100);
        }

        return false;
    }
}
