namespace RbxManager.App.ViewModels;

using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RbxManager.Core.Models;

public partial class AccountItemViewModel : ObservableObject
{
    public Account Account { get; }

    public AccountId Id => Account.Id;
    public string Alias => Account.Alias;
    public string? Group => Account.Group;
    public string? Note => Account.Note;
    public DateTimeOffset CreatedAt => Account.CreatedAt;
    public string CreatedFormatted => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public AccountItemViewModel(Account account)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
    }
}
