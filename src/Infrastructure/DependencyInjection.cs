namespace RbxManager.Infrastructure;

using System;
using System.Net.Http;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RbxManager.Core.Auth;
using RbxManager.Core.Launch;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Auth;
using RbxManager.Infrastructure.Http;
using RbxManager.Infrastructure.Launch;
using RbxManager.Infrastructure.Storage;

[SupportedOSPlatform("windows")]
public static class InfrastructureServiceExtensions
{
    public const string RobloxHttpClientName = "RobloxClient";

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IAccountStore, EncryptedFileVault>();
        services.AddSingleton<IProcessStarter, WindowsProcessStarter>();
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

        return services;
    }
}
