namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

public sealed class XamlLiteralTests
{
    private static readonly HashSet<string> TargetAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text",
        "Content",
        "Header",
        "Title"
    };

    private static readonly HashSet<string> AllowedLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        "RbxManager",
        "Tiếng Việt (vi-VN)",
        "English (en-US)"
    };

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
    public void AllUserVisibleXamlAttributes_AreBoundOrReferenced_NoRawLiterals()
    {
        string repoRoot = FindRepoRoot();
        string wpfDir = Path.Combine(repoRoot, "src", "App.Wpf");
        Assert.True(Directory.Exists(wpfDir), $"App.Wpf directory not found at: {wpfDir}");

        string[] xamlFiles = Directory.GetFiles(wpfDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();

        List<string> violations = new();

        foreach (string file in xamlFiles)
        {
            string fileName = Path.GetFileName(file);
            XDocument doc;
            try
            {
                doc = XDocument.Load(file);
            }
            catch (Exception ex)
            {
                violations.Add($"Failed to parse {fileName}: {ex.Message}");
                continue;
            }

            foreach (XElement element in doc.Descendants())
            {
                foreach (XAttribute attr in element.Attributes())
                {
                    if (TargetAttributes.Contains(attr.Name.LocalName))
                    {
                        string value = attr.Value.Trim();

                        // Markup extensions ({Binding ...}, {x:Static ...}, {x:Type ...}, {DynamicResource ...}, {StaticResource ...}, {ui:SymbolIcon ...})
                        if (value.StartsWith('{') && value.EndsWith('}'))
                        {
                            continue;
                        }

                        // Allow empty or whitespace
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        // Allow pure symbols/numbers
                        if (value.All(c => char.IsDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c)))
                        {
                            continue;
                        }

                        // Allow explicit allowlisted names
                        if (AllowedLiterals.Contains(value))
                        {
                            continue;
                        }

                        violations.Add($"{fileName}: element <{element.Name.LocalName}> has literal {attr.Name.LocalName}=\"{value}\"");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found raw XAML literal texts instead of resources/bindings:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
