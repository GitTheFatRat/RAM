namespace RbxManager.App.Views.Windows;

using System;
using System.Security.Cryptography;
using System.Windows;
using RbxManager.App.Resources;
using RbxManager.Core.Exceptions;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using Wpf.Ui.Controls;

public partial class MasterPasswordWindow : FluentWindow
{
    private readonly IVaultKeyProvider _vaultKeyProvider;
    private readonly IAccountStore _accountStore;
    private readonly PasswordRebindConfirmation _rebindConfirmation = new();

    public MasterPasswordWindow(IVaultKeyProvider vaultKeyProvider, IAccountStore accountStore)
    {
        _vaultKeyProvider = vaultKeyProvider ?? throw new ArgumentNullException(nameof(vaultKeyProvider));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        InitializeComponent();
        Loaded += (s, e) => MasterPasswordBox.Focus();
        Closed += (s, e) => _rebindConfirmation.Clear();
    }

    private async void OnUnlockClicked(object sender, RoutedEventArgs e)
    {
        string rawPassword = MasterPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(rawPassword))
        {
            ErrorTextBlock.Text = Strings.MasterPasswordRequired;
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        using SecretValue key = SecretValue.FromString(rawPassword);

        // A DPAPI-only vault "accepts" any password, so there is nothing to verify against. Binding it to a typo would
        // lock the user out for good: require the same password twice before re-protecting the vault with it.
        if (_accountStore is Infrastructure.Storage.EncryptedFileVault unprotectedVault &&
            !SafeIsPasswordProtected(unprotectedVault) &&
            !_rebindConfirmation.Confirm(key))
        {
            ErrorTextBlock.Text = Strings.MasterPasswordRebindConfirm;
            ErrorTextBlock.Visibility = Visibility.Visible;
            MasterPasswordBox.Clear();
            MasterPasswordBox.Focus();
            return;
        }

        _vaultKeyProvider.SetKey(key.Clone());

        try
        {
            var unlockedAccounts = await _accountStore.ListAsync();
            foreach (Account acc in unlockedAccounts)
            {
                acc.Cookie.Dispose();
            }

            if (_accountStore is Infrastructure.Storage.EncryptedFileVault encryptedVault)
            {
                try
                {
                    _ = await encryptedVault.EnsurePasswordProtectionAsync(key);
                }
                catch (Exception healFailure) when (healFailure is System.IO.IOException or UnauthorizedAccessException)
                {
                    // Unlock still succeeds; the next save re-applies the password (key provider already holds it).
                    Serilog.Log.Warning("Could not re-protect vault after unlock: {ExceptionType}.", healFailure.GetType().Name);
                }
            }

            _rebindConfirmation.Clear();
            DialogResult = true;
            Close();
        }
        catch (Exception ex) when (ex is VaultCorruptedException or CryptographicException or System.IO.IOException or UnauthorizedAccessException)
        {
            // Never leave an unverified key behind (an IO failure here used to escape this async void handler).
            _vaultKeyProvider.Clear();
            ErrorTextBlock.Text = Strings.MasterPasswordInvalid;
            ErrorTextBlock.Visibility = Visibility.Visible;
            MasterPasswordBox.Clear();
            MasterPasswordBox.Focus();
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        _rebindConfirmation.Clear();
        DialogResult = false;
        Close();
    }

    internal static bool SafeIsPasswordProtected(Infrastructure.Storage.EncryptedFileVault vault)
    {
        try
        {
            return vault.IsPasswordProtectedOnDisk();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Unknown state (header briefly locked by AV/backup): treat as unprotected. Worst case is one extra
            // confirmation prompt; the alternative could bind an unconfirmed typo and lock the user out.
            return false;
        }
    }
}

/// <summary>Accepts a password only when the same value was entered on the previous attempt.</summary>
internal sealed class PasswordRebindConfirmation
{
    private SecretValue? _firstEntry;

    public bool Confirm(SecretValue entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_firstEntry != null && _firstEntry.Equals(entry))
        {
            return true;
        }

        _firstEntry?.Dispose();
        _firstEntry = entry.Clone();
        return false;
    }

    /// <summary>Wipes the remembered first entry from memory.</summary>
    public void Clear()
    {
        _firstEntry?.Dispose();
        _firstEntry = null;
    }
}
