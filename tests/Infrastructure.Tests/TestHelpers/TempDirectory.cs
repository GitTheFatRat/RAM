namespace RbxManager.Infrastructure.Tests.TestHelpers;

using System;
using System.IO;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RbxManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string CreateFilePath(string fileName = "vault.dat")
    {
        return System.IO.Path.Combine(Path, fileName);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
