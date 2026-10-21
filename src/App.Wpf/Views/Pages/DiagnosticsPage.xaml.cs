namespace RbxManager.App.Views.Pages;

using System.Windows.Controls;
using RbxManager.App.ViewModels;

public partial class DiagnosticsPage : Page
{
    private readonly DiagnosticsViewModel _viewModel;

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += async (s, e) => await _viewModel.InitializeAsync();
    }
}
