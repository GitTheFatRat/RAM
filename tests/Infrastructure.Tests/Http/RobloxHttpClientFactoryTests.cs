namespace RbxManager.Infrastructure.Tests.Http;

using System;
using System.Net;
using System.Net.Http;
using RbxManager.Core.Models;
using RbxManager.Infrastructure.Http;

public sealed class RobloxHttpClientFactoryTests
{
    private const string FakeProxyPassword = "FAKE_PROXY_PASSWORD_123!";

    [Fact]
    public void CreateHandler_NullProxy_DoesNotSetCustomProxy()
    {
        using SocketsHttpHandler handler = RobloxHttpClientFactory.CreateHandler(null);

        Assert.Null(handler.Proxy);
    }

    [Fact]
    public void CreateHandler_HttpProxyWithoutCredentials_SetsHttpWebProxy()
    {
        using ProxyConfig proxy = new("127.0.0.1", 8080, null, null, ProxyType.Http);
        using SocketsHttpHandler handler = RobloxHttpClientFactory.CreateHandler(proxy);

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        WebProxy webProxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal("http://127.0.0.1:8080/", webProxy.Address?.ToString());
        Assert.Null(webProxy.Credentials);
    }

    [Fact]
    public void CreateHandler_Socks5ProxyWithCredentials_SetsSocks5WebProxyAndCredentials()
    {
        using SecretValue password = SecretValue.FromString(FakeProxyPassword);
        using ProxyConfig proxy = new("192.168.1.50", 1080, "proxyuser", password, ProxyType.Socks5);
        using SocketsHttpHandler handler = RobloxHttpClientFactory.CreateHandler(proxy);

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        WebProxy webProxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal("socks5://192.168.1.50:1080/", webProxy.Address?.ToString());
        Assert.NotNull(webProxy.Credentials);

        Uri targetUri = new("http://192.168.1.50:1080");
        NetworkCredential? cred = webProxy.Credentials.GetCredential(targetUri, "Basic");
        Assert.NotNull(cred);
        Assert.Equal("proxyuser", cred.UserName);
        Assert.Equal(FakeProxyPassword, cred.Password);
    }

    [Fact]
    public void CreateClient_ReturnsConfiguredHttpClient()
    {
        RobloxHttpClientFactory factory = new();
        using ProxyConfig proxy = new("127.0.0.1", 8888, "user", null, ProxyType.Http);
        using HttpClient client = factory.CreateClient(proxy);

        Assert.NotNull(client);
        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.True(client.DefaultRequestHeaders.Contains("User-Agent"));
    }
}
