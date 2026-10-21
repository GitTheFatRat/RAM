namespace RbxManager.App.Views.Pages;

using System.Windows.Controls;
using RbxManager.App.ViewModels;

public partial class ServersPage : Page
{
    public ServersPage(ServersViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
