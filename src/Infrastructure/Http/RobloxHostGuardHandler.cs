namespace RbxManager.Infrastructure.Http;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class RobloxHostGuardHandler : DelegatingHandler
{
    public RobloxHostGuardHandler()
    {
    }

    public RobloxHostGuardHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Contains("Cookie"))
        {
            Uri? uri = request.RequestUri;
            if (uri == null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Roblox security cookies may only be sent over HTTPS.");
            }

            string host = uri.Host;
            bool isAllowed = string.Equals(host, "roblox.com", StringComparison.OrdinalIgnoreCase) ||
                             host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase);

            if (!isAllowed)
            {
                throw new InvalidOperationException("Roblox security cookies may only be sent to roblox.com or its subdomains.");
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
