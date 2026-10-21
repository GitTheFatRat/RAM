namespace RbxManager.App.Resources;

using System.Globalization;
using System.Resources;

public static class Strings
{
    private static readonly ResourceManager ResourceManagerInstance =
        new("RbxManager.App.Resources.Strings", typeof(Strings).Assembly);

    public static CultureInfo? Culture { get; set; }

    public static string AppTitle => ResourceManagerInstance.GetString(nameof(AppTitle), Culture) ?? "RbxManager";
    public static string NavAccounts => ResourceManagerInstance.GetString(nameof(NavAccounts), Culture) ?? "Tài khoản";
    public static string NavSettings => ResourceManagerInstance.GetString(nameof(NavSettings), Culture) ?? "Cài đặt";
    public static string AccountsPageTitle => ResourceManagerInstance.GetString(nameof(AccountsPageTitle), Culture) ?? "Tài khoản";
    public static string SettingsPageTitle => ResourceManagerInstance.GetString(nameof(SettingsPageTitle), Culture) ?? "Cài đặt";
}
