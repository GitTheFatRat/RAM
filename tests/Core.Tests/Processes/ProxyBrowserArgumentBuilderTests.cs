namespace RbxManager.Core.Tests.Processes;

using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using Xunit;

public sealed class ProxyBrowserArgumentBuilderTests
{
    [Fact]
    public void Build_NullProxy_ReturnsNull()
    {
        string? result = ProxyBrowserArgumentBuilder.BuildAdditionalBrowserArguments(null);
        Assert.Null(result);
    }

    [Fact]
    public void Build_HttpProxy_ReturnsCorrectArgument()
    {
        using ProxyConfig proxy = new("127.0.0.1", 8080, Type: ProxyType.Http);
        string? result = ProxyBrowserArgumentBuilder.BuildAdditionalBrowserArguments(proxy);
        Assert.Equal("--proxy-server=http://127.0.0.1:8080", result);
    }

    [Fact]
    public void Build_Socks5Proxy_ReturnsCorrectArgument()
    {
        using ProxyConfig proxy = new("proxy.network.local", 1080, Type: ProxyType.Socks5);
        string? result = ProxyBrowserArgumentBuilder.BuildAdditionalBrowserArguments(proxy);
        Assert.Equal("--proxy-server=socks5://proxy.network.local:1080", result);
    }

    [Fact]
    public void Build_Ipv6Literal_EnclosesInBrackets()
    {
        using ProxyConfig proxy1 = new("2001:db8::1", 8080, Type: ProxyType.Http);
        string? result1 = ProxyBrowserArgumentBuilder.BuildAdditionalBrowserArguments(proxy1);
        Assert.Equal("--proxy-server=http://[2001:db8::1]:8080", result1);

        using ProxyConfig proxy2 = new("[::1]", 1080, Type: ProxyType.Socks5);
        string? result2 = ProxyBrowserArgumentBuilder.BuildAdditionalBrowserArguments(proxy2);
        Assert.Equal("--proxy-server=socks5://[::1]:1080", result2);
    }
}
