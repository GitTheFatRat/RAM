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

        ViewModel.RequestClose += OnRequestClose;

        Loaded += (_, _) =>
        {
            ViewModel.StartExtensionBridge();
            CookiesTextBox.Focus();
        };

        Closed += (_, _) =>
        {
            ViewModel.RequestClose -= OnRequestClose;
            ViewModel.Dispose();
        };
    }

    private void OnRequestClose(bool result)
    {
        if (Dispatcher.CheckAccess())
        {
            DialogResult = result;
            Close();
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                DialogResult = result;
                Close();
            });
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
