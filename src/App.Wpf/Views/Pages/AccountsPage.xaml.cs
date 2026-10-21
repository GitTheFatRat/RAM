namespace RbxManager.App.Views.Pages;

using System.Windows.Controls;
using RbxManager.App.ViewModels;

public partial class AccountsPage : Page
{
    public AccountsViewModel ViewModel { get; }

    public AccountsPage(AccountsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        Loaded += async (_, _) => await ViewModel.LoadAccountsCommand.ExecuteAsync(null);
    }
}
