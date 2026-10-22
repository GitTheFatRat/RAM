namespace RbxManager.Infrastructure.Http;

using System.Net.Http;
using RbxManager.Core.Models;

public interface IRobloxHttpClientFactory
{
    HttpClient CreateClient(ProxyConfig? proxy = null);
}
