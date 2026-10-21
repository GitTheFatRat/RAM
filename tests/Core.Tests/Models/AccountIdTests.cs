namespace RbxManager.Core.Tests.Models;

using RbxManager.Core.Models;

public class AccountIdTests
{
    [Fact]
    public void Constructor_WithPositiveValue_InitializesCorrectly()
    {
        const long expected = 123456789L;
        AccountId accountId = new(expected);

        Assert.Equal(expected, accountId.Value);
        Assert.Equal(expected, (long)accountId);
        Assert.Equal("123456789", accountId.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void Constructor_WithZeroOrNegativeValue_ThrowsArgumentOutOfRangeException(long invalidValue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountId(invalidValue));
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        AccountId id1 = new(42);
        AccountId id2 = new(42);

        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
    }
}
