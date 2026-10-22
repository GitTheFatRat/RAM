namespace RbxManager.Infrastructure.Processes;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;

public sealed partial class RamCleanerService : BackgroundService
{
    private readonly ISettingsService _settingsService;
    private readonly IMemoryOptimizer _memoryOptimizer;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayFunc;
    private readonly ILogger<RamCleanerService> _logger;

    public RamCleanerService(
        ISettingsService settingsService,
        IMemoryOptimizer memoryOptimizer,
        ILogger<RamCleanerService>? logger = null)
        : this(settingsService, memoryOptimizer, null, logger)
    {
    }

    internal RamCleanerService(
        ISettingsService settingsService,
        IMemoryOptimizer memoryOptimizer,
        Func<TimeSpan, CancellationToken, Task>? delayFunc,
        ILogger<RamCleanerService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _memoryOptimizer = memoryOptimizer ?? throw new ArgumentNullException(nameof(memoryOptimizer));
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
                if (settings.EnableRamCleaner)
                {
                    int intervalMinutes = Math.Max(1, settings.RamCleanerIntervalMinutes);
                    await _delayFunc(TimeSpan.FromMinutes(intervalMinutes), stoppingToken).ConfigureAwait(false);

                    if (_settingsService.CurrentSettings.EnableRamCleaner && !stoppingToken.IsCancellationRequested)
                    {
                        await _memoryOptimizer.TrimMemoryAsync(stoppingToken).ConfigureAwait(false);

                        if (OperatingSystem.IsWindows() && _memoryOptimizer is WindowsMemoryOptimizer winOptimizer)
                        {
                            LogCleanerCycleTrimmed(_logger, winOptimizer.LastTrimmedCount);
                        }
                        else
                        {
                            LogCleanerCycleExecuted(_logger);
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Smart RAM cleaner cycle completed: trimmed {Count} processes.")]
    private static partial void LogCleanerCycleTrimmed(ILogger logger, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Smart RAM cleaner cycle executed.")]
    private static partial void LogCleanerCycleExecuted(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Smart RAM cleaner encountered an error.")]
    private static partial void LogCleanerServiceError(ILogger logger, Exception ex);
}
