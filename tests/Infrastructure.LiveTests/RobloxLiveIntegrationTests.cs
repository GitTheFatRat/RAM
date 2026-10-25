namespace RbxManager.Infrastructure.LiveTests;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Servers;
using RbxManager.Infrastructure.Auth;
using RbxManager.Infrastructure.Http;
using RbxManager.Infrastructure.Servers;
using Xunit;

[Trait("Category", "Live")]
public sealed class RobloxLiveIntegrationTests
{
    private const string LiveTestCookieEnvVar = "RBX_LIVE_TEST_COOKIE";
    private const long BloxFruitsPlaceId = 2753915549;

    [Fact]
    public async Task ValidateCookieAsync_WithRealCookie_ReturnsAuthenticatedRobloxUser()
    {
        string? rawCookie = Environment.GetEnvironmentVariable(LiveTestCookieEnvVar);
        if (string.IsNullOrWhiteSpace(rawCookie))
        {
            return;
        }

        RobloxHttpClientFactory clientFactory = new();
        RobloxAuthService authService = new(clientFactory);

        using SecretValue cookie = SecretValue.FromString(rawCookie.Trim());
        Result<RobloxUser> result = await authService.ValidateCookieAsync(cookie);

        Assert.True(result.IsSuccess, $"Live cookie validation failed: {result.Error} ({result.ErrorCode})");
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Id > 0, $"Expected UserId > 0, got {result.Value.Id}");
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Name));
    }

    [Fact]
    public async Task GetServersAsync_WithBloxFruitsPlaceId_ReturnsLiveServersWithValidJobIdsAndCounts()
    {
        string? rawCookie = Environment.GetEnvironmentVariable(LiveTestCookieEnvVar);
        if (string.IsNullOrWhiteSpace(rawCookie))
        {
            return;
        }

        RobloxHttpClientFactory clientFactory = new();
        using HttpClient httpClient = clientFactory.CreateClient();
        ServerBrowserService serverBrowserService = new(httpClient);

        Result<ServerPage> result = await serverBrowserService.GetServersAsync(BloxFruitsPlaceId, ServerSort.Descending);

        Assert.True(result.IsSuccess, $"Live server browser query failed: {result.Error} ({result.ErrorCode})");
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.Servers);

        foreach (ServerInfo server in result.Value.Servers)
        {
            Assert.False(string.IsNullOrWhiteSpace(server.Id));
            Assert.True(server.MaxPlayers > 0, $"Expected MaxPlayers > 0, got {server.MaxPlayers}");
            Assert.True(server.Playing >= 0 && server.Playing <= server.MaxPlayers,
                $"Expected Playing in [0, {server.MaxPlayers}], got {server.Playing}");
        }
    }

    [Fact]
    public async Task GetAuthTicketAsync_WithRealCookie_ReturnsTicket()
    {
        string? rawCookie = Environment.GetEnvironmentVariable(LiveTestCookieEnvVar);
        if (string.IsNullOrWhiteSpace(rawCookie))
        {
            return;
        }

        RobloxAuthService authService = new(new RobloxHttpClientFactory());
        using SecretValue cookie = SecretValue.FromString(rawCookie.Trim());

        Result<SecretValue> result = await authService.GetAuthTicketAsync(cookie);

        Assert.True(
            result.IsSuccess,
            $"Live ticket request failed: {result.Error} ({result.ErrorCode}), HTTP {result.Details?.HttpStatus}, csrf={result.Details?.CsrfTokenObtained}");
        result.Value!.Dispose();
    }

    [Fact]
    public async Task FindUserServerAsync_WithOwnUsername_ReturnsServerOrHiddenNotTransportError()
    {
        string? rawCookie = Environment.GetEnvironmentVariable(LiveTestCookieEnvVar);
        if (string.IsNullOrWhiteSpace(rawCookie))
        {
            return;
        }

        RobloxHttpClientFactory clientFactory = new();
        using SecretValue cookie = SecretValue.FromString(rawCookie.Trim());
        Result<RobloxUser> self = await new RobloxAuthService(clientFactory).ValidateCookieAsync(cookie);
        Assert.True(self.IsSuccess, $"Live cookie validation failed: {self.Error} ({self.ErrorCode})");

        using HttpClient httpClient = clientFactory.CreateClient();
        Result<TargetServer> result = await new PresenceService(httpClient).FindUserServerAsync(self.Value!.Name, cookie);

        // The cookie owner is usually not in-game while the test runs, so ServerHidden is a valid live answer.
        Assert.True(
            result.IsSuccess || result.ErrorCode == PresenceErrorCodes.ServerHidden,
            $"Live presence lookup failed: {result.Error} ({result.ErrorCode})");
    }
}
