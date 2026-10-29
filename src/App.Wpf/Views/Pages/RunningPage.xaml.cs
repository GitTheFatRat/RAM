namespace RbxManager.App.Views.Pages;

using System.Windows.Controls;
using RbxManager.App.ViewModels;

public partial class RunningPage : Page
{
    public RunningPage(RunningViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _ = viewModel.RefreshSessionsAsync();
            if (viewModel.IsRotationIdle)
            {
                _ = viewModel.LoadRotationOptionsAsync();
            }
        };
    }
}
