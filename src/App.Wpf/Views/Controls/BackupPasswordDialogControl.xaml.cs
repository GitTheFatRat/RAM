namespace RbxManager.App.Views.Controls;

using System;
using System.Windows;
using System.Windows.Controls;
using RbxManager.App.Resources;

public partial class BackupPasswordDialogControl : UserControl
{
    private readonly bool _isConfirmRequired;

    public BackupPasswordDialogControl(bool isConfirmRequired = true)
    {
        InitializeComponent();
        _isConfirmRequired = isConfirmRequired;

        if (!isConfirmRequired)
        {
            ConfirmContainer.Visibility = Visibility.Collapsed;
        }
    }

    public string? GetPassword()
    {
        string password = PasswordInput.Password;

        if (string.IsNullOrEmpty(password) || password.Length < 12)
        {
            ShowError(Strings.PasswordTooShort);
            return null;
        }

        if (_isConfirmRequired)
        {
            string confirm = ConfirmPasswordInput.Password;
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                ShowError(Strings.PasswordMismatch);
                return null;
            }
        }

        ErrorTextBlock.Visibility = Visibility.Collapsed;
        return password;
    }

    public void ClearPasswords()
    {
        PasswordInput.Clear();
        ConfirmPasswordInput.Clear();
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
