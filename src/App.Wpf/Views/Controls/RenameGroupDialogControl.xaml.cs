namespace RbxManager.App.Views.Controls;

using System;
using System.Windows;
using System.Windows.Controls;
using RbxManager.App.Resources;

public partial class RenameGroupDialogControl : UserControl
{
    private readonly string _currentGroup;

    public RenameGroupDialogControl(string currentGroup)
    {
        InitializeComponent();
        _currentGroup = currentGroup;
        CurrentGroupTextBlock.Text = $"Nhóm hiện tại: {currentGroup}";
        NewGroupNameInput.Text = currentGroup;
        NewGroupNameInput.SelectAll();
    }

    public string? GetNewGroupName()
    {
        string newName = NewGroupNameInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newName))
        {
            ErrorTextBlock.Text = "Tên nhóm mới không được để trống.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return null;
        }

        if (newName.Length > 30)
        {
            ErrorTextBlock.Text = "Tên nhóm không được vượt quá 30 ký tự.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return null;
        }

        ErrorTextBlock.Visibility = Visibility.Collapsed;
        return newName;
    }
}
