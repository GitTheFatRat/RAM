namespace RbxManager.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

public partial class AccountEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string? _group;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsEdit { get; }

    public AccountEditorViewModel(bool isEdit = false)
    {
        IsEdit = isEdit;
    }
}
