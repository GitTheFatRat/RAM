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

    public Func<long, string?, string>? DryRunBuilder { get; set; }

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

    private void OnDryRunButtonClick(object sender, RoutedEventArgs e)
    {
        string placeIdStr = PlaceIdText;
        if (!long.TryParse(placeIdStr, out long placeId) || placeId <= 0)
        {
            SetError(RbxManager.App.Resources.Strings.PlaceIdRequired);
            DryRunResultPanel.Visibility = Visibility.Collapsed;
            return;
        }

        string? jobId = JobIdText;
        if (!string.IsNullOrWhiteSpace(jobId) && !Guid.TryParse(jobId, out _))
        {
            SetError(RbxManager.App.Resources.Strings.JobIdInvalid);
            DryRunResultPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SetError(null);

        if (DryRunBuilder != null)
        {
            string uri = DryRunBuilder(placeId, jobId);
            DryRunResultTextBox.Text = uri;
            DryRunResultPanel.Visibility = Visibility.Visible;
        }
    }

    private void OnPlaceIdPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
    }
}
