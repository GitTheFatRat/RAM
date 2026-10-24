namespace RbxManager.Infrastructure.Servers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Common;
using RbxManager.Core.Servers;
using RbxManager.Infrastructure.Serialization;

public sealed partial class ServerBrowserService : IServerBrowserService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerBrowserService> _logger;

    public ServerBrowserService(HttpClient httpClient, ILogger<ServerBrowserService>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<ServerBrowserService>.Instance;
    }

    public async Task<Result<ServerPage>> GetServersAsync(
        long placeId,
        ServerSort sort = ServerSort.Descending,
        string? cursor = null,
        CancellationToken ct = default)
    {
        if (placeId <= 0)
        {
            return Result.Failure<ServerPage>("PlaceId must be greater than 0.", "InvalidPlaceId");
        }

        string sortStr = sort == ServerSort.Ascending ? "Asc" : "Desc";
        string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder={sortStr}&limit=100";
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url += $"&cursor={Uri.EscapeDataString(cursor.Trim())}";
        }

        LogFetchingServers(placeId, sortStr);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            // No cookie header - server browser is unauthenticated

            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if ((int)response.StatusCode == 429)
            {
                LogFetchFailed("RateLimited");
                return Result.Failure<ServerPage>("Rate limited while querying servers.", AuthErrorCodes.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                LogFetchFailed(response.StatusCode.ToString());
                return Result.Failure<ServerPage>($"Failed to fetch servers: {response.StatusCode}", AuthErrorCodes.Unexpected);
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            RobloxServerListResponse? doc = await JsonSerializer.DeserializeAsync(stream, RbxManagerJsonContext.Default.RobloxServerListResponse, ct).ConfigureAwait(false);

            if (doc == null || doc.Data == null)
            {
                LogFetchFailed("MalformedJson");
                return Result.Failure<ServerPage>("Malformed response from Roblox servers endpoint.", AuthErrorCodes.Unexpected);
            }

            List<ServerInfo> servers = new(doc.Data.Count);
            foreach (RobloxServerItem item in doc.Data)
            {
                if (!string.IsNullOrWhiteSpace(item.Id))
                {
                    servers.Add(new ServerInfo(
                        Id: item.Id,
                        Playing: item.Playing,
                        MaxPlayers: item.MaxPlayers,
                        Fps: item.Fps,
                        Ping: item.Ping));
                }
            }

            LogFetchSuccess(servers.Count, placeId);
            return Result.Success(new ServerPage(servers, doc.NextPageCursor));
        }
        catch (HttpRequestException)
        {
            LogFetchFailed(AuthErrorCodes.Network);
            return Result.Failure<ServerPage>("Network error querying Roblox servers.", AuthErrorCodes.Network);
        }
        catch (JsonException)
        {
            LogFetchFailed("MalformedJson");
            return Result.Failure<ServerPage>("Unable to parse Roblox servers response.", AuthErrorCodes.Unexpected);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            LogFetchFailed(AuthErrorCodes.Network);
            return Result.Failure<ServerPage>("Request timed out querying servers.", AuthErrorCodes.Network);
        }
        catch (Exception ex)
        {
            LogFetchError(ex.GetType().Name);
            return Result.Failure<ServerPage>("An unexpected error occurred querying servers.", AuthErrorCodes.Unexpected);
        }
    }

    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "Fetching public servers for place {PlaceId} with sort {Sort}.")]
    private partial void LogFetchingServers(long placeId, string sort);

    [LoggerMessage(EventId = 402, Level = LogLevel.Information, Message = "Successfully fetched {Count} servers for place {PlaceId}.")]
    private partial void LogFetchSuccess(int count, long placeId);

    [LoggerMessage(EventId = 403, Level = LogLevel.Warning, Message = "Failed to fetch servers: {Reason}.")]
    private partial void LogFetchFailed(string reason);

    [LoggerMessage(EventId = 404, Level = LogLevel.Error, Message = "Exception fetching servers: {ExceptionType}.")]
    private partial void LogFetchError(string exceptionType);

    internal sealed class RobloxServerListResponse
    {
        [JsonPropertyName("previousPageCursor")]
        public string? PreviousPageCursor { get; set; }

        [JsonPropertyName("nextPageCursor")]
        public string? NextPageCursor { get; set; }

        [JsonPropertyName("data")]
        public List<RobloxServerItem>? Data { get; set; }
    }

    internal sealed class RobloxServerItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("maxPlayers")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("playing")]
        public int Playing { get; set; }

        [JsonPropertyName("fps")]
        public double? Fps { get; set; }

        [JsonPropertyName("ping")]
        public int? Ping { get; set; }
    }
}
