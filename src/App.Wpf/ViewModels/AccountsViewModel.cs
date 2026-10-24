namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.App.Views.Controls;
using RbxManager.App.Views.Windows;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Exceptions;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using RbxManager.Core.Validation;
using RbxManager.Infrastructure.Launch;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public partial class AccountsViewModel : ObservableObject, IDisposable
{
    public const int MinSequentialLaunchDelaySeconds = 60;
    public const int MaxSequentialLaunchDelaySeconds = 120;

    private static string _lastPlaceId = string.Empty;

    private readonly IAccountStore _accountStore;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IRobloxAuthService _authService;
    private readonly ILauncher _launcher;
    private readonly Core.Settings.ISettingsService? _settingsService;
    private readonly Core.Backup.IBackupService? _backupService;
    private readonly Core.Network.IDpiToolService? _dpiToolService;
    private readonly Core.Instance.IInstanceGate? _instanceGate;
    private readonly TimeProvider _timeProvider;
    private readonly Func<int, int, int> _randomDelayGenerator;
    private readonly List<AccountItemViewModel> _allAccounts = new();

    private CancellationTokenSource? _checkAllCts;
    private CancellationTokenSource? _bulkLaunchCts;
    private bool _disposed;

    public ObservableCollection<AccountItemViewModel> FilteredAccounts { get; } = new();
    public ObservableCollection<string> Groups { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenameGroupVisible))]
    [NotifyPropertyChangedFor(nameof(IsLaunchGroupVisible))]
    private string? _selectedGroup;

    public bool IsRenameGroupVisible => !string.IsNullOrEmpty(SelectedGroup) && !string.Equals(SelectedGroup, Strings.GroupFilterAll, StringComparison.OrdinalIgnoreCase);
    public bool IsLaunchGroupVisible => IsRenameGroupVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccountSelected))]
    [NotifyPropertyChangedFor(nameof(CanLaunchAccount))]
    private AccountItemViewModel? _selectedAccount;

    public bool IsAccountSelected => SelectedAccount != null;
    public bool HasCheckedAccounts => _allAccounts.Any(a => a.IsSelected);
    public bool CanLaunchAccount => (IsAccountSelected || HasCheckedAccounts) && !IsBulkLaunching;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsNoAccounts))]
    [NotifyPropertyChangedFor(nameof(IsNoSearchResults))]
    private bool _hasAccounts;

    public bool IsEmptyStateVisible => !HasAccounts;
    public bool IsNoAccounts => !IsLoading && _allAccounts.Count == 0;
    public bool IsNoSearchResults => !IsLoading && _allAccounts.Count > 0 && FilteredAccounts.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isCheckingAll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isCheckingSingle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(CanLaunchAccount))]
    private bool _isBulkLaunching;

    [ObservableProperty]
    private string _bulkLaunchCountdownText = string.Empty;

    [ObservableProperty]
    private string _bulkLaunchQueueText = string.Empty;

    public bool IsNotBusy => !IsLoading && !IsCheckingAll && !IsCheckingSingle && !IsBulkLaunching;

    [ObservableProperty]
    private string _checkAllProgressText = string.Empty;

    [ObservableProperty]
    private string _sortColumn = "Alias";

    [ObservableProperty]
    private bool _sortDescending;

    private readonly DebounceDispatcher _searchDebouncer;

    public AccountsViewModel(
        IAccountStore accountStore,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IRobloxAuthService authService,
        ILauncher launcher,
        Core.Settings.ISettingsService? settingsService = null,
        Core.Backup.IBackupService? backupService = null,
        TimeProvider? timeProvider = null,
        Core.Network.IDpiToolService? dpiToolService = null,
        Func<int, int, int>? randomDelayGenerator = null,
        Core.Instance.IInstanceGate? instanceGate = null)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
        _snackbarService = snackbarService ?? throw new ArgumentNullException(nameof(snackbarService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _settingsService = settingsService;
        _backupService = backupService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dpiToolService = dpiToolService;
        _randomDelayGenerator = randomDelayGenerator ?? Random.Shared.Next;
        _instanceGate = instanceGate;

        _searchDebouncer = new DebounceDispatcher(TimeSpan.FromMilliseconds(200));
        _selectedGroup = Strings.GroupFilterAll;

        if (_settingsService?.CurrentSettings.UiState != null)
        {
            _sortColumn = _settingsService.CurrentSettings.UiState.SortColumn ?? "Alias";
            _sortDescending = _settingsService.CurrentSettings.UiState.SortDescending;
        }

        if (!string.IsNullOrWhiteSpace(_settingsService?.CurrentSettings.LastPlaceId))
        {
            _lastPlaceId = _settingsService.CurrentSettings.LastPlaceId;
        }
    }

    partial void OnSelectedGroupChanged(string? value) => ApplyFilter();

    partial void OnSearchTextChanged(string value)
    {
        _searchDebouncer.Debounce(() =>
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(ApplyFilter);
            }
            else
            {
                ApplyFilter();
            }
        });
    }

    [RelayCommand]
    private void SelectGroup(string? group)
    {
        SelectedGroup = group;
    }

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        IsLoading = true;
        try
        {
            HashSet<AccountId> checkedIds = _allAccounts
                .Where(a => a.IsSelected)
                .Select(a => a.Id)
                .ToHashSet();

            Dictionary<AccountId, int> activePids = _allAccounts
                .Where(a => a.ActiveProcessId.HasValue)
                .ToDictionary(a => a.Id, a => a.ActiveProcessId!.Value);

            IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
            foreach (AccountItemViewModel staleItem in _allAccounts)
            {
                staleItem.PropertyChanged -= OnAccountItemPropertyChanged;
                staleItem.Dispose();
            }

            _allAccounts.Clear();
            foreach (Account account in accounts)
            {
                AccountItemViewModel item = new(account)
                {
                    IsSelected = checkedIds.Contains(account.Id),
                    ActiveProcessId = activePids.TryGetValue(account.Id, out int pid) ? pid : null
                };
                item.PropertyChanged += OnAccountItemPropertyChanged;
                _allAccounts.Add(item);
            }
            UpdateGroups();
            ApplyFilter();
            OnPropertyChanged(nameof(HasCheckedAccounts));
            OnPropertyChanged(nameof(CanLaunchAccount));
        }
        catch (VaultCorruptedException)
        {
            await ShowVaultCorruptedDialogAsync(_accountStore.VaultPath);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnAccountItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AccountItemViewModel.IsSelected), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(HasCheckedAccounts));
            OnPropertyChanged(nameof(CanLaunchAccount));
        }
    }

    private void UpdateGroups()
    {
        string? currentSelected = SelectedGroup;
        Groups.Clear();
        Groups.Add(Strings.GroupFilterAll);

        HashSet<string> distinct = _allAccounts
            .Select(a => a.Group?.Trim())
            .Where(g => !string.IsNullOrEmpty(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        foreach (string group in distinct)
        {
            Groups.Add(group);
        }

        if (currentSelected != null && Groups.Contains(currentSelected))
        {
            SelectedGroup = currentSelected;
        }
        else
        {
            SelectedGroup = Strings.GroupFilterAll;
        }
    }

    [RelayCommand]
    public async Task AddViaLoginAsync()
    {
        AddAccountViewModel addViewModel = new(_accountStore, _authService, _snackbarService, dpiToolService: _dpiToolService);
        AddAccountWindow addWindow = new(addViewModel)
        {
            Owner = Application.Current?.MainWindow
        };

        bool? result = addWindow.ShowDialog();
        if (result == true || addViewModel.ImportedCount > 0)
        {
            await LoadAccountsAsync();
        }
    }

    [RelayCommand]
    public async Task AddAccountAsync()
    {
        AccountEditorViewModel editorViewModel = new(isEdit: false);
        foreach (string group in _allAccounts.Select(a => a.Group?.Trim()).Where(g => !string.IsNullOrEmpty(g)).Distinct()!)
        {
            editorViewModel.AvailableGroups.Add(group);
        }
        AccountEditorControl editorControl = new()
        {
            DataContext = editorViewModel
        };

        ContentDialog dialog = new()
        {
            Title = Strings.AddAccountDialogTitle,
            PrimaryButtonText = Strings.Save,
            CloseButtonText = Strings.Cancel,
            Content = editorControl
        };

        dialog.Closing += (sender, args) =>
        {
            if (args.Result == ContentDialogResult.Primary)
            {
                string rawCookie = editorControl.ReadPassword();
                using SecretValue testCookie = SecretValue.FromString(rawCookie);

                Account testAccount = new(
                    Id: new AccountId(0),
                    Alias: editorViewModel.Alias,
                    Group: editorViewModel.Group,
                    Note: editorViewModel.Note,
                    Cookie: testCookie,
                    CreatedAt: DateTimeOffset.UtcNow);

                Result validation = AccountValidator.Validate(testAccount, _allAccounts.Select(a => a.Account));
                if (!validation.IsSuccess)
                {
                    args.Cancel = true;
                    editorViewModel.ErrorMessage = validation.Error ?? string.Empty;
                }
            }
        };

        ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.AddAccountDialogTitle,
                PrimaryButtonText = Strings.Save,
                CloseButtonText = Strings.Cancel,
                Content = editorControl
            });

        if (result == ContentDialogResult.Primary)
        {
            string rawCookie = editorControl.ReadPassword();
            using SecretValue cookieSecret = SecretValue.FromString(rawCookie);
            editorControl.ClearPassword();

            long nextId = _allAccounts.Count > 0 ? _allAccounts.Max(a => a.Id.Value) + 1 : 1;
            Account newAccount = new(
                Id: new AccountId(nextId),
                Alias: editorViewModel.Alias.Trim(),
                Group: string.IsNullOrWhiteSpace(editorViewModel.Group) ? null : editorViewModel.Group.Trim(),
                Note: string.IsNullOrWhiteSpace(editorViewModel.Note) ? null : editorViewModel.Note.Trim(),
                Cookie: cookieSecret,
                CreatedAt: DateTimeOffset.UtcNow);

            Result validation = AccountValidator.Validate(newAccount, _allAccounts.Select(a => a.Account));
            if (!validation.IsSuccess)
            {
                return;
            }

            try
            {
                Result addResult = await _accountStore.AddAsync(newAccount);
                if (addResult.IsSuccess)
                {
                    await LoadAccountsAsync();
                    _snackbarService.Show(Strings.SuccessTitle, Strings.AccountSavedMessage, ControlAppearance.Success);
                }
                else
                {
                    _snackbarService.Show(Strings.ErrorTitle, addResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
                }
            }
            catch (VaultCorruptedException)
            {
                await ShowVaultCorruptedDialogAsync(_accountStore.VaultPath);
            }
        }
        else
        {
            editorControl.ClearPassword();
        }
    }

    [RelayCommand]
    public async Task EditAccountAsync()
    {
        if (SelectedAccount == null)
        {
            return;
        }

        Account current = SelectedAccount.Account;
        AccountEditorViewModel editorViewModel = new(isEdit: true)
        {
            Alias = current.Alias,
            Group = current.Group ?? string.Empty,
            Note = current.Note ?? string.Empty
        };

        foreach (string group in _allAccounts.Select(a => a.Group?.Trim()).Where(g => !string.IsNullOrEmpty(g)).Distinct()!)
        {
            editorViewModel.AvailableGroups.Add(group);
        }

        AccountEditorControl editorControl = new()
        {
            DataContext = editorViewModel
        };

        ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.EditAccountDialogTitle,
                PrimaryButtonText = Strings.Save,
                CloseButtonText = Strings.Cancel,
                Content = editorControl
            });

        if (result == ContentDialogResult.Primary)
        {
            string newCookie = editorControl.ReadPassword();
            SecretValue cookieToUse = !string.IsNullOrWhiteSpace(newCookie)
                ? SecretValue.FromString(newCookie)
                : current.Cookie.Clone();

            editorControl.ClearPassword();

            Account updatedAccount = new(
                Id: current.Id,
                Alias: editorViewModel.Alias.Trim(),
                Group: string.IsNullOrWhiteSpace(editorViewModel.Group) ? null : editorViewModel.Group.Trim(),
                Note: string.IsNullOrWhiteSpace(editorViewModel.Note) ? null : editorViewModel.Note.Trim(),
                Cookie: cookieToUse,
                CreatedAt: current.CreatedAt,
                Username: current.Username,
                UserId: current.UserId,
                Status: current.Status,
                LastCheckedAt: current.LastCheckedAt);

            Result validation = AccountValidator.Validate(updatedAccount, _allAccounts.Select(a => a.Account));
            if (!validation.IsSuccess)
            {
                _snackbarService.Show(Strings.ErrorTitle, validation.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
                cookieToUse.Dispose();
                return;
            }

            try
            {
                Result updateResult = await _accountStore.UpdateAsync(updatedAccount);
                if (updateResult.IsSuccess)
                {
                    await LoadAccountsAsync();
                    _snackbarService.Show(Strings.SuccessTitle, Strings.AccountSavedMessage, ControlAppearance.Success);
                }
                else
                {
                    _snackbarService.Show(Strings.ErrorTitle, updateResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
                }
            }
            catch (VaultCorruptedException)
            {
                await ShowVaultCorruptedDialogAsync(_accountStore.VaultPath);
            }
            finally
            {
                cookieToUse.Dispose();
            }
        }
        else
        {
            editorControl.ClearPassword();
        }
    }

    [RelayCommand]
    public async Task DeleteAccountAsync()
    {
        if (SelectedAccount == null)
        {
            return;
        }

        CompositeFormat format = CompositeFormat.Parse(Strings.DeleteConfirmMessage);
        string message = string.Format(CultureInfo.CurrentCulture, format, SelectedAccount.Alias);

        ContentDialogResult confirmResult = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.DeleteConfirmTitle,
                Content = message,
                PrimaryButtonText = Strings.Delete,
                CloseButtonText = Strings.Cancel
            });

        if (confirmResult == ContentDialogResult.Primary)
        {
            try
            {
                await _accountStore.RemoveAsync(SelectedAccount.Id);
                await LoadAccountsAsync();
                _snackbarService.Show(Strings.SuccessTitle, Strings.AccountDeletedMessage, ControlAppearance.Success);
            }
            catch (VaultCorruptedException)
            {
                await ShowVaultCorruptedDialogAsync(_accountStore.VaultPath);
            }
        }
    }

    [RelayCommand]
    public async Task CheckAccountAsync()
    {
        if (SelectedAccount == null || IsCheckingSingle)
        {
            return;
        }

        IsCheckingSingle = true;
        try
        {
            await ValidateAndUpdateAccountAsync(SelectedAccount.Account);
            await LoadAccountsAsync();
        }
        finally
        {
            IsCheckingSingle = false;
        }
    }

    [RelayCommand]
    public async Task CheckAllAsync()
    {
        if (_allAccounts.Count == 0 || IsCheckingAll)
        {
            return;
        }

        IsCheckingAll = true;
        _checkAllCts = new CancellationTokenSource();
        CancellationToken ct = _checkAllCts.Token;

        int total = _allAccounts.Count;
        int completed = 0;
        CheckAllProgressText = $"0 / {total}";

        try
        {
            List<AccountItemViewModel> list = _allAccounts.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                AccountItemViewModel item = list[i];
                await ValidateAndUpdateAccountAsync(item.Account, ct);
                completed++;
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        CheckAllProgressText = $"{completed} / {total}";
                    });
                }
                else
                {
                    CheckAllProgressText = $"{completed} / {total}";
                }

                if (i < list.Count - 1 && !ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), _timeProvider, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            IsCheckingAll = false;
            CheckAllProgressText = string.Empty;
            _checkAllCts?.Dispose();
            _checkAllCts = null;
            await LoadAccountsAsync();
        }
    }

    [RelayCommand]
    public void CancelCheckAll()
    {
        _checkAllCts?.Cancel();
    }

    [RelayCommand]
    public async Task LaunchAccountAsync()
    {
        if (IsBulkLaunching)
        {
            return;
        }

        List<AccountItemViewModel> checkedAccounts = GetSelectedAccountsInQueueOrder();
        if (checkedAccounts.Count > 1 || (checkedAccounts.Count == 1 && SelectedAccount == null))
        {
            await ExecuteBulkLaunchAsync(checkedAccounts);
            return;
        }

        AccountItemViewModel? targetAccount = checkedAccounts.Count == 1 ? checkedAccounts[0] : SelectedAccount;
        if (targetAccount == null)
        {
            return;
        }

        LaunchDialogControl dialogControl = new()
        {
            PlaceIdText = !string.IsNullOrWhiteSpace(_settingsService?.CurrentSettings.LastPlaceId)
                ? _settingsService.CurrentSettings.LastPlaceId
                : _lastPlaceId,
            DryRunBuilder = (placeId, jobId) =>
            {
                long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string trackerId = "123456789";
                string? launchTemplate = _settingsService?.CurrentSettings.Advanced?.LaunchUriTemplate;
                string? placeLauncherTemplate = _settingsService?.CurrentSettings.Advanced?.PlaceLauncherUrlTemplate;
                return RobloxUriBuilder.BuildLaunchUri("<ticket>", placeId, jobId, unixMs, trackerId, launchTemplate, placeLauncherTemplate);
            }
        };

        ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.LaunchDialogTitle,
                PrimaryButtonText = Strings.Launch,
                CloseButtonText = Strings.Cancel,
                Content = dialogControl
            });

        if (result == ContentDialogResult.Primary)
        {
            string placeIdStr = dialogControl.PlaceIdText;
            if (!long.TryParse(placeIdStr, out long placeId) || placeId <= 0)
            {
                _snackbarService.Show(Strings.ErrorTitle, Strings.PlaceIdRequired, ControlAppearance.Danger);
                return;
            }

            string? jobId = dialogControl.JobIdText;
            if (!string.IsNullOrWhiteSpace(jobId) && !Guid.TryParse(jobId, out _))
            {
                _snackbarService.Show(Strings.ErrorTitle, Strings.JobIdInvalid, ControlAppearance.Danger);
                return;
            }

            _lastPlaceId = placeIdStr;
            if (_settingsService != null && !string.Equals(_settingsService.CurrentSettings.LastPlaceId, placeIdStr, StringComparison.Ordinal))
            {
                _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings with { LastPlaceId = placeIdStr });
            }

            if (_instanceGate is { IsMultiInstanceEnabled: false })
            {
                _instanceGate.TryEnable();
            }

            HashSet<int> beforePids = SnapshotRobloxProcessIds();
            LaunchRequest launchRequest = new(targetAccount.Id, placeId, jobId);
            Result launchResult = await _launcher.LaunchAsync(launchRequest);

            if (launchResult.IsSuccess)
            {
                if (ReferenceEquals(_timeProvider, TimeProvider.System))
                {
                    _ = TrackSpawnedRobloxProcessAsync(targetAccount, beforePids, CancellationToken.None);
                }

                _snackbarService.Show(Strings.SuccessTitle, Strings.LaunchSuccess, ControlAppearance.Success);
            }
            else
            {
                string message = launchResult.ErrorCode switch
                {
                    "RobloxNotInstalled" => Strings.RobloxNotInstalled,
                    AuthErrorCodes.Expired => Strings.ExpiredHint,
                    _ => launchResult.Error ?? Strings.ErrorTitle
                };
                _snackbarService.Show(Strings.ErrorTitle, message, ControlAppearance.Danger);
            }
        }
    }

    private async Task ValidateAndUpdateAccountAsync(Account account, CancellationToken ct = default)
    {
        Result<RobloxUser> validation = await _authService.ValidateCookieAsync(account.Cookie, ct);

        AccountStatus newStatus;
        string? username = account.Username;
        long? userId = account.UserId;

        if (validation.IsSuccess && validation.Value != null)
        {
            newStatus = AccountStatus.Valid;
            username = validation.Value.Name;
            userId = validation.Value.Id;
        }
        else if (string.Equals(validation.ErrorCode, AuthErrorCodes.Expired, StringComparison.OrdinalIgnoreCase))
        {
            newStatus = AccountStatus.Expired;
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _snackbarService.Show(account.Alias, Strings.ExpiredHint, ControlAppearance.Caution);
                });
            }
            else
            {
                _snackbarService.Show(account.Alias, Strings.ExpiredHint, ControlAppearance.Caution);
            }
        }
        else
        {
            newStatus = AccountStatus.Error;
        }

        using SecretValue cookieClone = account.Cookie.Clone();
        Account updated = new(
            Id: account.Id,
            Alias: account.Alias,
            Group: account.Group,
            Note: account.Note,
            Cookie: cookieClone,
            CreatedAt: account.CreatedAt,
            Username: username,
            UserId: userId,
            Status: newStatus,
            LastCheckedAt: DateTimeOffset.UtcNow);

        await _accountStore.UpdateAsync(updated, CancellationToken.None);
    }

    private void ApplyFilter()
    {
        FilteredAccounts.Clear();
        IEnumerable<AccountItemViewModel> query = _allAccounts;

        if (!string.IsNullOrEmpty(SelectedGroup) && !string.Equals(SelectedGroup, Strings.GroupFilterAll, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => string.Equals(a.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string search = SearchText.Trim();
            query = query.Where(a =>
                a.Alias.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (a.Group != null && a.Group.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (a.Note != null && a.Note.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (a.Account.Username != null && a.Account.Username.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        query = SortAccounts(query, SortColumn, SortDescending);

        foreach (AccountItemViewModel item in query)
        {
            FilteredAccounts.Add(item);
        }

        HasAccounts = FilteredAccounts.Count > 0;
        OnPropertyChanged(nameof(IsNoAccounts));
        OnPropertyChanged(nameof(IsNoSearchResults));
    }

    private static IEnumerable<AccountItemViewModel> SortAccounts(IEnumerable<AccountItemViewModel> items, string sortColumn, bool descending)
    {
        Func<AccountItemViewModel, object?> keySelector = sortColumn switch
        {
            "Group" => a => a.Group ?? string.Empty,
            "Username" => a => a.Username,
            "Status" => a => (int)a.Status,
            "LastChecked" => a => a.Account.LastCheckedAt ?? DateTimeOffset.MinValue,
            "Created" => a => a.CreatedAt,
            _ => a => a.Alias
        };

        return descending
            ? items.OrderByDescending(keySelector)
            : items.OrderBy(keySelector);
    }

    [RelayCommand]
    public void Sort(string column)
    {
        if (string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }

        ApplyFilter();
        SaveSortState();
    }

    private void SaveSortState()
    {
        if (_settingsService == null) return;
        try
        {
            AppSettings current = _settingsService.CurrentSettings;
            UiStateSettings uiState = (current.UiState ?? new UiStateSettings()) with
            {
                SortColumn = SortColumn,
                SortDescending = SortDescending
            };
            _ = _settingsService.SaveSettingsAsync(current with { UiState = uiState });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
        SelectedGroup = Strings.GroupFilterAll;
    }

    [RelayCommand]
    public void CopyAlias()
    {
        if (SelectedAccount != null)
        {
            try
            {
                Clipboard.SetText(SelectedAccount.Alias);
                _snackbarService.Show(Strings.SuccessTitle, Strings.AliasCopied, ControlAppearance.Success);
            }
            catch (Exception ex)
            {
                _snackbarService.Show(Strings.ErrorTitle, ex.Message, ControlAppearance.Danger);
            }
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAccountsAsync();
    }

    [RelayCommand]
    private async Task RenameGroupAsync()
    {
        if (string.IsNullOrEmpty(SelectedGroup) || string.Equals(SelectedGroup, Strings.GroupFilterAll, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RenameGroupDialogControl dialogControl = new(SelectedGroup);
        ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Strings.RenameGroupTitle,
            Content = dialogControl,
            PrimaryButtonText = Strings.Save,
            CloseButtonText = Strings.Cancel
        });

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        string? newGroupName = dialogControl.GetNewGroupName();
        if (string.IsNullOrWhiteSpace(newGroupName))
        {
            return;
        }

        string oldGroup = SelectedGroup;
        int count = 0;

        foreach (AccountItemViewModel item in _allAccounts.Where(a => string.Equals(a.Group, oldGroup, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            using SecretValue cookieClone = item.Account.Cookie.Clone();
            Account updated = item.Account with { Group = newGroupName, Cookie = cookieClone };
            Result updateResult = await _accountStore.UpdateAsync(updated);
            if (updateResult.IsSuccess)
            {
                count++;
            }
        }

        await LoadAccountsAsync();
        SelectedGroup = newGroupName;
        CompositeFormat renameFormat = CompositeFormat.Parse(Strings.GroupRenamed);
        string message = string.Format(CultureInfo.CurrentCulture, renameFormat, oldGroup, newGroupName, count);
        _snackbarService.Show(Strings.SuccessTitle, message, ControlAppearance.Success);
    }

    public List<AccountItemViewModel> GetSelectedAccountsInQueueOrder()
    {
        List<AccountItemViewModel> selected = FilteredAccounts.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            selected = _allAccounts.Where(a => a.IsSelected).ToList();
        }

        return selected;
    }

    [RelayCommand]
    public async Task LaunchSelectedAccountsAsync()
    {
        if (IsBulkLaunching)
        {
            return;
        }

        List<AccountItemViewModel> selected = GetSelectedAccountsInQueueOrder();
        if (selected.Count == 0 && SelectedAccount != null)
        {
            selected = [SelectedAccount];
        }

        if (selected.Count == 0)
        {
            return;
        }

        await ExecuteBulkLaunchAsync(selected);
    }

    [RelayCommand]
    public async Task LaunchGroupAccountsAsync()
    {
        if (IsBulkLaunching || string.IsNullOrEmpty(SelectedGroup) || string.Equals(SelectedGroup, Strings.GroupFilterAll, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        List<AccountItemViewModel> groupAccounts = FilteredAccounts
            .Where(a => string.Equals(a.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (groupAccounts.Count == 0)
        {
            groupAccounts = _allAccounts
                .Where(a => string.Equals(a.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (groupAccounts.Count == 0)
        {
            return;
        }

        await ExecuteBulkLaunchAsync(groupAccounts);
    }

    private async Task ExecuteBulkLaunchAsync(List<AccountItemViewModel> accounts)
    {
        if (IsBulkLaunching)
        {
            return;
        }

        List<AccountItemViewModel> queueSnapshot = accounts.Where(a => a != null).ToList();
        if (queueSnapshot.Count == 0)
        {
            return;
        }

        LaunchDialogControl dialogControl = new()
        {
            PlaceIdText = !string.IsNullOrWhiteSpace(_settingsService?.CurrentSettings.LastPlaceId)
                ? _settingsService.CurrentSettings.LastPlaceId
                : _lastPlaceId,
            DryRunBuilder = (placeId, jobId) =>
            {
                long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string trackerId = "123456789";
                string? launchTemplate = _settingsService?.CurrentSettings.Advanced?.LaunchUriTemplate;
                string? placeLauncherTemplate = _settingsService?.CurrentSettings.Advanced?.PlaceLauncherUrlTemplate;
                return RobloxUriBuilder.BuildLaunchUri("<ticket>", placeId, jobId, unixMs, trackerId, launchTemplate, placeLauncherTemplate);
            }
        };
        ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Strings.LaunchDialogTitle,
            Content = dialogControl,
            PrimaryButtonText = Strings.Launch,
            CloseButtonText = Strings.Cancel
        });

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        string placeIdStr = dialogControl.PlaceIdText;
        if (!long.TryParse(placeIdStr, out long placeId) || placeId <= 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.PlaceIdRequired, ControlAppearance.Danger);
            return;
        }

        _lastPlaceId = placeIdStr;
        if (_settingsService != null && !string.Equals(_settingsService.CurrentSettings.LastPlaceId, placeIdStr, StringComparison.Ordinal))
        {
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings with { LastPlaceId = placeIdStr });
        }
        string? jobId = dialogControl.JobIdText;
        if (!string.IsNullOrWhiteSpace(jobId) && !Guid.TryParse(jobId, out _))
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.JobIdInvalid, ControlAppearance.Danger);
            return;
        }

        await ExecuteSequentialLaunchQueueAsync(queueSnapshot, placeId, jobId);
    }

    public async Task ExecuteSequentialLaunchQueueAsync(
        IReadOnlyList<AccountItemViewModel> accounts,
        long placeId,
        string? jobId = null)
    {
        if (accounts == null || accounts.Count == 0 || IsBulkLaunching)
        {
            return;
        }

        List<AccountItemViewModel> queue = accounts.Where(a => a != null).ToList();
        if (queue.Count == 0)
        {
            return;
        }

        if (_instanceGate is { IsMultiInstanceEnabled: false })
        {
            _instanceGate.TryEnable();
        }

        // The loop owns this CTS through a local reference: Cancel()/Dispose() from the UI or app shutdown
        // only signal it, so the loop never dereferences a field that was nulled/disposed mid-delay.
        CancellationTokenSource launchCts = new();
        _bulkLaunchCts = launchCts;
        CancellationToken ct = launchCts.Token;
        int successCount = 0;
        int failCount = 0;
        RunOnUiThread(() => IsBulkLaunching = true);

        string queueStatus = string.Create(CultureInfo.InvariantCulture, $"Queued {queue.Count} accounts for launch...");
        RunOnUiThread(() => BulkLaunchQueueText = queueStatus);
        Console.WriteLine(queueStatus);
        Serilog.Log.Information("Queued {Count} accounts for launch: {Aliases}", queue.Count, string.Join(", ", queue.Select(a => a.Alias)));

        try
        {
            int currentIndex = 0;

            foreach (AccountItemViewModel account in queue)
            {
                currentIndex++;
                ct.ThrowIfCancellationRequested();

                string progressText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Queued {queue.Count} accounts for launch... ({currentIndex} / {queue.Count}: {account.Alias})");
                RunOnUiThread(() => BulkLaunchQueueText = progressText);

                HashSet<int> beforePids = SnapshotRobloxProcessIds();
                LaunchRequest req = new(account.Id, placeId, jobId);
                Result launchResult = await _launcher.LaunchAsync(req, ct);

                if (launchResult.IsSuccess)
                {
                    successCount++;
                    if (ReferenceEquals(_timeProvider, TimeProvider.System))
                    {
                        _ = TrackSpawnedRobloxProcessAsync(account, beforePids, ct);
                    }
                }
                else
                {
                    failCount++;
                    ShowSnackbar(account.Alias, launchResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
                }

                bool isLastAccount = currentIndex >= queue.Count;
                if (!isLastAccount)
                {
                    ct.ThrowIfCancellationRequested();
                    int randomSeconds = _randomDelayGenerator(
                        MinSequentialLaunchDelaySeconds,
                        MaxSequentialLaunchDelaySeconds + 1);

                    CompositeFormat nextAccountFormat = CompositeFormat.Parse(Strings.NextAccountCountdown);
                    for (int remainingSeconds = randomSeconds; remainingSeconds >= 1; remainingSeconds--)
                    {
                        ct.ThrowIfCancellationRequested();
                        string countdownText = string.Format(CultureInfo.CurrentCulture, nextAccountFormat, remainingSeconds);
                        RunOnUiThread(() => BulkLaunchCountdownText = countdownText);

                        if (ReferenceEquals(_timeProvider, TimeProvider.System))
                        {
                            await Task.Delay(1000, ct);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, ct);
                        }
                    }

                    RunOnUiThread(() => BulkLaunchCountdownText = string.Empty);
                }
            }

            CompositeFormat bulkFormat = CompositeFormat.Parse(Strings.BulkLaunchCompleted);
            string summary = string.Format(CultureInfo.CurrentCulture, bulkFormat, successCount, failCount);
            ShowSnackbar(Strings.SuccessTitle, summary, ControlAppearance.Success);
        }
        catch (OperationCanceledException)
        {
            Serilog.Log.Information("Sequential launch queue cancelled after {Success} success / {Fail} failed", successCount, failCount);
            if (!_disposed)
            {
                ShowSnackbar(Strings.AppTitle, Strings.BulkLaunchCancelled, ControlAppearance.Caution);
            }
        }
        finally
        {
            if (ReferenceEquals(_bulkLaunchCts, launchCts))
            {
                _bulkLaunchCts = null;
            }

            launchCts.Dispose();
            RunOnUiThread(() =>
            {
                IsBulkLaunching = false;
                BulkLaunchCountdownText = string.Empty;
                BulkLaunchQueueText = string.Empty;
            });
        }
    }

    [RelayCommand]
    public void CancelBulkLaunch()
    {
        try
        {
            _bulkLaunchCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Queue finished between the null-check and Cancel(); nothing left to cancel.
        }
    }

    [RelayCommand]
    public void KillInstance(AccountItemViewModel? item = null)
    {
        AccountItemViewModel? target = item ?? SelectedAccount;
        target?.KillInstance();
    }

    private static HashSet<int> SnapshotRobloxProcessIds()
    {
        HashSet<int> pids = new();
        try
        {
            foreach (Process proc in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                try
                {
                    pids.Add(proc.Id);
                }
                catch
                {
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
        }

        return pids;
    }

    private async Task TrackSpawnedRobloxProcessAsync(
        AccountItemViewModel account,
        HashSet<int> beforePids,
        CancellationToken ct)
    {
        const int maxPolls = 30;
        for (int attempt = 0; attempt < maxPolls && !ct.IsCancellationRequested && !_disposed; attempt++)
        {
            try
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            HashSet<int> currentPids = SnapshotRobloxProcessIds();
            int? newPid = currentPids.FirstOrDefault(pid =>
                !beforePids.Contains(pid) &&
                !_allAccounts.Any(a => a.ActiveProcessId == pid));

            if (newPid is > 0)
            {
                int capturedPid = newPid.Value;
                RunOnUiThread(() => account.ActiveProcessId = capturedPid);
                await OnRobloxProcessCapturedAsync(capturedPid, ct).ConfigureAwait(false);
                return;
            }
        }
    }

    protected virtual Task OnRobloxProcessCapturedAsync(int processId, CancellationToken ct) =>
        Task.CompletedTask;

    private void ShowSnackbar(string title, string message, ControlAppearance appearance) =>
        RunOnUiThread(() => _snackbarService.Show(title, message, appearance));

    private static void RunOnUiThread(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // BeginInvoke (not Invoke): a synchronous hop would deadlock if the UI thread is itself blocked waiting
        // on this queue (e.g. during shutdown); FIFO dispatcher ordering keeps status updates in sequence.
        dispatcher.BeginInvoke(action);
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        if (_backupService == null)
        {
            return;
        }

        if (_allAccounts.Count == 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.EmptyVaultCannotBackup, ControlAppearance.Caution);
            return;
        }

        BackupPasswordDialogControl dialogControl = new(isConfirmRequired: true);
        ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Strings.BackupPasswordTitle,
            Content = dialogControl,
            PrimaryButtonText = Strings.Save,
            CloseButtonText = Strings.Cancel
        });

        if (result != ContentDialogResult.Primary)
        {
            dialogControl.ClearPasswords();
            return;
        }

        string? password = dialogControl.GetPassword();
        if (string.IsNullOrEmpty(password))
        {
            dialogControl.ClearPasswords();
            return;
        }

        Microsoft.Win32.SaveFileDialog saveDialog = new()
        {
            Filter = "Roblox Backup (*.rbxbackup)|*.rbxbackup",
            DefaultExt = ".rbxbackup",
            FileName = $"rbxmanager-backup-{DateTime.Now:yyyyMMdd}.rbxbackup"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                Core.Backup.BackupExportResult exportResult = await _backupService.ExportAsync(saveDialog.FileName, password);
                CompositeFormat exportSuccessFormat = CompositeFormat.Parse(Strings.BackupExportSuccess);
                string message = string.Format(CultureInfo.CurrentCulture, exportSuccessFormat, exportResult.ExportedCount);
                _snackbarService.Show(Strings.SuccessTitle, message, ControlAppearance.Success);
            }
            catch (Exception ex)
            {
                _snackbarService.Show(Strings.ErrorTitle, ex.Message, ControlAppearance.Danger);
            }
            finally
            {
                dialogControl.ClearPasswords();
            }
        }
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        if (_backupService == null)
        {
            return;
        }

        Microsoft.Win32.OpenFileDialog openDialog = new()
        {
            Filter = "Roblox Backup (*.rbxbackup)|*.rbxbackup",
            DefaultExt = ".rbxbackup"
        };

        if (openDialog.ShowDialog() != true)
        {
            return;
        }

        BackupPasswordDialogControl dialogControl = new(isConfirmRequired: false);
        ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Strings.BackupPasswordTitle,
            Content = dialogControl,
            PrimaryButtonText = Strings.Save,
            CloseButtonText = Strings.Cancel
        });

        if (result != ContentDialogResult.Primary)
        {
            dialogControl.ClearPasswords();
            return;
        }

        string? password = dialogControl.GetPassword();
        if (string.IsNullOrEmpty(password))
        {
            dialogControl.ClearPasswords();
            return;
        }

        try
        {
            Core.Backup.BackupImportResult importResult = await _backupService.ImportAsync(openDialog.FileName, password);
            await LoadAccountsAsync();
            CompositeFormat importSuccessFormat = CompositeFormat.Parse(Strings.BackupImportSuccess);
            string message = string.Format(CultureInfo.CurrentCulture, importSuccessFormat, importResult.ImportedCount, importResult.SkippedCount);
            _snackbarService.Show(Strings.SuccessTitle, message, ControlAppearance.Success);
        }
        catch (Core.Backup.BackupException ex)
        {
            CompositeFormat importFailedFormat = CompositeFormat.Parse(Strings.BackupImportFailed);
            string message = string.Format(CultureInfo.CurrentCulture, importFailedFormat, ex.Message);
            _snackbarService.Show(Strings.ErrorTitle, message, ControlAppearance.Danger);
        }
        catch (Exception ex)
        {
            _snackbarService.Show(Strings.ErrorTitle, ex.Message, ControlAppearance.Danger);
        }
        finally
        {
            dialogControl.ClearPasswords();
        }
    }

    private async Task ShowVaultCorruptedDialogAsync(string vaultPath)
    {
        CompositeFormat format = CompositeFormat.Parse(Strings.VaultCorruptedMessage);
        string message = string.Format(CultureInfo.CurrentCulture, format, vaultPath);

        await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.VaultCorruptedTitle,
                Content = message,
                CloseButtonText = Strings.Close
            });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_checkAllCts != null)
        {
            _checkAllCts.Cancel();
            _checkAllCts.Dispose();
            _checkAllCts = null;
        }

        // The running queue owns and disposes its CTS; we only signal cancellation here.
        CancelBulkLaunch();

        foreach (AccountItemViewModel item in _allAccounts)
        {
            item.PropertyChanged -= OnAccountItemPropertyChanged;
            item.Dispose();
        }

        _searchDebouncer.Dispose();

        GC.SuppressFinalize(this);
    }
}
