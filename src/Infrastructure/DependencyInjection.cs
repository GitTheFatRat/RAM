namespace RbxManager.Infrastructure;

using System;
using System.Net.Http;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RbxManager.Core.Auth;
using RbxManager.Core.Instance;
using RbxManager.Core.Launch;
using RbxManager.Core.Processes;
using RbxManager.Core.Servers;
using RbxManager.Core.Sessions;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Auth;
using RbxManager.Infrastructure.Http;
using RbxManager.Infrastructure.Launch;
using RbxManager.Infrastructure.Processes;
using RbxManager.Infrastructure.Servers;
using RbxManager.Infrastructure.Sessions;
using RbxManager.Infrastructure.Storage;

[SupportedOSPlatform("windows")]
public static class InfrastructureServiceExtensions
{
    public const string RobloxHttpClientName = "RobloxClient";

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IAccountStore, EncryptedFileVault>();
        services.AddSingleton<IProcessStarter, WindowsProcessStarter>();
        services.AddSingleton<IProcessProvider, SystemProcessProvider>();
        services.AddSingleton<IInstanceGate, DefaultInstanceGate>();
        services.AddSingleton<ILauncher, RobloxLauncher>();

        services.AddTransient<RobloxHostGuardHandler>();
        services.AddTransient<RobloxCsrfHandler>();
        services.AddTransient<RobloxRetryHandler>();

        services.AddHttpClient(RobloxHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RbxManager/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = false
        })
        .AddHttpMessageHandler<RobloxHostGuardHandler>()
        .AddHttpMessageHandler<RobloxCsrfHandler>()
        .AddHttpMessageHandler<RobloxRetryHandler>();

        services.AddTransient<IRobloxAuthService>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient(RobloxHttpClientName);
            ILogger<RobloxAuthService>? logger = sp.GetService<ILogger<RobloxAuthService>>();
            return new RobloxAuthService(httpClient, logger);
        });

        services.AddTransient<IServerBrowserService>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient(RobloxHttpClientName);
            ILogger<ServerBrowserService>? logger = sp.GetService<ILogger<ServerBrowserService>>();
            return new ServerBrowserService(httpClient, logger);
        });

        services.AddSingleton<SessionTracker>();
        services.AddSingleton<ISessionTracker>(sp => sp.GetRequiredService<SessionTracker>());
        services.AddHostedService(sp => sp.GetRequiredService<SessionTracker>());

        return services;
    }
}
