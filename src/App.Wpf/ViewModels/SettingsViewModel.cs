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
    private readonly IVaultKeyProvider? _vaultKeyProvider;

    [ObservableProperty]
    private string _selectedLanguage = "vi";

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private int _launchDelaySeconds = 20;

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
    private bool _isMasterPasswordEnabled;

    [ObservableProperty]
    private bool _throttleBackgroundRoblox;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamCleanerIntervalDisplay))]
    private bool _enableRamCleaner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamCleanerIntervalDisplay))]
    private int _ramCleanerIntervalMinutes = 15;

    public string RamCleanerIntervalDisplay =>
        string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.RamCleanerIntervalFormat), RamCleanerIntervalMinutes);

    [ObservableProperty]
    private bool _enableSmartServerHopping = true;

    [ObservableProperty]
    private bool _enableGhostDisconnectDetection = true;

    [ObservableProperty]
    private Core.Processes.GhostDisconnectMode _selectedGhostDisconnectMode = Core.Processes.GhostDisconnectMode.NotifyOnly;

    [ObservableProperty]
    private bool _enableUatTelemetry;

    [ObservableProperty]
    private bool _enableCustomResolution;

    [ObservableProperty]
    private int _resolutionWidth;

    [ObservableProperty]
    private int _resolutionHeight;

    [ObservableProperty]
    private bool _enableAutoTile;

    [ObservableProperty]
    private bool _enableAutoReconnect;

    [ObservableProperty]
    private bool _isBusy;

    private string _initialLanguage = "vi";
    private bool _isLoadingSettings;

    public SettingsViewModel(
        ISettingsService settingsService,
        IInstanceGate instanceGate,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IAccountStore? accountStore = null,
        Core.Backup.IBackupService? backupService = null,
        IVaultKeyProvider? vaultKeyProvider = null)
    {
        _settingsService = settingsService;
        _instanceGate = instanceGate;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _accountStore = accountStore;
        _backupService = backupService;
        _vaultKeyProvider = vaultKeyProvider;

        IsMultiInstanceAvailable = _instanceGate.IsAvailable;
        LoadFromCurrentSettings();
    }

    public void LoadFromCurrentSettings()
    {
        _isLoadingSettings = true;
        try
        {
            AppSettings settings = _settingsService.CurrentSettings;
            SelectedLanguage = settings.Language;
            _initialLanguage = settings.Language;
            SelectedTheme = settings.Theme;
            LaunchDelaySeconds = settings.LaunchDelaySeconds;
            RejoinDelaySeconds = settings.RejoinDelaySeconds;
            RejoinMaxAttempts = settings.RejoinMaxAttempts;
            MultiInstanceEnabled = settings.MultiInstanceEnabled || _instanceGate.IsMultiInstanceEnabled;
            if (MultiInstanceEnabled && !_instanceGate.IsMultiInstanceEnabled)
            {
                _instanceGate.TryEnable();
            }
            IsMasterPasswordEnabled = settings.IsMasterPasswordEnabled;
            ThrottleBackgroundRoblox = settings.ThrottleBackgroundRoblox;
            EnableRamCleaner = settings.EnableRamCleaner;
            RamCleanerIntervalMinutes = settings.RamCleanerIntervalMinutes;
            EnableSmartServerHopping = settings.EnableSmartServerHopping;
            EnableGhostDisconnectDetection = settings.GhostDisconnectMode != Core.Processes.GhostDisconnectMode.Off;
            SelectedGhostDisconnectMode = settings.GhostDisconnectMode;
            EnableUatTelemetry = settings.EnableUatTelemetry;
            EnableCustomResolution = settings.EnableCustomResolution;
            ResolutionWidth = settings.ResolutionWidth;
            ResolutionHeight = settings.ResolutionHeight;
            EnableAutoTile = settings.EnableAutoTile;
            EnableAutoReconnect = settings.EnableAutoReconnect;

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
        finally
        {
            _isLoadingSettings = false;
        }
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
            if (MultiInstanceEnabled && !_instanceGate.IsMultiInstanceEnabled)
            {
                _instanceGate.TryEnable();
            }
            else if (!MultiInstanceEnabled && _instanceGate.IsMultiInstanceEnabled)
            {
                _instanceGate.Disable();
            }

            AppSettings current = _settingsService.CurrentSettings;
            AppSettings updated = new(
                Language: SelectedLanguage,
                Theme: SelectedTheme,
                LaunchDelaySeconds: LaunchDelaySeconds,
                RejoinDelaySeconds: RejoinDelaySeconds,
                RejoinMaxAttempts: RejoinMaxAttempts,
                MultiInstanceEnabled: MultiInstanceEnabled,
                MultiInstanceAcknowledged: current.MultiInstanceAcknowledged || MultiInstanceEnabled,
                IsMasterPasswordEnabled: IsMasterPasswordEnabled,
                ThrottleBackgroundRoblox: ThrottleBackgroundRoblox,
                ThrottleBackgroundAcknowledged: current.ThrottleBackgroundAcknowledged,
                EnableRamCleaner: EnableRamCleaner,
                RamCleanerAcknowledged: current.RamCleanerAcknowledged,
                RamCleanerIntervalMinutes: RamCleanerIntervalMinutes,
                EnableSmartServerHopping: EnableSmartServerHopping,
                GhostDisconnectMode: SelectedGhostDisconnectMode,
                EnableUatTelemetry: EnableUatTelemetry,
                LastPlaceId: current.LastPlaceId,
                EnableCustomResolution: EnableCustomResolution,
                ResolutionWidth: ResolutionWidth,
                ResolutionHeight: ResolutionHeight,
                EnableAutoTile: EnableAutoTile,
                EnableAutoReconnect: EnableAutoReconnect,
                Advanced: current.Advanced,
                UiState: current.UiState);

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

                current = current with { MultiInstanceAcknowledged = true };
                await _settingsService.SaveSettingsAsync(current);
            }

            Result enableResult = _instanceGate.TryEnable();
            if (!enableResult.IsSuccess)
            {
                MultiInstanceEnabled = false;
                string errorMessage = enableResult.ErrorCode switch
                {
                    "MultiInstanceNotAvailable" => Strings.MultiInstanceNotAvailable,
                    "RobloxRunning" => Strings.MultiInstanceRobloxRunning,
                    "CouldNotTakeOver" => Strings.MultiInstanceCouldNotTakeOver,
                    "Disposed" => Strings.MultiInstanceDisposed,
                    "MutexError" => Strings.MultiInstanceMutexError,
                    "Timeout" => Strings.MultiInstanceTimeout,
                    _ => enableResult.Error ?? Strings.ErrorTitle
                };
                _snackbarService.Show(Strings.ErrorTitle, errorMessage, ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
            }
            else
            {
                MultiInstanceEnabled = true;
                AppSettings updated = _settingsService.CurrentSettings with
                {
                    MultiInstanceEnabled = true,
                    MultiInstanceAcknowledged = true
                };
                await _settingsService.SaveSettingsAsync(updated);
            }
        }
        else
        {
            _instanceGate.Disable();
            MultiInstanceEnabled = false;
            AppSettings updated = _settingsService.CurrentSettings with { MultiInstanceEnabled = false };
            await _settingsService.SaveSettingsAsync(updated);
        }
    }

    [RelayCommand]
    private async Task OnThrottleBackgroundRobloxToggledAsync(bool enabled)
    {
        if (enabled)
        {
            AppSettings current = _settingsService.CurrentSettings;
            if (!current.ThrottleBackgroundAcknowledged)
            {
                ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = Strings.ThrottleRiskDialogTitle,
                    Content = Strings.ThrottleRiskDialogContent,
                    PrimaryButtonText = Strings.Save,
                    CloseButtonText = Strings.Cancel
                });

                if (result != ContentDialogResult.Primary)
                {
                    ThrottleBackgroundRoblox = false;
                    return;
                }

                current = current with { ThrottleBackgroundAcknowledged = true };
            }

            ThrottleBackgroundRoblox = true;
            await _settingsService.SaveSettingsAsync(current with { ThrottleBackgroundRoblox = true });
        }
        else
        {
            ThrottleBackgroundRoblox = false;
            AppSettings current = _settingsService.CurrentSettings;
            await _settingsService.SaveSettingsAsync(current with { ThrottleBackgroundRoblox = false });
        }
    }

    [RelayCommand]
    private async Task OnEnableRamCleanerToggledAsync(bool enabled)
    {
        if (enabled)
        {
            AppSettings current = _settingsService.CurrentSettings;
            if (!current.RamCleanerAcknowledged)
            {
                ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = Strings.RamCleanerRiskDialogTitle,
                    Content = Strings.RamCleanerRiskDialogContent,
                    PrimaryButtonText = Strings.Save,
                    CloseButtonText = Strings.Cancel
                });

                if (result != ContentDialogResult.Primary)
                {
                    EnableRamCleaner = false;
                    return;
                }

                current = current with { RamCleanerAcknowledged = true };
            }

            EnableRamCleaner = true;
            await _settingsService.SaveSettingsAsync(current with { EnableRamCleaner = true });
        }
        else
        {
            EnableRamCleaner = false;
            AppSettings current = _settingsService.CurrentSettings;
            await _settingsService.SaveSettingsAsync(current with { EnableRamCleaner = false });
        }
    }

    [RelayCommand]
    private async Task OnGhostDisconnectModeChangedAsync(Core.Processes.GhostDisconnectMode mode)
    {
        SelectedGhostDisconnectMode = mode;
        EnableGhostDisconnectDetection = mode != Core.Processes.GhostDisconnectMode.Off;
        AppSettings current = _settingsService.CurrentSettings;
        await _settingsService.SaveSettingsAsync(current with { GhostDisconnectMode = mode });
    }

    async partial void OnSelectedGhostDisconnectModeChanged(Core.Processes.GhostDisconnectMode value)
    {
        AppSettings current = _settingsService.CurrentSettings;
        if (current.GhostDisconnectMode != value)
        {
            await _settingsService.SaveSettingsAsync(current with { GhostDisconnectMode = value });
        }
    }

    [RelayCommand]
    private async Task OnEnableSmartServerHoppingToggledAsync(bool enabled)
    {
        EnableSmartServerHopping = enabled;
        AppSettings current = _settingsService.CurrentSettings;
        await _settingsService.SaveSettingsAsync(current with { EnableSmartServerHopping = enabled });
    }

    [RelayCommand]
    private async Task OnEnableUatTelemetryToggledAsync(bool enabled)
    {
        EnableUatTelemetry = enabled;
        AppSettings current = _settingsService.CurrentSettings;
        await _settingsService.SaveSettingsAsync(current with { EnableUatTelemetry = enabled });
    }

    [RelayCommand]
    private async Task OnEnableAutoTileToggledAsync(bool enabled)
    {
        EnableAutoTile = enabled;
        AppSettings current = _settingsService.CurrentSettings;
        await _settingsService.SaveSettingsAsync(current with { EnableAutoTile = enabled });
    }

    [RelayCommand]
    private async Task OnEnableAutoReconnectToggledAsync(bool enabled)
    {
        EnableAutoReconnect = enabled;
        AppSettings current = _settingsService.CurrentSettings;
        await _settingsService.SaveSettingsAsync(current with { EnableAutoReconnect = enabled });
    }

    [RelayCommand]
    private async Task OnEnableCustomResolutionToggledAsync(bool enabled)
    {
        EnableCustomResolution = enabled;
        await PersistCustomResolutionAsync();
    }

    async partial void OnEnableCustomResolutionChanged(bool value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        await PersistCustomResolutionAsync();
    }

    async partial void OnResolutionWidthChanged(int value)
    {
        if (_isLoadingSettings || value <= 0)
        {
            return;
        }

        await PersistCustomResolutionAsync();
    }

    async partial void OnResolutionHeightChanged(int value)
    {
        if (_isLoadingSettings || value <= 0)
        {
            return;
        }

        await PersistCustomResolutionAsync();
    }

    private Task PersistCustomResolutionAsync()
    {
        AppSettings current = _settingsService.CurrentSettings;
        return _settingsService.SaveSettingsAsync(current with
        {
            EnableCustomResolution = EnableCustomResolution,
            ResolutionWidth = ResolutionWidth,
            ResolutionHeight = ResolutionHeight
        });
    }

    [RelayCommand]
    private async Task OnEnableGhostDisconnectDetectionToggledAsync(bool enabled)
    {
        EnableGhostDisconnectDetection = enabled;
        Core.Processes.GhostDisconnectMode mode = enabled ? Core.Processes.GhostDisconnectMode.NotifyOnly : Core.Processes.GhostDisconnectMode.Off;
        SelectedGhostDisconnectMode = mode;
        AppSettings current = _settingsService.CurrentSettings;
        await _settingsService.SaveSettingsAsync(current with { GhostDisconnectMode = mode });
    }

    [RelayCommand]
    private async Task OnMasterPasswordToggledAsync(bool enabled)
    {
        if (enabled)
        {
            MasterPasswordSetupControl setupControl = new();
            ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
            {
                Title = Strings.SetupMasterPasswordTitle,
                Content = setupControl,
                PrimaryButtonText = Strings.Save,
                CloseButtonText = Strings.Cancel
            });

            if (result != ContentDialogResult.Primary)
            {
                IsMasterPasswordEnabled = false;
                setupControl.ClearPasswords();
                return;
            }

            if (!setupControl.IsAcknowledged)
            {
                IsMasterPasswordEnabled = false;
                setupControl.ClearPasswords();
                _snackbarService.Show(Strings.ErrorTitle, Strings.MasterPasswordAcknowledgeRequired, ControlAppearance.Danger);
                return;
            }

            string password = setupControl.ReadPassword();
            string confirm = setupControl.ReadConfirmPassword();
            setupControl.ClearPasswords();

            if (password != confirm)
            {
                IsMasterPasswordEnabled = false;
                _snackbarService.Show(Strings.ErrorTitle, Strings.MasterPasswordMismatch, ControlAppearance.Danger);
                return;
            }

            Result policyResult = Core.Security.MasterPasswordPolicy.Validate(password);
            if (!policyResult.IsSuccess)
            {
                IsMasterPasswordEnabled = false;
                _snackbarService.Show(Strings.ErrorTitle, policyResult.Error ?? Strings.MasterPasswordTooShort, ControlAppearance.Danger);
                return;
            }

            if (_vaultKeyProvider != null)
            {
                using SecretValue keySecret = SecretValue.FromString(password);
                _vaultKeyProvider.SetKey(keySecret.Clone());
            }

            if (_accountStore != null)
            {
                IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
                if (accounts.Count > 0)
                {
                    await _accountStore.UpdateAsync(accounts[0]);
                }
            }

            AppSettings current = _settingsService.CurrentSettings;
            await _settingsService.SaveSettingsAsync(current with { IsMasterPasswordEnabled = true });
            IsMasterPasswordEnabled = true;
            _snackbarService.Show(Strings.SuccessTitle, Strings.MasterPasswordEnabledSuccess, ControlAppearance.Success);
        }
        else
        {
            ContentDialogResult confirmResult = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
            {
                Title = Strings.DisableMasterPasswordTitle,
                Content = Strings.DisableMasterPasswordMessage,
                PrimaryButtonText = Strings.Save,
                CloseButtonText = Strings.Cancel
            });

            if (confirmResult != ContentDialogResult.Primary)
            {
                IsMasterPasswordEnabled = true;
                return;
            }

            _vaultKeyProvider?.Clear();

            if (_accountStore != null)
            {
                IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
                if (accounts.Count > 0)
                {
                    await _accountStore.UpdateAsync(accounts[0]);
                }
            }

            AppSettings current = _settingsService.CurrentSettings;
            await _settingsService.SaveSettingsAsync(current with { IsMasterPasswordEnabled = false });
            IsMasterPasswordEnabled = false;
            _snackbarService.Show(Strings.SuccessTitle, Strings.MasterPasswordDisabledSuccess, ControlAppearance.Success);
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
