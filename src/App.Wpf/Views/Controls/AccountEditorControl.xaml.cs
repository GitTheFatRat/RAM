namespace RbxManager.App.Views.Controls;

using System.Windows.Controls;

public partial class AccountEditorControl : UserControl
{
    public AccountEditorControl()
    {
        InitializeComponent();
    }

    public string ReadPassword() => CookieBox.Password;

    public void ClearPassword() => CookieBox.Clear();

    public string ReadProxyPassword() => ProxyPasswordBox.Password;

    public void ClearProxyPassword() => ProxyPasswordBox.Clear();
}
