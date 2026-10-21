namespace RbxManager.Core.Tests.Models;

using System;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public sealed class LaunchRequestTests
{
    [Fact]
    public void Validate_ValidPlaceIdAndNoJobId_ReturnsSuccess()
    {
        LaunchRequest request = new(new AccountId(1), 1818);
        Result result = request.Validate();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ValidPlaceIdAndValidGuidJobId_ReturnsSuccess()
    {
        string jobId = Guid.NewGuid().ToString();
        LaunchRequest request = new(new AccountId(1), 1818, jobId);
        Result result = request.Validate();
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public void Validate_InvalidPlaceId_ReturnsFailure(long placeId)
    {
        LaunchRequest request = new(new AccountId(1), placeId);
        Result result = request.Validate();
        Assert.False(result.IsSuccess);
        Assert.Contains("PlaceId", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("xyz-abc-def")]
    public void Validate_InvalidJobId_ReturnsFailure(string invalidJobId)
    {
        LaunchRequest request = new(new AccountId(1), 1818, invalidJobId);
        Result result = request.Validate();
        Assert.False(result.IsSuccess);
        Assert.Contains("GUID", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
