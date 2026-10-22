namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;

public sealed partial class RamCleanerService : BackgroundService
{
    private readonly ISettingsService _settingsService;
    private readonly IMemoryOptimizer _memoryOptimizer;
    private readonly ISessionTracker? _sessionTracker;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayFunc;
    private readonly ILogger<RamCleanerService> _logger;
    private int _consecutiveAccessDeniedCycles;

    public static event Action? RamCleanerAutoDisabled;
    public static bool LastDisabledDueToAccessDenied { get; set; }

    public RamCleanerService(
        ISettingsService settingsService,
        IMemoryOptimizer memoryOptimizer,
        ISessionTracker? sessionTracker = null,
        ILogger<RamCleanerService>? logger = null)
        : this(settingsService, memoryOptimizer, sessionTracker, null, logger)
    {
    }

    internal RamCleanerService(
        ISettingsService settingsService,
        IMemoryOptimizer memoryOptimizer,
        ISessionTracker? sessionTracker,
        Func<TimeSpan, CancellationToken, Task>? delayFunc,
        ILogger<RamCleanerService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _memoryOptimizer = memoryOptimizer ?? throw new ArgumentNullException(nameof(memoryOptimizer));
        _sessionTracker = sessionTracker;
        _delayFunc = delayFunc ?? Task.Delay;
        _logger = logger ?? NullLogger<RamCleanerService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                AppSettings settings = _settingsService.CurrentSettings;
                if (settings.EnableRamCleaner && settings.RamCleanerAcknowledged)
                {
                    int intervalMinutes = Math.Max(1, settings.RamCleanerIntervalMinutes);
                    await _delayFunc(TimeSpan.FromMinutes(intervalMinutes), stoppingToken).ConfigureAwait(false);

                    if (_settingsService.CurrentSettings.EnableRamCleaner && !stoppingToken.IsCancellationRequested)
                    {
                        int[]? trackedPids = _sessionTracker != null
                            ? _sessionTracker.ActiveSessions.Select(s => s.ProcessId).ToArray()
                            : null;

                        if (trackedPids == null || trackedPids.Length > 0)
                        {
                            await _memoryOptimizer.TrimMemoryAsync(trackedPids, stoppingToken).ConfigureAwait(false);

                            if (_memoryOptimizer.LastAccessDeniedCount > 0 &&
                                _memoryOptimizer.LastTrimmedCount == 0 &&
                                _memoryOptimizer.LastFailedCount == 0)
                            {
                                _consecutiveAccessDeniedCycles++;
                                if (_consecutiveAccessDeniedCycles >= 3)
                                {
                                    _consecutiveAccessDeniedCycles = 0;
                                    LastDisabledDueToAccessDenied = true;
                                    await _settingsService.SaveSettingsAsync(
                                        _settingsService.CurrentSettings with { EnableRamCleaner = false },
                                        stoppingToken).ConfigureAwait(false);

                                    LogRamCleanerAutoDisabled(_logger);
                                    RamCleanerAutoDisabled?.Invoke();
                                }
                            }
                            else if (_memoryOptimizer.LastTrimmedCount > 0)
                            {
                                _consecutiveAccessDeniedCycles = 0;
                            }

                            LogCleanerCycleTrimmed(_logger, _memoryOptimizer.LastTrimmedCount);
                        }
                    }
                }
                else
                {
                    await _delayFunc(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogCleanerServiceError(_logger, ex);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "RAM cleaner cycle executed. Trimmed processes: {TrimmedCount}.")]
    private static partial void LogCleanerCycleTrimmed(ILogger logger, int trimmedCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "RAM cleaner disabled automatically due to 3 consecutive access-denied cycles.")]
    private static partial void LogRamCleanerAutoDisabled(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Error occurred during RAM cleaner background service loop.")]
    private static partial void LogCleanerServiceError(ILogger logger, Exception ex);
}
