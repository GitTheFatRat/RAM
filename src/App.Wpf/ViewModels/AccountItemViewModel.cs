namespace RbxManager.App.ViewModels;

using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RbxManager.App.Resources;
using RbxManager.Core.Models;
using Wpf.Ui.Controls;

public partial class AccountItemViewModel : ObservableObject
{
    public Account Account { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AccountId Id => Account.Id;
    public string Alias => Account.Alias;
    public string? Group => Account.Group;
    public string? Note => Account.Note;
    public DateTimeOffset CreatedAt => Account.CreatedAt;
    public string CreatedFormatted => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Username => !string.IsNullOrWhiteSpace(Account.Username) ? Account.Username : "-";
    public long? UserId => Account.UserId;
    public AccountStatus Status => Account.Status;

    public string StatusText => Account.Status switch
    {
        AccountStatus.Valid => Strings.StatusValid,
        AccountStatus.Expired => Strings.StatusExpired,
        AccountStatus.Error => Strings.StatusError,
        _ => Strings.StatusUnknown
    };

    public InfoBadgeSeverity StatusSeverity => Account.Status switch
    {
        AccountStatus.Valid => InfoBadgeSeverity.Success,
        AccountStatus.Expired => InfoBadgeSeverity.Critical,
        AccountStatus.Error => InfoBadgeSeverity.Caution,
        _ => InfoBadgeSeverity.Informational
    };

    public string LastCheckedFormatted => Account.LastCheckedAt.HasValue
        ? Account.LastCheckedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
        : Strings.Never;

    public AccountItemViewModel(Account account)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
    }
}
