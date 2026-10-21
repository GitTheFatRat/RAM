namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using RbxManager.Core.Storage;
using RbxManager.Core.Validation;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public partial class AccountsViewModel : ObservableObject, IDisposable
{
    private static string _lastPlaceId = string.Empty;

    private readonly IAccountStore _accountStore;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IRobloxAuthService _authService;
    private readonly ILauncher _launcher;
    private readonly Core.Settings.ISettingsService? _settingsService;
    private readonly Core.Backup.IBackupService? _backupService;
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
    private AccountItemViewModel? _selectedAccount;

    public bool IsAccountSelected => SelectedAccount != null;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private bool _hasAccounts;

    public bool IsEmptyStateVisible => !HasAccounts;

    [ObservableProperty]
    private bool _isCheckingAll;

    [ObservableProperty]
    private string _checkAllProgressText = string.Empty;

    public AccountsViewModel(
        IAccountStore accountStore,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IRobloxAuthService authService,
        ILauncher launcher,
        Core.Settings.ISettingsService? settingsService = null,
        Core.Backup.IBackupService? backupService = null)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
        _snackbarService = snackbarService ?? throw new ArgumentNullException(nameof(snackbarService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _settingsService = settingsService;
        _backupService = backupService;

        _selectedGroup = Strings.GroupFilterAll;
    }

    partial void OnSelectedGroupChanged(string? value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

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
            IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
            _allAccounts.Clear();
            foreach (Account account in accounts)
            {
                _allAccounts.Add(new AccountItemViewModel(account));
            }
            UpdateGroups();
            ApplyFilter();
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
        LoginWindow loginWindow = new()
        {
            Owner = Application.Current.MainWindow
        };

        bool? result = loginWindow.ShowDialog();
        if (result != true || loginWindow.CapturedCookie == null)
        {
            return;
        }

        using SecretValue cookie = loginWindow.CapturedCookie;
        _snackbarService.Show(Strings.SuccessTitle, Strings.LoginSuccessValidating, ControlAppearance.Info);

        Result<RobloxUser> validationResult = await _authService.ValidateCookieAsync(cookie);
        if (!validationResult.IsSuccess || validationResult.Value == null)
        {
            _snackbarService.Show(Strings.ErrorTitle, validationResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
            return;
        }

        RobloxUser user = validationResult.Value;

        long nextId = _allAccounts.Count > 0 ? _allAccounts.Max(a => a.Id.Value) + 1 : 1;
        Account newAccount = new(
            Id: new AccountId(nextId),
            Alias: user.Name,
            Group: null,
            Note: null,
            Cookie: SecretValue.FromString(cookie.Reveal()),
            CreatedAt: DateTimeOffset.UtcNow,
            Username: user.Name,
            UserId: user.Id,
            Status: AccountStatus.Valid,
            LastCheckedAt: DateTimeOffset.UtcNow);

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
                : SecretValue.FromString(current.Cookie.Reveal());

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
        if (SelectedAccount == null)
        {
            return;
        }

        await ValidateAndUpdateAccountAsync(SelectedAccount.Account);
        await LoadAccountsAsync();
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

        using SemaphoreSlim semaphore = new(3, 3);
        int total = _allAccounts.Count;
        int completed = 0;
        CheckAllProgressText = $"0 / {total}";

        List<Task> tasks = new();
        foreach (AccountItemViewModel item in _allAccounts.ToList())
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    await ValidateAndUpdateAccountAsync(item.Account, ct);
                    int currentCompleted = Interlocked.Increment(ref completed);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CheckAllProgressText = $"{currentCompleted} / {total}";
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            IsCheckingAll = false;
            CheckAllProgressText = string.Empty;
            _checkAllCts.Dispose();
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
        if (SelectedAccount == null)
        {
            return;
        }

        LaunchDialogControl dialogControl = new()
        {
            PlaceIdText = _lastPlaceId
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

            LaunchRequest launchRequest = new(SelectedAccount.Id, placeId, jobId);
            Result launchResult = await _launcher.LaunchAsync(launchRequest);

            if (launchResult.IsSuccess)
            {
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
            Application.Current.Dispatcher.Invoke(() =>
            {
                _snackbarService.Show(account.Alias, Strings.ExpiredHint, ControlAppearance.Caution);
            });
        }
        else
        {
            newStatus = AccountStatus.Error;
        }

        using SecretValue cookieClone = SecretValue.FromString(account.Cookie.Reveal());
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

        await _accountStore.UpdateAsync(updated);
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

        foreach (AccountItemViewModel item in query)
        {
            FilteredAccounts.Add(item);
        }

        HasAccounts = FilteredAccounts.Count > 0;
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
            using SecretValue cookieClone = SecretValue.FromString(item.Account.Cookie.Reveal());
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

    [RelayCommand]
    private async Task LaunchSelectedAccountsAsync()
    {
        List<AccountItemViewModel> selected = _allAccounts.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        await ExecuteBulkLaunchAsync(selected);
    }

    [RelayCommand]
    private async Task LaunchGroupAccountsAsync()
    {
        if (string.IsNullOrEmpty(SelectedGroup) || string.Equals(SelectedGroup, Strings.GroupFilterAll, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        List<AccountItemViewModel> groupAccounts = _allAccounts.Where(a => string.Equals(a.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase)).ToList();
        if (groupAccounts.Count == 0)
        {
            return;
        }

        await ExecuteBulkLaunchAsync(groupAccounts);
    }

    private async Task ExecuteBulkLaunchAsync(List<AccountItemViewModel> accounts)
    {
        LaunchDialogControl dialogControl = new()
        {
            PlaceIdText = _lastPlaceId
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
        string? jobId = dialogControl.JobIdText;
        if (!string.IsNullOrWhiteSpace(jobId) && !Guid.TryParse(jobId, out _))
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.JobIdInvalid, ControlAppearance.Danger);
            return;
        }

        _bulkLaunchCts = new CancellationTokenSource();
        int successCount = 0;
        int failCount = 0;
        int delayMs = _settingsService?.CurrentSettings.LaunchDelayMs ?? 3000;

        for (int i = 0; i < accounts.Count; i++)
        {
            if (_bulkLaunchCts.IsCancellationRequested)
            {
                _snackbarService.Show(Strings.AppTitle, Strings.BulkLaunchCancelled, ControlAppearance.Caution);
                return;
            }

            AccountItemViewModel account = accounts[i];
            LaunchRequest req = new(account.Id, placeId, jobId);
            Result launchResult = await _launcher.LaunchAsync(req, _bulkLaunchCts.Token);

            if (launchResult.IsSuccess)
            {
                successCount++;
            }
            else
            {
                failCount++;
                _snackbarService.Show(account.Alias, launchResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
            }

            if (i < accounts.Count - 1 && delayMs > 0)
            {
                try
                {
                    await Task.Delay(delayMs, _bulkLaunchCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _snackbarService.Show(Strings.AppTitle, Strings.BulkLaunchCancelled, ControlAppearance.Caution);
                    return;
                }
            }
        }

        CompositeFormat bulkFormat = CompositeFormat.Parse(Strings.BulkLaunchCompleted);
        string summary = string.Format(CultureInfo.CurrentCulture, bulkFormat, successCount, failCount);
        _snackbarService.Show(Strings.SuccessTitle, summary, ControlAppearance.Success);
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

        if (_bulkLaunchCts != null)
        {
            _bulkLaunchCts.Cancel();
            _bulkLaunchCts.Dispose();
            _bulkLaunchCts = null;
        }

        GC.SuppressFinalize(this);
    }
}
