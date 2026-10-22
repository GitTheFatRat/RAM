namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

public sealed partial class XamlScanTests
{
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b")]
    private static partial Regex HexColorRegex();

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

        throw new InvalidOperationException("Could not find repository root.");
    }

    [Fact]
    public void NoHexColorsInXaml_OutsideThemesTokens()
    {
        string root = FindRepoRoot();
        string wpfDir = Path.Combine(root, "src", "App.Wpf");
        string tokensPath = Path.Combine(wpfDir, "Themes", "Tokens.xaml");

        string[] xamlFiles = Directory.GetFiles(wpfDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(tokensPath), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        List<string> violations = new();

        foreach (string file in xamlFiles)
        {
            string content = File.ReadAllText(file);
            MatchCollection matches = HexColorRegex().Matches(content);
            if (matches.Count > 0)
            {
                string relativePath = Path.GetRelativePath(root, file);
                foreach (Match match in matches)
                {
                    violations.Add($"{relativePath}: found hex color '{match.Value}'");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found hard-coded hex colors in XAML files outside Themes/Tokens.xaml:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void NoHardCodedFontFamiliesInPages_UseTokens()
    {
        string root = FindRepoRoot();
        string pagesDir = Path.Combine(root, "src", "App.Wpf", "Views", "Pages");

        if (!Directory.Exists(pagesDir))
        {
            return;
        }

        string[] pageXamlFiles = Directory.GetFiles(pagesDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();

        List<string> violations = new();

        foreach (string file in pageXamlFiles)
        {
            XDocument doc = XDocument.Load(file);
            foreach (XElement element in doc.Descendants())
            {
                XAttribute? fontAttr = element.Attribute("FontFamily");
                if (fontAttr != null)
                {
                    string val = fontAttr.Value.Trim();
                    // Must be a resource reference ({StaticResource ...} or {DynamicResource ...})
                    if (!(val.StartsWith("{StaticResource", StringComparison.OrdinalIgnoreCase) ||
                          val.StartsWith("{DynamicResource", StringComparison.OrdinalIgnoreCase)))
                    {
                        string relativePath = Path.GetRelativePath(root, file);
                        violations.Add($"{relativePath} line {((System.Xml.IXmlLineInfo)fontAttr).LineNumber}: FontFamily='{val}' is hardcoded. Use token resources.");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found hard-coded FontFamily in pages. Must reference tokens:\n{string.Join("\n", violations)}");
    }
}
