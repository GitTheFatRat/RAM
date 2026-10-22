namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RbxManager.Core.Models;

public partial class AccountEditorViewModel : ObservableObject
{
    public ObservableCollection<string> AvailableGroups { get; } = new();

    public IReadOnlyList<ProxyType> AvailableProxyTypes { get; } = [ProxyType.Http, ProxyType.Socks5];

    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string? _group;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private ProxyType _selectedProxyType = ProxyType.Http;

    [ObservableProperty]
    private string _proxyHost = string.Empty;

    [ObservableProperty]
    private int _proxyPort = 8080;

    [ObservableProperty]
    private string? _proxyUsername;

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
