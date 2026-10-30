namespace RbxManager.App.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using RbxManager.App.ViewModels;
using RbxManager.Core.Launch;
using RbxManager.Core.Servers;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Settings;
using Xunit;

/// <summary>
/// M44 Flow 6 real-data: the window-close UiState save runs against the real SettingsService and a real settings.json.
/// Reproduces the shutdown ordering where Closing captured state before an in-flight resolution save committed.
/// </summary>
public sealed class ShutdownSettingsPersistenceRealDataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RbxM44Settings_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WindowCloseSave_MergesIntoLatestState_AndNeverRevertsInFlightResolutionEdit()
    {
        string settingsPath = Path.Combine(_directory, "settings.json");
        using (SettingsService service = new(settingsPath))
        {
            await service.LoadSettingsAsync();

            // Debounced resolution edit commits while the window is closing.
            await service.UpdateSettingsAsync(s => s with { ResolutionWidth = 640, ResolutionHeight = 360 });
            await service.UpdateSettingsAsync(MainWindow.WithUiState(1280, 720, 10, 20, isMaximized: false, lastPage: "SettingsPage"));
        }

        using SettingsService reloaded = new(settingsPath);
        AppSettings onDisk = await reloaded.LoadSettingsAsync();
        Assert.Equal(640, onDisk.ResolutionWidth);
        Assert.Equal(360, onDisk.ResolutionHeight);
        Assert.NotNull(onDisk.UiState);
        Assert.Equal(1280, onDisk.UiState!.WindowWidth);
        Assert.Equal("SettingsPage", onDisk.UiState.LastSelectedPage);
    }

    // M44 B17: ServersViewModel.PlaceIdText used to save a CurrentSettings snapshot, silently reverting concurrent edits.
    [Fact]
    public async Task ServersPlaceIdEdits_RacingOtherWriters_LoseNoUpdatesOnDisk()
    {
        string settingsPath = Path.Combine(_directory, "settings.json");
        using SettingsService service = new(settingsPath);
        await service.LoadSettingsAsync();
        await service.UpdateSettingsAsync(s => s with { LaunchDelaySeconds = 20 });

        ServersViewModel viewModel = new(
            Substitute.For<IServerBrowserService>(),
            Substitute.For<IAccountStore>(),
            Substitute.For<ILauncher>(),
            Substitute.For<ISessionTracker>(),
            Substitute.For<Wpf.Ui.IContentDialogService>(),
            Substitute.For<Wpf.Ui.ISnackbarService>(),
            Substitute.For<IPresenceService>(),
            settingsService: service);

        const int Increments = 40;
        Task writers = Task.WhenAll(Enumerable.Range(0, Increments)
            .Select(_ => Task.Run(() => service.UpdateSettingsAsync(s => s with { LaunchDelaySeconds = s.LaunchDelaySeconds + 1 }))));
        for (int i = 0; i < Increments; i++)
        {
            viewModel.PlaceIdText = (1000000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Task.Yield();
        }

        await writers;
        await service.UpdateSettingsAsync(s => s); // queue behind any fire-and-forget place-id save

        using SettingsService reloaded = new(settingsPath);
        AppSettings onDisk = await reloaded.LoadSettingsAsync();
        Assert.Equal(20 + Increments, onDisk.LaunchDelaySeconds);
        Assert.StartsWith("10000", onDisk.LastPlaceId, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
