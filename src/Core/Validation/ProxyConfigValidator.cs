namespace RbxManager.Core.Validation;

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public static class ProxyConfigValidator
{
    public const int MinimumPort = 1;
    public const int MaximumPort = 65535;
    public const int MaximumHostLength = 253;
    public const int MaximumUsernameLength = 128;
    public const int MaximumPasswordLength = 256;

    public static Result Validate(ProxyConfig? proxy)
    {
        if (proxy == null)
        {
            return Result.Success();
        }

        if (!Enum.IsDefined(proxy.Type))
        {
            return Result.Failure("Invalid proxy type specified.");
        }

        return ValidateStrict(proxy.Host, proxy.Port, proxy.Username, null);
    }

    public static Result ValidateStrict(string? host, int port, string? username = null, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Result.Failure("Proxy host cannot be empty.");
        }

        string trimmedHost = host.Trim();
        if (trimmedHost.Contains("://", StringComparison.Ordinal))
        {
            return Result.Failure("Proxy host should not include a protocol scheme (e.g. http://).");
        }

        if (trimmedHost.Length > MaximumHostLength)
        {
            return Result.Failure($"Proxy host exceeds maximum length of {MaximumHostLength} characters.");
        }

        if (trimmedHost.StartsWith('-'))
        {
            return Result.Failure("Proxy host cannot start with a hyphen.");
        }

        foreach (char c in trimmedHost)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '\'' || c == '"' || c == '/' || c == '\\' || c == '@' || c == ';' || c == ',' || c == '=')
            {
                return Result.Failure($"Proxy host contains forbidden character '{c}'. Proxy host must be a valid domain name or IP address.");
            }
        }

        string unbracketed = trimmedHost;
        if (unbracketed.StartsWith('[') && unbracketed.EndsWith(']'))
        {
            unbracketed = unbracketed[1..^1];
        }

        bool isIpv6 = IPAddress.TryParse(unbracketed, out IPAddress? parsedIp) &&
                      parsedIp.AddressFamily == AddressFamily.InterNetworkV6;

        if (!isIpv6 && trimmedHost.Contains(':'))
        {
            return Result.Failure("Proxy host cannot contain colons unless it is a valid IPv6 literal. Proxy host must be a valid domain name or IP address.");
        }

        if (!isIpv6)
        {
            if (trimmedHost.All(c => char.IsAsciiDigit(c) || c == '.'))
            {
                if (!IPAddress.TryParse(trimmedHost, out IPAddress? v4) || v4.AddressFamily != AddressFamily.InterNetwork)
                {
                    return Result.Failure("Proxy host must be a valid domain name or IP address.");
                }
            }
            else
            {
                foreach (char c in trimmedHost)
                {
                    if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-')
                    {
                        return Result.Failure($"Proxy host contains invalid character '{c}'. Proxy host must be a valid domain name or IP address.");
                    }
                }

                string[] labels = trimmedHost.Split('.');
                if (labels.Length == 0 || labels.Any(string.IsNullOrEmpty))
                {
                    return Result.Failure("Proxy host contains empty labels. Proxy host must be a valid domain name or IP address.");
                }

                foreach (string label in labels)
                {
                    if (label.StartsWith('-') || label.EndsWith('-'))
                    {
                        return Result.Failure("Proxy domain labels cannot start or end with a hyphen. Proxy host must be a valid domain name or IP address.");
                    }
                }
            }
        }

        if (port < MinimumPort || port > MaximumPort)
        {
            return Result.Failure($"Proxy port must be between {MinimumPort} and {MaximumPort}.");
        }

        if (username != null)
        {
            if (username.Length > MaximumUsernameLength)
            {
                return Result.Failure($"Proxy username cannot exceed {MaximumUsernameLength} characters.");
            }

            if (username.Any(char.IsControl))
            {
                return Result.Failure("Proxy username cannot contain control characters.");
            }
        }

        if (password != null)
        {
            if (password.Length > MaximumPasswordLength)
            {
                return Result.Failure($"Proxy password cannot exceed {MaximumPasswordLength} characters.");
            }

            if (password.Any(char.IsControl))
            {
                return Result.Failure("Proxy password cannot contain control characters.");
            }
        }

        return Result.Success();
    }
}
