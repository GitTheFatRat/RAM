namespace RbxManager.Core.Tests.Settings;

using System.Collections.Generic;
using System.Text.Json;
using RbxManager.Core.Settings;
using Xunit;

public sealed class UiStateTests
{
    [Fact]
    public void UiState_Clamping_EnforcesMinimumDimensions()
    {
        UiStateSettings state = new(
            WindowWidth: 500,
            WindowHeight: 300,
            WindowTop: 100,
            WindowLeft: 100);

        UiStateSettings clamped = state.ValidateAndClamp(1920, 1080);

        Assert.Equal(UiStateSettings.MinimumWindowWidth, clamped.WindowWidth);
        Assert.Equal(UiStateSettings.MinimumWindowHeight, clamped.WindowHeight);
    }

    [Fact]
    public void UiState_Clamping_ResetsOffScreenBoundsToCentered()
    {
        UiStateSettings offscreen = new(
            WindowWidth: 1000,
            WindowHeight: 700,
            WindowTop: 3000,
            WindowLeft: -500);

        UiStateSettings clamped = offscreen.ValidateAndClamp(1920, 1080);

        // Centered coordinates: (1920 - 1000) / 2 = 460; (1080 - 700) / 2 = 190
        Assert.Equal(460, clamped.WindowLeft);
        Assert.Equal(190, clamped.WindowTop);
    }

    [Fact]
    public void UiState_Clamping_PreservesValidCoordinates()
    {
        UiStateSettings valid = new(
            WindowWidth: 1200,
            WindowHeight: 800,
            WindowTop: 50,
            WindowLeft: 150);

        UiStateSettings clamped = valid.ValidateAndClamp(1920, 1080);

        Assert.Equal(1200, clamped.WindowWidth);
        Assert.Equal(800, clamped.WindowHeight);
        Assert.Equal(50, clamped.WindowTop);
        Assert.Equal(150, clamped.WindowLeft);
    }

    [Fact]
    public void UiState_JsonRoundTrip_PersistsAndRestores()
    {
        AppSettings original = new(
            Language: "vi",
            Theme: "Dark",
            UiState: new UiStateSettings(
                WindowWidth: 1280,
                WindowHeight: 720,
                WindowTop: 100,
                WindowLeft: 200,
                IsMaximized: true,
                LastSelectedPage: "Accounts",
                SortColumn: "Alias",
                SortDescending: true,
                AccountsColumnWidths: new Dictionary<string, double> { ["Alias"] = 180, ["Status"] = 150 }));

        string json = JsonSerializer.Serialize(original);
        AppSettings? deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.UiState);
        Assert.Equal(1280, deserialized.UiState.WindowWidth);
        Assert.Equal(720, deserialized.UiState.WindowHeight);
        Assert.True(deserialized.UiState.IsMaximized);
        Assert.Equal("Accounts", deserialized.UiState.LastSelectedPage);
        Assert.Equal("Alias", deserialized.UiState.SortColumn);
        Assert.True(deserialized.UiState.SortDescending);
        Assert.NotNull(deserialized.UiState.AccountsColumnWidths);
        Assert.Equal(180, deserialized.UiState.AccountsColumnWidths["Alias"]);
    }

    [Fact]
    public void UiState_TolerantLoad_MissingOrCorruptSection()
    {
        // JSON without UiState section
        string jsonWithoutUi = """{"Language":"vi","Theme":"Dark"}""";
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(jsonWithoutUi);
        Assert.NotNull(settings);
        Assert.Null(settings.UiState);

        // Null UiState clamps with defaults
        UiStateSettings defaults = new();
        UiStateSettings clamped = defaults.ValidateAndClamp(1920, 1080);
        Assert.Equal(UiStateSettings.DefaultWindowWidth, clamped.WindowWidth);
        Assert.Equal(UiStateSettings.DefaultWindowHeight, clamped.WindowHeight);
    }
}
