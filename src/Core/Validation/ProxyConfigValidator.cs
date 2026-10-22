namespace RbxManager.Core.Validation;

using System;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public static class ProxyConfigValidator
{
    public const int MinimumPort = 1;
    public const int MaximumPort = 65535;

    public static Result Validate(ProxyConfig? proxy)
    {
        if (proxy == null)
        {
            return Result.Success();
        }

        string trimmedHost = proxy.Host?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedHost))
        {
            return Result.Failure("Proxy host cannot be empty.");
        }

        if (trimmedHost.Contains("://", StringComparison.Ordinal))
        {
            return Result.Failure("Proxy host should not include a protocol scheme (e.g. http://).");
        }

        UriHostNameType hostType = Uri.CheckHostName(trimmedHost);
        if (hostType == UriHostNameType.Unknown)
        {
            return Result.Failure("Proxy host must be a valid domain name or IP address.");
        }

        if (trimmedHost.All(c => char.IsAsciiDigit(c) || c == '.') && !System.Net.IPAddress.TryParse(trimmedHost, out _))
        {
            return Result.Failure("Proxy host must be a valid domain name or IP address.");
        }

        if (proxy.Port < MinimumPort || proxy.Port > MaximumPort)
        {
            return Result.Failure($"Proxy port must be between {MinimumPort} and {MaximumPort}.");
        }

        if (!Enum.IsDefined(proxy.Type))
        {
            return Result.Failure("Invalid proxy type specified.");
        }

        return Result.Success();
    }
}
