namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.App.Views.Controls;
using RbxManager.Core.Common;
using RbxManager.Core.Instance;
using RbxManager.Core.Models;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IInstanceGate _instanceGate;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IAccountStore? _accountStore;
    private readonly Core.Backup.IBackupService? _backupService;

    [ObservableProperty]
    private string _selectedLanguage = "vi";

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private int _launchDelayMs = 3000;

    [ObservableProperty]
    private int _rejoinDelaySeconds = 10;

    [ObservableProperty]
    private int _rejoinMaxAttempts = 5;

    [ObservableProperty]
    private string _launchUriTemplateStatus = string.Empty;

    [ObservableProperty]
    private string _placeLauncherUrlTemplateStatus = string.Empty;

    [ObservableProperty]
    private bool _multiInstanceEnabled;

    [ObservableProperty]
    private bool _isMultiInstanceAvailable;

    [ObservableProperty]
    private bool _isBusy;

    private string _initialLanguage = "vi";

    public SettingsViewModel(
        ISettingsService settingsService,
        IInstanceGate instanceGate,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IAccountStore? accountStore = null,
        Core.Backup.IBackupService? backupService = null)
    {
        _settingsService = settingsService;
        _instanceGate = instanceGate;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _accountStore = accountStore;
        _backupService = backupService;

        IsMultiInstanceAvailable = _instanceGate.IsAvailable;
        LoadFromCurrentSettings();
    }

    public void LoadFromCurrentSettings()
    {
        AppSettings settings = _settingsService.CurrentSettings;
        SelectedLanguage = settings.Language;
        _initialLanguage = settings.Language;
        SelectedTheme = settings.Theme;
        LaunchDelayMs = settings.LaunchDelayMs;
        RejoinDelaySeconds = settings.RejoinDelaySeconds;
        RejoinMaxAttempts = settings.RejoinMaxAttempts;
        MultiInstanceEnabled = _instanceGate.IsMultiInstanceEnabled;

        string? launchUriTpl = settings.Advanced?.LaunchUriTemplate;
        LaunchUriTemplateStatus = string.IsNullOrWhiteSpace(launchUriTpl)
            ? Strings.TemplateDefault
            : Core.Launch.LaunchTemplateValidator.ValidateLaunchUriTemplate(launchUriTpl).IsSuccess
                ? Strings.TemplateCustomValid
                : Strings.TemplateCustomInvalid;

        string? placeLauncherTpl = settings.Advanced?.PlaceLauncherUrlTemplate;
        PlaceLauncherUrlTemplateStatus = string.IsNullOrWhiteSpace(placeLauncherTpl)
            ? Strings.TemplateDefault
            : Core.Launch.LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate(placeLauncherTpl).IsSuccess
                ? Strings.TemplateCustomValid
                : Strings.TemplateCustomInvalid;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AppSettings current = _settingsService.CurrentSettings;
            AppSettings updated = new(
                Language: SelectedLanguage,
                Theme: SelectedTheme,
                LaunchDelayMs: LaunchDelayMs,
                RejoinDelaySeconds: RejoinDelaySeconds,
                RejoinMaxAttempts: RejoinMaxAttempts,
                MultiInstanceEnabled: MultiInstanceEnabled,
                MultiInstanceAcknowledged: current.MultiInstanceAcknowledged,
                Advanced: current.Advanced);

            await _settingsService.SaveSettingsAsync(updated);

            // Apply theme live
            ApplyTheme(SelectedTheme);

            _snackbarService.Show(Strings.SuccessTitle, Strings.SettingsSaved, ControlAppearance.Success, null, TimeSpan.FromSeconds(3));

            if (!string.Equals(_initialLanguage, SelectedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                _snackbarService.Show(Strings.AppTitle, Strings.RestartRequiredMessage, ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OnMultiInstanceToggledAsync(bool enabled)
    {
        if (!_instanceGate.IsAvailable)
        {
            MultiInstanceEnabled = false;
            _snackbarService.Show(Strings.ErrorTitle, Strings.MultiInstanceNotAvailable, ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
            return;
        }

        if (enabled)
        {
            AppSettings current = _settingsService.CurrentSettings;
            if (!current.MultiInstanceAcknowledged)
            {
                ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = Strings.MultiInstanceWarningTitle,
                    Content = Strings.MultiInstanceWarningMessage,
                    PrimaryButtonText = Strings.Save,
                    CloseButtonText = Strings.Cancel
                });

                if (result != ContentDialogResult.Primary)
                {
                    MultiInstanceEnabled = false;
                    return;
                }

                await _settingsService.SaveSettingsAsync(current with { MultiInstanceAcknowledged = true });
            }

            Result enableResult = _instanceGate.TryEnable();
            if (!enableResult.IsSuccess)
            {
                MultiInstanceEnabled = false;
                _snackbarService.Show(Strings.ErrorTitle, enableResult.Error ?? Strings.ErrorTitle, ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
            }
            else
            {
                MultiInstanceEnabled = true;
            }
        }
        else
        {
            _instanceGate.Disable();
            MultiInstanceEnabled = false;
        }
    }

    private static void ApplyTheme(string theme)
    {
        try
        {
            ApplicationTheme appTheme = theme switch
            {
                var t when string.Equals(t, "Light", StringComparison.OrdinalIgnoreCase) => ApplicationTheme.Light,
                var t when string.Equals(t, "System", StringComparison.OrdinalIgnoreCase) => ApplicationTheme.HighContrast,
                _ => ApplicationTheme.Dark
            };
            ApplicationThemeManager.Apply(appTheme);
            RbxManager.App.Themes.ThemeTokens.ApplyTheme(appTheme);
        }
        catch
        {
            // Ignore theme application error in non-window contexts
        }
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        if (_backupService == null || _accountStore == null)
        {
            return;
        }

        IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
        if (accounts.Count == 0)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.EmptyVaultCannotBackup, ControlAppearance.Caution);
            return;
        }

        Microsoft.Win32.SaveFileDialog saveDialog = new()
        {
            Filter = "Roblox Backup (*.rbxbackup)|*.rbxbackup",
            DefaultExt = ".rbxbackup",
            FileName = string.Create(CultureInfo.InvariantCulture, $"rbx_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.rbxbackup")
        };

        if (saveDialog.ShowDialog() != true)
        {
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

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        if (_backupService == null || _accountStore == null)
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
}
