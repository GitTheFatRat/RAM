namespace RbxManager.App.Views.Windows;

using System.Windows;
using RbxManager.App.Services;
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
            IncognitoBrowserLauncher.CookieCaptured += OnBrowserCookieCaptured;
            CookiesTextBox.Focus();
        };

        Closed += (_, _) =>
        {
            ViewModel.RequestClose -= OnRequestClose;
            IncognitoBrowserLauncher.CookieCaptured -= OnBrowserCookieCaptured;
            IncognitoBrowserLauncher.CleanupAllSessions();
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

    private void OnBrowserCookieCaptured(string cookie)
    {
        _ = Dispatcher.InvokeAsync(() => ViewModel.AppendCapturedCookie(cookie));
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
