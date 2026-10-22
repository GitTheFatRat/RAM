namespace RbxManager.Infrastructure.Tests.Http;

using System;
using System.Net.Http;
using RbxManager.Infrastructure.Http;

public sealed class RobloxHttpClientFactoryTests
{
    [Fact]
    public void CreateHandler_UsesDefaultHandlerConfiguration()
    {
        using SocketsHttpHandler handler = RobloxHttpClientFactory.CreateHandler();

        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Null(handler.Proxy);
    }

    [Fact]
    public void CreateClient_ReturnsConfiguredHttpClient()
    {
        RobloxHttpClientFactory factory = new();
        using HttpClient client = factory.CreateClient();

        Assert.NotNull(client);
        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.True(client.DefaultRequestHeaders.Contains("User-Agent"));
    }
}
