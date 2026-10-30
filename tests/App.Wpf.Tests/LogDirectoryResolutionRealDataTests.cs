namespace RbxManager.App.Tests;

using System;
using System.IO;
using Xunit;

/// <summary>M44 Flow 1 real-data: log directory must never depend on the process working directory.</summary>
public sealed class LogDirectoryResolutionRealDataTests : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), "RbxM44Logs_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveLogDirectory_Default_IsAbsoluteAndAnchoredToExeFolder()
    {
        string resolved = RbxManager.App.App.ResolveLogDirectory();

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "logs"), resolved);
        Assert.True(Directory.Exists(resolved));
    }

    [Fact]
    public void ResolveLogDirectory_WritableBase_UsesBaseLogsFolder()
    {
        Directory.CreateDirectory(_baseDirectory);

        Assert.Equal(Path.Combine(_baseDirectory, "logs"), RbxManager.App.App.ResolveLogDirectory(_baseDirectory));
        Assert.Empty(Directory.GetFiles(Path.Combine(_baseDirectory, "logs")));
    }

    [Fact]
    public void ResolveLogDirectory_UnwritableBase_FallsBackToLocalAppData()
    {
        // A real file named "logs" makes the folder impossible to create, exactly like a read-only install dir.
        Directory.CreateDirectory(_baseDirectory);
        File.WriteAllText(Path.Combine(_baseDirectory, "logs"), "blocker");

        string resolved = RbxManager.App.App.ResolveLogDirectory(_baseDirectory);

        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RbxManager", "logs");
        Assert.Equal(expected, resolved);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
