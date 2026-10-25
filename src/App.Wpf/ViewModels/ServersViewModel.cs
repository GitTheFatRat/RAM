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

public partial class ServersViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource? _joinCts;
    private readonly IServerBrowserService _serverBrowserService;
    private readonly IPresenceService _presenceService;
    private readonly IAccountStore _accountStore;
    private readonly ILauncher _launcher;
    private readonly ISessionTracker _sessionTracker;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly Core.Instance.IInstanceGate? _instanceGate;
    private readonly Core.Settings.ISettingsService? _settingsService;
    private bool _suppressPlaceIdSave;

    [ObservableProperty]
    private string _placeIdText = "1818";

    partial void OnPlaceIdTextChanged(string value)
    {
        if (_suppressPlaceIdSave || _settingsService == null)
        {
            return;
        }

        string trimmed = value?.Trim() ?? string.Empty;
        Core.Settings.AppSettings current = _settingsService.CurrentSettings;
        if (!string.Equals(current.LastPlaceId, trimmed, StringComparison.Ordinal))
        {
            _ = _settingsService.SaveSettingsAsync(current with { LastPlaceId = trimmed });
        }
    }

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

    [ObservableProperty]
    private bool _isJoining;

    [ObservableProperty]
    private string _joinStatusText = string.Empty;

    [ObservableProperty]
    private string _followTargetUsername = string.Empty;

    public ObservableCollection<ServerInfo> Servers { get; } = new();

    public ServersViewModel(
        IServerBrowserService serverBrowserService,
        IAccountStore accountStore,
        ILauncher launcher,
        ISessionTracker sessionTracker,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IPresenceService presenceService,
        Core.Instance.IInstanceGate? instanceGate = null,
        Core.Settings.ISettingsService? settingsService = null)
    {
        _serverBrowserService = serverBrowserService;
        _presenceService = presenceService;
        _accountStore = accountStore;
        _launcher = launcher;
        _sessionTracker = sessionTracker;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _instanceGate = instanceGate;
        _settingsService = settingsService;

        if (!string.IsNullOrWhiteSpace(_settingsService?.CurrentSettings.LastPlaceId))
        {
            _suppressPlaceIdSave = true;
            _placeIdText = _settingsService.CurrentSettings.LastPlaceId;
            _suppressPlaceIdSave = false;
        }
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

        IReadOnlyList<Account> selectedAccounts = await PickAccountsAsync();
        if (selectedAccounts.Count == 0)
        {
            return;
        }

        await RunJoinQueueAsync(
            selectedAccounts,
            (account, _) => Task.FromResult<LaunchRequest?>(new LaunchRequest(account.Id, placeId, server.Id)));
    }

    [RelayCommand]
    public async Task FollowTargetAsync()
    {
        string targetUsername = FollowTargetUsername.Trim();
        if (targetUsername.Length == 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.FollowTargetRequired, ControlAppearance.Danger);
            return;
        }

        IReadOnlyList<Account> selectedAccounts = await PickAccountsAsync();
        if (selectedAccounts.Count == 0)
        {
            return;
        }

        await RunJoinQueueAsync(
            selectedAccounts,
            (account, ct) => ResolveFollowRequestAsync(account, targetUsername, ct));
    }

    // Re-resolved before every launch: the target may hop servers during the 20 s pacing gap,
    // and presence visibility depends on the viewing account.
    private async Task<LaunchRequest?> ResolveFollowRequestAsync(Account account, string targetUsername, CancellationToken ct)
    {
        JoinStatusText = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            System.Text.CompositeFormat.Parse(Strings.FollowTargetResolving),
            targetUsername);

        Result<TargetServer> lookup = await _presenceService.FindUserServerAsync(targetUsername, account.Cookie, ct);
        if (lookup.IsSuccess && lookup.Value != null)
        {
            return new LaunchRequest(account.Id, lookup.Value.PlaceId, lookup.Value.JobId);
        }

        string message = lookup.ErrorCode switch
        {
            PresenceErrorCodes.UserNotFound => string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                System.Text.CompositeFormat.Parse(Strings.FollowTargetUserNotFound),
                targetUsername),
            PresenceErrorCodes.ServerHidden => Strings.FollowTargetServerHidden,
            _ => lookup.Error ?? Strings.ErrorTitle
        };
        _snackbarService.Show(Strings.ErrorTitle, message, ControlAppearance.Danger);
        return null;
    }

    private async Task<IReadOnlyList<Account>> PickAccountsAsync()
    {
        IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
        if (accounts.Count == 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.EmptyAccountsList, ControlAppearance.Caution);
            return [];
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

        return dialogResult == ContentDialogResult.Primary ? pickerControl.GetSelectedAccounts() : [];
    }

    /// <param name="buildRequest">Returns null to abort the remaining queue (e.g. target went offline).</param>
    private async Task RunJoinQueueAsync(
        IReadOnlyList<Account> queue,
        Func<Account, CancellationToken, Task<LaunchRequest?>> buildRequest)
    {
        if (_instanceGate is { IsMultiInstanceEnabled: false })
        {
            _instanceGate.TryEnable();
        }

        string queueStatus = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Queued {queue.Count} accounts for launch...");
        Console.WriteLine(queueStatus);
        Serilog.Log.Information("Queued {Count} accounts for launch: {Aliases}", queue.Count, string.Join(", ", queue.Select(a => a.Alias)));

        int successCount = 0;
        int currentIndex = 0;
        bool aborted = false;
        CancellationTokenSource joinCts = new();
        _joinCts = joinCts;
        CancellationToken ct = joinCts.Token;
        IsJoining = true;
        try
        {
            foreach (Account account in queue)
            {
                currentIndex++;
                ct.ThrowIfCancellationRequested();
                LaunchRequest? request = await buildRequest(account, ct);
                if (request == null)
                {
                    aborted = true;
                    break;
                }

                JoinStatusText = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Launching {currentIndex} / {queue.Count}: {account.Alias}");
                RunningSession? session = await _sessionTracker.TrackLaunchAsync(
                    account.Id,
                    request,
                    () => _launcher.LaunchAsync(request, ct));

                if (session != null)
                {
                    successCount++;
                    account.ActiveProcessId = session.ProcessId;
                }

                if (currentIndex < queue.Count)
                {
                    int randomSeconds = Random.Shared.Next(
                        AccountsViewModel.MinSequentialLaunchDelaySeconds,
                        AccountsViewModel.MaxSequentialLaunchDelaySeconds + 1);
                    System.Text.CompositeFormat countdownFormat = System.Text.CompositeFormat.Parse(Strings.NextAccountCountdown);
                    for (int remainingSeconds = randomSeconds; remainingSeconds >= 1; remainingSeconds--)
                    {
                        ct.ThrowIfCancellationRequested();
                        JoinStatusText = string.Format(
                            System.Globalization.CultureInfo.CurrentCulture,
                            countdownFormat,
                            remainingSeconds);
                        await Task.Delay(1000, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Serilog.Log.Information("Server join queue cancelled after {Success} launches", successCount);
            _snackbarService.Show(Strings.AppTitle, Strings.BulkLaunchCancelled, ControlAppearance.Caution);
            return;
        }
        finally
        {
            if (ReferenceEquals(_joinCts, joinCts))
            {
                _joinCts = null;
            }

            joinCts.Dispose();
            IsJoining = false;
            JoinStatusText = string.Empty;
        }

        if (successCount > 0)
        {
            _snackbarService.Show(Strings.SuccessTitle, Strings.LaunchSuccess, ControlAppearance.Success);
        }
        else if (!aborted)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.ErrorTitle, ControlAppearance.Danger);
        }
    }

    [RelayCommand]
    public void CancelJoin()
    {
        try
        {
            _joinCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Queue finished between the null-check and Cancel().
        }
    }

    public void Dispose()
    {
        CancelJoin();
        GC.SuppressFinalize(this);
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
