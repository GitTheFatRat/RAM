namespace RbxManager.Core.Security;

using System;

public static class ProxyAuthenticationDecision
{
    public static bool ShouldAuthenticate(
        IBasicAuthenticationChallenge challenge,
        string? configuredProxyHost,
        string? username,
        bool hasPassword)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        if (string.IsNullOrWhiteSpace(configuredProxyHost) ||
            string.IsNullOrWhiteSpace(username) ||
            !hasPassword)
        {
            challenge.Cancel();
            return false;
        }

        if (string.IsNullOrWhiteSpace(challenge.Uri))
        {
            challenge.Cancel();
            return false;
        }

        string challengeUriStr = challenge.Uri.Trim();
        string challengeHost;
        if (Uri.TryCreate(challengeUriStr, UriKind.Absolute, out Uri? parsedUri))
        {
            challengeHost = parsedUri.Host;
        }
        else if (Uri.TryCreate($"http://{challengeUriStr}", UriKind.Absolute, out Uri? fallbackUri))
        {
            challengeHost = fallbackUri.Host;
        }
        else
        {
            challenge.Cancel();
            return false;
        }

        string expectedHost = configuredProxyHost.Trim();
        if (expectedHost.StartsWith('[') && expectedHost.EndsWith(']'))
        {
            expectedHost = expectedHost[1..^1];
        }

        if (challengeHost.StartsWith('[') && challengeHost.EndsWith(']'))
        {
            challengeHost = challengeHost[1..^1];
        }

        if (!string.Equals(challengeHost, expectedHost, StringComparison.OrdinalIgnoreCase))
        {
            challenge.Cancel();
            return false;
        }

        return true;
    }
}
