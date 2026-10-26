namespace RbxManager.Infrastructure.Launch;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Instance;
using RbxManager.Core.Launch;
using RbxManager.Core.Maintenance;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;

public sealed partial class RobloxLauncher : ILauncher, IDisposable
{
    private readonly IAccountStore _accountStore;
    private readonly IRobloxAuthService _authService;
    private readonly IProcessStarter _processStarter;
    private readonly IProcessProvider _processProvider;
    private readonly IInstanceGate _instanceGate;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<RobloxLauncher> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan> _jitterProvider;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private DateTimeOffset? _lastLaunchStartTime;
    private DateTimeOffset? _lastCacheCleanTime;
    private DateTimeOffset? _cooldownUntil;
    private int _consecutiveThrottleCount;
    private readonly IRobloxCacheCleaner? _cacheCleaner;
    private bool _disposed;

    public RobloxLauncher(
        IAccountStore accountStore,
        IRobloxAuthService authService,
        IProcessStarter processStarter,
        IProcessProvider processProvider,
        IInstanceGate instanceGate,
        ILogger<RobloxLauncher>? logger = null,
        ISettingsService? settingsService = null,
        TimeProvider? timeProvider = null,
        IRobloxCacheCleaner? cacheCleaner = null,
        Func<TimeSpan>? jitterProvider = null)
    {
        _accountStore = accountStore;
        _authService = authService;
        _processStarter = processStarter;
        _processProvider = processProvider;
        _instanceGate = instanceGate;
        _logger = logger ?? NullLogger<RobloxLauncher>.Instance;
        _settingsService = settingsService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cacheCleaner = cacheCleaner;
        _jitterProvider = jitterProvider ?? (timeProvider == null
            ? static () => TimeSpan.FromMilliseconds(Random.Shared.Next(1500, 6000))
            : static () => TimeSpan.Zero);
    }

    // A bulk launch of 20 accounts would otherwise rescan the cache 20 times within minutes for nothing.
    internal static readonly TimeSpan AutoCleanInterval = TimeSpan.FromMinutes(10);

    private async Task CleanCacheIfDueAsync(CancellationToken ct)
    {
        if (_cacheCleaner == null || _settingsService?.CurrentSettings.EnableAutoCleanCache != true)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_lastCacheCleanTime.HasValue && now - _lastCacheCleanTime.Value < AutoCleanInterval)
        {
            return;
        }

        _lastCacheCleanTime = now;
        await _cacheCleaner.CleanAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
    {
        Result validationResult = request.Validate();
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        await _launchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cooldownUntil.HasValue)
            {
                DateTimeOffset cooldownNow = _timeProvider.GetUtcNow();
                if (cooldownNow < _cooldownUntil.Value)
                {
                    await Task.Delay(_cooldownUntil.Value - cooldownNow, _timeProvider, ct).ConfigureAwait(false);
                }
            }

            int delaySeconds = _settingsService?.CurrentSettings.LaunchDelaySeconds ?? AppSettings.DefaultLaunchDelaySeconds;
            TimeSpan minInterval = TimeSpan.FromSeconds(delaySeconds) + _jitterProvider();

            if (_lastLaunchStartTime.HasValue)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                TimeSpan elapsed = now - _lastLaunchStartTime.Value;
                if (elapsed < minInterval)
                {
                    TimeSpan remaining = minInterval - elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, _timeProvider, ct).ConfigureAwait(false);
                    }
                }
            }

            _lastLaunchStartTime = _timeProvider.GetUtcNow();
            await CleanCacheIfDueAsync(ct).ConfigureAwait(false);

            if (!_instanceGate.IsMultiInstanceEnabled)
            {
                IReadOnlyList<ProcessSnapshot> running = _processProvider.GetRobloxProcesses();
                if (running.Count > 0)
                {
                    LogRobloxAlreadyRunning();
                    return Result.Failure("Roblox is already running; enable multi-instance in Settings.", "RobloxAlreadyRunning");
                }
            }

            IReadOnlyList<Account> accounts = await _accountStore.ListAsync(ct).ConfigureAwait(false);
            try
            {
                Account? account = accounts.FirstOrDefault(a => a.Id == request.AccountId);
                if (account == null)
                {
                    return Result.Failure("Account was not found.", "AccountNotFound");
                }

                LogRequestingTicketForLaunch(account.Id.Value);

                Result<SecretValue> ticketResult = await _authService.GetAuthTicketAsync(account.Cookie, ct).ConfigureAwait(false);
                if (!ticketResult.IsSuccess)
                {
                    if (string.Equals(ticketResult.ErrorCode, AuthErrorCodes.RateLimited, StringComparison.OrdinalIgnoreCase) ||
                        ticketResult.Details?.HttpStatus is 429 or 403)
                    {
                        _consecutiveThrottleCount = Math.Min(_consecutiveThrottleCount + 1, 5);
                        int backoffSeconds = Math.Min(300, 30 * (1 << (_consecutiveThrottleCount - 1)));
                        _cooldownUntil = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(backoffSeconds);
                    }

                    LogLaunchFailed(ticketResult.ErrorCode ?? "TicketFailed");
                    return Result.Failure(ticketResult.Error ?? "Failed to acquire authentication ticket.", ticketResult.ErrorCode);
                }

                _consecutiveThrottleCount = 0;
                _cooldownUntil = null;

                using SecretValue ticketSecret = ticketResult.Value!;
                string ticket = ticketSecret.Reveal();
                string trackerId = Random.Shared.Next(100000000, 999999999).ToString(CultureInfo.InvariantCulture);
                long unixMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

                string? launchUriTemplate = _settingsService?.CurrentSettings.Advanced?.LaunchUriTemplate;
                string? placeLauncherUrlTemplate = _settingsService?.CurrentSettings.Advanced?.PlaceLauncherUrlTemplate;

                if (!string.IsNullOrWhiteSpace(launchUriTemplate) &&
                    !LaunchTemplateValidator.ValidateLaunchUriTemplate(launchUriTemplate).IsSuccess)
                {
                    LogInvalidLaunchUriTemplateOverride();
                }

                if (!string.IsNullOrWhiteSpace(placeLauncherUrlTemplate) &&
                    !LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate(placeLauncherUrlTemplate).IsSuccess)
                {
                    LogInvalidPlaceLauncherUrlTemplateOverride();
                }

                string launchUri = RobloxUriBuilder.BuildLaunchUri(
                    ticket,
                    request.PlaceId,
                    request.JobId,
                    unixMs,
                    trackerId,
                    launchUriTemplate,
                    placeLauncherUrlTemplate);

                bool started = _processStarter.Start(launchUri);

                if (!started)
                {
                    LogLaunchFailed("ProcessStartFailed");
                    return Result.Failure("Failed to start Roblox process.", "ProcessStartFailed");
                }

                LogLaunchSuccess(account.Id.Value, request.PlaceId);
                return Result.Success();
            }
            finally
            {
                foreach (Account a in accounts)
                {
                    a.Cookie.Dispose();
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 1155 or 2)
        {
            LogLaunchFailed("RobloxNotInstalledOrRegistered");
            return Result.Failure("Roblox player is not installed or protocol is not registered on this system.", "RobloxNotInstalledOrRegistered");
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogLaunchError(ex.GetType().Name);
            return Result.Failure("An error occurred launching Roblox.", "LaunchError");
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _launchGate.Dispose();
    }

    [LoggerMessage(EventId = 301, Level = LogLevel.Warning, Message = "Roblox player registry check failed: not installed.")]
    private partial void LogRobloxNotInstalled();

    [LoggerMessage(EventId = 302, Level = LogLevel.Information, Message = "Requesting ticket to launch account {AccountId}.")]
    private partial void LogRequestingTicketForLaunch(long accountId);

    [LoggerMessage(EventId = 303, Level = LogLevel.Information, Message = "Successfully initiated Roblox launch for account {AccountId} to place {PlaceId}.")]
    private partial void LogLaunchSuccess(long accountId, long placeId);

    [LoggerMessage(EventId = 304, Level = LogLevel.Warning, Message = "Roblox launch failed with code: {ErrorCode}.")]
    private partial void LogLaunchFailed(string errorCode);

    [LoggerMessage(EventId = 305, Level = LogLevel.Error, Message = "Unexpected exception during launch: {ExceptionType}.")]
    private partial void LogLaunchError(string exceptionType);

    [LoggerMessage(EventId = 306, Level = LogLevel.Warning, Message = "Roblox is already running and multi-instance is disabled.")]
    private partial void LogRobloxAlreadyRunning();

    [LoggerMessage(EventId = 307, Level = LogLevel.Warning, Message = "Invalid LaunchUriTemplate override specified in settings; falling back to default.")]
    private partial void LogInvalidLaunchUriTemplateOverride();

    [LoggerMessage(EventId = 308, Level = LogLevel.Warning, Message = "Invalid PlaceLauncherUrlTemplate override specified in settings; falling back to default.")]
    private partial void LogInvalidPlaceLauncherUrlTemplateOverride();
}
