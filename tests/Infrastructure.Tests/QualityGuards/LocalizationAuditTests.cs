namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

public sealed class LocalizationAuditTests
{
    private static readonly HashSet<string> AllowedIdenticalValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "RbxManager",
        "FPS",
        "Ping",
        "PID",
        "WebView2",
        "JSON",
        "Proxy",
        "HTTP",
        "SOCKS5"
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

    private static Dictionary<string, string> LoadResx(string path)
    {
        Assert.True(File.Exists(path), $"Resx not found at {path}");
        XDocument doc = XDocument.Load(path);
        Dictionary<string, string> dict = new(StringComparer.Ordinal);
        foreach (XElement el in doc.Descendants("data"))
        {
            string? name = el.Attribute("name")?.Value;
            string? val = el.Element("value")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                dict[name] = val ?? string.Empty;
            }
        }
        return dict;
    }

    [Fact]
    public void VietnameseStrings_DoNotEqualEnglish_OutsideAllowlist()
    {
        string root = FindRepoRoot();
        string enPath = Path.Combine(root, "src", "App.Wpf", "Resources", "Strings.resx");
        string viPath = Path.Combine(root, "src", "App.Wpf", "Resources", "Strings.vi.resx");

        Dictionary<string, string> en = LoadResx(enPath);
        Dictionary<string, string> vi = LoadResx(viPath);

        List<string> violations = new();

        foreach (KeyValuePair<string, string> kvp in vi)
        {
            string key = kvp.Key;
            string viValue = kvp.Value;
            if (en.TryGetValue(key, out string? enValue))
            {
                string viTrimmed = viValue.Trim();
                string enTrimmed = enValue.Trim();

                if (string.Equals(viTrimmed, enTrimmed, StringComparison.OrdinalIgnoreCase))
                {
                    if (!AllowedIdenticalValues.Contains(viTrimmed))
                    {
                        violations.Add($"{key}: '{viTrimmed}'");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Strings.vi.resx contains values identical to English without being in the allowlist:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void VietnameseKeyTerms_HaveProperDiacritics()
    {
        string root = FindRepoRoot();
        string viPath = Path.Combine(root, "src", "App.Wpf", "Resources", "Strings.vi.resx");
        Dictionary<string, string> vi = LoadResx(viPath);

        // Core navigation and primary terms must have proper diacritics
        Assert.Contains("Tài khoản", vi["NavAccounts"], StringComparison.Ordinal);
        Assert.Contains("Cài đặt", vi["NavSettings"], StringComparison.Ordinal);
        Assert.Contains("Máy chủ", vi["NavServers"], StringComparison.Ordinal);
        Assert.Contains("Đang chạy", vi["NavRunning"], StringComparison.Ordinal);
        Assert.Contains("Chẩn đoán", vi["NavDiagnostics"], StringComparison.Ordinal);
    }
}
