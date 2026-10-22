namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

public sealed class NoHardCodedVietnameseInCoreOrInfraTests
{
    private static readonly Regex VietnameseLettersRegex = new(
        @"[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
    public void CoreAndInfrastructure_ContainNoVietnameseCharacters()
    {
        string repoRoot = FindRepoRoot();
        List<string> violations = new();

        string[] targetDirs =
        [
            Path.Combine(repoRoot, "src", "Core"),
            Path.Combine(repoRoot, "src", "Infrastructure")
        ];

        foreach (string dir in targetDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            string[] csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                            !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .ToArray();

            foreach (string file in csFiles)
            {
                string relativePath = Path.GetRelativePath(repoRoot, file);
                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (VietnameseLettersRegex.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{i + 1} -> {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found hard-coded Vietnamese characters in Core or Infrastructure:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
