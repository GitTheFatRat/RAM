namespace RbxManager.Infrastructure.Tests.Http;

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Infrastructure.Http;

public sealed class RobloxHostGuardHandlerTests
{
    private sealed class EchoHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task SendAsync_HttpsRobloxHostWithCookie_Succeeds()
    {
        using RobloxHostGuardHandler guard = new(new EchoHandler());
        using HttpMessageInvoker invoker = new(guard);

        using HttpRequestMessage request = new(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
        request.Headers.Add("Cookie", ".ROBLOSECURITY=FAKE_COOKIE_FOR_TESTS_123");

        HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_HttpsApexRobloxHostWithCookie_Succeeds()
    {
        using RobloxHostGuardHandler guard = new(new EchoHandler());
        using HttpMessageInvoker invoker = new(guard);

        using HttpRequestMessage request = new(HttpMethod.Get, "https://roblox.com/login");
        request.Headers.Add("Cookie", ".ROBLOSECURITY=FAKE_COOKIE_FOR_TESTS_123");

        HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_HttpWithCookie_ThrowsInvalidOperationException()
    {
        using RobloxHostGuardHandler guard = new(new EchoHandler());
        using HttpMessageInvoker invoker = new(guard);

        using HttpRequestMessage request = new(HttpMethod.Get, "http://users.roblox.com/v1/users/authenticated");
        request.Headers.Add("Cookie", ".ROBLOSECURITY=FAKE_COOKIE_FOR_TESTS_123");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.SendAsync(request, CancellationToken.None));

        Assert.DoesNotContain("FAKE_COOKIE_FOR_TESTS_123", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://roblox.com.evil.com/test")]
    [InlineData("https://notroblox.com/test")]
    [InlineData("https://evil-roblox.com/test")]
    [InlineData("https://example.com/test")]
    public async Task SendAsync_NonRobloxHostWithCookie_ThrowsInvalidOperationException(string url)
    {
        using RobloxHostGuardHandler guard = new(new EchoHandler());
        using HttpMessageInvoker invoker = new(guard);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Add("Cookie", ".ROBLOSECURITY=FAKE_COOKIE_FOR_TESTS_123");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.SendAsync(request, CancellationToken.None));

        Assert.DoesNotContain("FAKE_COOKIE_FOR_TESTS_123", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_RequestWithoutCookie_NotBlockedByGuard()
    {
        using RobloxHostGuardHandler guard = new(new EchoHandler());
        using HttpMessageInvoker invoker = new(guard);

        using HttpRequestMessage request = new(HttpMethod.Get, "https://example.com/public-endpoint");
        // No Cookie header

        HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
