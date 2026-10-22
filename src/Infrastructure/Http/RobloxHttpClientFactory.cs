namespace RbxManager.Infrastructure.Http;

using System;
using System.Net.Http;

public sealed class RobloxHttpClientFactory : IRobloxHttpClientFactory
{
    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false
        };
    }

    public HttpClient CreateClient()
    {
        SocketsHttpHandler primaryHandler = CreateHandler();

        HttpMessageHandler pipeline = new RobloxRetryHandler(primaryHandler);
        pipeline = new RobloxCsrfHandler(pipeline);
        pipeline = new RobloxHostGuardHandler(pipeline);

        HttpClient client = new(pipeline)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RbxManager/1.0");

        return client;
    }
}
