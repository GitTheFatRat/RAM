namespace RbxManager.App;

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Pages;
using RbxManager.Core.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;

public partial class MainWindow : FluentWindow
{
    private readonly ISettingsService _settingsService;
    public MainViewModel ViewModel { get; }

    public MainWindow(
        MainViewModel viewModel,
        IServiceProvider serviceProvider,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(contentDialogService);
        ArgumentNullException.ThrowIfNull(snackbarService);
        ArgumentNullException.ThrowIfNull(settingsService);

        ViewModel = viewModel;
        _settingsService = settingsService;
        ViewModel.IsMultiInstanceEnabled = _settingsService.CurrentSettings.MultiInstanceEnabled;
        _settingsService.SettingsChanged += s =>
        {
            if (Dispatcher.CheckAccess())
            {
                ViewModel.IsMultiInstanceEnabled = s.MultiInstanceEnabled;
            }
            else
            {
                _ = Dispatcher.BeginInvoke(() => ViewModel.IsMultiInstanceEnabled = s.MultiInstanceEnabled);
            }
        };
        DataContext = this;

        InitializeComponent();

        contentDialogService.SetDialogHost(RootContentDialogHost);
        snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Navigated += (_, _) => _ = RootSnackbarPresenter.HideCurrent();

        RestoreWindowState();

        Closing += OnWindowClosing;
    }

    private void RestoreWindowState()
    {
        try
        {
            AppSettings settings = _settingsService.CurrentSettings;
            double screenWidth = SystemParameters.VirtualScreenWidth > 0 ? SystemParameters.VirtualScreenWidth : 1920;
            double screenHeight = SystemParameters.VirtualScreenHeight > 0 ? SystemParameters.VirtualScreenHeight : 1080;

            UiStateSettings uiState = (settings.UiState ?? new UiStateSettings()).ValidateAndClamp(screenWidth, screenHeight);

            Width = uiState.WindowWidth ?? UiStateSettings.DefaultWindowWidth;
            Height = uiState.WindowHeight ?? UiStateSettings.DefaultWindowHeight;
            Top = uiState.WindowTop ?? 100;
            Left = uiState.WindowLeft ?? 100;

            if (uiState.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }

            Type targetPageType = typeof(AccountsPage);
            if (!string.IsNullOrEmpty(uiState.LastSelectedPage))
            {
                targetPageType = uiState.LastSelectedPage switch
                {
                    nameof(ServersPage) => typeof(ServersPage),
                    nameof(RunningPage) => typeof(RunningPage),
                    nameof(DiagnosticsPage) => typeof(DiagnosticsPage),
                    nameof(SettingsPage) => typeof(SettingsPage),
                    _ => typeof(AccountsPage)
                };
            }

            RootNavigation.Loaded += (_, _) => RootNavigation.Navigate(targetPageType);
        }
        catch
        {
            RootNavigation.Loaded += (_, _) => RootNavigation.Navigate(typeof(AccountsPage));
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        try
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            double width = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;
            double height = RestoreBounds.Height > 0 ? RestoreBounds.Height : Height;
            string lastPage = RootNavigation.SelectedItem?.TargetPageType?.Name ?? nameof(AccountsPage);

            // Merge into the latest state inside the settings lock: a snapshot taken here would overwrite a resolution
            // or toggle save that is still in flight. Bounded wait so the write lands before the process exits.
            Task uiStateSave = _settingsService.UpdateSettingsAsync(
                WithUiState(width, height, RestoreBounds.Top, RestoreBounds.Left, isMaximized, lastPage));
            if (!uiStateSave.Wait(TimeSpan.FromSeconds(2)))
            {
                Serilog.Log.Warning("Window state save did not finish before close.");
            }
        }
        catch
        {
        }
    }

    internal static Func<AppSettings, AppSettings> WithUiState(
        double width,
        double height,
        double top,
        double left,
        bool isMaximized,
        string lastPage) =>
        settings => settings with
        {
            UiState = (settings.UiState ?? new UiStateSettings()) with
            {
                WindowWidth = width,
                WindowHeight = height,
                WindowTop = top,
                WindowLeft = left,
                IsMaximized = isMaximized,
                LastSelectedPage = lastPage
            }
        };

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (RootNavigation.SelectedItem?.TargetPageType != typeof(AccountsPage))
            {
                RootNavigation.Navigate(typeof(AccountsPage));
            }
        }
#if DEBUG
        if (string.Equals(Environment.GetEnvironmentVariable("RBXMANAGER_UITEST"), "1", StringComparison.Ordinal))
        {
            switch (e.Key)
            {
                case Key.F1:
                    RootNavigation.Navigate(typeof(AccountsPage));
                    e.Handled = true;
                    break;
                case Key.F2:
                    RootNavigation.Navigate(typeof(ServersPage));
                    e.Handled = true;
                    break;
                case Key.F3:
                    RootNavigation.Navigate(typeof(RunningPage));
                    e.Handled = true;
                    break;
                case Key.F4:
                    RootNavigation.Navigate(typeof(DiagnosticsPage));
                    e.Handled = true;
                    break;
                case Key.F6:
                    RootNavigation.Navigate(typeof(SettingsPage));
                    e.Handled = true;
                    break;
            }
        }
#endif
    }
}
