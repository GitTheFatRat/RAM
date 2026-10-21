namespace RbxManager.Core.Tests.Launch;

using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using Xunit;

public sealed class LaunchTemplateValidatorTests
{
    [Fact]
    public void ValidateLaunchUriTemplate_DefaultTemplate_IsValid()
    {
        Result result = LaunchTemplateValidator.ValidateLaunchUriTemplate(LaunchTemplateValidator.DefaultLaunchUriTemplate);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateLaunchUriTemplate_NullOrEmpty_IsValid()
    {
        Assert.True(LaunchTemplateValidator.ValidateLaunchUriTemplate(null).IsSuccess);
        Assert.True(LaunchTemplateValidator.ValidateLaunchUriTemplate(string.Empty).IsSuccess);
        Assert.True(LaunchTemplateValidator.ValidateLaunchUriTemplate("   ").IsSuccess);
    }

    [Fact]
    public void ValidateLaunchUriTemplate_MissingScheme_Fails()
    {
        Result result = LaunchTemplateValidator.ValidateLaunchUriTemplate("custom-protocol:1+gameinfo:{ticket}");
        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidScheme", result.ErrorCode);
    }

    [Fact]
    public void ValidateLaunchUriTemplate_MissingTicket_Fails()
    {
        Result result = LaunchTemplateValidator.ValidateLaunchUriTemplate("roblox-player:1+launchmode:play");
        Assert.False(result.IsSuccess);
        Assert.Equal("MissingTicketPlaceholder", result.ErrorCode);
    }

    [Fact]
    public void ValidateLaunchUriTemplate_UnknownPlaceholder_Fails()
    {
        Result result = LaunchTemplateValidator.ValidateLaunchUriTemplate("roblox-player:1+gameinfo:{ticket}+{unknownPlaceholder}");
        Assert.False(result.IsSuccess);
        Assert.Equal("UnknownPlaceholder", result.ErrorCode);
    }

    [Fact]
    public void ValidatePlaceLauncherUrlTemplate_DefaultTemplate_IsValid()
    {
        Result result = LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate(LaunchTemplateValidator.DefaultPlaceLauncherUrlTemplate);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidatePlaceLauncherUrlTemplate_NullOrEmpty_IsValid()
    {
        Assert.True(LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate(null).IsSuccess);
        Assert.True(LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate(string.Empty).IsSuccess);
        Assert.True(LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate("   ").IsSuccess);
    }

    [Fact]
    public void ValidatePlaceLauncherUrlTemplate_HttpScheme_Fails()
    {
        Result result = LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate("http://assetgame.roblox.com/game/PlaceLauncher.ashx?placeId={placeId}");
        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidScheme", result.ErrorCode);
    }

    [Fact]
    public void ValidatePlaceLauncherUrlTemplate_NonRobloxHost_Fails()
    {
        Result result = LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate("https://attacker.com/PlaceLauncher.ashx?placeId={placeId}");
        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidHost", result.ErrorCode);
    }

    [Fact]
    public void ValidatePlaceLauncherUrlTemplate_MissingPlaceId_Fails()
    {
        Result result = LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate("https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame");
        Assert.False(result.IsSuccess);
        Assert.Equal("MissingPlaceIdPlaceholder", result.ErrorCode);
    }

    [Fact]
    public void ValidatePlaceLauncherUrlTemplate_UnknownPlaceholder_Fails()
    {
        Result result = LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate("https://assetgame.roblox.com/game/PlaceLauncher.ashx?placeId={placeId}&extra={unknownParam}");
        Assert.False(result.IsSuccess);
        Assert.Equal("UnknownPlaceholder", result.ErrorCode);
    }
}
