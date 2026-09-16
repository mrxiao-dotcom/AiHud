using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AiDaBenYing.Models;
using AiDaBenYing.Services;
using AiDaBenYing.Services.Abstractions;
using AiDaBenYing.ViewModels;
using MaterialDesignThemes.Wpf;

namespace AiDaBenYing.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void AllApps_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedCategory == null)
        {
            // Already showing all apps, force refresh by temporarily switching
            var tempCategory = _viewModel.Categories.FirstOrDefault();
            _viewModel.SelectedCategory = tempCategory; // Will trigger change
            _viewModel.SelectedCategory = null; // Set back to null
        }
        else
        {
            _viewModel.SelectedCategory = null;
        }
    }

    private void CategoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Find the Border with Tag inside the Grid
        Border? border = null;
        if (sender is Grid grid)
        {
            border = grid.Children.OfType<Border>().FirstOrDefault(b => b.Tag != null);
        }
        else if (sender is Border b)
        {
            border = b;
        }

        if (border?.Tag is Category category)
        {
            _viewModel.SelectedCategory = category;
        }
    }

    private async void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCategoryDialog();
        var result = await DialogHost.Show(dialog, "RootDialog");

        if (result is AddCategoryDialog addDialog && !string.IsNullOrEmpty(addDialog.CategoryName))
        {
            _viewModel.AddCategory(addDialog.CategoryName, addDialog.CategoryIcon, addDialog.CategoryColor);
        }
    }

    private async void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Category category)
        {
            var dialog = new EditCategoryDialog(category);
            await DialogHost.Show(dialog, "RootDialog");
            await _viewModel.SaveDataAsync();
        }
    }

    private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Category category)
        {
            if (category.AppItems.Count > 0)
            {
                var result = MessageBox.Show(
                    $"分类 \"{category.Name}\" 中有 {category.AppItems.Count} 个应用，删除分类后这些应用也会被删除。\n\n确定要删除吗？",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }
            else
            {
                var result = MessageBox.Show(
                    $"确定要删除分类 \"{category.Name}\" 吗？",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;
            }

            _viewModel.DeleteCategory(category);
        }
    }

    private async void EditApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is AppItem appItem)
        {
            var dialog = new EditAppDialog();
            dialog.SetAppItem(appItem);

            var result = await DialogHost.Show(dialog, "RootDialog");

            if (result is EditAppDialog editDialog && editDialog.AppItem != null)
            {
                await _viewModel.SaveDataAsync();
            }
        }
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is AppItem appItem)
        {
            _viewModel.OpenInExplorerCommand.Execute(appItem);
        }
    }

    private async void MoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is AppItem appItem)
        {
            var dialog = new MoveAppDialog(_viewModel.Categories, appItem);
            var result = await DialogHost.Show(dialog, "RootDialog");

            if (result is Category targetCategory)
            {
                _viewModel.MoveAppCommand.Execute(new Converters.MoveAppParameter { AppItem = appItem, TargetCategory = targetCategory });
            }
        }
    }

    private void DeleteApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is AppItem appItem)
        {
            _viewModel.DeleteAppCommand.Execute(appItem);
        }
    }

    private void AppButton_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.DataContext = button.DataContext;
            button.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// 打开"会员应用市场"窗口（内嵌 WebView2，访问 82.157.32.44:8888）。
    /// 窗口本身不暴露地址栏/URL 输入框，用户无法从界面看到具体网址。
    /// </summary>
    private void ShowAppMarket_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 不设 Owner：避免关闭应用市场时强制让主窗口失焦、回到任务栏。
            // 改在 Closed 事件里手动 Activate() 主窗口 —— 体验上等价于"点回来"。
            var win = new AppMarketWindow();
            win.Closed += (_, _) =>
            {
                if (IsLoaded) Activate();
            };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "无法打开应用市场：" + ex.Message +
                "\n\n提示：本功能依赖 Edge WebView2 Runtime，请确认系统已安装最新版的 Edge 浏览器。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SettingsDialog { Owner = this };
            dlg.ShowDialog();
            // 设置保存后无需重载数据；其它模块（市场下载）会读最新的 SettingsService.Current
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开系统设置: " + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cachedKey = LicenseCache.Load()?.KeyCode;
            var regResult = App.License!.ShowRegistrationWindow(
                appName: "AI大本营",
                appVersion: App.AppVersion.ToString(),
                prefillKey: cachedKey);

            if (regResult.IsValid && !string.IsNullOrWhiteSpace(regResult.ValidatedLicenseKey))
            {
                LicenseCache.Save(regResult.ValidatedLicenseKey);
                App.IsLicenseValid = true;
                _ = _viewModel.InitializeAsync();
            }
            else if (regResult.IsValid)
            {
                App.IsLicenseValid = true;
            }
            else if (!string.IsNullOrEmpty(cachedKey))
            {
                MessageBox.Show(
                    "注册窗口未返回有效注册码。\n\n" +
                    "如果你是刚刚更换了服务器地址，原缓存的注册码绑定的是旧服务器，\n" +
                    "请用新服务器的管理员生成的注册码再次验证（也可以直接申请试用）。",
                    "AI大本营", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开注册窗口：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var status = "正在检查更新...";
            _ = status; // 保留以备扩展
            Cursor = System.Windows.Input.Cursors.Wait;
            var result = await App.Updater!.CheckAsync(App.AppVersion.ToString());
            Cursor = System.Windows.Input.Cursors.Arrow;

            if (!result.IsSuccess)
            {
                MessageBox.Show($"检查更新失败：{result.Message}", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!result.HasUpdate || result.Info == null)
            {
                MessageBox.Show("已是最新版本。", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new UpdateDialog(result.Info, App.AppVersion.ToString())
            {
                Owner = this
            };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            Cursor = System.Windows.Input.Cursors.Arrow;
            MessageBox.Show("更新检查异常：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}
