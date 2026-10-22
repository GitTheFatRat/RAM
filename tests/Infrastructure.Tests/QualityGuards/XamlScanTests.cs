namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

public sealed partial class XamlScanTests
{
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"\{ui:SymbolIcon\s+(?:Symbol=)?([A-Za-z0-9_]+)\}")]
    private static partial Regex SymbolIconMarkupRegex();

    [GeneratedRegex(@"<ui:SymbolIcon\b[^>]*\bSymbol=""([A-Za-z0-9_]+)""")]
    private static partial Regex SymbolIconElementRegex();

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

    [Fact]
    public void AllSymbolIconReferences_MustBeValidFluentSymbols()
    {
        string root = FindRepoRoot();
        HashSet<string> validSymbols = LoadValidSymbols(root);

        string wpfDir = Path.Combine(root, "src", "App.Wpf");
        string[] xamlFiles = Directory.GetFiles(wpfDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();

        List<string> invalidOccurrences = new();

        foreach (string file in xamlFiles)
        {
            string content = File.ReadAllText(file);
            string relativePath = Path.GetRelativePath(root, file);

            foreach (Match match in SymbolIconMarkupRegex().Matches(content))
            {
                string symbol = match.Groups[1].Value;
                if (!validSymbols.Contains(symbol))
                {
                    invalidOccurrences.Add($"{relativePath}: unknown symbol '{symbol}' in '{match.Value}'");
                }
            }

            foreach (Match match in SymbolIconElementRegex().Matches(content))
            {
                string symbol = match.Groups[1].Value;
                if (!validSymbols.Contains(symbol))
                {
                    invalidOccurrences.Add($"{relativePath}: unknown symbol '{symbol}' in '{match.Value}'");
                }
            }
        }

        Assert.True(invalidOccurrences.Count == 0,
            $"Found invalid SymbolIcon symbols in XAML:\n{string.Join("\n", invalidOccurrences)}");
    }

    private static HashSet<string> LoadValidSymbols(string repoRoot)
    {
        string candidate = Path.Combine(repoRoot, "src", "App.Wpf", "bin", "Release", "net10.0-windows", "Wpf.Ui.dll");
        if (!File.Exists(candidate))
        {
            candidate = Path.Combine(repoRoot, "src", "App.Wpf", "bin", "Debug", "net10.0-windows", "Wpf.Ui.dll");
        }
        if (!File.Exists(candidate))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string nugetDir = Path.Combine(userProfile, ".nuget", "packages", "wpf-ui");
            if (Directory.Exists(nugetDir))
            {
                string[] found = Directory.GetFiles(nugetDir, "Wpf.Ui.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    candidate = found[0];
                }
            }
        }

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException("Could not locate Wpf.Ui.dll to inspect SymbolRegular enum.");
        }

        using FileStream stream = File.OpenRead(candidate);
        using PEReader peReader = new(stream);
        MetadataReader metadataReader = peReader.GetMetadataReader();

        HashSet<string> symbols = new(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle typeHandle in metadataReader.TypeDefinitions)
        {
            TypeDefinition typeDef = metadataReader.GetTypeDefinition(typeHandle);
            if (metadataReader.GetString(typeDef.Name) == "SymbolRegular")
            {
                foreach (FieldDefinitionHandle fieldHandle in typeDef.GetFields())
                {
                    FieldDefinition fieldDef = metadataReader.GetFieldDefinition(fieldHandle);
                    symbols.Add(metadataReader.GetString(fieldDef.Name));
                }
                break;
            }
        }

        return symbols;
    }
}
