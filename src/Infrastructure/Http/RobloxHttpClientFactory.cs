namespace RbxManager.Infrastructure.Http;

using System;
using System.Net;
using System.Net.Http;
using RbxManager.Core.Models;

public sealed class RobloxHttpClientFactory : IRobloxHttpClientFactory
{
    public static SocketsHttpHandler CreateHandler(ProxyConfig? proxy = null)
    {
        SocketsHttpHandler primaryHandler = new()
        {
            UseCookies = false,
            AllowAutoRedirect = false
        };

        if (proxy != null)
        {
            primaryHandler.UseProxy = true;
            string scheme = proxy.Type == ProxyType.Socks5 ? "socks5" : "http";
            WebProxy webProxy = new($"{scheme}://{proxy.Host}:{proxy.Port}");

            if (!string.IsNullOrWhiteSpace(proxy.Username))
            {
                if (proxy.Password != null)
                {
                    string password = proxy.Password.Reveal();
                    webProxy.Credentials = new NetworkCredential(proxy.Username, password);
                }
                else
                {
                    webProxy.Credentials = new NetworkCredential(proxy.Username, string.Empty);
                }
            }

            primaryHandler.Proxy = webProxy;
        }

        return primaryHandler;
    }

    public HttpClient CreateClient(ProxyConfig? proxy = null)
    {
        SocketsHttpHandler primaryHandler = CreateHandler(proxy);

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
