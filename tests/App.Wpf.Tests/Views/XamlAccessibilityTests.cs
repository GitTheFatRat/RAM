namespace RbxManager.App.Tests.Views;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

public sealed class XamlAccessibilityTests
{
    [Fact]
    public void AllIconOnlyButtons_MustHaveAutomationPropertiesName()
    {
        string repoRoot = FindRepoRoot();
        string appWpfDir = Path.Combine(repoRoot, "src", "App.Wpf");
        Assert.True(Directory.Exists(appWpfDir), $"App.Wpf directory not found: {appWpfDir}");

        string[] xamlFiles = Directory.GetFiles(appWpfDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToArray();

        List<string> missingNameViolations = new();

        foreach (string file in xamlFiles)
        {
            XDocument doc;
            using (FileStream fs = File.OpenRead(file))
            {
                doc = XDocument.Load(fs, LoadOptions.SetLineInfo);
            }

            foreach (XElement element in doc.Descendants())
            {
                if (!IsButtonElement(element))
                {
                    continue;
                }

                if (IsIconOnlyButton(element))
                {
                    bool hasName = HasAutomationPropertiesName(element);
                    if (!hasName)
                    {
                        IXmlLineInfo lineInfo = element;
                        string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                        missingNameViolations.Add($"{relPath}:{lineInfo.LineNumber} -> <{element.Name.LocalName}> is icon-only but missing AutomationProperties.Name");
                    }
                }
            }
        }

        Assert.True(
            missingNameViolations.Count == 0,
            $"Found icon-only buttons without AutomationProperties.Name:{Environment.NewLine}{string.Join(Environment.NewLine, missingNameViolations)}");
    }

    private static bool IsButtonElement(XElement element)
    {
        string localName = element.Name.LocalName;
        return localName.Equals("Button", StringComparison.Ordinal) ||
               localName.EndsWith("Button", StringComparison.Ordinal);
    }

    private static bool IsIconOnlyButton(XElement element)
    {
        // 1. Check Content attribute
        XAttribute? contentAttr = element.Attribute("Content");
        string contentVal = contentAttr?.Value?.Trim() ?? string.Empty;

        // If content is an icon markup extension e.g. {ui:SymbolIcon ...}
        bool contentIsIconExtension = contentVal.StartsWith("{ui:SymbolIcon", StringComparison.OrdinalIgnoreCase) ||
                                      contentVal.StartsWith("{ui:FontIcon", StringComparison.OrdinalIgnoreCase);

        // Check if Icon attribute is set
        bool hasIconAttribute = element.Attribute("Icon") != null;

        // Check child elements for icon or text
        bool hasChildIcon = element.Elements().Any(c => c.Name.LocalName.EndsWith("Icon", StringComparison.OrdinalIgnoreCase) ||
                                                        c.Name.LocalName.Equals("Image", StringComparison.OrdinalIgnoreCase));
        bool hasChildText = element.Elements().Any(c => c.Name.LocalName.Equals("TextBlock", StringComparison.OrdinalIgnoreCase));

        // If it has non-icon text content attribute, not icon-only
        if (!string.IsNullOrEmpty(contentVal) && !contentIsIconExtension)
        {
            return false;
        }

        // If it has child text elements, not icon-only
        if (hasChildText)
        {
            return false;
        }

        // If it has an icon attribute or child icon or icon content extension, it is icon-only
        if (hasIconAttribute || hasChildIcon || contentIsIconExtension)
        {
            return true;
        }

        // If content is empty and has no child text, but is a button, treat as icon-only if it has no text
        return string.IsNullOrEmpty(contentVal);
    }

    private static bool HasAutomationPropertiesName(XElement element)
    {
        // Check attributes matching AutomationProperties.Name
        foreach (XAttribute attr in element.Attributes())
        {
            if (attr.Name.LocalName.Equals("AutomationProperties.Name", StringComparison.Ordinal) ||
                (attr.Name.LocalName.Equals("Name", StringComparison.Ordinal) && attr.Name.NamespaceName.Contains("AutomationProperties", StringComparison.OrdinalIgnoreCase)))
            {
                return !string.IsNullOrWhiteSpace(attr.Value);
            }
        }

        // Check child element <AutomationProperties.Name>
        XElement? childName = element.Elements().FirstOrDefault(c => c.Name.LocalName.Equals("AutomationProperties.Name", StringComparison.Ordinal));
        return childName != null && !string.IsNullOrWhiteSpace(childName.Value);
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
