namespace RbxManager.Infrastructure.Tests.Auth;

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Infrastructure.Auth;
using RbxManager.Infrastructure.Http;

public sealed class RobloxAuthServiceTests
{
    private const string FakeCookie = "FAKE_COOKIE_FOR_TESTS_123";
    private const string FakeTicket = "FAKE_TICKET_FOR_TESTS_456";

    private sealed class DelegatingMockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public DelegatingMockHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private static HttpClient CreateClient(HttpMessageHandler mockHandler)
    {
        RobloxHostGuardHandler guard = new(mockHandler);
        RobloxCsrfHandler csrf = new(guard);
        RobloxRetryHandler retry = new(csrf, TimeSpan.Zero); // Zero delay for instant test execution
        return new HttpClient(retry);
    }

    [Fact]
    public async Task ValidateCookieAsync_200Response_MapsToRobloxUser()
    {
        string json = """{"id": 1234567, "name": "TestUser", "displayName": "Display User"}""";

        using HttpClient client = CreateClient(new DelegatingMockHandler(req =>
        {
            HttpResponseMessage resp = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }));

        RobloxAuthService service = new(client);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);

        Result<RobloxUser> result = await service.ValidateCookieAsync(cookie);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1234567, result.Value.Id);
        Assert.Equal("TestUser", result.Value.Name);
        Assert.Equal("Display User", result.Value.DisplayName);
    }

    [Fact]
    public async Task ValidateCookieAsync_401Unauthorized_ReturnsExpiredCode()
    {
        using HttpClient client = CreateClient(new DelegatingMockHandler(req =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));

        RobloxAuthService service = new(client);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);

        Result<RobloxUser> result = await service.ValidateCookieAsync(cookie);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthErrorCodes.Expired, result.ErrorCode);
    }

    [Fact]
    public async Task ValidateCookieAsync_429Then200_SucceedsAfterRetry()
    {
        int attempts = 0;
        string json = """{"id": 999, "name": "RetrySuccess", "displayName": "Retry Success"}""";

        using HttpClient client = CreateClient(new DelegatingMockHandler(req =>
        {
            attempts++;
            if (attempts == 1)
            {
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429));
            }

            HttpResponseMessage success = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(success);
        }));

        RobloxAuthService service = new(client);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);

        Result<RobloxUser> result = await service.ValidateCookieAsync(cookie);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempts);
        Assert.Equal(999, result.Value!.Id);
    }

    [Fact]
    public async Task ValidateCookieAsync_Persistent429_ReturnsRateLimitedCode()
    {
        int attempts = 0;
        using HttpClient client = CreateClient(new DelegatingMockHandler(req =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429));
        }));

        RobloxAuthService service = new(client);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);

        Result<RobloxUser> result = await service.ValidateCookieAsync(cookie);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthErrorCodes.RateLimited, result.ErrorCode);
        Assert.Equal(4, attempts); // 1 initial + 3 retries
    }

    [Fact]
    public async Task ValidateCookieAsync_500Then200_SucceedsAfterRetry()
    {
        int attempts = 0;
        string json = """{"id": 888, "name": "ServerErrorRecovered", "displayName": "Server Error Recovered"}""";

        using HttpClient client = CreateClient(new DelegatingMockHandler(req =>
        {
            attempts++;
            if (attempts < 3)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            HttpResponseMessage success = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(success);
        }));

        RobloxAuthService service = new(client);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);

        Result<RobloxUser> result = await service.ValidateCookieAsync(cookie);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, attempts);
        Assert.Equal(888, result.Value!.Id);
    }

    [Fact]
    public async Task GetAuthTicketAsync_403CsrfThenRetryWithToken_ReturnsTicketFromHeader()
    {
        int attempts = 0;
        const string expectedCsrfToken = "EXPECTED_CSRF_TOKEN_777";

        using HttpClient client = CreateClient(new DelegatingMockHandler(req =>
        {
            attempts++;
            if (attempts == 1)
            {
                HttpResponseMessage forbidden = new(HttpStatusCode.Forbidden);
                forbidden.Headers.Add("x-csrf-token", expectedCsrfToken);
                return Task.FromResult(forbidden);
            }

            Assert.True(req.Headers.Contains("X-CSRF-TOKEN"));
            Assert.Equal(expectedCsrfToken, System.Linq.Enumerable.First(req.Headers.GetValues("X-CSRF-TOKEN")));

            HttpResponseMessage ok = new(HttpStatusCode.OK);
            ok.Headers.Add("rbx-authentication-ticket", FakeTicket);
            return Task.FromResult(ok);
        }));

        RobloxAuthService service = new(client);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);

        Result<SecretValue> result = await service.GetAuthTicketAsync(cookie);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempts);
        Assert.NotNull(result.Value);
        Assert.Equal(FakeTicket, result.Value.Reveal());
    }

    [Fact]
    public async Task GetAuthTicketAsync_401Unauthorized_ReturnsExpiredCode()
    {
        using HttpClient client = CreateClient(new DelegatingMockHandler(req =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));

        RobloxAuthService service = new(client);
        using SecretValue cookie = SecretValue.FromString(FakeCookie);

        Result<SecretValue> result = await service.GetAuthTicketAsync(cookie);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthErrorCodes.Expired, result.ErrorCode);
    }
}
