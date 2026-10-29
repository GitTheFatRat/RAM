namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
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
    private static readonly TimeSpan ResolutionSaveDebounce = TimeSpan.FromMilliseconds(500);

    private CancellationTokenSource? _resolutionSaveDebounceCts;
    private Task _resolutionSaveTask = Task.CompletedTask;

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
    private bool _enableAutoCleanCache;

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
            EnableAutoCleanCache = settings.EnableAutoCleanCache;

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

            // This full save already includes the resolution fields, so a pending debounced write would be redundant.
            CancelPendingResolutionSave();
            await _settingsService.UpdateSettingsAsync(current => current with
            {
                Language = SelectedLanguage,
                Theme = SelectedTheme,
                LaunchDelaySeconds = LaunchDelaySeconds,
                RejoinDelaySeconds = RejoinDelaySeconds,
                RejoinMaxAttempts = RejoinMaxAttempts,
                MultiInstanceEnabled = MultiInstanceEnabled,
                MultiInstanceAcknowledged = current.MultiInstanceAcknowledged || MultiInstanceEnabled,
                IsMasterPasswordEnabled = IsMasterPasswordEnabled,
                ThrottleBackgroundRoblox = ThrottleBackgroundRoblox,
                EnableRamCleaner = EnableRamCleaner,
                RamCleanerIntervalMinutes = RamCleanerIntervalMinutes,
                EnableSmartServerHopping = EnableSmartServerHopping,
                GhostDisconnectMode = SelectedGhostDisconnectMode,
                EnableUatTelemetry = EnableUatTelemetry,
                EnableCustomResolution = EnableCustomResolution,
                ResolutionWidth = ResolutionWidth,
                ResolutionHeight = ResolutionHeight,
                EnableAutoTile = EnableAutoTile,
                EnableAutoReconnect = EnableAutoReconnect,
                EnableAutoCleanCache = EnableAutoCleanCache
            });
            Services.RobloxWindowSizer.ApplyToRunningProcesses(_settingsService);

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
                    OnPropertyChanged(nameof(MultiInstanceEnabled));
                    return;
                }

                await _settingsService.UpdateSettingsAsync(s => s with { MultiInstanceAcknowledged = true });
            }

            Result enableResult = _instanceGate.TryEnable();
            if (!enableResult.IsSuccess)
            {
                MultiInstanceEnabled = false;
                OnPropertyChanged(nameof(MultiInstanceEnabled));
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
                await _settingsService.UpdateSettingsAsync(s => s with
                {
                    MultiInstanceEnabled = true,
                    MultiInstanceAcknowledged = true
                });
            }
        }
        else
        {
            _instanceGate.Disable();
            MultiInstanceEnabled = false;
            await _settingsService.UpdateSettingsAsync(s => s with { MultiInstanceEnabled = false });
        }
    }

    [RelayCommand]
    private async Task OnThrottleBackgroundRobloxToggledAsync(bool enabled)
    {
        if (enabled)
        {
            if (!_settingsService.CurrentSettings.ThrottleBackgroundAcknowledged)
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
                    OnPropertyChanged(nameof(ThrottleBackgroundRoblox));
                    return;
                }
            }

            ThrottleBackgroundRoblox = true;
            OnPropertyChanged(nameof(ThrottleBackgroundRoblox));
            // Reaching here means the risk was acknowledged now or earlier, so persisting the flag is idempotent.
            await _settingsService.UpdateSettingsAsync(s => s with { ThrottleBackgroundRoblox = true, ThrottleBackgroundAcknowledged = true });
        }
        else
        {
            ThrottleBackgroundRoblox = false;
            OnPropertyChanged(nameof(ThrottleBackgroundRoblox));
            await _settingsService.UpdateSettingsAsync(s => s with { ThrottleBackgroundRoblox = false });
        }
    }

    [RelayCommand]
    private async Task OnEnableRamCleanerToggledAsync(bool enabled)
    {
        if (enabled)
        {
            if (!_settingsService.CurrentSettings.RamCleanerAcknowledged)
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
                    OnPropertyChanged(nameof(EnableRamCleaner));
                    return;
                }
            }

            EnableRamCleaner = true;
            OnPropertyChanged(nameof(EnableRamCleaner));
            await _settingsService.UpdateSettingsAsync(s => s with { EnableRamCleaner = true, RamCleanerAcknowledged = true });
        }
        else
        {
            EnableRamCleaner = false;
            OnPropertyChanged(nameof(EnableRamCleaner));
            await _settingsService.UpdateSettingsAsync(s => s with { EnableRamCleaner = false });
        }
    }

    [RelayCommand]
    private async Task OnGhostDisconnectModeChangedAsync(Core.Processes.GhostDisconnectMode mode)
    {
        SelectedGhostDisconnectMode = mode;
        EnableGhostDisconnectDetection = mode != Core.Processes.GhostDisconnectMode.Off;
        await _settingsService.UpdateSettingsAsync(s => s with { GhostDisconnectMode = mode });
    }

    async partial void OnSelectedGhostDisconnectModeChanged(Core.Processes.GhostDisconnectMode value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        if (_settingsService.CurrentSettings.GhostDisconnectMode != value)
        {
            await _settingsService.UpdateSettingsAsync(s => s with { GhostDisconnectMode = value });
        }
    }

    [RelayCommand]
    private async Task OnEnableSmartServerHoppingToggledAsync(bool enabled)
    {
        EnableSmartServerHopping = enabled;
        await _settingsService.UpdateSettingsAsync(s => s with { EnableSmartServerHopping = enabled });
    }

    [RelayCommand]
    private async Task OnEnableUatTelemetryToggledAsync(bool enabled)
    {
        EnableUatTelemetry = enabled;
        await _settingsService.UpdateSettingsAsync(s => s with { EnableUatTelemetry = enabled });
    }

    [RelayCommand]
    private async Task OnEnableAutoTileToggledAsync(bool enabled)
    {
        EnableAutoTile = enabled;
        await _settingsService.UpdateSettingsAsync(s => s with { EnableAutoTile = enabled });
        Services.RobloxWindowSizer.ApplyToRunningProcesses(_settingsService);
    }

    [RelayCommand]
    private async Task OnEnableAutoReconnectToggledAsync(bool enabled)
    {
        EnableAutoReconnect = enabled;
        await _settingsService.UpdateSettingsAsync(s => s with { EnableAutoReconnect = enabled });
    }

    [RelayCommand]
    private async Task OnEnableAutoCleanCacheToggledAsync(bool enabled)
    {
        EnableAutoCleanCache = enabled;
        await _settingsService.UpdateSettingsAsync(s => s with { EnableAutoCleanCache = enabled });
    }

    [RelayCommand]
    private async Task OnEnableCustomResolutionToggledAsync(bool enabled)
    {
        _isLoadingSettings = true;
        try
        {
            EnableCustomResolution = enabled;
        }
        finally
        {
            _isLoadingSettings = false;
        }

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
        if (_isLoadingSettings || !IsValidResolutionDimension(value))
        {
            return;
        }

        await DebouncePersistCustomResolutionAsync();
    }

    async partial void OnResolutionHeightChanged(int value)
    {
        if (_isLoadingSettings || !IsValidResolutionDimension(value))
        {
            return;
        }

        await DebouncePersistCustomResolutionAsync();
    }

    /// <summary>
    /// Persists a resolution edit still waiting in the debounce window. Call before shutdown so the last typed value is not lost.
    /// </summary>
    public async Task FlushPendingAsync()
    {
        if (CancelPendingResolutionSave())
        {
            await SaveCustomResolutionAsync();
        }

        await _resolutionSaveTask;
    }

    private static bool IsValidResolutionDimension(int value) =>
        value >= AppSettings.MinimumResolutionDimension && value <= AppSettings.MaximumResolutionDimension;

    private async Task PersistCustomResolutionAsync()
    {
        CancelPendingResolutionSave();
        await SaveCustomResolutionAsync();
        Services.RobloxWindowSizer.ApplyToRunningProcesses(_settingsService);
    }

    // Typing "1920" fires four change notifications; only the value left after the quiet period is written and applied.
    private async Task DebouncePersistCustomResolutionAsync()
    {
        CancellationTokenSource debounceCts = new();
        CancelPendingResolutionSave(replacement: debounceCts);
        try
        {
            await Task.Delay(ResolutionSaveDebounce, debounceCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A flush or newer edit may have claimed this save between the delay completing and this continuation running.
        if (debounceCts.IsCancellationRequested)
        {
            return;
        }

        await PersistCustomResolutionAsync();
    }

    private bool CancelPendingResolutionSave(CancellationTokenSource? replacement = null)
    {
        CancellationTokenSource? previousCts = Interlocked.Exchange(ref _resolutionSaveDebounceCts, replacement);
        if (previousCts is null)
        {
            return false;
        }

        previousCts.Cancel();
        previousCts.Dispose();
        return true;
    }

    private Task SaveCustomResolutionAsync()
    {
        bool enableCustomResolution = EnableCustomResolution;
        int width = ResolutionWidth;
        int height = ResolutionHeight;

        // A half-typed dimension (e.g. "19" on the way to "1920") must never reach disk; keep the last persisted value instead.
        _resolutionSaveTask = _settingsService.UpdateSettingsAsync(s => s with
        {
            EnableCustomResolution = enableCustomResolution,
            ResolutionWidth = IsValidResolutionDimension(width) ? width : s.ResolutionWidth,
            ResolutionHeight = IsValidResolutionDimension(height) ? height : s.ResolutionHeight
        });
        return _resolutionSaveTask;
    }

    [RelayCommand]
    private async Task OnEnableGhostDisconnectDetectionToggledAsync(bool enabled)
    {
        EnableGhostDisconnectDetection = enabled;
        Core.Processes.GhostDisconnectMode mode = enabled ? Core.Processes.GhostDisconnectMode.NotifyOnly : Core.Processes.GhostDisconnectMode.Off;
        SelectedGhostDisconnectMode = mode;
        await _settingsService.UpdateSettingsAsync(s => s with { GhostDisconnectMode = mode });
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
                OnPropertyChanged(nameof(IsMasterPasswordEnabled));
                setupControl.ClearPasswords();
                return;
            }

            if (!setupControl.IsAcknowledged)
            {
                IsMasterPasswordEnabled = false;
                OnPropertyChanged(nameof(IsMasterPasswordEnabled));
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
                OnPropertyChanged(nameof(IsMasterPasswordEnabled));
                _snackbarService.Show(Strings.ErrorTitle, Strings.MasterPasswordMismatch, ControlAppearance.Danger);
                return;
            }

            Result policyResult = Core.Security.MasterPasswordPolicy.Validate(password);
            if (!policyResult.IsSuccess)
            {
                IsMasterPasswordEnabled = false;
                OnPropertyChanged(nameof(IsMasterPasswordEnabled));
                _snackbarService.Show(Strings.ErrorTitle, policyResult.Error ?? Strings.MasterPasswordTooShort, ControlAppearance.Danger);
                return;
            }

            using SecretValue keySecret = SecretValue.FromString(password);
            if (_accountStore is Infrastructure.Storage.EncryptedFileVault encryptedVault)
            {
                await encryptedVault.ReencryptAsync(keySecret);
            }
            else
            {
                IReadOnlyList<Account>? accounts = _accountStore != null ? await _accountStore.ListAsync() : null;
                try
                {
                    _vaultKeyProvider?.SetKey(keySecret.Clone());
                    if (_accountStore != null && accounts != null && accounts.Count > 0)
                    {
                        await _accountStore.UpdateAsync(accounts[0]);
                    }
                }
                finally
                {
                    if (accounts != null)
                    {
                        foreach (Account acc in accounts)
                        {
                            acc.Cookie.Dispose();
                        }
                    }
                }
            }

            await _settingsService.UpdateSettingsAsync(s => s with { IsMasterPasswordEnabled = true });
            IsMasterPasswordEnabled = true;
            OnPropertyChanged(nameof(IsMasterPasswordEnabled));
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
                OnPropertyChanged(nameof(IsMasterPasswordEnabled));
                return;
            }

            if (_accountStore is Infrastructure.Storage.EncryptedFileVault encryptedVault)
            {
                await encryptedVault.ReencryptAsync(null);
            }
            else
            {
                IReadOnlyList<Account>? accounts = _accountStore != null ? await _accountStore.ListAsync() : null;
                try
                {
                    _vaultKeyProvider?.Clear();
                    if (_accountStore != null && accounts != null && accounts.Count > 0)
                    {
                        await _accountStore.UpdateAsync(accounts[0]);
                    }
                }
                finally
                {
                    if (accounts != null)
                    {
                        foreach (Account acc in accounts)
                        {
                            acc.Cookie.Dispose();
                        }
                    }
                }
            }

            await _settingsService.UpdateSettingsAsync(s => s with { IsMasterPasswordEnabled = false });
            IsMasterPasswordEnabled = false;
            OnPropertyChanged(nameof(IsMasterPasswordEnabled));
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
        int accountCount = accounts.Count;
        foreach (Account acc in accounts)
        {
            acc.Cookie.Dispose();
        }

        if (accountCount == 0)
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
            _snackbarService.Show(Strings.ErrorTitle, dialogControl.ErrorTextBlock.Text, ControlAppearance.Danger);
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
            _snackbarService.Show(Strings.ErrorTitle, dialogControl.ErrorTextBlock.Text, ControlAppearance.Danger);
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
