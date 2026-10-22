namespace RbxManager.Infrastructure.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

public sealed class RobloxSecretRedactionEnricher : ILogEventEnricher
{
    private static readonly Regex SecretRedactRegex = new(
        @"(_\|WARNING:-DO-NOT-SHARE-THIS\S*)|(\.ROBLOSECURITY=\S*)|(\|_[A-Za-z0-9]{16,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (KeyValuePair<string, LogEventPropertyValue> kvp in logEvent.Properties.ToList())
        {
            LogEventPropertyValue redacted = RedactPropertyValue(kvp.Value);
            if (!ReferenceEquals(redacted, kvp.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(kvp.Key, redacted));
            }
        }
    }

    private static LogEventPropertyValue RedactPropertyValue(LogEventPropertyValue value)
    {
        if (value is ScalarValue scalar)
        {
            if (scalar.Value is string text && ContainsSensitiveMarker(text))
            {
                return new ScalarValue("[REDACTED]");
            }
            return scalar;
        }

        if (value is SequenceValue seq)
        {
            bool changed = false;
            List<LogEventPropertyValue> newElements = new(seq.Elements.Count);
            foreach (LogEventPropertyValue elem in seq.Elements)
            {
                LogEventPropertyValue redacted = RedactPropertyValue(elem);
                if (!ReferenceEquals(redacted, elem))
                {
                    changed = true;
                }
                newElements.Add(redacted);
            }
            return changed ? new SequenceValue(newElements) : seq;
        }

        if (value is StructureValue structVal)
        {
            bool changed = false;
            List<LogEventProperty> newProps = new(structVal.Properties.Count);
            foreach (LogEventProperty prop in structVal.Properties)
            {
                LogEventPropertyValue redacted = RedactPropertyValue(prop.Value);
                if (!ReferenceEquals(redacted, prop.Value))
                {
                    changed = true;
                    newProps.Add(new LogEventProperty(prop.Name, redacted));
                }
                else
                {
                    newProps.Add(prop);
                }
            }
            return changed ? new StructureValue(newProps, structVal.TypeTag) : structVal;
        }

        if (value is DictionaryValue dict)
        {
            bool changed = false;
            Dictionary<ScalarValue, LogEventPropertyValue> newDict = new(dict.Elements.Count);
            foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> kv in dict.Elements)
            {
                ScalarValue key = kv.Key;
                if (key.Value is string keyText && ContainsSensitiveMarker(keyText))
                {
                    key = new ScalarValue("[REDACTED]");
                    changed = true;
                }
                LogEventPropertyValue redactedVal = RedactPropertyValue(kv.Value);
                if (!ReferenceEquals(redactedVal, kv.Value))
                {
                    changed = true;
                }
                newDict[key] = redactedVal;
            }
            return changed ? new DictionaryValue(newDict) : dict;
        }

        return value;
    }

    public static bool ContainsSensitiveMarker(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return SecretRedactRegex.IsMatch(text);
    }
}
