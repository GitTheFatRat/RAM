namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.App.Views.Controls;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Servers;
using RbxManager.Core.Sessions;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public partial class ServersViewModel : ObservableObject
{
    private readonly IServerBrowserService _serverBrowserService;
    private readonly IAccountStore _accountStore;
    private readonly ILauncher _launcher;
    private readonly ISessionTracker _sessionTracker;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;

    [ObservableProperty]
    private string _placeIdText = "1818";

    [ObservableProperty]
    private int _selectedSortIndex = 1; // 0: Asc, 1: Desc

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    private string? _nextCursor;

    public bool HasNextPage => !string.IsNullOrWhiteSpace(NextCursor);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private bool _hasServers;

    public bool IsEmptyStateVisible => !HasServers && !IsLoading && !HasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<ServerInfo> Servers { get; } = new();

    public ServersViewModel(
        IServerBrowserService serverBrowserService,
        IAccountStore accountStore,
        ILauncher launcher,
        ISessionTracker sessionTracker,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService)
    {
        _serverBrowserService = serverBrowserService;
        _accountStore = accountStore;
        _launcher = launcher;
        _sessionTracker = sessionTracker;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadServersInternalAsync(cursor: null);
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (string.IsNullOrWhiteSpace(NextCursor))
        {
            return;
        }

        await LoadServersInternalAsync(cursor: NextCursor, append: true);
    }

    [RelayCommand]
    public void CopyServerId(ServerInfo? server)
    {
        if (server == null || string.IsNullOrWhiteSpace(server.Id))
        {
            return;
        }

        Clipboard.SetText(server.Id);
        _snackbarService.Show(Strings.SuccessTitle, Strings.ServerIdCopied, ControlAppearance.Success);
    }

    [RelayCommand]
    public async Task JoinWithAccountAsync(ServerInfo? server)
    {
        if (server == null || !long.TryParse(PlaceIdText.Trim(), out long placeId) || placeId <= 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.PlaceIdRequired, ControlAppearance.Danger);
            return;
        }

        IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
        if (accounts.Count == 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.EmptyAccountsList, ControlAppearance.Caution);
            return;
        }

        AccountPickerControl pickerControl = new(accounts);
        ContentDialogResult dialogResult = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.AccountPickerTitle,
                PrimaryButtonText = Strings.Launch,
                CloseButtonText = Strings.Cancel,
                Content = pickerControl
            });

        if (dialogResult != ContentDialogResult.Primary)
        {
            return;
        }

        IReadOnlyList<Account> selectedAccounts = pickerControl.GetSelectedAccounts();
        if (selectedAccounts.Count == 0)
        {
            return;
        }

        int successCount = 0;
        foreach (Account account in selectedAccounts)
        {
            LaunchRequest request = new(account.Id, placeId, server.Id);
            RunningSession? session = await _sessionTracker.TrackLaunchAsync(
                account.Id,
                request,
                () => _launcher.LaunchAsync(request));

            if (session != null)
            {
                successCount++;
            }
        }

        if (successCount > 0)
        {
            _snackbarService.Show(Strings.SuccessTitle, Strings.LaunchSuccess, ControlAppearance.Success);
        }
        else
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.ErrorTitle, ControlAppearance.Danger);
        }
    }

    private async Task LoadServersInternalAsync(string? cursor, bool append = false)
    {
        if (!long.TryParse(PlaceIdText.Trim(), out long placeId) || placeId <= 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.PlaceIdRequired, ControlAppearance.Danger);
            return;
        }

        IsLoading = true;
        try
        {
            ServerSort sort = SelectedSortIndex == 0 ? ServerSort.Ascending : ServerSort.Descending;
            Result<ServerPage> result = await _serverBrowserService.GetServersAsync(placeId, sort, cursor);

            if (result.IsSuccess && result.Value != null)
            {
                HasError = false;
                ErrorMessage = string.Empty;
                if (!append)
                {
                    Servers.Clear();
                }

                foreach (ServerInfo server in result.Value.Servers)
                {
                    Servers.Add(server);
                }

                NextCursor = result.Value.NextCursor;
                HasServers = Servers.Count > 0;
            }
            else
            {
                HasError = true;
                ErrorMessage = result.Error ?? Strings.ErrorTitle;
                _snackbarService.Show(Strings.ErrorTitle, result.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
