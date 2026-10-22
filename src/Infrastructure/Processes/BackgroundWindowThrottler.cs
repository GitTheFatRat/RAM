namespace RbxManager.Infrastructure.Processes;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;

public sealed partial class BackgroundWindowThrottler : BackgroundService
{
    private readonly ISettingsService _settingsService;
    private readonly IWindowController _windowController;
    private readonly ILogger<BackgroundWindowThrottler> _logger;
    private bool _wasThrottling;

    public BackgroundWindowThrottler(
        ISettingsService settingsService,
        IWindowController windowController,
        ILogger<BackgroundWindowThrottler>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _windowController = windowController ?? throw new ArgumentNullException(nameof(windowController));
        _logger = logger ?? NullLogger<BackgroundWindowThrottler>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool isThrottlingEnabled = _settingsService.CurrentSettings.ThrottleBackgroundRoblox;
                if (isThrottlingEnabled)
                {
                    _windowController.ThrottleBackgroundProcesses();
                    _wasThrottling = true;
                }
                else if (_wasThrottling)
                {
                    _windowController.RestoreProcesses();
                    _wasThrottling = false;
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogThrottlerError(_logger, ex);
            }
        }

        if (_wasThrottling)
        {
            _windowController.RestoreProcesses();
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Background window throttler encountered an error.")]
    private static partial void LogThrottlerError(ILogger logger, Exception ex);
}
