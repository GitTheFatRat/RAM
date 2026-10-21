namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.App.Views.Controls;
using RbxManager.Core.Common;
using RbxManager.Core.Exceptions;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using RbxManager.Core.Validation;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public partial class AccountsViewModel : ObservableObject
{
    private readonly IAccountStore _accountStore;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly List<AccountItemViewModel> _allAccounts = new();

    public ObservableCollection<AccountItemViewModel> FilteredAccounts { get; } = new();

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

    public AccountsViewModel(
        IAccountStore accountStore,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
        _snackbarService = snackbarService ?? throw new ArgumentNullException(nameof(snackbarService));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

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

            ApplyFilter();
        }
        catch (VaultCorruptedException)
        {
            await _contentDialogService.ShowSimpleDialogAsync(
                new SimpleContentDialogCreateOptions
                {
                    Title = Strings.VaultCorruptedTitle,
                    Content = Strings.VaultCorruptedMessage.Replace("{0}", _accountStore.VaultPath, StringComparison.CurrentCulture),
                    CloseButtonText = Strings.Close
                });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        FilteredAccounts.Clear();

        IEnumerable<AccountItemViewModel> filtered = _allAccounts;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string query = SearchText.Trim();
            filtered = filtered.Where(a =>
                (a.Alias != null && a.Alias.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (a.Group != null && a.Group.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (a.Note != null && a.Note.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (AccountItemViewModel item in filtered)
        {
            FilteredAccounts.Add(item);
        }

        HasAccounts = FilteredAccounts.Count > 0;
    }

    [RelayCommand]
    public async Task AddAccountAsync()
    {
        ContentDialogHost? host = _contentDialogService.GetDialogHostEx();
        if (host == null)
        {
            return;
        }

        AccountEditorViewModel editorVm = new(isEdit: false);
        AccountEditorControl editorControl = new() { DataContext = editorVm };

        ContentDialog dialog = new(host)
        {
            Title = Strings.AddAccountDialogTitle,
            PrimaryButtonText = Strings.Save,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            Content = editorControl
        };

        Account? candidateAccount = null;

        dialog.Closing += (_, args) =>
        {
            if (args.Result != ContentDialogResult.Primary)
            {
                editorControl.ClearPassword();
                return;
            }

            string enteredCookie = editorControl.ReadPassword();
            editorControl.ClearPassword();

            if (string.IsNullOrWhiteSpace(enteredCookie))
            {
                editorVm.ErrorMessage = Strings.ValidationCookieRequired;
                args.Cancel = true;
                return;
            }

            SecretValue cookie = SecretValue.FromString(enteredCookie.Trim());
            long newId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Account candidate = new(
                Id: new AccountId(newId),
                Alias: editorVm.Alias,
                Group: string.IsNullOrWhiteSpace(editorVm.Group) ? null : editorVm.Group.Trim(),
                Note: string.IsNullOrWhiteSpace(editorVm.Note) ? null : editorVm.Note.Trim(),
                Cookie: cookie,
                CreatedAt: DateTimeOffset.UtcNow);

            Result validation = AccountValidator.Validate(candidate, _allAccounts.Select(a => a.Account));
            if (!validation.IsSuccess)
            {
                editorVm.ErrorMessage = validation.Error;
                args.Cancel = true;
                cookie.Dispose();
                return;
            }

            candidateAccount = candidate;
        };

        ContentDialogResult dialogResult = await dialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary || candidateAccount == null)
        {
            return;
        }

        Result saveResult = await _accountStore.AddAsync(candidateAccount);
        if (saveResult.IsSuccess)
        {
            _snackbarService.Show(Strings.SuccessTitle, Strings.AccountSavedMessage, ControlAppearance.Success);
            await LoadAccountsAsync();
        }
        else
        {
            _snackbarService.Show(Strings.ErrorTitle, saveResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
        }
    }

    [RelayCommand]
    public async Task EditAccountAsync()
    {
        if (SelectedAccount == null)
        {
            return;
        }

        ContentDialogHost? host = _contentDialogService.GetDialogHostEx();
        if (host == null)
        {
            return;
        }

        Account current = SelectedAccount.Account;
        AccountEditorViewModel editorVm = new(isEdit: true)
        {
            Alias = current.Alias,
            Group = current.Group,
            Note = current.Note
        };
        AccountEditorControl editorControl = new() { DataContext = editorVm };

        ContentDialog dialog = new(host)
        {
            Title = Strings.EditAccountDialogTitle,
            PrimaryButtonText = Strings.Save,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            Content = editorControl
        };

        Account? candidateAccount = null;

        dialog.Closing += (_, args) =>
        {
            if (args.Result != ContentDialogResult.Primary)
            {
                editorControl.ClearPassword();
                return;
            }

            string enteredCookie = editorControl.ReadPassword();
            editorControl.ClearPassword();

            SecretValue cookieToUse = string.IsNullOrWhiteSpace(enteredCookie)
                ? current.Cookie
                : SecretValue.FromString(enteredCookie.Trim());

            Account candidate = new(
                Id: current.Id,
                Alias: editorVm.Alias,
                Group: string.IsNullOrWhiteSpace(editorVm.Group) ? null : editorVm.Group.Trim(),
                Note: string.IsNullOrWhiteSpace(editorVm.Note) ? null : editorVm.Note.Trim(),
                Cookie: cookieToUse,
                CreatedAt: current.CreatedAt,
                Username: current.Username);

            Result validation = AccountValidator.Validate(candidate, _allAccounts.Select(a => a.Account));
            if (!validation.IsSuccess)
            {
                editorVm.ErrorMessage = validation.Error;
                args.Cancel = true;
                if (!ReferenceEquals(cookieToUse, current.Cookie))
                {
                    cookieToUse.Dispose();
                }

                return;
            }

            candidateAccount = candidate;
        };

        ContentDialogResult dialogResult = await dialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary || candidateAccount == null)
        {
            return;
        }

        Result saveResult = await _accountStore.UpdateAsync(candidateAccount);
        if (saveResult.IsSuccess)
        {
            _snackbarService.Show(Strings.SuccessTitle, Strings.AccountSavedMessage, ControlAppearance.Success);
            await LoadAccountsAsync();
        }
        else
        {
            _snackbarService.Show(Strings.ErrorTitle, saveResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger);
        }
    }

    [RelayCommand]
    public async Task DeleteAccountAsync()
    {
        if (SelectedAccount == null)
        {
            return;
        }

        ContentDialogResult dialogResult = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.DeleteConfirmTitle,
                Content = Strings.DeleteConfirmMessage.Replace("{0}", SelectedAccount.Alias, StringComparison.CurrentCulture),
                PrimaryButtonText = Strings.Delete,
                CloseButtonText = Strings.Cancel
            });

        if (dialogResult == ContentDialogResult.Primary)
        {
            await _accountStore.RemoveAsync(SelectedAccount.Id);
            _snackbarService.Show(Strings.SuccessTitle, Strings.AccountDeletedMessage, ControlAppearance.Success);
            await LoadAccountsAsync();
        }
    }
}
