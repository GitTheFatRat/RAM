namespace RbxManager.App;

using System;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Pages;
using Wpf.Ui.Controls;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Loaded += (_, _) => RootNavigation.Navigate(typeof(AccountsPage));
    }
}
