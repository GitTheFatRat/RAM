namespace RbxManager.Infrastructure.Auth;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public sealed partial class RobloxAuthService : IRobloxAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RobloxAuthService> _logger;

    public RobloxAuthService(HttpClient httpClient, ILogger<RobloxAuthService>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<RobloxAuthService>.Instance;
    }

    public async Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default)
    {
        LogValidatingCookie();

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie.Reveal()}");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                AuthenticatedUserDto? userDto = await JsonSerializer.DeserializeAsync<AuthenticatedUserDto>(stream, cancellationToken: ct).ConfigureAwait(false);

                if (userDto != null && userDto.Id > 0 && !string.IsNullOrWhiteSpace(userDto.Name))
                {
                    LogValidateSuccess(userDto.Id);
                    return Result.Success(new RobloxUser(userDto.Id, userDto.Name, userDto.DisplayName ?? userDto.Name));
                }

                LogValidateFailed("PayloadInvalid");
                return Result.Failure<RobloxUser>("Invalid authenticated user payload.", AuthErrorCodes.Unexpected);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                LogValidateFailed(AuthErrorCodes.Expired);
                return Result.Failure<RobloxUser>("The account cookie has expired.", AuthErrorCodes.Expired);
            }

            if ((int)response.StatusCode == 429)
            {
                LogValidateFailed(AuthErrorCodes.RateLimited);
                return Result.Failure<RobloxUser>("Rate limited by Roblox.", AuthErrorCodes.RateLimited);
            }

            if ((int)response.StatusCode >= 500)
            {
                LogValidateFailed(AuthErrorCodes.Network);
                return Result.Failure<RobloxUser>("Roblox service error.", AuthErrorCodes.Network);
            }

            LogValidateFailed(AuthErrorCodes.Unexpected);
            return Result.Failure<RobloxUser>("Unexpected response status from Roblox.", AuthErrorCodes.Unexpected);
        }
        catch (HttpRequestException)
        {
            LogValidateFailed(AuthErrorCodes.Network);
            return Result.Failure<RobloxUser>("Network error validating cookie.", AuthErrorCodes.Network);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            LogValidateFailed(AuthErrorCodes.Network);
            return Result.Failure<RobloxUser>("Request timed out.", AuthErrorCodes.Network);
        }
        catch (Exception ex)
        {
            LogValidateError(ex.GetType().Name);
            return Result.Failure<RobloxUser>("Error validating account cookie.", AuthErrorCodes.Unexpected);
        }
    }

    public async Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default)
    {
        LogRequestingTicket();

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie.Reveal()}");
            request.Headers.Add("Referer", "https://www.roblox.com/");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                if (response.Headers.TryGetValues("rbx-authentication-ticket", out var values))
                {
                    string? ticket = values.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(ticket))
                    {
                        LogTicketSuccess();
                        return Result.Success(SecretValue.FromString(ticket));
                    }
                }

                LogTicketFailed("MissingHeader");
                return Result.Failure<SecretValue>("Authentication ticket header missing from response.", AuthErrorCodes.Unexpected);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                LogTicketFailed(AuthErrorCodes.Expired);
                return Result.Failure<SecretValue>("Account cookie has expired.", AuthErrorCodes.Expired);
            }

            if ((int)response.StatusCode == 429)
            {
                LogTicketFailed(AuthErrorCodes.RateLimited);
                return Result.Failure<SecretValue>("Rate limited requesting authentication ticket.", AuthErrorCodes.RateLimited);
            }

            if ((int)response.StatusCode >= 500)
            {
                LogTicketFailed(AuthErrorCodes.Network);
                return Result.Failure<SecretValue>("Roblox server error requesting authentication ticket.", AuthErrorCodes.Network);
            }

            LogTicketFailed(AuthErrorCodes.Unexpected);
            return Result.Failure<SecretValue>("Unexpected response requesting authentication ticket.", AuthErrorCodes.Unexpected);
        }
        catch (HttpRequestException)
        {
            LogTicketFailed(AuthErrorCodes.Network);
            return Result.Failure<SecretValue>("Network error requesting authentication ticket.", AuthErrorCodes.Network);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            LogTicketFailed(AuthErrorCodes.Network);
            return Result.Failure<SecretValue>("Request timed out requesting authentication ticket.", AuthErrorCodes.Network);
        }
        catch (Exception ex)
        {
            LogTicketError(ex.GetType().Name);
            return Result.Failure<SecretValue>("Error requesting authentication ticket.", AuthErrorCodes.Unexpected);
        }
    }

    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "Validating account cookie.")]
    private partial void LogValidatingCookie();

    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "Account cookie validated successfully for UserId {UserId}.")]
    private partial void LogValidateSuccess(long userId);

    [LoggerMessage(EventId = 203, Level = LogLevel.Warning, Message = "Account cookie validation failed: {Reason}.")]
    private partial void LogValidateFailed(string reason);

    [LoggerMessage(EventId = 204, Level = LogLevel.Error, Message = "Exception during account cookie validation: {ExceptionType}.")]
    private partial void LogValidateError(string exceptionType);

    [LoggerMessage(EventId = 205, Level = LogLevel.Information, Message = "Requesting game authentication ticket.")]
    private partial void LogRequestingTicket();

    [LoggerMessage(EventId = 206, Level = LogLevel.Information, Message = "Authentication ticket acquired successfully.")]
    private partial void LogTicketSuccess();

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "Authentication ticket acquisition failed: {Reason}.")]
    private partial void LogTicketFailed(string reason);

    [LoggerMessage(EventId = 208, Level = LogLevel.Error, Message = "Exception during authentication ticket acquisition: {ExceptionType}.")]
    private partial void LogTicketError(string exceptionType);

    private sealed class AuthenticatedUserDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }
}
