namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

public sealed class I18nParityTests
{
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

    private static Dictionary<string, string> LoadResxEntries(string resxPath)
    {
        Assert.True(File.Exists(resxPath), $"Resx file not found: {resxPath}");

        XDocument doc = XDocument.Load(resxPath);
        Dictionary<string, string> entries = new(StringComparer.Ordinal);

        foreach (XElement dataElement in doc.Descendants("data"))
        {
            string? name = dataElement.Attribute("name")?.Value;
            string? value = dataElement.Element("value")?.Value;

            if (!string.IsNullOrEmpty(name))
            {
                entries[name] = value ?? string.Empty;
            }
        }

        return entries;
    }

    [Fact]
    public void Strings_EnAndVi_HaveExactKeyParity_AndNoEmptyValues()
    {
        string repoRoot = FindRepoRoot();
        string resxEn = Path.Combine(repoRoot, "src", "App.Wpf", "Resources", "Strings.resx");
        string resxVi = Path.Combine(repoRoot, "src", "App.Wpf", "Resources", "Strings.vi.resx");

        Dictionary<string, string> enEntries = LoadResxEntries(resxEn);
        Dictionary<string, string> viEntries = LoadResxEntries(resxVi);

        // 1. Check no empty values
        List<string> emptyEn = enEntries.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        List<string> emptyVi = viEntries.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();

        Assert.True(emptyEn.Count == 0, $"Empty English keys: {string.Join(", ", emptyEn)}");
        Assert.True(emptyVi.Count == 0, $"Empty Vietnamese keys: {string.Join(", ", emptyVi)}");

        // 2. Check keys in EN missing in VI
        List<string> missingInVi = enEntries.Keys.Where(k => !viEntries.ContainsKey(k)).ToList();
        Assert.True(missingInVi.Count == 0, $"Keys present in Strings.resx but missing in Strings.vi.resx: {string.Join(", ", missingInVi)}");

        // 3. Check keys in VI missing in EN
        List<string> missingInEn = viEntries.Keys.Where(k => !enEntries.ContainsKey(k)).ToList();
        Assert.True(missingInEn.Count == 0, $"Keys present in Strings.vi.resx but missing in Strings.resx: {string.Join(", ", missingInEn)}");

        // 4. Counts match
        Assert.Equal(enEntries.Count, viEntries.Count);
    }
}
