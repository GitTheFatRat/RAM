namespace RbxManager.Infrastructure.Auth;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Infrastructure.Http;

public sealed partial class RobloxAuthService : IRobloxAuthService
{
    private readonly HttpClient? _defaultHttpClient;
    private readonly IRobloxHttpClientFactory? _clientFactory;
    private readonly ILogger<RobloxAuthService> _logger;

    public RobloxAuthService(IRobloxHttpClientFactory clientFactory, ILogger<RobloxAuthService>? logger = null)
    {
        _clientFactory = clientFactory;
        _logger = logger ?? NullLogger<RobloxAuthService>.Instance;
    }

    public RobloxAuthService(HttpClient httpClient, ILogger<RobloxAuthService>? logger = null)
    {
        _defaultHttpClient = httpClient;
        _logger = logger ?? NullLogger<RobloxAuthService>.Instance;
    }

    private HttpClient ResolveClient(ProxyConfig? proxy, out bool shouldDispose)
    {
        if (proxy != null && _clientFactory != null)
        {
            shouldDispose = true;
            return _clientFactory.CreateClient(proxy);
        }

        if (_defaultHttpClient != null)
        {
            shouldDispose = false;
            return _defaultHttpClient;
        }

        if (_clientFactory != null)
        {
            shouldDispose = true;
            return _clientFactory.CreateClient(null);
        }

        throw new InvalidOperationException("No HttpClient or IRobloxHttpClientFactory configured.");
    }

    public async Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default)
    {
        LogValidatingCookie();

        HttpClient client = ResolveClient(proxy, out bool shouldDispose);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie.Reveal()}");

            using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                AuthenticatedUserDto? userDto = await JsonSerializer.DeserializeAsync<AuthenticatedUserDto>(stream, cancellationToken: ct).ConfigureAwait(false);

                if (userDto != null && userDto.Id > 0 && !string.IsNullOrWhiteSpace(userDto.Name))
                {
                    LogValidateSuccess(userDto.Id);
                    return Result.Success(new RobloxUser(userDto.Id, userDto.Name, userDto.DisplayName ?? userDto.Name));
                }

                bool csrf = request.Options.TryGetValue(RobloxCsrfHandler.CsrfTokenObtainedKey, out bool c) && c;
                List<string> headerNames = response.Headers.Select(h => h.Key).ToList();
                AuthFailureDetails invalidPayloadDetails = new(
                    Stage: "Validate",
                    HttpStatus: (int)response.StatusCode,
                    RobloxErrorCodes: [],
                    RobloxMessage: null,
                    CsrfTokenObtained: csrf,
                    ResponseHeaderNames: headerNames,
                    TicketHeaderPresent: false);

                LogValidateFailed("PayloadInvalid");
                return Result.Failure<RobloxUser>("Invalid authenticated user payload.", AuthErrorCodes.Unexpected, invalidPayloadDetails);
            }

            (List<int> errorCodes, string? robloxMessage) = await ParseRobloxErrorsAsync(response, ct).ConfigureAwait(false);
            bool csrfObtained = request.Options.TryGetValue(RobloxCsrfHandler.CsrfTokenObtainedKey, out bool csrfVal) && csrfVal;
            List<string> responseHeaders = response.Headers.Select(h => h.Key).ToList();
            AuthFailureDetails details = new(
                Stage: "Validate",
                HttpStatus: (int)response.StatusCode,
                RobloxErrorCodes: errorCodes,
                RobloxMessage: robloxMessage,
                CsrfTokenObtained: csrfObtained,
                ResponseHeaderNames: responseHeaders,
                TicketHeaderPresent: false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                LogValidateFailed(AuthErrorCodes.Expired);
                return Result.Failure<RobloxUser>("The account cookie has expired.", AuthErrorCodes.Expired, details);
            }

            if ((int)response.StatusCode == 429)
            {
                LogValidateFailed(AuthErrorCodes.RateLimited);
                return Result.Failure<RobloxUser>("Rate limited by Roblox.", AuthErrorCodes.RateLimited, details);
            }

            if ((int)response.StatusCode >= 500)
            {
                LogValidateFailed(AuthErrorCodes.Network);
                return Result.Failure<RobloxUser>("Roblox service error.", AuthErrorCodes.Network, details);
            }

            LogValidateFailed(AuthErrorCodes.Unexpected);
            return Result.Failure<RobloxUser>("Unexpected response status from Roblox.", AuthErrorCodes.Unexpected, details);
        }
        catch (HttpRequestException)
        {
            AuthFailureDetails details = new(
                Stage: "Validate",
                HttpStatus: null,
                RobloxErrorCodes: [],
                RobloxMessage: null,
                CsrfTokenObtained: false,
                ResponseHeaderNames: [],
                TicketHeaderPresent: false);

            LogValidateFailed(AuthErrorCodes.Network);
            return Result.Failure<RobloxUser>("Network error validating cookie.", AuthErrorCodes.Network, details);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            AuthFailureDetails details = new(
                Stage: "Validate",
                HttpStatus: null,
                RobloxErrorCodes: [],
                RobloxMessage: null,
                CsrfTokenObtained: false,
                ResponseHeaderNames: [],
                TicketHeaderPresent: false);

            LogValidateFailed(AuthErrorCodes.Network);
            return Result.Failure<RobloxUser>("Request timed out.", AuthErrorCodes.Network, details);
        }
        catch (Exception ex)
        {
            AuthFailureDetails details = new(
                Stage: "Validate",
                HttpStatus: null,
                RobloxErrorCodes: [],
                RobloxMessage: null,
                CsrfTokenObtained: false,
                ResponseHeaderNames: [],
                TicketHeaderPresent: false);

            LogValidateError(ex.GetType().Name);
            return Result.Failure<RobloxUser>("Error validating account cookie.", AuthErrorCodes.Unexpected, details);
        }
        finally
        {
            if (shouldDispose)
            {
                client.Dispose();
            }
        }
    }

    public async Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default)
    {
        LogRequestingTicket();

        HttpClient client = ResolveClient(proxy, out bool shouldDispose);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie.Reveal()}");
            request.Headers.Add("Referer", "https://www.roblox.com/");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                if (response.Headers.TryGetValues("rbx-authentication-ticket", out IEnumerable<string>? values))
                {
                    string? ticket = values.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(ticket))
                    {
                        LogTicketSuccess();
                        return Result.Success(SecretValue.FromString(ticket));
                    }
                }

                bool csrf = request.Options.TryGetValue(RobloxCsrfHandler.CsrfTokenObtainedKey, out bool c) && c;
                List<string> headerNames = response.Headers.Select(h => h.Key).ToList();
                AuthFailureDetails missingHeaderDetails = new(
                    Stage: "Ticket",
                    HttpStatus: (int)response.StatusCode,
                    RobloxErrorCodes: [],
                    RobloxMessage: null,
                    CsrfTokenObtained: csrf,
                    ResponseHeaderNames: headerNames,
                    TicketHeaderPresent: response.Headers.Contains("rbx-authentication-ticket"));

                LogTicketFailed("MissingHeader");
                return Result.Failure<SecretValue>("Authentication ticket header missing from response.", AuthErrorCodes.Unexpected, missingHeaderDetails);
            }

            (List<int> errorCodes, string? robloxMessage) = await ParseRobloxErrorsAsync(response, ct).ConfigureAwait(false);
            bool csrfObtained = request.Options.TryGetValue(RobloxCsrfHandler.CsrfTokenObtainedKey, out bool csrfVal) && csrfVal;
            bool ticketHeaderPresent = response.Headers.Contains("rbx-authentication-ticket");
            List<string> responseHeaders = response.Headers.Select(h => h.Key).ToList();
            AuthFailureDetails details = new(
                Stage: "Ticket",
                HttpStatus: (int)response.StatusCode,
                RobloxErrorCodes: errorCodes,
                RobloxMessage: robloxMessage,
                CsrfTokenObtained: csrfObtained,
                ResponseHeaderNames: responseHeaders,
                TicketHeaderPresent: ticketHeaderPresent);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                LogTicketFailed(AuthErrorCodes.Expired);
                return Result.Failure<SecretValue>("Account cookie has expired.", AuthErrorCodes.Expired, details);
            }

            if ((int)response.StatusCode == 429)
            {
                LogTicketFailed(AuthErrorCodes.RateLimited);
                return Result.Failure<SecretValue>("Rate limited requesting authentication ticket.", AuthErrorCodes.RateLimited, details);
            }

            if ((int)response.StatusCode >= 500)
            {
                LogTicketFailed(AuthErrorCodes.Network);
                return Result.Failure<SecretValue>("Roblox server error requesting authentication ticket.", AuthErrorCodes.Network, details);
            }

            LogTicketFailed(AuthErrorCodes.Unexpected);
            return Result.Failure<SecretValue>("Unexpected response requesting authentication ticket.", AuthErrorCodes.Unexpected, details);
        }
        catch (HttpRequestException)
        {
            AuthFailureDetails details = new(
                Stage: "Ticket",
                HttpStatus: null,
                RobloxErrorCodes: [],
                RobloxMessage: null,
                CsrfTokenObtained: false,
                ResponseHeaderNames: [],
                TicketHeaderPresent: false);

            LogTicketFailed(AuthErrorCodes.Network);
            return Result.Failure<SecretValue>("Network error requesting authentication ticket.", AuthErrorCodes.Network, details);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            AuthFailureDetails details = new(
                Stage: "Ticket",
                HttpStatus: null,
                RobloxErrorCodes: [],
                RobloxMessage: null,
                CsrfTokenObtained: false,
                ResponseHeaderNames: [],
                TicketHeaderPresent: false);

            LogTicketFailed(AuthErrorCodes.Network);
            return Result.Failure<SecretValue>("Request timed out requesting authentication ticket.", AuthErrorCodes.Network, details);
        }
        catch (Exception ex)
        {
            AuthFailureDetails details = new(
                Stage: "Ticket",
                HttpStatus: null,
                RobloxErrorCodes: [],
                RobloxMessage: null,
                CsrfTokenObtained: false,
                ResponseHeaderNames: [],
                TicketHeaderPresent: false);

            LogTicketError(ex.GetType().Name);
            return Result.Failure<SecretValue>("Error requesting authentication ticket.", AuthErrorCodes.Unexpected, details);
        }
        finally
        {
            if (shouldDispose)
            {
                client.Dispose();
            }
        }
    }

    private static async Task<(List<int> Codes, string? Message)> ParseRobloxErrorsAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return ([], null);
            }

            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out JsonElement errorsElem) &&
                errorsElem.ValueKind == JsonValueKind.Array)
            {
                List<int> codes = [];
                string? firstMessage = null;
                foreach (JsonElement err in errorsElem.EnumerateArray())
                {
                    if (err.ValueKind == JsonValueKind.Object)
                    {
                        if (err.TryGetProperty("code", out JsonElement codeElem) && codeElem.TryGetInt32(out int code))
                        {
                            codes.Add(code);
                        }
                        if (firstMessage == null && err.TryGetProperty("message", out JsonElement msgElem) && msgElem.ValueKind == JsonValueKind.String)
                        {
                            firstMessage = msgElem.GetString();
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(firstMessage))
                {
                    firstMessage = SanitizeRobloxMessage(firstMessage);
                }

                return (codes, firstMessage);
            }
        }
        catch
        {
            // Defensive error body parsing; malformed JSON or reading errors never throw
        }

        return ([], null);
    }

    private static string SanitizeRobloxMessage(string msg)
    {
        string singleLine = Regex.Replace(msg, @"[\r\n\t]+", " ").Trim();
        return singleLine.Length > 120 ? singleLine.Substring(0, 120) : singleLine;
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
