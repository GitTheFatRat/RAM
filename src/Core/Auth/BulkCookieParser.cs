namespace RbxManager.Core.Auth;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RbxManager.Core.Models;

public static partial class BulkCookieParser
{
    [GeneratedRegex(@"(_\|WARNING:-DO-NOT-SHARE-THIS[^\s""',;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RobloxCookiePattern();

    public static IReadOnlyList<SecretValue> ParseCookies(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return Array.Empty<SecretValue>();
        }

        List<SecretValue> results = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        string[] lines = rawInput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string extracted = ExtractRawCookieFromLine(line);
            if (!string.IsNullOrWhiteSpace(extracted) && seen.Add(extracted))
            {
                results.Add(SecretValue.FromString(extracted));
            }
        }

        return results;
    }

    private static string ExtractRawCookieFromLine(string line)
    {
        string trimmed = line.Trim().Trim('"', '\'');

        Match match = RobloxCookiePattern().Match(trimmed);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim('"', '\'');
        }

        const string prefix = ".ROBLOSECURITY=";
        int prefixIdx = trimmed.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIdx >= 0)
        {
            string afterPrefix = trimmed[(prefixIdx + prefix.Length)..].Trim();
            int semiIdx = afterPrefix.IndexOf(';');
            if (semiIdx >= 0)
            {
                afterPrefix = afterPrefix[..semiIdx];
            }
            return afterPrefix.Trim('"', '\'');
        }

        // Check User:Pass:Cookie format where Cookie might not have standard warning prefix (e.g., in synthetic test cookies)
        int firstColon = trimmed.IndexOf(':');
        if (firstColon > 0)
        {
            int secondColon = trimmed.IndexOf(':', firstColon + 1);
            if (secondColon > firstColon)
            {
                string candidate = trimmed[(secondColon + 1)..].Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
        }

        return trimmed;
    }
}
