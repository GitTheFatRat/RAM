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
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Storage;

public sealed partial class RobloxLauncher : ILauncher, IDisposable
{
    private const int MinimumLaunchIntervalMs = 3000;

    private readonly IAccountStore _accountStore;
    private readonly IRobloxAuthService _authService;
    private readonly IProcessStarter _processStarter;
    private readonly IProcessProvider _processProvider;
    private readonly IInstanceGate _instanceGate;
    private readonly Core.Settings.ISettingsService? _settingsService;
    private readonly ILogger<RobloxLauncher> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private DateTimeOffset _lastLaunchTime = DateTimeOffset.MinValue;
    private bool _disposed;

    public RobloxLauncher(
        IAccountStore accountStore,
        IRobloxAuthService authService,
        IProcessStarter processStarter,
        IProcessProvider processProvider,
        IInstanceGate instanceGate,
        ILogger<RobloxLauncher>? logger = null,
        Core.Settings.ISettingsService? settingsService = null)
    {
        _accountStore = accountStore;
        _authService = authService;
        _processStarter = processStarter;
        _processProvider = processProvider;
        _instanceGate = instanceGate;
        _logger = logger ?? NullLogger<RobloxLauncher>.Instance;
        _settingsService = settingsService;
    }

    public async Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
    {
        Result validationResult = request.Validate();
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        if (!_instanceGate.IsMultiInstanceEnabled)
        {
            IReadOnlyList<ProcessSnapshot> running = _processProvider.GetRobloxProcesses();
            if (running.Count > 0)
            {
                LogRobloxAlreadyRunning();
                return Result.Failure("Roblox is already running; enable multi-instance in Settings.", "RobloxAlreadyRunning");
            }
        }

        if (!_processStarter.IsRobloxPlayerInstalled())
        {
            LogRobloxNotInstalled();
            return Result.Failure("Roblox player is not installed on this system.", "RobloxNotInstalled");
        }

        IReadOnlyList<Account> accounts = await _accountStore.ListAsync().ConfigureAwait(false);
        Account? account = accounts.FirstOrDefault(a => a.Id == request.AccountId);
        if (account == null)
        {
            return Result.Failure("Account was not found.", "AccountNotFound");
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            int minInterval = _settingsService?.CurrentSettings.LaunchDelayMs ?? MinimumLaunchIntervalMs;
            TimeSpan elapsed = DateTimeOffset.UtcNow - _lastLaunchTime;
            if (elapsed.TotalMilliseconds < minInterval)
            {
                int delayMs = minInterval - (int)elapsed.TotalMilliseconds;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }

            LogRequestingTicketForLaunch(account.Id.Value);

            Result<SecretValue> ticketResult = await _authService.GetAuthTicketAsync(account.Cookie, ct).ConfigureAwait(false);
            if (!ticketResult.IsSuccess)
            {
                LogLaunchFailed(ticketResult.ErrorCode ?? "TicketFailed");
                return Result.Failure(ticketResult.Error ?? "Failed to acquire authentication ticket.", ticketResult.ErrorCode);
            }

            using SecretValue ticketSecret = ticketResult.Value!;
            string ticket = ticketSecret.Reveal();
            string trackerId = Random.Shared.Next(100000000, 999999999).ToString(CultureInfo.InvariantCulture);
            long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string launchUri = RobloxUriBuilder.BuildLaunchUri(ticket, request.PlaceId, request.JobId, unixMs, trackerId);

            bool started = _processStarter.Start(launchUri);
            _lastLaunchTime = DateTimeOffset.UtcNow;

            if (!started)
            {
                LogLaunchFailed("ProcessStartFailed");
                return Result.Failure("Failed to start Roblox process.", "ProcessStartFailed");
            }

            LogLaunchSuccess(account.Id.Value, request.PlaceId);
            return Result.Success();
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
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _semaphore.Dispose();
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
}
