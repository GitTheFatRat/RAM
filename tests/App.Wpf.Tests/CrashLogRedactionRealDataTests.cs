namespace RbxManager.App.Tests;

using System;
using System.IO;
using Xunit;

/// <summary>M44 Flow 1 real-data: crash_log.txt is written with File.AppendAllText, bypassing Serilog entirely.</summary>
public sealed class CrashLogRedactionRealDataTests
{
    private const string SecretTail = "FAKE0123456789ABCDEFCRASHTAIL";

    [Fact]
    public void WriteCrashLog_PhysicalFile_NeverContainsCookie()
    {
        string crashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
        string marker = "M44-" + Guid.NewGuid().ToString("N");
        InvalidOperationException crash = new(
            "Request failed: Cookie: .ROBLOSECURITY=_|WARNING:-DO-NOT-SHARE-THIS.--x|_" + SecretTail,
            new InvalidDataException("inner _|WARNING:-DO-NOT-SHARE-THIS.--y|_" + SecretTail));

        RbxManager.App.App.WriteCrashLog(marker, crash);

        string physical = File.ReadAllText(crashLogPath);
        string block = physical[physical.LastIndexOf(marker, StringComparison.Ordinal)..];
        Assert.Contains("System.InvalidOperationException", block, StringComparison.Ordinal);
        Assert.Contains("InnerException [1]", block, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretTail, block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DO-NOT-SHARE-THIS", block, StringComparison.OrdinalIgnoreCase);
    }
}
