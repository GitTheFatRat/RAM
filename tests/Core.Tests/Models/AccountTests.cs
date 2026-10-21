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
}
