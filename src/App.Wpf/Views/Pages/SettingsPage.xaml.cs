namespace RbxManager.App.Views.Pages;

using System;
using System.Windows.Controls;
using System.Windows.Input;
using RbxManager.App.ViewModels;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SettingsScrollViewer == null)
        {
            return;
        }

        double delta = e.Delta;
        double newOffset = SettingsScrollViewer.VerticalOffset - (delta * 0.6);
        SettingsScrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(newOffset, SettingsScrollViewer.ScrollableHeight)));
        e.Handled = true;
    }
}
