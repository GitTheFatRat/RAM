namespace RbxManager.Infrastructure.Logging;

using System;
using System.Globalization;
using System.IO;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

/// <summary>
/// Scrubs the fully rendered line. <see cref="RobloxSecretRedactionEnricher"/> can only rewrite properties;
/// <see cref="LogEvent.Exception"/> is read-only and rendered via ToString() (message, inner exceptions, stack),
/// so a cookie inside an exception message would otherwise reach disk verbatim.
/// </summary>
public sealed class RedactingTextFormatter : ITextFormatter
{
    public const string DefaultOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    private readonly ITextFormatter _inner;

    public RedactingTextFormatter(ITextFormatter? inner = null)
    {
        _inner = inner ?? new MessageTemplateTextFormatter(DefaultOutputTemplate, CultureInfo.InvariantCulture);
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using StringWriter buffer = new(CultureInfo.InvariantCulture);
        _inner.Format(logEvent, buffer);
        output.Write(RobloxSecretRedactionEnricher.Redact(buffer.ToString()));
    }
}
