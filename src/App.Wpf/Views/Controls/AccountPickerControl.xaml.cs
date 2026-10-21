namespace RbxManager.App.Views.Controls;

using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RbxManager.Core.Models;

public partial class AccountPickerControl : UserControl
{
    public sealed class SelectableAccountItem : ObservableObject
    {
        public Account Account { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public SelectableAccountItem(Account account, bool isSelected = false)
        {
            Account = account;
            _isSelected = isSelected;
        }
    }

    public sealed class AccountPickerViewModel : ObservableObject
    {
        public List<SelectableAccountItem> Accounts { get; }

        public AccountPickerViewModel(IEnumerable<Account> accounts)
        {
            Accounts = accounts.Select(a => new SelectableAccountItem(a)).ToList();
        }

        public IReadOnlyList<Account> GetSelectedAccounts() =>
            Accounts.Where(a => a.IsSelected).Select(a => a.Account).ToList();
    }

    public AccountPickerControl(IEnumerable<Account> accounts)
    {
        InitializeComponent();
        DataContext = new AccountPickerViewModel(accounts);
    }

    public IReadOnlyList<Account> GetSelectedAccounts()
    {
        if (DataContext is AccountPickerViewModel vm)
        {
            return vm.GetSelectedAccounts();
        }
        return [];
    }
}
