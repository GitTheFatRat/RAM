namespace RbxManager.App.Views.Pages;

using System.Windows;
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
        if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (ViewModel.AddViaLoginCommand.CanExecute(null))
            {
                ViewModel.AddViaLoginCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F5)
        {
            if (ViewModel.RefreshCommand.CanExecute(null))
            {
                ViewModel.RefreshCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

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

    private void OnDataGridSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        string? sortMember = e.Column.SortMemberPath;
        if (!string.IsNullOrEmpty(sortMember))
        {
            ViewModel.SortCommand.Execute(sortMember);
        }
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
        }
    }

    private void OnAddSplitMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
        }
    }
}
