using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using AiDaBenYing.Converters;
using AiDaBenYing.Models;
using AiDaBenYing.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;

namespace AiDaBenYing.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DataService _dataService;
    private readonly AppLauncherService _launcherService;
    private AppData? _appData;
    private static readonly string LogFile = @"d:\CSharpProjects\AiHUD\viewmodel_log.txt";

    private void Log(string message)
    {
        try
        {
            var logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            System.IO.File.AppendAllText(LogFile, logMessage + Environment.NewLine);
            System.Diagnostics.Debug.WriteLine(logMessage);
        }
        catch { }
    }

    [ObservableProperty]
    private ObservableCollection<Category> _categories = new();

    // SelectedCategory 为 null 表示"全部应用"，非 null 表示选中某个分类
    [ObservableProperty]
    private Category? _selectedCategory;

    private ObservableCollection<AppItem> _allApps = new();
    public ObservableCollection<AppItem> AllApps => _allApps;

    [ObservableProperty]
    private AppItem? _selectedAppItem;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDarkMode = true;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private ICollectionView _filteredAppsView = null!;

    public ICollectionView FilteredApps
    {
        get
        {
            var count = _filteredAppsView?.Cast<object>()?.Count() ?? -1;
            Log($"[FilteredApps GET] _filteredAppsView count: {count}");
            return _filteredAppsView;
        }
    }

    // 是否显示全部应用（SelectedCategory 为 null）
    public bool IsAllCategories => SelectedCategory == null;

    public bool HasNoApps
    {
        get
        {
            if (IsAllCategories)
            {
                return _allApps.Count == 0;
            }
            return SelectedCategory!.AppItems.Count == 0;
        }
    }

    public string CurrentCategoryName => IsAllCategories ? "全部应用" : (SelectedCategory?.Name ?? "选择分类");
    public string CurrentCategoryIcon => IsAllCategories ? "Apps" : (SelectedCategory?.Icon ?? "FolderOutline");
    public string CurrentCategoryColor => IsAllCategories ? "#7C4DFF" : (SelectedCategory?.Color ?? "#7C4DFF");

    public void RefreshHasNoApps()
    {
        OnPropertyChanged(nameof(HasNoApps));
    }

    public MainViewModel()
    {
        _dataService = new DataService();
        _launcherService = new AppLauncherService();
        _launcherService.AppLaunched += OnAppLaunched;
    }

    partial void OnSelectedCategoryChanged(Category? value)
    {
        Log($"[OnSelectedCategoryChanged] Entered with value: {value?.Name ?? "null"}");

        // Set the FilteredAppsView FIRST before notifying
        if (value == null)
        {
            Log($"[OnSelectedCategoryChanged] Setting FilteredAppsView to AllApps, count: {_allApps.Count}");
            _filteredAppsView = CollectionViewSource.GetDefaultView(_allApps);
            _filteredAppsView.Filter = FilterAppItems;
        }
        else
        {
            Log($"[OnSelectedCategoryChanged] Setting FilteredAppsView to category: {value.Name}, count: {value.AppItems.Count}");
            _filteredAppsView = CollectionViewSource.GetDefaultView(value.AppItems);
            _filteredAppsView.Filter = FilterAppItems;
        }

        // THEN notify property changes
        OnPropertyChanged(nameof(HasNoApps));
        OnPropertyChanged(nameof(FilteredApps));
        OnPropertyChanged(nameof(IsAllCategories));

        Log($"[OnSelectedCategoryChanged] Exit");
    }

    private void OnSelectedCategoryAppItemsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasNoApps));
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_filteredAppsView != null)
        {
            _filteredAppsView.Refresh();
        }
        OnPropertyChanged(nameof(HasNoApps));
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            _appData = await _dataService.LoadDataAsync();
            Categories = _appData.Categories;

            // 收集所有应用（去重）
            _allApps.Clear();
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in Categories)
            {
                var appsToRemove = new List<AppItem>();
                foreach (var app in category.AppItems)
                {
                    if (!addedPaths.Contains(app.ExecutablePath))
                    {
                        _allApps.Add(app);
                        addedPaths.Add(app.ExecutablePath);
                    }
                    else
                    {
                        appsToRemove.Add(app);
                    }
                }
                foreach (var dup in appsToRemove)
                {
                    category.AppItems.Remove(dup);
                }
            }

            // 异步加载所有应用图标
            _ = Task.Run(() => LoadAllAppIcons());

            // 默认显示全部应用（SelectedCategory = null）
            SelectedCategory = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadAllAppIcons()
    {
        foreach (var app in _allApps)
        {
            app.EnsureIconLoaded();
        }
    }

    private bool FilterAppItems(object obj)
    {
        if (obj is not AppItem app) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               app.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void OnAppLaunched(object? sender, AppItem appItem)
    {
        _ = SaveDataAsync();
    }

    public void AddCategory(string name, string icon, string color)
    {
        var category = new Category
        {
            Name = name,
            Icon = icon,
            Color = color,
            Order = Categories.Count + 1,
            AppItems = new ObservableCollection<AppItem>()
        };

        Categories.Add(category);
        SelectedCategory = category;
        _ = SaveDataAsync();
    }

    public void DeleteCategory(Category category)
    {
        if (category == null) return;

        // 如果删除的是当前选中的分类，切换到"全部应用"
        if (SelectedCategory == category)
        {
            SelectedCategory = null;
        }

        // 从 AllApps 中移除该分类下的所有应用
        foreach (var app in category.AppItems.ToList())
        {
            _allApps.Remove(app);
        }

        // 从 Categories 中移除该分类
        Categories.Remove(category);
        _ = SaveDataAsync();
    }

    [RelayCommand]
    private void LaunchApp(AppItem? appItem)
    {
        if (appItem == null) return;

        if (!_launcherService.ValidatePath(appItem.ExecutablePath))
        {
            System.Windows.MessageBox.Show($"找不到可执行文件: {appItem.ExecutablePath}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        _launcherService.LaunchApp(appItem);
    }

    [RelayCommand]
    private void AddApp()
    {
        System.Diagnostics.Debug.WriteLine("[MainViewModel.AddApp] 开始添加应用");
        if (SelectedCategory == null)
        {
            System.Diagnostics.Debug.WriteLine("[MainViewModel.AddApp] SelectedCategory 为 null，无法添加");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            var exePath = dialog.FileName;
            var appName = System.IO.Path.GetFileNameWithoutExtension(exePath);

            // 检查是否已存在相同的应用（按路径去重）
            if (_allApps.Any(a => a.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
            {
                System.Windows.MessageBox.Show($"应用 '{appName}' 已经存在，不能重复添加！", "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[MainViewModel.AddApp] 选择文件: {exePath}, 名称: {appName}");

            var appItem = new AppItem(appName, exePath, null);
            appItem.EnsureIconLoaded(); // 加载图标
            SelectedCategory.AppItems.Add(appItem);
            _allApps.Add(appItem);

            OnPropertyChanged(nameof(HasNoApps));
            _ = SaveDataAsync();
        }
    }

    [RelayCommand]
    private void AddAppManually()
    {
        if (SelectedCategory == null) return;

        var appItem = new AppItem
        {
            Name = "新应用",
            ExecutablePath = string.Empty
        };

        SelectedCategory.AppItems.Add(appItem);
        SelectedAppItem = appItem;
        IsEditMode = true;
        OnPropertyChanged(nameof(HasNoApps));
    }

    [RelayCommand]
    private void DeleteApp(AppItem? appItem)
    {
        if (appItem == null) return;

        var result = System.Windows.MessageBox.Show($"确定要删除 '{appItem.Name}' 吗？",
            "确认删除", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            // 从所有分类中删除
            foreach (var category in Categories)
            {
                if (category.AppItems.Contains(appItem))
                {
                    category.AppItems.Remove(appItem);
                    break;
                }
            }
            // 从全部应用列表中删除
            _allApps.Remove(appItem);

            if (SelectedAppItem == appItem)
            {
                SelectedAppItem = null;
            }
            OnPropertyChanged(nameof(HasNoApps));
            _ = SaveDataAsync();
        }
    }

    [RelayCommand]
    private void BrowseForApp()
    {
        if (SelectedAppItem == null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedAppItem.ExecutablePath = dialog.FileName;
            SelectedAppItem.Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            _ = SaveDataAsync();
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        var paletteHelper = new MaterialDesignThemes.Wpf.PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(IsDarkMode ? MaterialDesignThemes.Wpf.BaseTheme.Dark : MaterialDesignThemes.Wpf.BaseTheme.Light);
        paletteHelper.SetTheme(theme);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        IsEditMode = false;
        _ = SaveDataAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditMode = false;
    }

    [RelayCommand]
    private void OpenInExplorer(AppItem? appItem)
    {
        if (appItem == null || string.IsNullOrEmpty(appItem.ExecutablePath)) return;

        try
        {
            var directory = System.IO.Path.GetDirectoryName(appItem.ExecutablePath);
            if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{appItem.ExecutablePath}\"");
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"无法打开文件位置: {ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SelectAllCategories()
    {
        SelectedCategory = null;
    }

    [RelayCommand]
    private void SelectCategory(Category? category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private void MoveApp(Converters.MoveAppParameter? param)
    {
        if (param?.TargetCategory == null || param?.AppItem == null) return;

        var appItem = param.AppItem;
        var targetCategory = param.TargetCategory;

        // Find the source category containing this app
        Category? sourceCategory = null;
        foreach (var cat in Categories)
        {
            if (cat.AppItems.Contains(appItem))
            {
                sourceCategory = cat;
                break;
            }
        }

        if (sourceCategory == null || sourceCategory == targetCategory) return;

        // Remove from source category
        sourceCategory.AppItems.Remove(appItem);

        // Add to target category
        targetCategory.AppItems.Add(appItem);

        // Select the target category
        SelectedCategory = targetCategory;

        // Save data
        _ = SaveDataAsync();
    }

    public async Task SaveDataAsync()
    {
        if (_appData != null)
        {
            _appData.Categories = Categories;
            await _dataService.SaveDataAsync(_appData);
        }
    }
}
