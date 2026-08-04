using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiDaBenYing.Models;

public partial class AppData : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Category> _categories = new();

    [ObservableProperty]
    private AppSettings _settings = new();
}

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string _themeMode = "Dark";

    [ObservableProperty]
    private string _primaryColor = "DeepPurple";

    [ObservableProperty]
    private string _secondaryColor = "Cyan";

    [ObservableProperty]
    private double _windowWidth = 1200;

    [ObservableProperty]
    private double _windowHeight = 800;

    [ObservableProperty]
    private bool _rememberWindowPosition = true;

    [ObservableProperty]
    private double _left;

    [ObservableProperty]
    private double _top;
}
