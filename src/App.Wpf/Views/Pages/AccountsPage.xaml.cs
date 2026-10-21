namespace RbxManager.App.Views.Pages;

using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    private void OnPagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (DataContext is not AccountsViewModel vm || vm.SelectedAccount == null)
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            if (vm.DeleteAccountCommand.CanExecute(null))
            {
                vm.DeleteAccountCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter)
        {
            if (vm.LaunchAccountCommand.CanExecute(null))
            {
                vm.LaunchAccountCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
