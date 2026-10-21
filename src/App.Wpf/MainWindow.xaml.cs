namespace RbxManager.App;

using System;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow(
        MainViewModel viewModel,
        IServiceProvider serviceProvider,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService)
    {
        ArgumentNullException.ThrowIfNull(contentDialogService);
        ArgumentNullException.ThrowIfNull(snackbarService);

        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        contentDialogService.SetDialogHost(RootContentDialogHost);
        snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Loaded += (_, _) => RootNavigation.Navigate(typeof(AccountsPage));
    }
}
