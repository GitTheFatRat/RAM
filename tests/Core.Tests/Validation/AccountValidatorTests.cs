namespace RbxManager.Core.Tests.Validation;

using System;
using System.Collections.Generic;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Validation;

public class AccountValidatorTests
{
    private const string FakeCookie = "FAKE_COOKIE_FOR_TESTS_123";

    [Fact]
    public void Validate_ValidAccount_ReturnsSuccess()
    {
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), "MainAccount", "Primary", "Notes here", cookie, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(account);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceAlias_ReturnsFailure(string alias)
    {
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), alias, null, null, cookie, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(account);

        Assert.False(result.IsSuccess);
        Assert.Equal("Alias is required.", result.Error);
    }

    [Fact]
    public void Validate_AliasExceeding50Chars_ReturnsFailure()
    {
        string longAlias = new('a', 51);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), longAlias, null, null, cookie, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(account);

        Assert.False(result.IsSuccess);
        Assert.Equal("Alias must be between 1 and 50 characters.", result.Error);
    }

    [Fact]
    public void Validate_GroupExceeding30Chars_ReturnsFailure()
    {
        string longGroup = new('g', 31);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), "ValidAlias", longGroup, null, cookie, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(account);

        Assert.False(result.IsSuccess);
        Assert.Equal("Group must not exceed 30 characters.", result.Error);
    }

    [Fact]
    public void Validate_NoteExceeding200Chars_ReturnsFailure()
    {
        string longNote = new('n', 201);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), "ValidAlias", null, longNote, cookie, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(account);

        Assert.False(result.IsSuccess);
        Assert.Equal("Note must not exceed 200 characters.", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyCookie_ReturnsFailure(string emptyCookie)
    {
        using SecretValue cookie = SecretValue.FromString(emptyCookie);
        Account account = new(new AccountId(1), "ValidAlias", null, null, cookie, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(account);

        Assert.False(result.IsSuccess);
        Assert.Equal("Cookie must not be empty.", result.Error);
    }

    [Fact]
    public void Validate_DuplicateAliasCaseInsensitive_ReturnsFailure()
    {
        using SecretValue cookie1 = SecretValue.FromString(FakeCookie);
        using SecretValue cookie2 = SecretValue.FromString("FAKE_COOKIE_FOR_TESTS_456");
        Account existing = new(new AccountId(1), "MyAccount", null, null, cookie1, DateTimeOffset.UtcNow);
        Account duplicate = new(new AccountId(2), "myaccount", null, null, cookie2, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(duplicate, new List<Account> { existing });

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_UpdatingSameAccount_DoesNotTriggerDuplicateError()
    {
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), "MyAccount", "Group", null, cookie, DateTimeOffset.UtcNow);

        Result result = AccountValidator.Validate(account, new List<Account> { account });

        Assert.True(result.IsSuccess);
    }
}
