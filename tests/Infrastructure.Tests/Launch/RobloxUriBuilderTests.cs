namespace RbxManager.Infrastructure.Tests.Launch;

using System;
using RbxManager.Infrastructure.Launch;

public sealed class RobloxUriBuilderTests
{
    private const string FakeTicket = "FAKE_TICKET_FOR_TESTS_456";

    [Fact]
    public void BuildLaunchUri_StandardPlace_ContainsAllExpectedComponents()
    {
        long placeId = 1818;
        long unixMs = 1700000000000;
        string trackerId = "987654321";

        string uri = RobloxUriBuilder.BuildLaunchUri(FakeTicket, placeId, null, unixMs, trackerId);

        Assert.StartsWith("roblox-player:1+launchmode:play+", uri, StringComparison.Ordinal);
        Assert.Contains("+gameinfo:" + FakeTicket + "+", uri, StringComparison.Ordinal);
        Assert.Contains("+launchtime:1700000000000+", uri, StringComparison.Ordinal);
        Assert.Contains("+browsertrackerid:987654321+", uri, StringComparison.Ordinal);
        Assert.Contains("+robloxLocale:en_us+", uri, StringComparison.Ordinal);
        Assert.Contains("+gameLocale:en_us+", uri, StringComparison.Ordinal);
        Assert.Contains("+channel:+LaunchExp:InApp", uri, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLaunchUri_PlaceLauncherUrl_IsCorrectlyUrlEncoded()
    {
        long placeId = 1818;
        long unixMs = 1700000000000;
        string trackerId = "987654321";

        string uri = RobloxUriBuilder.BuildLaunchUri(FakeTicket, placeId, null, unixMs, trackerId);

        // Raw PlaceLauncherUrl starts with https://assetgame.roblox.com
        // Encoded must start with https%3A%2F%2Fassetgame.roblox.com
        Assert.Contains("https%3A%2F%2Fassetgame.roblox.com", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLaunchUri_WithJobId_UsesRequestGameJobAndIncludesJobId()
    {
        long placeId = 1818;
        string jobId = Guid.NewGuid().ToString();
        long unixMs = 1700000000000;
        string trackerId = "987654321";

        string rawUrl = RobloxUriBuilder.BuildPlaceLauncherUrl(placeId, jobId, trackerId);
        Assert.Contains("request=RequestGameJob", rawUrl, StringComparison.Ordinal);
        Assert.Contains("gameId=" + jobId, rawUrl, StringComparison.Ordinal);

        string uri = RobloxUriBuilder.BuildLaunchUri(FakeTicket, placeId, jobId, unixMs, trackerId);
        Assert.Contains("RequestGameJob", uri, StringComparison.Ordinal);
        Assert.Contains(jobId, uri, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLaunchUri_WithoutJobId_UsesRequestGame()
    {
        long placeId = 1818;
        string trackerId = "987654321";

        string rawUrl = RobloxUriBuilder.BuildPlaceLauncherUrl(placeId, null, trackerId);
        Assert.Contains("request=RequestGame&", rawUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestGameJob", rawUrl, StringComparison.Ordinal);
    }
}
