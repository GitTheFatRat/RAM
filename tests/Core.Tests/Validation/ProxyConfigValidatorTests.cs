namespace RbxManager.Core.Tests.Validation;

using System;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Security;
using RbxManager.Core.Validation;

public sealed class ProxyConfigValidatorTests
{
    [Fact]
    public void Validate_NullProxy_ReturnsSuccess()
    {
        Result result = ProxyConfigValidator.Validate(null);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("127.0.0.1", 8080)]
    [InlineData("192.168.1.100", 1080)]
    [InlineData("proxy.example.com", 3128)]
    [InlineData("sub.domain.corp.net", 80)]
    [InlineData("localhost", 9050)]
    public void Validate_ValidHostAndPort_ReturnsSuccess(string host, int port)
    {
        using ProxyConfig proxy = new(host, port, "user", null, ProxyType.Http);
        Result result = ProxyConfigValidator.Validate(proxy);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceHost_ReturnsFailure(string host)
    {
        using ProxyConfig proxy = new(host, 8080);
        Result result = ProxyConfigValidator.Validate(proxy);
        Assert.False(result.IsSuccess);
        Assert.Contains("host cannot be empty", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("socks5://proxy.example.com")]
    [InlineData("https://localhost")]
    public void Validate_HostWithScheme_ReturnsFailure(string host)
    {
        using ProxyConfig proxy = new(host, 8080);
        Result result = ProxyConfigValidator.Validate(proxy);
        Assert.False(result.IsSuccess);
        Assert.Contains("protocol scheme", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid..domain..")]
    [InlineData("inv@lid:host")]
    [InlineData("999.999.999.999")]
    public void Validate_InvalidHost_ReturnsFailure(string host)
    {
        using ProxyConfig proxy = new(host, 8080);
        Result result = ProxyConfigValidator.Validate(proxy);
        Assert.False(result.IsSuccess);
        Assert.Contains("valid domain name or IP", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(65536)]
    [InlineData(70000)]
    public void Validate_PortOutOfRange_ReturnsFailure(int port)
    {
        using ProxyConfig proxy = new("127.0.0.1", port);
        Result result = ProxyConfigValidator.Validate(proxy);
        Assert.False(result.IsSuccess);
        Assert.Contains("between 1 and 65535", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(443)]
    [InlineData(8080)]
    [InlineData(65535)]
    public void Validate_PortAtBoundary_ReturnsSuccess(int port)
    {
        using ProxyConfig proxy = new("127.0.0.1", port);
        Result result = ProxyConfigValidator.Validate(proxy);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_InvalidProxyType_ReturnsFailure()
    {
        using ProxyConfig proxy = new("127.0.0.1", 8080, null, null, (ProxyType)999);
        Result result = ProxyConfigValidator.Validate(proxy);
        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid proxy type", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProxyConfig_DeepClone_CreatesIndependentSecretValue()
    {
        using SecretValue password = SecretValue.FromString("ProxySecret123");
        using ProxyConfig original = new("127.0.0.1", 8080, "myuser", password, ProxyType.Socks5);
        using ProxyConfig cloned = original.DeepClone();

        Assert.Equal(original.Host, cloned.Host);
        Assert.Equal(original.Port, cloned.Port);
        Assert.Equal(original.Username, cloned.Username);
        Assert.Equal(original.Type, cloned.Type);
        Assert.NotNull(cloned.Password);
        Assert.False(ReferenceEquals(original.Password, cloned.Password));
        Assert.True(cloned.Password.HasContent);
    }

    [Fact]
    public void MasterPasswordPolicy_ValidationRules()
    {
        Assert.False(MasterPasswordPolicy.Validate(null).IsSuccess);
        Assert.False(MasterPasswordPolicy.Validate("").IsSuccess);
        Assert.False(MasterPasswordPolicy.Validate("   ").IsSuccess);
        Assert.False(MasterPasswordPolicy.Validate("short123").IsSuccess);
        Assert.False(MasterPasswordPolicy.Validate("12345678901").IsSuccess); // 11 chars
        Assert.True(MasterPasswordPolicy.Validate("123456789012").IsSuccess);  // 12 chars
        Assert.True(MasterPasswordPolicy.Validate("MySuperStrongPassword2026!").IsSuccess);
    }
}
