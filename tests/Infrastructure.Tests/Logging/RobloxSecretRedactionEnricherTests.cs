namespace RbxManager.Infrastructure.Tests.Logging;

using System;
using System.Collections.Generic;
using RbxManager.Infrastructure.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

public sealed class RobloxSecretRedactionEnricherTests
{
    private const string RealisticCookie = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_FAKE0123456789ABCDEFSECRETTAIL";
    private const string StandaloneTail = "|_FAKE0123456789ABCDEFSECRETTAIL";

    private sealed class MemorySink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }

    [Theory]
    [InlineData("Valid test log with no secrets", "Valid test log with no secrets")]
    [InlineData("Found cookie: " + RealisticCookie, "[REDACTED]")]
    [InlineData("Found tail: " + StandaloneTail, "[REDACTED]")]
    [InlineData("Header set: .ROBLOSECURITY=fake-cookie-value", "[REDACTED]")]
    public void Enricher_RedactsSensitiveStrings_PreservesNormalStrings(string input, string expected)
    {
        var sink = new MemorySink();

        using Logger logger = new LoggerConfiguration()
            .Enrich.With<RobloxSecretRedactionEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("{Payload}", input);

        LogEvent logEvent = Assert.Single(sink.Events);
        Assert.True(logEvent.Properties.TryGetValue("Payload", out LogEventPropertyValue? propValue));
        ScalarValue scalar = Assert.IsType<ScalarValue>(propValue);
        Assert.Equal(expected, scalar.Value);
    }

    [Fact]
    public void Enricher_RedactsStructuredPropertiesRecursively()
    {
        var sink = new MemorySink();

        using Logger logger = new LoggerConfiguration()
            .Enrich.With<RobloxSecretRedactionEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var complexObject = new
        {
            SecretArray = new[] { "safe", RealisticCookie, StandaloneTail },
            Nested = new
            {
                CookieValue = RealisticCookie,
                NormalValue = "hello"
            },
            Dict = new Dictionary<string, string>
            {
                ["safe_key"] = RealisticCookie,
                [RealisticCookie] = "secret_as_key"
            }
        };

        logger.Information("{@Complex}", complexObject);

        LogEvent logEvent = Assert.Single(sink.Events);
        string rendered = logEvent.Properties["Complex"].ToString();

        Assert.DoesNotContain("WARNING:-DO-NOT-SHARE-THIS", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAKE0123456789ABCDEFSECRETTAIL", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", rendered, StringComparison.Ordinal);
        Assert.Contains("hello", rendered, StringComparison.Ordinal);
    }
}
