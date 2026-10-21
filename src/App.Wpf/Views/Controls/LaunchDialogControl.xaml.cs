namespace RbxManager.App.Views.Controls;

using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

public partial class LaunchDialogControl : UserControl
{
    private static readonly Regex DigitsOnlyRegex = new("^[0-9]+$");

    public string PlaceIdText
    {
        get => PlaceIdTextBox.Text.Trim();
        set => PlaceIdTextBox.Text = value;
    }

    public string? JobIdText
    {
        get => string.IsNullOrWhiteSpace(JobIdTextBox.Text) ? null : JobIdTextBox.Text.Trim();
        set => JobIdTextBox.Text = value ?? string.Empty;
    }

    public LaunchDialogControl()
    {
        InitializeComponent();
    }

    public void SetError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            ErrorTextBlock.Text = string.Empty;
            ErrorTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            ErrorTextBlock.Text = errorMessage;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void OnPlaceIdPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
    }
}
