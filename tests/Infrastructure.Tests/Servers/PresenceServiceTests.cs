namespace RbxManager.Infrastructure.Tests.Servers;

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Servers;
using RbxManager.Infrastructure.Servers;

public sealed class PresenceServiceTests
{
    private const string JobId = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string UsernamesJson = """{"data":[{"requestedUsername":"Builderman","id":156,"name":"builderman","displayName":"builderman"}]}""";

    private sealed class RoutingHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
    }

    private static PresenceService CreateService(string usernamesJson, string presenceJson, Action<HttpRequestMessage>? inspect = null)
    {
        HttpClient client = new(new RoutingHandler(request =>
        {
            inspect?.Invoke(request);
            return request.RequestUri!.Host == "users.roblox.com" ? usernamesJson : presenceJson;
        }));
        return new PresenceService(client);
    }

    [Fact]
    public async Task FindUserServerAsync_TargetInVisibleServer_ReturnsPlaceAndJob()
    {
        string presenceJson = $$"""{"userPresences":[{"userPresenceType":2,"placeId":1818,"gameId":"{{JobId}}","userId":156}]}""";
        PresenceService service = CreateService(UsernamesJson, presenceJson, request =>
        {
            bool isPresence = request.RequestUri!.Host == "presence.roblox.com";
            Assert.Equal(isPresence, request.Headers.Contains("Cookie"));
            Assert.Equal(HttpMethod.Post, request.Method);
        });

        Result<TargetServer> result = await service.FindUserServerAsync(" Builderman ", SecretValue.FromString("cookie"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new TargetServer(156, 1818, JobId), result.Value);
    }

    [Fact]
    public async Task FindUserServerAsync_UnknownUsername_ReturnsUserNotFound()
    {
        PresenceService service = CreateService("""{"data":[]}""", "{}");

        Result<TargetServer> result = await service.FindUserServerAsync("nobody", SecretValue.FromString("cookie"));

        Assert.False(result.IsSuccess);
        Assert.Equal(PresenceErrorCodes.UserNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task FindUserServerAsync_GameIdHidden_ReturnsServerHidden()
    {
        string presenceJson = """{"userPresences":[{"userPresenceType":2,"placeId":null,"gameId":null,"userId":156}]}""";
        PresenceService service = CreateService(UsernamesJson, presenceJson);

        Result<TargetServer> result = await service.FindUserServerAsync("Builderman", SecretValue.FromString("cookie"));

        Assert.False(result.IsSuccess);
        Assert.Equal(PresenceErrorCodes.ServerHidden, result.ErrorCode);
    }
}
