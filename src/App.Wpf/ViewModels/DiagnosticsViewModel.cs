namespace RbxManager.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.Core.Common;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Integrations;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public sealed partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IAccountStore _accountStore;
    private readonly ISnackbarService _snackbarService;
    private readonly IPotassiumService? _potassiumService;

    [ObservableProperty]
    private string _reportText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRunning))]
    private bool _isRunning;

    public bool IsNotRunning => !IsRunning;

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private string _potassiumStatusSummary = Strings.PotassiumStatusNotScanned;

    [ObservableProperty]
    private bool _isPotassiumInstalled;

    public ObservableCollection<Account> Accounts { get; } = [];

    public DiagnosticsViewModel(
        IDiagnosticsService diagnosticsService,
        IAccountStore accountStore,
        ISnackbarService snackbarService,
        IPotassiumService? potassiumService = null)
    {
        _diagnosticsService = diagnosticsService;
        _accountStore = accountStore;
        _snackbarService = snackbarService;
        _potassiumService = potassiumService;
    }

    public async Task InitializeAsync()
    {
        foreach (Account existing in Accounts)
        {
            existing.Cookie.Dispose();
        }
        Accounts.Clear();
        IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
        foreach (Account account in accounts)
        {
            Accounts.Add(account);
        }

        if (_potassiumService != null)
        {
            await ScanPotassiumAsync();
        }
    }

    [RelayCommand]
    public async Task ScanPotassiumAsync()
    {
        if (_potassiumService == null)
        {
            return;
        }

        PotassiumScanResult scan = await _potassiumService.ScanAsync();
        IsPotassiumInstalled = scan.IsInstalled && !string.IsNullOrWhiteSpace(scan.ExecutablePath);
        if (!scan.IsInstalled)
        {
            PotassiumStatusSummary = Strings.PotassiumStatusNotFound;
        }
        else if (scan.IsRunning)
        {
            PotassiumStatusSummary = string.Format(
                CultureInfo.CurrentCulture,
                CompositeFormat.Parse(Strings.PotassiumStatusRunningFormat),
                scan.RunningProcessIds.Count,
                scan.AutoExecScriptCount);
        }
        else
        {
            PotassiumStatusSummary = string.Format(
                CultureInfo.CurrentCulture,
                CompositeFormat.Parse(Strings.PotassiumStatusInstalledFormat),
                scan.AutoExecScriptCount);
        }
    }

    [RelayCommand]
    public async Task LaunchPotassiumAsync()
    {
        if (_potassiumService == null)
        {
            return;
        }

        Result launchResult = _potassiumService.Launch();
        if (launchResult.IsSuccess)
        {
            _snackbarService.Show(Strings.SuccessTitle, Strings.PotassiumLaunchedSuccess, ControlAppearance.Success);
            await ScanPotassiumAsync();
        }
        else
        {
            _snackbarService.Show(Strings.ErrorTitle, launchResult.Error ?? Strings.PotassiumLaunchFailed, ControlAppearance.Danger);
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
            await ScanPotassiumAsync();
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

    public void Dispose()
    {
        foreach (Account existing in Accounts)
        {
            existing.Cookie.Dispose();
        }

        Accounts.Clear();
    }
}
