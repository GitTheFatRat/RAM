namespace RbxManager.App.Resources;

using System.Globalization;
using System.Resources;

public static class Strings
{
    private static readonly ResourceManager ResourceManagerInstance =
        new("RbxManager.App.Resources.Strings", typeof(Strings).Assembly);

    public static CultureInfo? Culture { get; set; }

    public static string AppTitle => GetString(nameof(AppTitle));
    public static string NavAccounts => GetString(nameof(NavAccounts));
    public static string NavSettings => GetString(nameof(NavSettings));
    public static string AccountsPageTitle => GetString(nameof(AccountsPageTitle));
    public static string SettingsPageTitle => GetString(nameof(SettingsPageTitle));
    public static string SearchPlaceholder => GetString(nameof(SearchPlaceholder));
    public static string AddAccount => GetString(nameof(AddAccount));
    public static string EditAccount => GetString(nameof(EditAccount));
    public static string DeleteAccount => GetString(nameof(DeleteAccount));
    public static string ColumnAlias => GetString(nameof(ColumnAlias));
    public static string ColumnGroup => GetString(nameof(ColumnGroup));
    public static string ColumnCreated => GetString(nameof(ColumnCreated));
    public static string EmptyAccountsList => GetString(nameof(EmptyAccountsList));
    public static string DeleteConfirmTitle => GetString(nameof(DeleteConfirmTitle));
    public static string DeleteConfirmMessage => GetString(nameof(DeleteConfirmMessage));
    public static string Cancel => GetString(nameof(Cancel));
    public static string Save => GetString(nameof(Save));
    public static string Delete => GetString(nameof(Delete));
    public static string Close => GetString(nameof(Close));
    public static string FieldAlias => GetString(nameof(FieldAlias));
    public static string FieldGroup => GetString(nameof(FieldGroup));
    public static string FieldNote => GetString(nameof(FieldNote));
    public static string FieldCookie => GetString(nameof(FieldCookie));
    public static string AddAccountDialogTitle => GetString(nameof(AddAccountDialogTitle));
    public static string EditAccountDialogTitle => GetString(nameof(EditAccountDialogTitle));
    public static string ErrorTitle => GetString(nameof(ErrorTitle));
    public static string SuccessTitle => GetString(nameof(SuccessTitle));
    public static string AccountSavedMessage => GetString(nameof(AccountSavedMessage));
    public static string AccountDeletedMessage => GetString(nameof(AccountDeletedMessage));
    public static string VaultCorruptedTitle => GetString(nameof(VaultCorruptedTitle));
    public static string VaultCorruptedMessage => GetString(nameof(VaultCorruptedMessage));
    public static string ValidationCookieRequired => GetString(nameof(ValidationCookieRequired));
    public static string AddViaLogin => GetString(nameof(AddViaLogin));
    public static string AddViaCookie => GetString(nameof(AddViaCookie));
    public static string Check => GetString(nameof(Check));
    public static string CheckAll => GetString(nameof(CheckAll));
    public static string Launch => GetString(nameof(Launch));
    public static string ColumnUsername => GetString(nameof(ColumnUsername));
    public static string ColumnStatus => GetString(nameof(ColumnStatus));
    public static string ColumnLastChecked => GetString(nameof(ColumnLastChecked));
    public static string Never => GetString(nameof(Never));
    public static string StatusUnknown => GetString(nameof(StatusUnknown));
    public static string StatusValid => GetString(nameof(StatusValid));
    public static string StatusExpired => GetString(nameof(StatusExpired));
    public static string StatusError => GetString(nameof(StatusError));
    public static string ExpiredHint => GetString(nameof(ExpiredHint));
    public static string LaunchDialogTitle => GetString(nameof(LaunchDialogTitle));
    public static string PlaceIdLabel => GetString(nameof(PlaceIdLabel));
    public static string JobIdLabel => GetString(nameof(JobIdLabel));
    public static string PlaceIdRequired => GetString(nameof(PlaceIdRequired));
    public static string JobIdInvalid => GetString(nameof(JobIdInvalid));
    public static string RobloxNotInstalled => GetString(nameof(RobloxNotInstalled));
    public static string LaunchSuccess => GetString(nameof(LaunchSuccess));
    public static string WebView2Missing => GetString(nameof(WebView2Missing));
    public static string LoginWindowTitle => GetString(nameof(LoginWindowTitle));
    public static string LoginSuccessValidating => GetString(nameof(LoginSuccessValidating));

    private static string GetString(string name) =>
        ResourceManagerInstance.GetString(name, Culture) ?? name;
}
