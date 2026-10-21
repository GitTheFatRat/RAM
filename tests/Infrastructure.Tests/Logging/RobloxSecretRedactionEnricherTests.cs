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
    [InlineData("Found cookie: _|WARNING:-DO-NOT-SHARE-THIS.--fake-cookie-data", "[REDACTED]")]
    [InlineData("Header set: .ROBLOSECURITY=fake-cookie-value", "[REDACTED]")]
    public void Enricher_RedactsSensitiveStrings_PreservesNormalStrings(string input, string expected)
    {
        var sink = new MemorySink();

        using var logger = new LoggerConfiguration()
            .Enrich.With<RobloxSecretRedactionEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("{Payload}", input);

        LogEvent logEvent = Assert.Single(sink.Events);
        Assert.True(logEvent.Properties.TryGetValue("Payload", out LogEventPropertyValue? propValue));
        var scalar = Assert.IsType<ScalarValue>(propValue);
        Assert.Equal(expected, scalar.Value);
    }
}
