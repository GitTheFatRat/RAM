namespace RbxManager.Core.Tests.Models;

using System;
using System.Reflection;
using RbxManager.Core.Models;

public class SecretValueTests
{
    private const string FakeCookie = "FAKE_COOKIE_FOR_TESTS_123";

    [Fact]
    public void FromString_RevealsExpectedValue()
    {
        using SecretValue secret = SecretValue.FromString(FakeCookie);

        Assert.Equal(FakeCookie, secret.Reveal());
    }

    [Fact]
    public void Reveal_AfterDispose_ThrowsObjectDisposedException()
    {
        SecretValue secret = SecretValue.FromString(FakeCookie);
        secret.Dispose();

        Assert.Throws<ObjectDisposedException>(() => secret.Reveal());
    }

    [Fact]
    public void ToString_ReturnsRedactedMarker()
    {
        using SecretValue secret = SecretValue.FromString(FakeCookie);

        Assert.Equal("[REDACTED]", secret.ToString());
    }

    [Fact]
    public void Dispose_ZeroesInternalBufferMemory()
    {
        SecretValue secret = SecretValue.FromString(FakeCookie);
        FieldInfo field = typeof(SecretValue).GetField("_bytes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        byte[] internalBytes = (byte[])field.GetValue(secret)!;

        Assert.Contains(internalBytes, b => b != 0);

        secret.Dispose();

        Assert.All(internalBytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Equals_WithIdenticalContent_ReturnsTrue()
    {
        using SecretValue secret1 = SecretValue.FromString(FakeCookie);
        using SecretValue secret2 = SecretValue.FromString(FakeCookie);

        Assert.True(secret1.Equals(secret2));
        Assert.True(secret1.Equals((object)secret2));
    }

    [Fact]
    public void Equals_WithDifferentContent_ReturnsFalse()
    {
        using SecretValue secret1 = SecretValue.FromString(FakeCookie);
        using SecretValue secret2 = SecretValue.FromString("DIFFERENT_FAKE_COOKIE");

        Assert.False(secret1.Equals(secret2));
    }

    [Fact]
    public void Equals_WithDisposedInstance_ReturnsFalse()
    {
        SecretValue secret1 = SecretValue.FromString(FakeCookie);
        using SecretValue secret2 = SecretValue.FromString(FakeCookie);
        secret1.Dispose();

        Assert.False(secret1.Equals(secret2));
        Assert.False(secret2.Equals(secret1));
    }

    [Fact]
    public void GetHashCode_ReturnsZeroWithoutThrowing()
    {
        using SecretValue secret = SecretValue.FromString(FakeCookie);

        Assert.Equal(0, secret.GetHashCode());
    }

    [Fact]
    public void FromString_NullArgument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SecretValue.FromString(null!));
    }
}
