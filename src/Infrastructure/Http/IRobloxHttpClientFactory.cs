namespace RbxManager.Infrastructure.Http;

using System.Net.Http;

public interface IRobloxHttpClientFactory
{
    HttpClient CreateClient();
}
