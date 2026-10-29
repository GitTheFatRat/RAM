namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.App.Services;
using RbxManager.Core.Auth;
using RbxManager.Core.Bridge;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Network;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Bridge;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public partial class AddAccountViewModel : ObservableObject, IDisposable
{
    private static readonly CompositeFormat BulkImportProgressFormat = CompositeFormat.Parse(Strings.BulkImportProgress);
    private static readonly CompositeFormat BulkImportCompletedFormat = CompositeFormat.Parse(Strings.BulkImportCompleted);

    private readonly IAccountStore _accountStore;
    private readonly IRobloxAuthService _authService;
    private readonly IDpiToolService? _dpiToolService;
    private readonly ISnackbarService? _snackbarService;
    private readonly Func<string, bool> _browserLauncher;
    private readonly ILocalExtensionBridge _extensionBridge;
    private readonly bool _ownsExtensionBridge;
    private bool _disposed;

    [ObservableProperty]
    private bool _isExtensionBridgeListening;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private string _rawCookiesText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _isImporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoodbyeDpiButtonText))]
    [NotifyPropertyChangedFor(nameof(CanStartGoodbyeDpi))]
    private bool _isGoodbyeDpiRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartGoodbyeDpi))]
    private bool _isStartingGoodbyeDpi;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string _errorMessage = string.Empty;

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanImport => !IsImporting && !string.IsNullOrWhiteSpace(RawCookiesText);

    public string GoodbyeDpiButtonText => IsGoodbyeDpiRunning ? Strings.GoodbyeDpiActive : Strings.InstallAndStartGoodbyeDpi;

    public bool CanStartGoodbyeDpi => !IsGoodbyeDpiRunning && !IsStartingGoodbyeDpi;

    public int ImportedCount { get; private set; }

    public event Action<bool>? RequestClose;

    public AddAccountViewModel(
        IAccountStore accountStore,
        IRobloxAuthService authService,
        ISnackbarService? snackbarService = null,
        Func<bool>? browserLauncher = null,
        IDpiToolService? dpiToolService = null,
        Func<string, bool>? urlBrowserLauncher = null,
        ILocalExtensionBridge? extensionBridge = null)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _snackbarService = snackbarService;
        _browserLauncher = urlBrowserLauncher
            ?? (browserLauncher != null
                ? (_ => browserLauncher())
                : IncognitoBrowserLauncher.TryLaunch);
        _dpiToolService = dpiToolService;
        if (extensionBridge != null)
        {
            _extensionBridge = extensionBridge;
            _ownsExtensionBridge = false;
        }
        else
        {
            _extensionBridge = new LocalExtensionBridgeService(_accountStore, _authService);
            _ownsExtensionBridge = true;
        }

        IsGoodbyeDpiRunning = _dpiToolService?.IsRunning() == true;
    }

    public void StartExtensionBridge()
    {
        _extensionBridge.AccountImported -= OnExtensionAccountImported;
        _extensionBridge.AccountImported += OnExtensionAccountImported;
        IsExtensionBridgeListening = _extensionBridge.StartListening();
    }

    public void StopExtensionBridge()
    {
        _extensionBridge.AccountImported -= OnExtensionAccountImported;
        _extensionBridge.StopListening();
        IsExtensionBridgeListening = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopExtensionBridge();
        if (_ownsExtensionBridge)
        {
            _extensionBridge.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void OnExtensionAccountImported(ExtensionImportedAccount imported)
    {
        void ApplyImportedOnUi()
        {
            ImportedCount++;
            ErrorMessage = string.Empty;
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                CompositeFormat.Parse(Strings.ExtensionBridgeImportedStatusFormat),
                imported.Username,
                ImportedCount);
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            _ = Application.Current.Dispatcher.InvokeAsync(ApplyImportedOnUi);
        }
        else
        {
            ApplyImportedOnUi();
        }
    }

    public void RefreshGoodbyeDpiStatus()
    {
        IsGoodbyeDpiRunning = _dpiToolService?.IsRunning() == true;
    }

    public void AppendCapturedCookie(string cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return;
        }

        string trimmed = cookie.Trim();
        if (RawCookiesText.Contains(trimmed, StringComparison.Ordinal))
        {
            return;
        }

        RawCookiesText = string.IsNullOrWhiteSpace(RawCookiesText)
            ? trimmed
            : $"{RawCookiesText.TrimEnd()}{Environment.NewLine}{trimmed}";
    }

    [RelayCommand]
    public async Task InstallAndStartGoodbyeDpiAsync()
    {
        if (IsStartingGoodbyeDpi || IsGoodbyeDpiRunning)
        {
            return;
        }

        if (_dpiToolService == null)
        {
            return;
        }

        ErrorMessage = string.Empty;
        IsStartingGoodbyeDpi = true;
        try
        {
            Result result = await _dpiToolService.EnsureInstalledAndStartAsync();
            if (result.IsSuccess)
            {
                IsGoodbyeDpiRunning = true;
            }
            else
            {
                string localizedError = result.ErrorCode switch
                {
                    DpiErrorCodes.UacDenied => Strings.GoodbyeDpiUacDenied,
                    DpiErrorCodes.DownloadFailed => Strings.GoodbyeDpiDownloadFailed,
                    _ => Strings.GoodbyeDpiLaunchFailed
                };
                ErrorMessage = localizedError;
                _snackbarService?.Show(Strings.ErrorTitle, localizedError, ControlAppearance.Danger);
            }
        }
        finally
        {
            IsStartingGoodbyeDpi = false;
        }
    }

    [RelayCommand]
    private void OpenIncognitoBrowser()
    {
        LaunchBrowserUrl(IncognitoBrowserLauncher.LoginUrl);
    }

    [RelayCommand]
    private void OpenSignUpBrowser()
    {
        LaunchBrowserUrl(IncognitoBrowserLauncher.SignUpUrl);
    }

    private void LaunchBrowserUrl(string url)
    {
        ErrorMessage = string.Empty;
        if (!_browserLauncher(url))
        {
            ErrorMessage = Strings.IncognitoBrowserFailed;
        }
    }

    [RelayCommand]
    public async Task ImportAccountsAsync()
    {
        if (IsImporting)
        {
            return;
        }

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        IReadOnlyList<SecretValue> parsedCookies = BulkCookieParser.ParseCookies(RawCookiesText);
        if (parsedCookies.Count == 0)
        {
            ErrorMessage = Strings.NoValidCookiesFound;
            return;
        }

        IsImporting = true;
        int successCount = 0;
        int failCount = 0;

        try
        {
            IReadOnlyList<Account> existingAccounts = await _accountStore.ListAsync();
            try
            {
                long nextId = existingAccounts.Count > 0 ? existingAccounts.Max(a => a.Id.Value) + 1 : 1;
                HashSet<string> usedAliases = existingAccounts
                    .Select(a => a.Alias.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<long> existingUserIds = existingAccounts
                    .Where(a => a.UserId.HasValue)
                    .Select(a => a.UserId!.Value)
                    .ToHashSet();

                for (int i = 0; i < parsedCookies.Count; i++)
                {
                    StatusMessage = string.Format(CultureInfo.CurrentCulture, BulkImportProgressFormat, i + 1, parsedCookies.Count);

                    SecretValue cookie = parsedCookies[i];
                    Result<RobloxUser> validationResult = await _authService.ValidateCookieAsync(cookie);
                    if (!validationResult.IsSuccess || validationResult.Value == null)
                    {
                        failCount++;
                        if (string.Equals(validationResult.ErrorCode, AuthErrorCodes.RateLimited, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        if (i < parsedCookies.Count - 1)
                        {
                            await Task.Delay(500);
                        }

                        continue;
                    }

                    RobloxUser user = validationResult.Value;
                    if (existingUserIds.Contains(user.Id))
                    {
                        Account? existingAccount = existingAccounts.FirstOrDefault(a =>
                            a.UserId == user.Id ||
                            string.Equals(a.Username, user.Name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a.Alias, user.Name, StringComparison.OrdinalIgnoreCase));

                        if (existingAccount != null)
                        {
                            Account updatedAccount = existingAccount with
                            {
                                Cookie = cookie.Clone(),
                                Username = user.Name,
                                UserId = user.Id,
                                Status = AccountStatus.Valid,
                                LastCheckedAt = DateTimeOffset.UtcNow
                            };

                            try
                            {
                                Result updateResult = await _accountStore.UpdateAsync(updatedAccount);
                                if (updateResult.IsSuccess)
                                {
                                    successCount++;
                                }
                                else
                                {
                                    failCount++;
                                }
                            }
                            finally
                            {
                                updatedAccount.Cookie.Dispose();
                            }
                        }
                        else
                        {
                            failCount++;
                        }

                        if (i < parsedCookies.Count - 1)
                        {
                            await Task.Delay(500);
                        }

                        continue;
                    }

                    string alias = EnsureUniqueAlias(user.Name, usedAliases);
                    usedAliases.Add(alias);
                    existingUserIds.Add(user.Id);

                    Account newAccount = new(
                        Id: new AccountId(nextId++),
                        Alias: alias,
                        Group: null,
                        Note: null,
                        Cookie: cookie.Clone(),
                        CreatedAt: DateTimeOffset.UtcNow,
                        Username: user.Name,
                        UserId: user.Id,
                        Status: AccountStatus.Valid,
                        LastCheckedAt: DateTimeOffset.UtcNow);

                    try
                    {
                        Result addResult = await _accountStore.AddAsync(newAccount);
                        if (addResult.IsSuccess)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    finally
                    {
                        newAccount.Cookie.Dispose();
                    }

                    if (i < parsedCookies.Count - 1)
                    {
                        await Task.Delay(500);
                    }
                }
            }
            finally
            {
                foreach (Account acc in existingAccounts)
                {
                    acc.Cookie.Dispose();
                }
            }
        }
        finally
        {
            foreach (SecretValue cookie in parsedCookies)
            {
                cookie.Dispose();
            }

            IsImporting = false;
        }

        ImportedCount += successCount;
        string summary = string.Format(CultureInfo.CurrentCulture, BulkImportCompletedFormat, successCount, failCount);
        StatusMessage = summary;

        if (successCount > 0)
        {
            RawCookiesText = string.Empty;
            _snackbarService?.Show(Strings.SuccessTitle, summary, ControlAppearance.Success);
            RequestClose?.Invoke(true);
        }
        else
        {
            ErrorMessage = summary;
        }
    }

    private static string EnsureUniqueAlias(string baseName, HashSet<string> usedAliases)
    {
        string trimmed = string.IsNullOrWhiteSpace(baseName) ? "RobloxUser" : baseName.Trim();
        if (trimmed.Length > 45)
        {
            trimmed = trimmed[..45];
        }

        if (!usedAliases.Contains(trimmed))
        {
            return trimmed;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"{trimmed}_{suffix}");
            if (!usedAliases.Contains(candidate))
            {
                return candidate;
            }
            suffix++;
        }
    }
}
