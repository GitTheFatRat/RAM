namespace RbxManager.Infrastructure.Tests.Logging;

using System;
using System.IO;
using System.Linq;
using RbxManager.Infrastructure.Logging;
using RbxManager.Infrastructure.Tests.TestHelpers;
using Serilog;
using Xunit;

/// <summary>
/// M44 Flow 1 real-data: writes through the production enricher + formatter into a real rolling file on disk
/// and reads the physical bytes back. No in-memory sinks.
/// </summary>
public sealed class LogFileRedactionRealDataTests
{
    private const string SecretTail = "FAKE0123456789ABCDEFSECRETTAIL";
    private const string Cookie =
        "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_" + SecretTail;

    [Fact]
    public void ProductionFileSink_RedactsCookieInPropertiesExceptionsAndEncodedForms()
    {
        using TempDirectory temp = new();
        string logDirectory = Path.Combine(temp.Path, "logs");
        string urlEncoded = Uri.EscapeDataString(".ROBLOSECURITY=" + Cookie);

        using (Serilog.Core.Logger logger = new LoggerConfiguration()
                   .MinimumLevel.Information()
                   .Enrich.With<RobloxSecretRedactionEnricher>()
                   .WriteTo.File(new RedactingTextFormatter(), Path.Combine(logDirectory, "app-.log"), rollingInterval: RollingInterval.Day)
                   .CreateLogger())
        {
            logger.Information("Infrastructure service started");
            logger.Information("Cookie header {Header}", "Cookie: .ROBLOSECURITY=" + Cookie);
            logger.Information("Request uri {Uri}", new Uri("https://auth.roblox.com/?c=" + urlEncoded));
            logger.Warning("Encoded payload {Body}", urlEncoded);
            logger.Warning("Json payload {Json}", "{\".ROBLOSECURITY\":\"" + SecretTail + "\"}");
            logger.Error(
                new InvalidOperationException("Auth failed for " + Cookie, new HttpRequestException("inner .ROBLOSECURITY = " + SecretTail)),
                "Validation failed");
        }

        string[] files = Directory.GetFiles(logDirectory, "app-*.log");
        Assert.Single(files);
        string physical = File.ReadAllText(files[0]);

        Assert.Contains("Infrastructure service started", physical, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", physical, StringComparison.Ordinal);
        Assert.Contains(RobloxSecretRedactionEnricher.RedactedPlaceholder, physical, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretTail, physical, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DO-NOT-SHARE-THIS", physical, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%7C_", physical, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, physical.Split('\n').Count(line => line.Contains(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData(".ROBLOSECURITY=" + Cookie)]
    [InlineData(".ROBLOSECURITY%3D_%7CWARNING%3A-DO-NOT-SHARE-THIS.--x%7C_" + SecretTail)]
    [InlineData("{\".ROBLOSECURITY\": \"" + SecretTail + "\"}")]
    [InlineData(".ROBLOSECURITY = " + SecretTail)]
    public void Redact_RemovesSecretTail_FromEveryKnownEncoding(string input)
    {
        string redacted = RobloxSecretRedactionEnricher.Redact("prefix " + input + " suffix");

        Assert.DoesNotContain(SecretTail, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("prefix ", redacted, StringComparison.Ordinal);
    }
}
