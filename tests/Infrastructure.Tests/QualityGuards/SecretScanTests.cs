namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class SecretScanTests
{
    private static readonly Regex RobloxCookieRegex = new(@"\.ROBLOSECURITY=[A-Za-z0-9_\-\.]{30,}", RegexOptions.Compiled);
    private static readonly Regex WarningMarkerRegex = new(@"_\|WARNING:-DO-NOT-SHARE-THIS[A-Za-z0-9_\-\.]{30,}", RegexOptions.Compiled);

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
    public void TrackedSourceAndDocs_ContainNoRobloxSecrets()
    {
        string repoRoot = FindRepoRoot();

        List<string> candidateFiles = new();

        // 1. All files under src/
        string srcDir = Path.Combine(repoRoot, "src");
        if (Directory.Exists(srcDir))
        {
            candidateFiles.AddRange(Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories));
        }

        // 2. All files under docs/
        string docsDir = Path.Combine(repoRoot, "docs");
        if (Directory.Exists(docsDir))
        {
            candidateFiles.AddRange(Directory.GetFiles(docsDir, "*.*", SearchOption.AllDirectories));
        }

        // 3. Root markdown documents
        candidateFiles.AddRange(Directory.GetFiles(repoRoot, "*.md", SearchOption.TopDirectoryOnly));

        // Filter out ignored / binary / obj / bin directories and tests
        string[] targetFiles = candidateFiles
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "prompts" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar))
            .Distinct()
            .ToArray();

        List<string> leakViolations = new();

        foreach (string file in targetFiles)
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            // Exclude definition markers in logging enricher or sanitizers
            string relativePath = Path.GetRelativePath(repoRoot, file);
            if (relativePath.EndsWith("RobloxSecretRedactionEnricher.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (WarningMarkerRegex.IsMatch(content))
            {
                leakViolations.Add($"{relativePath}: contains _|WARNING:-DO-NOT-SHARE-THIS pattern with actual credential payload");
            }

            if (RobloxCookieRegex.IsMatch(content))
            {
                leakViolations.Add($"{relativePath}: contains .ROBLOSECURITY pattern with actual credential payload");
            }
        }

        Assert.True(
            leakViolations.Count == 0,
            $"Found potential secret leaks in production source or docs:{Environment.NewLine}{string.Join(Environment.NewLine, leakViolations)}");
    }
}
