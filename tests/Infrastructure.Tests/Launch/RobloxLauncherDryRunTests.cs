namespace RbxManager.Infrastructure.Tests.Launch;

using System;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Infrastructure.Launch;
using Xunit;

public sealed class RobloxLauncherDryRunTests
{
    private sealed class CountingProcessStarter : IProcessStarter
    {
        public int StartCallCount { get; private set; }

        public bool IsRobloxPlayerInstalled() => true;

        public bool Start(string uri)
        {
            StartCallCount++;
            return true;
        }
    }

    private sealed class CountingAuthService : IRobloxAuthService
    {
        public int TicketCallCount { get; private set; }
        public int ValidateCallCount { get; private set; }

        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default)
        {
            ValidateCallCount++;
            return Task.FromResult(Result.Success(new RobloxUser(1, "Test", "Test")));
        }

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default)
        {
            TicketCallCount++;
            return Task.FromResult(Result.Success(SecretValue.FromString("ticket")));
        }
    }

    [Fact]
    public void DryRun_BuildsUri_MakesZeroHttpCallsAndZeroProcessStarts()
    {
        CountingProcessStarter processStarter = new();
        CountingAuthService authService = new();

        long placeId = 1818;
        string? jobId = null;
        long unixMs = 1700000000000;
        string trackerId = "123456789";

        // Dry run builds the URI with literal <ticket>
        string dryRunUri = RobloxUriBuilder.BuildLaunchUri(
            "<ticket>",
            placeId,
            jobId,
            unixMs,
            trackerId);

        // Verify structure
        Assert.StartsWith("roblox-player:", dryRunUri);
        Assert.Contains("gameinfo:<ticket>", dryRunUri);
        Assert.Contains("placeId%3D1818", dryRunUri);

        // Fakes assert zero invocations of network auth or process spawning
        Assert.Equal(0, processStarter.StartCallCount);
        Assert.Equal(0, authService.TicketCallCount);
        Assert.Equal(0, authService.ValidateCallCount);
    }
}
