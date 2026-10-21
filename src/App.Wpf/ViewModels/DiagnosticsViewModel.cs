namespace RbxManager.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IAccountStore _accountStore;
    private readonly ISnackbarService _snackbarService;

    [ObservableProperty]
    private string _reportText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRunning))]
    private bool _isRunning;

    public bool IsNotRunning => !IsRunning;

    [ObservableProperty]
    private Account? _selectedAccount;

    public ObservableCollection<Account> Accounts { get; } = [];

    public DiagnosticsViewModel(
        IDiagnosticsService diagnosticsService,
        IAccountStore accountStore,
        ISnackbarService snackbarService)
    {
        _diagnosticsService = diagnosticsService;
        _accountStore = accountStore;
        _snackbarService = snackbarService;
    }

    public async Task InitializeAsync()
    {
        Accounts.Clear();
        IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
        foreach (Account account in accounts)
        {
            Accounts.Add(account);
        }
    }

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        try
        {
            ReportText = await _diagnosticsService.RunDiagnosticsAsync(SelectedAccount);
        }
        catch (Exception ex)
        {
            ReportText = $"Diagnostics failed to complete: {ex.GetType().Name}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (string.IsNullOrWhiteSpace(ReportText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(ReportText);
            _snackbarService.Show(Strings.SuccessTitle, Strings.ReportCopied, ControlAppearance.Success);
        }
        catch (Exception ex)
        {
            _snackbarService.Show(Strings.ErrorTitle, ex.Message, ControlAppearance.Danger);
        }
    }
}
