namespace RbxManager.App.Views.Windows;

using System.Windows;
using RbxManager.App.ViewModels;
using Wpf.Ui.Controls;

public partial class AddAccountWindow : FluentWindow
{
    public AddAccountViewModel ViewModel { get; }

    public AddAccountWindow(AddAccountViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };

        Loaded += (_, _) => CookiesTextBox.Focus();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
