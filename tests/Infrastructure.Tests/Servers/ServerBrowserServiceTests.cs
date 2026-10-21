namespace RbxManager.Infrastructure.Tests.Servers;

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Servers;
using RbxManager.Infrastructure.Http;
using RbxManager.Infrastructure.Servers;

public sealed class ServerBrowserServiceTests
{
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

    [Fact]
    public async Task GetServersAsync_ValidJson_ParsesCorrectly()
    {
        string json = """
        {
            "previousPageCursor": null,
            "nextPageCursor": "cursor_abc123",
            "data": [
                {
                    "id": "server-guid-1",
                    "maxPlayers": 20,
                    "playing": 18,
                    "playerTokens": [],
                    "players": [],
                    "fps": 59.8,
                    "ping": 45
                },
                {
                    "id": "server-guid-2",
                    "maxPlayers": 20,
                    "playing": 10,
                    "playerTokens": [],
                    "players": [],
                    "fps": null,
                    "ping": null
                }
            ]
        }
        """;

        using HttpClient client = new(new DelegatingMockHandler(req =>
        {
            Assert.Contains("games.roblox.com/v1/games/1818/servers/Public", req.RequestUri!.ToString(), StringComparison.Ordinal);
            Assert.False(req.Headers.Contains("Cookie"));
            HttpResponseMessage resp = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }));

        ServerBrowserService service = new(client);
        Result<ServerPage> result = await service.GetServersAsync(1818, ServerSort.Descending);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("cursor_abc123", result.Value.NextCursor);
        Assert.Equal(2, result.Value.Servers.Count);

        ServerInfo s1 = result.Value.Servers[0];
        Assert.Equal("server-guid-1", s1.Id);
        Assert.Equal(18, s1.Playing);
        Assert.Equal(20, s1.MaxPlayers);
        Assert.Equal(59.8, s1.Fps);
        Assert.Equal(45, s1.Ping);

        ServerInfo s2 = result.Value.Servers[1];
        Assert.Equal("server-guid-2", s2.Id);
        Assert.Equal(10, s2.Playing);
        Assert.Equal(20, s2.MaxPlayers);
        Assert.Null(s2.Fps);
        Assert.Null(s2.Ping);
    }

    [Fact]
    public async Task GetServersAsync_PaginationCursor_AppendsToQuery()
    {
        string? requestedUrl = null;
        using HttpClient client = new(new DelegatingMockHandler(req =>
        {
            requestedUrl = req.RequestUri!.ToString();
            HttpResponseMessage resp = new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }));

        ServerBrowserService service = new(client);
        await service.GetServersAsync(1818, ServerSort.Ascending, "test_cursor_123");

        Assert.NotNull(requestedUrl);
        Assert.Contains("cursor=test_cursor_123", requestedUrl, StringComparison.Ordinal);
        Assert.Contains("sortOrder=Asc", requestedUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetServersAsync_SortParameter_AscAndDescWork()
    {
        string? requestedUrl = null;
        using HttpClient client = new(new DelegatingMockHandler(req =>
        {
            requestedUrl = req.RequestUri!.ToString();
            HttpResponseMessage resp = new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }));

        ServerBrowserService service = new(client);
        await service.GetServersAsync(1818, ServerSort.Descending);
        Assert.Contains("sortOrder=Desc", requestedUrl, StringComparison.Ordinal);

        await service.GetServersAsync(1818, ServerSort.Ascending);
        Assert.Contains("sortOrder=Asc", requestedUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetServersAsync_429WithRobloxRetryHandler_RetriesAndSucceeds()
    {
        int attempt = 0;
        DelegatingMockHandler inner = new(_ =>
        {
            attempt++;
            if (attempt == 1)
            {
                HttpResponseMessage rateLimited = new((HttpStatusCode)429);
                rateLimited.Headers.Add("Retry-After", "0");
                return Task.FromResult(rateLimited);
            }

            HttpResponseMessage ok = new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(ok);
        });

        RobloxRetryHandler retryHandler = new(inner, TimeSpan.Zero);
        using HttpClient client = new(retryHandler);

        ServerBrowserService service = new(client);
        Result<ServerPage> result = await service.GetServersAsync(1818);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task GetServersAsync_MalformedJson_ReturnsUnexpectedWithoutThrowing()
    {
        using HttpClient client = new(new DelegatingMockHandler(_ =>
        {
            HttpResponseMessage resp = new(HttpStatusCode.OK)
            {
                Content = new StringContent("This is not JSON {invalid}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }));

        ServerBrowserService service = new(client);
        Result<ServerPage> result = await service.GetServersAsync(1818);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthErrorCodes.Unexpected, result.ErrorCode);
    }

    [Fact]
    public async Task GetServersAsync_MissingDataProperty_ReturnsUnexpected()
    {
        using HttpClient client = new(new DelegatingMockHandler(_ =>
        {
            HttpResponseMessage resp = new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"errors": [{"code": 404, "message": "Not found"}]}""", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }));

        ServerBrowserService service = new(client);
        Result<ServerPage> result = await service.GetServersAsync(1818);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthErrorCodes.Unexpected, result.ErrorCode);
    }

    [Fact]
    public async Task GetServersAsync_InvalidPlaceId_ReturnsFailure()
    {
        using HttpClient client = new(new DelegatingMockHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        ServerBrowserService service = new(client);
        Result<ServerPage> resultZero = await service.GetServersAsync(0);
        Result<ServerPage> resultNegative = await service.GetServersAsync(-5);

        Assert.False(resultZero.IsSuccess);
        Assert.Equal("InvalidPlaceId", resultZero.ErrorCode);

        Assert.False(resultNegative.IsSuccess);
        Assert.Equal("InvalidPlaceId", resultNegative.ErrorCode);
    }
}
