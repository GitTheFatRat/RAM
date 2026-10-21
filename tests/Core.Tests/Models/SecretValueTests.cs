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

    [Fact]
    public void Clone_CreatesIndependentBuffer_DisposingOriginalDoesNotAffectClone()
    {
        SecretValue original = SecretValue.FromString(FakeCookie);
        using SecretValue clone = original.Clone();

        original.Dispose();

        Assert.Equal(FakeCookie, clone.Reveal());
    }

    [Fact]
    public void Clone_DisposingCloneDoesNotAffectOriginal()
    {
        using SecretValue original = SecretValue.FromString(FakeCookie);
        SecretValue clone = original.Clone();

        clone.Dispose();

        Assert.Equal(FakeCookie, original.Reveal());
    }

    [Fact]
    public void Clone_AfterDispose_ThrowsObjectDisposedException()
    {
        SecretValue secret = SecretValue.FromString(FakeCookie);
        secret.Dispose();

        Assert.Throws<ObjectDisposedException>(() => secret.Clone());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\t\r\n \v\f", false)]
    [InlineData("a", true)]
    [InlineData("   cookie   ", true)]
    [InlineData(FakeCookie, true)]
    public void HasContent_ReturnsExpectedValue(string input, bool expected)
    {
        using SecretValue secret = SecretValue.FromString(input);

        Assert.Equal(expected, secret.HasContent);
    }

    [Fact]
    public void HasContent_AfterDispose_ReturnsFalse()
    {
        SecretValue secret = SecretValue.FromString(FakeCookie);
        secret.Dispose();

        Assert.False(secret.HasContent);
    }
}
