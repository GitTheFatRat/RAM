namespace RbxManager.Infrastructure.Servers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Servers;
using RbxManager.Infrastructure.Serialization;

public sealed partial class PresenceService : IPresenceService
{
    private const string UsernamesUrl = "https://users.roblox.com/v1/usernames/users";
    private const string PresenceUrl = "https://presence.roblox.com/v1/presence/users";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PresenceService> _logger;

    public PresenceService(HttpClient httpClient, ILogger<PresenceService>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<PresenceService>.Instance;
    }

    public async Task<Result<TargetServer>> FindUserServerAsync(
        string targetUsername,
        SecretValue viewerCookie,
        CancellationToken ct = default)
    {
        string username = targetUsername?.Trim() ?? string.Empty;
        if (username.Length == 0)
        {
            return Result.Failure<TargetServer>("Target username is required.", PresenceErrorCodes.UserNotFound);
        }

        try
        {
            Result<long> userIdResult = await ResolveUserIdAsync(username, ct).ConfigureAwait(false);
            if (!userIdResult.IsSuccess)
            {
                return Result.Failure<TargetServer>(userIdResult.Error ?? "Target user not found.", userIdResult.ErrorCode);
            }

            return await FetchServerAsync(userIdResult.Value, viewerCookie, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            LogLookupFailed(AuthErrorCodes.Network);
            return Result.Failure<TargetServer>("Network error looking up target presence.", AuthErrorCodes.Network);
        }
        catch (JsonException)
        {
            LogLookupFailed("MalformedJson");
            return Result.Failure<TargetServer>("Unable to parse Roblox presence response.", AuthErrorCodes.Unexpected);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            LogLookupFailed(AuthErrorCodes.Network);
            return Result.Failure<TargetServer>("Request timed out looking up target presence.", AuthErrorCodes.Network);
        }
        catch (Exception)
        {
            LogLookupFailed(AuthErrorCodes.Unexpected);
            return Result.Failure<TargetServer>("Unexpected error looking up target presence.", AuthErrorCodes.Unexpected);
        }
    }

    private async Task<Result<long>> ResolveUserIdAsync(string username, CancellationToken ct)
    {
        UsernamesRequest body = new() { Usernames = [username], ExcludeBannedUsers = true };
        using HttpRequestMessage request = new(HttpMethod.Post, UsernamesUrl)
        {
            Content = JsonContent.Create(body, RbxManagerJsonContext.Default.UsernamesRequest)
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        Result? statusFailure = MapStatus(response, "username lookup");
        if (statusFailure != null)
        {
            return Result.Failure<long>(statusFailure.Error!, statusFailure.ErrorCode);
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        UsernamesResponse? doc = await JsonSerializer.DeserializeAsync(stream, RbxManagerJsonContext.Default.UsernamesResponse, ct).ConfigureAwait(false);
        long userId = doc?.Data is [{ Id: > 0 } user, ..] ? user.Id : 0;
        if (userId == 0)
        {
            LogLookupFailed(PresenceErrorCodes.UserNotFound);
            return Result.Failure<long>("Target user not found.", PresenceErrorCodes.UserNotFound);
        }

        return Result.Success(userId);
    }

    private async Task<Result<TargetServer>> FetchServerAsync(long userId, SecretValue viewerCookie, CancellationToken ct)
    {
        PresenceRequest body = new() { UserIds = [userId] };
        using HttpRequestMessage request = new(HttpMethod.Post, PresenceUrl)
        {
            Content = JsonContent.Create(body, RbxManagerJsonContext.Default.PresenceRequest)
        };
        string rawCookie = viewerCookie.Reveal().Trim();
        if (rawCookie.StartsWith(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase))
        {
            rawCookie = rawCookie[".ROBLOSECURITY=".Length..].Trim();
        }
        request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={rawCookie}");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        Result? statusFailure = MapStatus(response, "presence lookup");
        if (statusFailure != null)
        {
            return Result.Failure<TargetServer>(statusFailure.Error!, statusFailure.ErrorCode);
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        PresenceResponse? doc = await JsonSerializer.DeserializeAsync(stream, RbxManagerJsonContext.Default.PresenceResponse, ct).ConfigureAwait(false);
        UserPresenceItem? presence = doc?.UserPresences is [var first, ..] ? first : null;

        // gameId is only populated when the target is in-game AND their join privacy admits the viewer.
        if (presence is not { PlaceId: > 0 } || !Guid.TryParse(presence.GameId, out _))
        {
            LogLookupFailed(PresenceErrorCodes.ServerHidden);
            return Result.Failure<TargetServer>("Target server not visible.", PresenceErrorCodes.ServerHidden);
        }

        LogLookupSuccess(userId, presence.PlaceId.Value);
        return Result.Success(new TargetServer(userId, presence.PlaceId.Value, presence.GameId!));
    }

    private Result? MapStatus(HttpResponseMessage response, string stage)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        LogLookupFailed(response.StatusCode.ToString());
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => Result.Failure($"Account cookie expired during {stage}.", AuthErrorCodes.Expired),
            HttpStatusCode.TooManyRequests => Result.Failure($"Rate limited during {stage}.", AuthErrorCodes.RateLimited),
            _ => Result.Failure($"Roblox {stage} failed: {response.StatusCode}", AuthErrorCodes.Unexpected)
        };
    }

    [LoggerMessage(EventId = 411, Level = LogLevel.Information, Message = "Target UserId {UserId} found in place {PlaceId}.")]
    private partial void LogLookupSuccess(long userId, long placeId);

    [LoggerMessage(EventId = 412, Level = LogLevel.Warning, Message = "Target presence lookup failed: {Reason}.")]
    private partial void LogLookupFailed(string reason);

    internal sealed class UsernamesRequest
    {
        [JsonPropertyName("usernames")]
        public List<string> Usernames { get; set; } = [];

        [JsonPropertyName("excludeBannedUsers")]
        public bool ExcludeBannedUsers { get; set; }
    }

    internal sealed class UsernamesResponse
    {
        [JsonPropertyName("data")]
        public List<UsernameItem>? Data { get; set; }
    }

    internal sealed class UsernameItem
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }

    internal sealed class PresenceRequest
    {
        [JsonPropertyName("userIds")]
        public List<long> UserIds { get; set; } = [];
    }

    internal sealed class PresenceResponse
    {
        [JsonPropertyName("userPresences")]
        public List<UserPresenceItem>? UserPresences { get; set; }
    }

    internal sealed class UserPresenceItem
    {
        [JsonPropertyName("placeId")]
        public long? PlaceId { get; set; }

        [JsonPropertyName("gameId")]
        public string? GameId { get; set; }
    }
}
