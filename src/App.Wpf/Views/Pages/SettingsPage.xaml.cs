namespace RbxManager.App.Views.Pages;

using System.Windows.Controls;
using RbxManager.App.ViewModels;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
