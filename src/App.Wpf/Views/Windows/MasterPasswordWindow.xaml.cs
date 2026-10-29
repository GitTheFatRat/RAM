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

    public MasterPasswordWindow(IVaultKeyProvider vaultKeyProvider, IAccountStore accountStore)
    {
        _vaultKeyProvider = vaultKeyProvider ?? throw new ArgumentNullException(nameof(vaultKeyProvider));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        InitializeComponent();
        Loaded += (s, e) => MasterPasswordBox.Focus();
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
        _vaultKeyProvider.SetKey(key.Clone());

        try
        {
            var unlockedAccounts = await _accountStore.ListAsync();
            foreach (Account acc in unlockedAccounts)
            {
                acc.Cookie.Dispose();
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex) when (ex is VaultCorruptedException or CryptographicException)
        {
            _vaultKeyProvider.Clear();
            ErrorTextBlock.Text = Strings.MasterPasswordInvalid;
            ErrorTextBlock.Visibility = Visibility.Visible;
            MasterPasswordBox.Clear();
            MasterPasswordBox.Focus();
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
