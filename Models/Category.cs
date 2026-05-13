using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiHUD.Models;

public partial class Category : ObservableObject
{
    [ObservableProperty]
    private string _id = System.Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = "FolderOutline";

    [ObservableProperty]
    private string _color = "#7C4DFF";

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private bool _isExpanded = true;

    private ObservableCollection<AppItem> _appItems = new();

    [JsonIgnore]
    public ObservableCollection<AppItem> AppItems
    {
        get => _appItems;
        set
        {
            if (SetProperty(ref _appItems, value))
            {
                _appItems.CollectionChanged += OnAppItemsChanged;
                OnPropertyChanged(nameof(AppCount));
            }
        }
    }

    public int AppCount => _appItems?.Count ?? 0;

    public event EventHandler? AppItemsChanged;

    public Category()
    {
        _appItems.CollectionChanged += OnAppItemsChanged;
    }

    private void OnAppItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AppCount));
        AppItemsChanged?.Invoke(this, EventArgs.Empty);
    }
}
