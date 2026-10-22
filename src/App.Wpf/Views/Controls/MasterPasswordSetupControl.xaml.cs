namespace RbxManager.App.Views.Controls;

using System.Windows;
using System.Windows.Controls;

public partial class MasterPasswordSetupControl : UserControl
{
    public MasterPasswordSetupControl()
    {
        InitializeComponent();
    }

    public string ReadPassword() => MasterPasswordBox.Password;

    public string ReadConfirmPassword() => ConfirmPasswordBox.Password;

    public void ClearPasswords()
    {
        MasterPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
    }

    public void SetError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ErrorTextBlock.Text = string.Empty;
            ErrorTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }
}
