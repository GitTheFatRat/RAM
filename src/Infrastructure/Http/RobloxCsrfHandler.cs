namespace RbxManager.Infrastructure.Http;

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class RobloxCsrfHandler : DelegatingHandler
{
    public RobloxCsrfHandler()
    {
    }

    public RobloxCsrfHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues("x-csrf-token", out IEnumerable<string>? csrfTokens) ||
                response.Headers.TryGetValues("X-CSRF-TOKEN", out csrfTokens))
            {
                string? token = csrfTokens.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    response.Dispose();

                    using HttpRequestMessage retryRequest = CloneHttpRequestMessage(request);
                    retryRequest.Headers.Remove("X-CSRF-TOKEN");
                    retryRequest.Headers.Add("X-CSRF-TOKEN", token);

                    return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return response;
    }

    private static HttpRequestMessage CloneHttpRequestMessage(HttpRequestMessage req)
    {
        HttpRequestMessage clone = new(req.Method, req.RequestUri)
        {
            Version = req.Version
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in req.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (req.Content != null)
        {
            byte[] contentBytes = req.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(contentBytes);
            foreach (KeyValuePair<string, IEnumerable<string>> header in req.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
