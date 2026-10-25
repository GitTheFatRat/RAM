namespace RbxManager.Core.Tests.Architecture;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class ArchitectureTests
{
    private static readonly HashSet<string> AllowedRevealRelativePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Core/Models/SecretValue.cs",
        "src/Infrastructure/Storage/EncryptedFileVault.cs",
        "src/Infrastructure/Auth/RobloxAuthService.cs",
        "src/Infrastructure/Launch/RobloxLauncher.cs",
        "src/Infrastructure/Backup/EncryptedBackupService.cs",
        "src/Infrastructure/Servers/PresenceService.cs"
    };

    [Fact]
    public void SecretValue_Reveal_CalledOnlyByExplicitAllowlist()
    {
        string repoRoot = FindRepoRoot();
        string srcDir = Path.Combine(repoRoot, "src");

        Assert.True(Directory.Exists(srcDir), $"Source directory not found: {srcDir}");

        string[] csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();

        List<string> unauthorizedUsages = new();

        foreach (string file in csFiles)
        {
            string relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Contains(".Reveal(", StringComparison.Ordinal) && !AllowedRevealRelativePaths.Contains(relativePath))
                {
                    unauthorizedUsages.Add($"{relativePath}:{i + 1} -> {line.Trim()}");
                }
            }
        }

        Assert.True(
            unauthorizedUsages.Count == 0,
            $"Found unauthorized calls to SecretValue.Reveal():{Environment.NewLine}{string.Join(Environment.NewLine, unauthorizedUsages)}");
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
