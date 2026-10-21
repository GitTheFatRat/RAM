namespace RbxManager.Infrastructure.Logging;

using System;
using System.Linq;
using Serilog.Core;
using Serilog.Events;

public sealed class RobloxSecretRedactionEnricher : ILogEventEnricher
{
    private static readonly string[] SensitiveMarkers =
    [
        "_|WARNING:-DO-NOT-SHARE-THIS",
        ".ROBLOSECURITY="
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var kvp in logEvent.Properties.ToList())
        {
            if (kvp.Value is ScalarValue scalar && scalar.Value is string text)
            {
                if (ContainsSensitiveMarker(text))
                {
                    logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(kvp.Key, "[REDACTED]"));
                }
            }
        }
    }

    public static bool ContainsSensitiveMarker(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (string marker in SensitiveMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
