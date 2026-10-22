namespace RbxManager.Core.Tests.Common;

using System;
using System.Collections.Generic;
using System.Linq;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using Xunit;

public sealed class AccountFilterSortTests
{
    private static Account CreateAccount(long id, string alias, string? group, string? username, AccountStatus status, DateTimeOffset created, DateTimeOffset? lastChecked)
    {
        using SecretValue cookie = SecretValue.FromString("dummy_cookie");
        return new Account(
            Id: new AccountId(id),
            Alias: alias,
            Group: group,
            Note: "Note for " + alias,
            Cookie: cookie.Clone(),
            CreatedAt: created,
            Username: username,
            UserId: id * 100,
            Status: status,
            LastCheckedAt: lastChecked);
    }

    [Fact]
    public void FilterAndSort_FiltersByGroup()
    {
        List<Account> accounts =
        [
            CreateAccount(1, "Alpha", "Main", "user1", AccountStatus.Valid, DateTimeOffset.UtcNow, null),
            CreateAccount(2, "Beta", "Alts", "user2", AccountStatus.Valid, DateTimeOffset.UtcNow, null),
            CreateAccount(3, "Gamma", "Main", "user3", AccountStatus.Valid, DateTimeOffset.UtcNow, null)
        ];

        List<Account> result = AccountFilterSort.FilterAndSort(accounts, "Main", null, "Alias", false).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal("Main", a.Group));
    }

    [Fact]
    public void FilterAndSort_FiltersBySearchText_AliasOrUsername()
    {
        List<Account> accounts =
        [
            CreateAccount(1, "Farming1", "Main", "FarmerBob", AccountStatus.Valid, DateTimeOffset.UtcNow, null),
            CreateAccount(2, "Trader2", "Trading", "MerchantDan", AccountStatus.Valid, DateTimeOffset.UtcNow, null),
            CreateAccount(3, "Sniper3", "Main", "BobShooter", AccountStatus.Valid, DateTimeOffset.UtcNow, null)
        ];

        // Search "Farmer"
        List<Account> res1 = AccountFilterSort.FilterAndSort(accounts, null, "Farmer", "Alias", false).ToList();
        Assert.Single(res1);
        Assert.Equal("Farming1", res1[0].Alias);

        // Search "Bob" (matches FarmerBob and BobShooter)
        List<Account> res2 = AccountFilterSort.FilterAndSort(accounts, null, "Bob", "Alias", false).ToList();
        Assert.Equal(2, res2.Count);
    }

    [Fact]
    public void FilterAndSort_SortsAscendingAndDescending()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<Account> accounts =
        [
            CreateAccount(1, "Zebra", "GroupB", "userZ", AccountStatus.Expired, now.AddDays(-2), now.AddHours(-1)),
            CreateAccount(2, "Apple", "GroupA", "userA", AccountStatus.Valid, now.AddDays(-1), now.AddHours(-5)),
            CreateAccount(3, "Mango", "GroupC", "userM", AccountStatus.Error, now.AddDays(-3), now.AddHours(-2))
        ];

        // Sort by Alias Asc
        List<Account> byAliasAsc = AccountFilterSort.FilterAndSort(accounts, null, null, "Alias", false).ToList();
        Assert.Equal("Apple", byAliasAsc[0].Alias);
        Assert.Equal("Mango", byAliasAsc[1].Alias);
        Assert.Equal("Zebra", byAliasAsc[2].Alias);

        // Sort by Alias Desc
        List<Account> byAliasDesc = AccountFilterSort.FilterAndSort(accounts, null, null, "Alias", true).ToList();
        Assert.Equal("Zebra", byAliasDesc[0].Alias);
        Assert.Equal("Mango", byAliasDesc[1].Alias);
        Assert.Equal("Apple", byAliasDesc[2].Alias);

        // Sort by Created Asc
        List<Account> byCreatedAsc = AccountFilterSort.FilterAndSort(accounts, null, null, "Created", false).ToList();
        Assert.Equal("Mango", byCreatedAsc[0].Alias); // oldest
        Assert.Equal("Apple", byCreatedAsc[2].Alias); // newest
    }
}
