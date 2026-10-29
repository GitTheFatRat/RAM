namespace RbxManager.Infrastructure.Http;

using System;
using System.Net.Http;

public sealed class RobloxHttpClientFactory : IRobloxHttpClientFactory
{
    public const string DefaultBrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

    private static readonly SocketsHttpHandler SharedPrimaryHandler = CreateHandler();

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
    }

    public HttpClient CreateClient()
    {
        HttpMessageHandler pipeline = new RobloxRetryHandler(SharedPrimaryHandler);
        pipeline = new RobloxCsrfHandler(pipeline);
        pipeline = new RobloxHostGuardHandler(pipeline);

        HttpClient client = new(pipeline, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultBrowserUserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        return client;
    }
}
