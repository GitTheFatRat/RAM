namespace RbxManager.Infrastructure;

using System;
using System.Net.Http;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RbxManager.Core.Auth;
using RbxManager.Core.Backup;
using RbxManager.Core.Instance;
using RbxManager.Core.Launch;
using RbxManager.Core.Processes;
using RbxManager.Core.Servers;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Auth;
using RbxManager.Infrastructure.Backup;
using RbxManager.Infrastructure.Http;
using RbxManager.Infrastructure.Instance;
using RbxManager.Infrastructure.Launch;
using RbxManager.Infrastructure.Processes;
using RbxManager.Infrastructure.Servers;
using RbxManager.Infrastructure.Sessions;
using RbxManager.Infrastructure.Settings;
using RbxManager.Infrastructure.Storage;

[SupportedOSPlatform("windows")]
public static class InfrastructureServiceExtensions
{
    public const string RobloxHttpClientName = "RobloxClient";

    public static SocketsHttpHandler CreatePrimarySocketsHttpHandler() => new()
    {
        UseCookies = false,
        AllowAutoRedirect = false
    };

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IVaultKeyProvider, VaultKeyProvider>();
        services.AddSingleton<IAccountStore>(sp => new EncryptedFileVault(
            null,
            sp.GetRequiredService<IVaultKeyProvider>(),
            sp.GetService<ILogger<EncryptedFileVault>>()));
        services.AddSingleton<IProcessStarter, WindowsProcessStarter>();
        services.AddSingleton<IProcessProvider, SystemProcessProvider>();
        services.AddSingleton<IWindowController, WindowController>();
        services.AddSingleton<IMemoryOptimizer, WindowsMemoryOptimizer>();
        services.AddSingleton<IInstanceGate, NamedMutexInstanceGate>();
        services.AddSingleton<ILauncher, RobloxLauncher>();
        services.AddTransient<IBackupService, EncryptedBackupService>();
        services.AddSingleton<IRobloxHttpClientFactory, RobloxHttpClientFactory>();

        services.AddTransient<RobloxHostGuardHandler>();
        services.AddTransient<RobloxCsrfHandler>();
        services.AddTransient<RobloxRetryHandler>();

        services.AddHttpClient(RobloxHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RbxManager/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(CreatePrimarySocketsHttpHandler)
        .AddHttpMessageHandler<RobloxHostGuardHandler>()
        .AddHttpMessageHandler<RobloxCsrfHandler>()
        .AddHttpMessageHandler<RobloxRetryHandler>();

        services.AddTransient<IRobloxAuthService>(sp =>
        {
            IRobloxHttpClientFactory factory = sp.GetRequiredService<IRobloxHttpClientFactory>();
            ILogger<RobloxAuthService>? logger = sp.GetService<ILogger<RobloxAuthService>>();
            return new RobloxAuthService(factory, logger);
        });

        services.AddTransient<IServerBrowserService>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient(RobloxHttpClientName);
            ILogger<ServerBrowserService>? logger = sp.GetService<ILogger<ServerBrowserService>>();
            return new ServerBrowserService(httpClient, logger);
        });

        services.AddTransient<Core.Diagnostics.IDiagnosticsService>(sp =>
        {
            Core.Diagnostics.IRegistryProvider registryProvider = sp.GetRequiredService<Core.Diagnostics.IRegistryProvider>();
            IProcessProvider processProvider = sp.GetRequiredService<IProcessProvider>();
            IInstanceGate instanceGate = sp.GetRequiredService<IInstanceGate>();
            ISettingsService settingsService = sp.GetRequiredService<ISettingsService>();
            IRobloxAuthService authService = sp.GetRequiredService<IRobloxAuthService>();
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient(RobloxHttpClientName);
            return new Diagnostics.DiagnosticsService(registryProvider, processProvider, instanceGate, settingsService, authService, httpClient);
        });

        services.AddSingleton<SessionTracker>();
        services.AddSingleton<ISessionTracker>(sp => sp.GetRequiredService<SessionTracker>());
        services.AddHostedService(sp => sp.GetRequiredService<SessionTracker>());
        services.AddHostedService<BackgroundWindowThrottler>();
        services.AddHostedService<RamCleanerService>();

        return services;
    }
}
