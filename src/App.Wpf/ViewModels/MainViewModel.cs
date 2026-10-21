namespace RbxManager.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using RbxManager.App.Resources;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = Strings.AppTitle;
}
