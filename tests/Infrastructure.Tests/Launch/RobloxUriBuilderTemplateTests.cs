namespace RbxManager.Infrastructure.Tests.Launch;

using System;
using RbxManager.Infrastructure.Launch;
using Xunit;

public sealed class RobloxUriBuilderTemplateTests
{
    private const string ExpectedPlaceLauncherPreM7 =
        "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId=123456789&placeId=1818&isPlayTogetherGame=false";

    private const string ExpectedPlaceLauncherWithJobIdPreM7 =
        "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId=123456789&placeId=1818&gameId=d3b07384-d113-46d8-9177-3e1b12480392&isPlayTogetherGame=false";

    private const string ExpectedLaunchUriPreM7 =
        "roblox-player:1+launchmode:play+gameinfo:test-ticket+launchtime:1700000000000+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestGame%26browserTrackerId%3D123456789%26placeId%3D1818%26isPlayTogetherGame%3Dfalse+browsertrackerid:123456789+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";

    [Fact]
    public void BuildPlaceLauncherUrl_DefaultTemplate_MatchesPreM7RegressionFixture()
    {
        string actualNoJob = RobloxUriBuilder.BuildPlaceLauncherUrl(1818, null, "123456789");
        Assert.Equal(ExpectedPlaceLauncherPreM7, actualNoJob);

        string actualWithJob = RobloxUriBuilder.BuildPlaceLauncherUrl(1818, "d3b07384-d113-46d8-9177-3e1b12480392", "123456789");
        Assert.Equal(ExpectedPlaceLauncherWithJobIdPreM7, actualWithJob);
    }

    [Fact]
    public void BuildLaunchUri_DefaultTemplate_MatchesPreM7RegressionFixture()
    {
        string actualUri = RobloxUriBuilder.BuildLaunchUri("test-ticket", 1818, null, 1700000000000, "123456789");
        Assert.Equal(ExpectedLaunchUriPreM7, actualUri);
    }

    [Fact]
    public void BuildLaunchUri_ValidOverrides_AppliesTemplatesCorrectly()
    {
        string customPlaceTemplate = "https://assetgame.roblox.com/custom?id={placeId}{gameIdParam}";
        string customLaunchTemplate = "roblox-player:custom+ticket:{ticket}+url:{placeLauncherUrl}";

        string uri = RobloxUriBuilder.BuildLaunchUri(
            "my-ticket",
            9999,
            null,
            1700000000000,
            "tracker",
            customLaunchTemplate,
            customPlaceTemplate);

        string expectedEncodedPlaceUrl = Uri.EscapeDataString("https://assetgame.roblox.com/custom?id=9999");
        string expectedUri = $"roblox-player:custom+ticket:my-ticket+url:{expectedEncodedPlaceUrl}";

        Assert.Equal(expectedUri, uri);
    }

    [Fact]
    public void BuildLaunchUri_InvalidOverrides_FallsBackToDefaults()
    {
        string invalidLaunchTemplate = "invalid-scheme:1+ticket:{ticket}";
        string invalidPlaceTemplate = "http://evil.com/fake?placeId={placeId}";

        string uri = RobloxUriBuilder.BuildLaunchUri(
            "test-ticket",
            1818,
            null,
            1700000000000,
            "123456789",
            invalidLaunchTemplate,
            invalidPlaceTemplate);

        Assert.Equal(ExpectedLaunchUriPreM7, uri);
    }
}
