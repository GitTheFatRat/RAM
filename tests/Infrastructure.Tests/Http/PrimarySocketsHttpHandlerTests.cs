namespace RbxManager.Infrastructure.Tests.Http;

using System.Net.Http;
using System.Runtime.Versioning;
using RbxManager.Infrastructure;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class PrimarySocketsHttpHandlerTests
{
    [Fact]
    public void CreatePrimarySocketsHttpHandler_ConfiguredWithCorrectSecurityDefaults()
    {
        using SocketsHttpHandler handler = InfrastructureServiceExtensions.CreatePrimarySocketsHttpHandler();

        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
    }
}
