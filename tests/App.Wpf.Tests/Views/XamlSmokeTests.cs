namespace RbxManager.App.Tests.Views;

using System;
using System.Diagnostics;
using System.Windows;
using NSubstitute;
using RbxManager.App.Services;
using RbxManager.App.Tests.Helpers;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Controls;
using RbxManager.App.Views.Pages;
using RbxManager.App.Views.Windows;
using RbxManager.Core.Auth;
using RbxManager.Core.Backup;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Instance;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Servers;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Xunit;

public sealed class XamlSmokeTests
{
    private static void ExecuteWithTraceListener(Action action)
    {
        StaRunner.Run(() =>
        {
            BindingErrorTraceListener listener = new();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            try
            {
                action();
                Assert.Empty(listener.Errors);
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            }
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Controls_LoadWithoutExceptionsOrBindingErrors(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            AccountEditorControl accountEditor = new();
            Assert.NotNull(accountEditor);

            AccountPickerControl accountPicker = new(Array.Empty<Account>());
            Assert.NotNull(accountPicker);

            BackupPasswordDialogControl backupDialog = new();
            Assert.NotNull(backupDialog);

            LaunchDialogControl launchDialog = new();
            Assert.NotNull(launchDialog);

            MasterPasswordSetupControl masterPasswordSetup = new();
            Assert.NotNull(masterPasswordSetup);

            RenameGroupDialogControl renameGroup = new("Default");
            Assert.NotNull(renameGroup);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void AccountsPage_LoadsWithoutExceptionsOrBindingErrors(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            IAccountStore accountStore = Substitute.For<IAccountStore>();
            IContentDialogService contentDialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();
            IRobloxAuthService authService = Substitute.For<IRobloxAuthService>();
            ILauncher launcher = Substitute.For<ILauncher>();
            ISettingsService settingsService = Substitute.For<ISettingsService>();
            settingsService.CurrentSettings.Returns(AppSettings.Default);
            IBackupService backupService = Substitute.For<IBackupService>();

            AccountsViewModel viewModel = new(
                accountStore,
                contentDialogService,
                snackbarService,
                authService,
                launcher,
                settingsService,
                backupService);

            AccountsPage page = new(viewModel);
            Assert.NotNull(page);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void RunningPage_LoadsWithoutExceptionsOrBindingErrors(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            ISessionTracker sessionTracker = Substitute.For<ISessionTracker>();
            IAccountStore accountStore = Substitute.For<IAccountStore>();
            IProcessProvider processProvider = Substitute.For<IProcessProvider>();
            IContentDialogService contentDialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            ISettingsService runningSettings = Substitute.For<ISettingsService>();
            runningSettings.CurrentSettings.Returns(AppSettings.Default);

            RunningViewModel viewModel = new(
                sessionTracker,
                accountStore,
                processProvider,
                contentDialogService,
                snackbarService,
                Substitute.For<Core.Rotation.IRotationManager>(),
                runningSettings);

            RunningPage page = new(viewModel);
            Assert.NotNull(page);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void ServersPage_LoadsWithoutExceptionsOrBindingErrors(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            IServerBrowserService serverBrowser = Substitute.For<IServerBrowserService>();
            IAccountStore accountStore = Substitute.For<IAccountStore>();
            ILauncher launcher = Substitute.For<ILauncher>();
            ISessionTracker sessionTracker = Substitute.For<ISessionTracker>();
            IContentDialogService contentDialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            ServersViewModel viewModel = new(
                serverBrowser,
                accountStore,
                launcher,
                sessionTracker,
                contentDialogService,
                snackbarService,
                Substitute.For<IPresenceService>());

            ServersPage page = new(viewModel);
            Assert.NotNull(page);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void SettingsPage_LoadsWithoutExceptionsOrBindingErrors(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            ISettingsService settingsService = Substitute.For<ISettingsService>();
            settingsService.CurrentSettings.Returns(AppSettings.Default);
            IInstanceGate instanceGate = Substitute.For<IInstanceGate>();
            IContentDialogService contentDialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            SettingsViewModel viewModel = new(
                settingsService,
                instanceGate,
                contentDialogService,
                snackbarService);

            SettingsPage page = new(viewModel);
            Assert.NotNull(page);
            Assert.True(page.Resources.Contains("SettingLabelStyle"));
            Assert.IsType<Style>(page.Resources["SettingLabelStyle"]);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void DiagnosticsPage_LoadsWithoutExceptionsOrBindingErrors(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            IDiagnosticsService diagnosticsService = Substitute.For<IDiagnosticsService>();
            IAccountStore accountStore = Substitute.For<IAccountStore>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            DiagnosticsViewModel viewModel = new(
                diagnosticsService,
                accountStore,
                snackbarService);

            DiagnosticsPage page = new(viewModel);
            Assert.NotNull(page);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void MasterPasswordWindow_LoadsWithoutExceptionsOrBindingErrors(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            IVaultKeyProvider vaultKeyProvider = Substitute.For<IVaultKeyProvider>();
            IAccountStore accountStore = Substitute.For<IAccountStore>();

            MasterPasswordWindow window = new(vaultKeyProvider, accountStore);
            Assert.NotNull(window);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void AddAccountWindow_LoadsInInitialState(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            IAccountStore accountStore = Substitute.For<IAccountStore>();
            IRobloxAuthService authService = Substitute.For<IRobloxAuthService>();
            AddAccountViewModel viewModel = new(accountStore, authService);
            AddAccountWindow window = new(viewModel);
            Assert.NotNull(window);
            Assert.Same(viewModel, window.ViewModel);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void MainWindow_LoadsWithoutExceptions(string theme)
    {
        ExecuteWithTraceListener(() =>
        {
            TestAppResources.Initialize(theme);

            ISettingsService settingsService = Substitute.For<ISettingsService>();
            settingsService.CurrentSettings.Returns(AppSettings.Default);
            IContentDialogService contentDialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();
            IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
            MainViewModel mainViewModel = new();

            MainWindow window = new(
                mainViewModel,
                serviceProvider,
                contentDialogService,
                snackbarService,
                settingsService);

            Assert.NotNull(window);
        });
    }

    [Fact]
    public void ServersViewModel_PlaceIdText_LoadsFromAndSavesToSettingsService()
    {
        IServerBrowserService serverBrowser = Substitute.For<IServerBrowserService>();
        IAccountStore accountStore = Substitute.For<IAccountStore>();
        ILauncher launcher = Substitute.For<ILauncher>();
        ISessionTracker sessionTracker = Substitute.For<ISessionTracker>();
        IContentDialogService contentDialogService = Substitute.For<IContentDialogService>();
        ISnackbarService snackbarService = Substitute.For<ISnackbarService>();
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        settingsService.CurrentSettings.Returns(new AppSettings(LastPlaceId: "2753915549"));

        ServersViewModel viewModel = new(
            serverBrowser,
            accountStore,
            launcher,
            sessionTracker,
            contentDialogService,
            snackbarService,
            Substitute.For<IPresenceService>(),
            settingsService: settingsService);

        Assert.Equal("2753915549", viewModel.PlaceIdText);
        settingsService.DidNotReceiveWithAnyArgs().SaveSettingsAsync(default!);

        viewModel.PlaceIdText = "1537690962";

        settingsService.Received(1).SaveSettingsAsync(
            Arg.Is<AppSettings>(s => s.LastPlaceId == "1537690962"));
    }
}
