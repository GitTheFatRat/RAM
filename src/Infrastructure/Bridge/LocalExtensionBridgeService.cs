namespace RbxManager.Infrastructure.Bridge;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RbxManager.Core.Auth;
using RbxManager.Core.Bridge;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;

public sealed partial class LocalExtensionBridgeService : ILocalExtensionBridge
{
    public const int DefaultPort = 48123;
    private const int MaxRequestBodyBytes = 16 * 1024;
    private const string CookieWarningPrefix = "_|WARNING:-DO-NOT-SHARE";

    private readonly IAccountStore _accountStore;
    private readonly IRobloxAuthService _authService;
    private readonly ILogger<LocalExtensionBridgeService>? _logger;
    private readonly int _port;
    private readonly object _lifecycleSync = new();
    private readonly SemaphoreSlim _importGate = new(1, 1);

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenLoopTask;
    private bool _disposed;

    public LocalExtensionBridgeService(
        IAccountStore accountStore,
        IRobloxAuthService authService,
        ILogger<LocalExtensionBridgeService>? logger = null,
        int port = DefaultPort)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger;
        _port = port > 0 ? port : DefaultPort;
    }

    public bool IsListening
    {
        get
        {
            lock (_lifecycleSync)
            {
                return _listener is { IsListening: true };
            }
        }
    }

    public string EndpointUrl => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_port}/import");

    public event Action<ExtensionImportedAccount>? AccountImported;

    public bool StartListening()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return false;
            }

            if (_listener is { IsListening: true })
            {
                return true;
            }

            CleanupListenerLocked();

            try
            {
                HttpListener listener = new();
                listener.Prefixes.Add(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_port}/import/"));
                listener.Start();

                CancellationTokenSource cts = new();
                _listener = listener;
                _cts = cts;
                _listenLoopTask = Task.Run(() => AcceptLoopAsync(listener, cts.Token));

                if (_logger is not null)
                {
                    LogBridgeStarted(_logger, _port);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogBridgeStartFailed(_logger, _port, ex);
                }

                CleanupListenerLocked();
                return false;
            }
        }
    }

    public void StopListening()
    {
        lock (_lifecycleSync)
        {
            CleanupListenerLocked();
        }
    }

    private void CleanupListenerLocked()
    {
        if (_cts != null)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _cts.Dispose();
            _cts = null;
        }

        if (_listener != null)
        {
            try
            {
                if (_listener.IsListening)
                {
                    _listener.Stop();
                }

                _listener.Close();
            }
            catch (Exception)
            {
            }

            _listener = null;
        }

        _listenLoopTask = null;
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    LogAcceptLoopStopped(_logger, ex);
                }

                break;
            }

            _ = Task.Run(() => HandleRequestSafeAsync(context, ct), CancellationToken.None);
        }
    }

    private async Task HandleRequestSafeAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            await HandleRequestAsync(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogUnhandledRequestError(_logger, ex);
            }

            try
            {
                await WriteJsonResponseAsync(
                    context.Response,
                    HttpStatusCode.InternalServerError,
                    new { ok = false, error = "Internal server error." }).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.RemoteEndPoint != null && !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
        {
            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.Forbidden,
                new { ok = false, error = "Loopback connections only." }).ConfigureAwait(false);
            return;
        }

        string? origin = request.Headers["Origin"];
        if (!IsAllowedExtensionOrigin(origin))
        {
            if (_logger is not null)
            {
                LogRejectedOrigin(_logger);
            }

            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.Forbidden,
                new { ok = false, error = "Forbidden origin." }).ConfigureAwait(false);
            return;
        }

        response.Headers["Access-Control-Allow-Origin"] = origin!;
        response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Vary"] = "Origin";

        if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();
            return;
        }

        if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.MethodNotAllowed,
                new { ok = false, error = "Only POST is allowed." }).ConfigureAwait(false);
            return;
        }

        string? absolutePath = request.Url?.AbsolutePath?.TrimEnd('/');
        if (!string.Equals(absolutePath, "/import", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.NotFound,
                new { ok = false, error = "Endpoint not found." }).ConfigureAwait(false);
            return;
        }

        if (request.ContentLength64 > MaxRequestBodyBytes)
        {
            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.RequestEntityTooLarge,
                new { ok = false, error = "Payload too large." }).ConfigureAwait(false);
            return;
        }

        string? rawCookie = await ReadCookieFromBodyAsync(request.InputStream, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(rawCookie))
        {
            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.BadRequest,
                new { ok = false, error = "Missing or empty cookie payload." }).ConfigureAwait(false);
            return;
        }

        string normalizedCookie = NormalizeCookieValue(rawCookie);
        if (string.IsNullOrWhiteSpace(normalizedCookie))
        {
            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.BadRequest,
                new { ok = false, error = "Invalid .ROBLOSECURITY cookie format." }).ConfigureAwait(false);
            return;
        }

        using SecretValue secretCookie = SecretValue.FromString(normalizedCookie);

        Result<RobloxUser> validationResult = await _authService.ValidateCookieAsync(secretCookie, ct).ConfigureAwait(false);
        if (!validationResult.IsSuccess || validationResult.Value == null)
        {
            string errorCode = validationResult.ErrorCode ?? AuthErrorCodes.Expired;
            if (_logger is not null)
            {
                LogValidationFailed(_logger, errorCode);
            }

            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.UnprocessableEntity,
                new
                {
                    ok = false,
                    errorCode,
                    error = validationResult.Error ?? "Cookie validation failed."
                }).ConfigureAwait(false);
            return;
        }

        RobloxUser validatedUser = validationResult.Value;
        ExtensionImportedAccount? importedAccount = await PersistValidatedAccountAsync(validatedUser, secretCookie, ct).ConfigureAwait(false);
        if (importedAccount == null)
        {
            await WriteJsonResponseAsync(
                response,
                HttpStatusCode.InternalServerError,
                new { ok = false, error = "Failed to save account to vault." }).ConfigureAwait(false);
            return;
        }

        if (_logger is not null)
        {
            LogAccountImported(
                _logger,
                importedAccount.AccountId.Value,
                importedAccount.UserId,
                importedAccount.WasUpdated);
        }

        try
        {
            AccountImported?.Invoke(importedAccount);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogSubscriberError(_logger, ex);
            }
        }

        await WriteJsonResponseAsync(
            response,
            HttpStatusCode.OK,
            new
            {
                ok = true,
                username = importedAccount.Username,
                userId = importedAccount.UserId,
                updated = importedAccount.WasUpdated
            }).ConfigureAwait(false);
    }

    public static bool IsAllowedExtensionOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        string trimmed = origin.Trim();
        return trimmed.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("extension://", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadCookieFromBodyAsync(Stream inputStream, CancellationToken ct)
    {
        using MemoryStream ms = new();
        byte[] buffer = new byte[4096];
        int totalRead = 0;

        while (true)
        {
            int read = await inputStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > MaxRequestBodyBytes)
            {
                return null;
            }

            ms.Write(buffer, 0, read);
        }

        if (totalRead == 0)
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(ms.ToArray());
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("cookie", out JsonElement cookieElement) &&
                cookieElement.ValueKind == JsonValueKind.String)
            {
                return cookieElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string NormalizeCookieValue(string rawCookie)
    {
        string trimmed = rawCookie.Trim().Trim('"');
        if (trimmed.StartsWith(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[".ROBLOSECURITY=".Length..].Trim();
        }

        int warningIdx = trimmed.IndexOf(CookieWarningPrefix, StringComparison.OrdinalIgnoreCase);
        if (warningIdx >= 0)
        {
            string extracted = trimmed[warningIdx..];
            int semiIdx = extracted.IndexOf(';');
            if (semiIdx >= 0)
            {
                extracted = extracted[..semiIdx];
            }

            return extracted.Trim();
        }

        return trimmed;
    }

    private async Task<ExtensionImportedAccount?> PersistValidatedAccountAsync(
        RobloxUser user,
        SecretValue cookie,
        CancellationToken ct)
    {
        await _importGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<Account> existingAccounts = await _accountStore.ListAsync(ct).ConfigureAwait(false);
            try
            {
                Account? existingAccount = existingAccounts.FirstOrDefault(a => a.UserId == user.Id)
                    ?? existingAccounts.FirstOrDefault(a => string.Equals(a.Username, user.Name, StringComparison.OrdinalIgnoreCase))
                    ?? existingAccounts.FirstOrDefault(a => a.UserId == null && string.Equals(a.Alias, user.Name, StringComparison.OrdinalIgnoreCase));

                if (existingAccount != null)
                {
                    Account updatedAccount = existingAccount with
                    {
                        Cookie = cookie.Clone(),
                        Username = user.Name,
                        UserId = user.Id,
                        Status = AccountStatus.Valid,
                        LastCheckedAt = DateTimeOffset.UtcNow
                    };

                    try
                    {
                        Result updateResult = await _accountStore.UpdateAsync(updatedAccount, ct).ConfigureAwait(false);
                        if (!updateResult.IsSuccess)
                        {
                            return null;
                        }

                        return new ExtensionImportedAccount(
                            updatedAccount.Id,
                            user.Name,
                            user.Id,
                            WasUpdated: true);
                    }
                    finally
                    {
                        updatedAccount.Cookie.Dispose();
                    }
                }

                long nextId = existingAccounts.Count > 0
                    ? existingAccounts.Max(a => a.Id.Value) + 1
                    : 1;

                HashSet<string> usedAliases = existingAccounts
                    .Select(a => a.Alias.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                string alias = EnsureUniqueAlias(user.Name, usedAliases);
                Account newAccount = new(
                    Id: new AccountId(nextId),
                    Alias: alias,
                    Group: null,
                    Note: null,
                    Cookie: cookie.Clone(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    Username: user.Name,
                    UserId: user.Id,
                    Status: AccountStatus.Valid,
                    LastCheckedAt: DateTimeOffset.UtcNow);

                try
                {
                    Result addResult = await _accountStore.AddAsync(newAccount, ct).ConfigureAwait(false);
                    if (!addResult.IsSuccess)
                    {
                        return null;
                    }

                    return new ExtensionImportedAccount(
                        newAccount.Id,
                        user.Name,
                        user.Id,
                        WasUpdated: false);
                }
                finally
                {
                    newAccount.Cookie.Dispose();
                }
            }
            finally
            {
                foreach (Account acc in existingAccounts)
                {
                    acc.Cookie.Dispose();
                }
            }
        }
        finally
        {
            _importGate.Release();
        }
    }

    private static string EnsureUniqueAlias(string baseName, HashSet<string> usedAliases)
    {
        string trimmed = string.IsNullOrWhiteSpace(baseName) ? "RobloxUser" : baseName.Trim();
        if (trimmed.Length > 45)
        {
            trimmed = trimmed[..45];
        }

        if (!usedAliases.Contains(trimmed))
        {
            return trimmed;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"{trimmed}_{suffix}");
            if (!usedAliases.Contains(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static async Task WriteJsonResponseAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        object payload)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";

        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        response.ContentLength64 = jsonBytes.Length;

        await response.OutputStream.WriteAsync(jsonBytes.AsMemory(0, jsonBytes.Length)).ConfigureAwait(false);
        response.Close();
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CleanupListenerLocked();
        }

        _importGate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Local extension bridge started on 127.0.0.1:{Port}/import")]
    private static partial void LogBridgeStarted(ILogger logger, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to start local extension bridge on 127.0.0.1:{Port}")]
    private static partial void LogBridgeStartFailed(ILogger logger, int port, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Local extension bridge accept loop stopped")]
    private static partial void LogAcceptLoopStopped(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled error processing local extension bridge request")]
    private static partial void LogUnhandledRequestError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected local bridge request due to disallowed Origin header")]
    private static partial void LogRejectedOrigin(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local extension bridge cookie validation failed with code {ErrorCode}")]
    private static partial void LogValidationFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local extension bridge imported account {AccountId} (UserId: {UserId}, Updated: {WasUpdated})")]
    private static partial void LogAccountImported(ILogger logger, long accountId, long userId, bool wasUpdated);

    [LoggerMessage(Level = LogLevel.Debug, Message = "AccountImported subscriber threw an exception")]
    private static partial void LogSubscriberError(ILogger logger, Exception ex);
}
