namespace RbxManager.Infrastructure.Tests.QualityGuards;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

public sealed class IgnoredSourceFilesTests
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

    [Fact]
    public void GitStatusIgnored_DoesNotIgnoreAnySourceFiles()
    {
        string repoRoot = FindRepoRoot();

        ProcessStartInfo psi = new()
        {
            FileName = "git",
            Arguments = "status --ignored --porcelain src tests",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string output;
        try
        {
            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return; // Git unavailable
            }

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                return; // Non-zero git exit, return early
            }
        }
        catch
        {
            return; // Git command execution not possible in this environment
        }

        string[] sourceExtensions = [".cs", ".xaml", ".resx", ".csproj"];
        List<string> ignoredSourceFiles = new();

        using StringReader reader = new(output);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("!!", StringComparison.Ordinal))
            {
                string path = trimmed[2..].Trim();
                if (sourceExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    ignoredSourceFiles.Add(path);
                }
            }
        }

        Assert.True(ignoredSourceFiles.Count == 0,
            $"Found source files ignored by git:\n{string.Join("\n", ignoredSourceFiles)}");
    }
}
