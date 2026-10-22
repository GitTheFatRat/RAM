namespace RbxManager.Core.Tests.Security;

using RbxManager.Core.Security;
using Xunit;

public sealed class ProxyAuthenticationDecisionTests
{
    private sealed class FakeChallenge : IBasicAuthenticationChallenge
    {
        public string? Uri { get; set; }
        public string? Challenge { get; set; }
        public bool IsCancelled { get; private set; }
        public string? ResponseUsername { get; private set; }
        public string? ResponsePassword { get; private set; }

        public FakeChallenge(string? uri, string? challenge = "Basic realm=\"proxy\"")
        {
            Uri = uri;
            Challenge = challenge;
        }

        public void SetResponse(string username, string password)
        {
            ResponseUsername = username;
            ResponsePassword = password;
            IsCancelled = false;
        }

        public void Cancel()
        {
            IsCancelled = true;
        }
    }

    [Fact]
    public void ShouldAuthenticate_MatchingHostAndCredentials_ReturnsTrue()
    {
        FakeChallenge challenge = new("http://proxy.example.com:8080/auth");
        bool result = ProxyAuthenticationDecision.ShouldAuthenticate(
            challenge,
            configuredProxyHost: "proxy.example.com",
            username: "proxyuser",
            hasPassword: true);

        Assert.True(result);
        Assert.False(challenge.IsCancelled);
    }

    [Fact]
    public void ShouldAuthenticate_CaseInsensitiveHost_ReturnsTrue()
    {
        FakeChallenge challenge = new("http://PROXY.EXAMPLE.COM:8080/");
        bool result = ProxyAuthenticationDecision.ShouldAuthenticate(
            challenge,
            configuredProxyHost: "proxy.example.com",
            username: "proxyuser",
            hasPassword: true);

        Assert.True(result);
        Assert.False(challenge.IsCancelled);
    }

    [Fact]
    public void ShouldAuthenticate_HostMismatch_CancelsAndReturnsFalse()
    {
        FakeChallenge challenge = new("http://malicious.site.com:8080/auth");
        bool result = ProxyAuthenticationDecision.ShouldAuthenticate(
            challenge,
            configuredProxyHost: "proxy.example.com",
            username: "proxyuser",
            hasPassword: true);

        Assert.False(result);
        Assert.True(challenge.IsCancelled);
    }

    [Fact]
    public void ShouldAuthenticate_MissingCredentials_CancelsAndReturnsFalse()
    {
        FakeChallenge challenge = new("http://proxy.example.com:8080/");
        bool resultNoUser = ProxyAuthenticationDecision.ShouldAuthenticate(
            challenge,
            configuredProxyHost: "proxy.example.com",
            username: null,
            hasPassword: true);

        Assert.False(resultNoUser);
        Assert.True(challenge.IsCancelled);

        FakeChallenge challenge2 = new("http://proxy.example.com:8080/");
        bool resultNoPass = ProxyAuthenticationDecision.ShouldAuthenticate(
            challenge2,
            configuredProxyHost: "proxy.example.com",
            username: "proxyuser",
            hasPassword: false);

        Assert.False(resultNoPass);
        Assert.True(challenge2.IsCancelled);
    }

    [Fact]
    public void ShouldAuthenticate_InvalidChallengeUri_CancelsAndReturnsFalse()
    {
        FakeChallenge challenge = new("not_a_valid_uri:::$$$");
        bool result = ProxyAuthenticationDecision.ShouldAuthenticate(
            challenge,
            configuredProxyHost: "proxy.example.com",
            username: "proxyuser",
            hasPassword: true);

        Assert.False(result);
        Assert.True(challenge.IsCancelled);
    }
}
