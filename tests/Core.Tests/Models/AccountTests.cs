namespace RbxManager.Core.Tests.Models;

using System;
using RbxManager.Core.Models;

public class AccountTests
{
    private const string FakeCookie = "FAKE_COOKIE_FOR_TESTS_123";

    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        AccountId id = new(987654321);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;

        Account account = new(
            Id: id,
            Alias: "MainBot",
            Group: "Farming",
            Note: "Primary worker account",
            Cookie: cookie,
            CreatedAt: createdAt,
            Username: null);

        Assert.Equal(id, account.Id);
        Assert.Equal("MainBot", account.Alias);
        Assert.Equal("Farming", account.Group);
        Assert.Equal("Primary worker account", account.Note);
        Assert.Equal(cookie, account.Cookie);
        Assert.Equal(createdAt, account.CreatedAt);
        Assert.Null(account.Username);
    }

    [Fact]
    public void ToString_DoesNotExposePlaintextCookie()
    {
        AccountId id = new(12345);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(
            Id: id,
            Alias: "TestAccount",
            Group: null,
            Note: null,
            Cookie: cookie,
            CreatedAt: DateTimeOffset.UtcNow);

        string serialized = account.ToString();

        Assert.DoesNotContain(FakeCookie, serialized, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WithProxy_CarriesProxyCorrectly()
    {
        AccountId id = new(1);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        using ProxyConfig proxy = new("proxy.local", 8080);
        Account account = new(id, "WithProxy", null, null, cookie, DateTimeOffset.UtcNow, Proxy: proxy);

        Assert.NotNull(account.Proxy);
        Assert.Equal("proxy.local", account.Proxy.Host);
        Assert.Equal(8080, account.Proxy.Port);
    }

    [Fact]
    public void Constructor_WithoutProxy_ProxyIsNull()
    {
        AccountId id = new(2);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(id, "WithoutProxy", null, null, cookie, DateTimeOffset.UtcNow, Proxy: null);

        Assert.Null(account.Proxy);
    }
}
