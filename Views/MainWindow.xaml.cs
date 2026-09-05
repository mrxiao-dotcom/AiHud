using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AiDaBenYing.Models;
using AiDaBenYing.Services;
using AiDaBenYing.ViewModels;
using MaterialDesignThemes.Wpf;
using RegisterSrv.ClientSDK;

namespace AiDaBenYing.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private static readonly string LogFile = @"d:\CSharpProjects\AiHUD\click_log.txt";

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
    }

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

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void AllApps_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Log($"[AllApps_Click] Sender: {sender?.GetType().Name}, Current SelectedCategory: {_viewModel.SelectedCategory?.Name ?? "null"}");
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
        Log($"[AllApps_Click] After set, SelectedCategory: {_viewModel.SelectedCategory?.Name ?? "null"}");
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
            Log($"[CategoryItem_Click] Sender: {sender?.GetType().Name}, Category: {category.Name}, Current SelectedCategory: {_viewModel.SelectedCategory?.Name ?? "null"}");
            _viewModel.SelectedCategory = category;
            Log($"[CategoryItem_Click] After set, SelectedCategory: {_viewModel.SelectedCategory?.Name ?? "null"}");
        }
        else
        {
            Log($"[CategoryItem_Click] Failed - Sender: {sender?.GetType().Name}");
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
            // 每次点击都重新 new 窗口，关闭即销毁。WebView2 持有 GPU/网络资源，
            // 不适合像普通 ViewModel 那样长期缓存。
            var win = new AppMarketWindow
            {
                Owner = this
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

    private void ShowLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 绕开 LicenseManager 直接 new LicenseWindow，以便：
            //  1) 把本地缓存的 KeyCode 注入进去，窗口打开即显示"已注册"
            //  2) 弹窗关闭后能拿到 ValidatedLicenseKey 写回本地缓存
            var cachedKey = LicenseCache.Load()?.KeyCode;
            var serverUrl = System.Configuration.ConfigurationManager.AppSettings["LicenseServerUrl"]
                            ?? Environment.GetEnvironmentVariable("AIHUD_LICENSE_SERVER")
                            ?? "http://localhost:5232";

            var licenseWindow = new LicenseWindow(App.AppId, "AI大本营", App.AppVersion.ToString(), serverUrl, cachedKey)
            {
                Owner = this
            };
            licenseWindow.ShowDialog();

            if (licenseWindow.IsLicenseValid && !string.IsNullOrWhiteSpace(licenseWindow.ValidatedLicenseKey))
            {
                LicenseCache.Save(licenseWindow.ValidatedLicenseKey);
                App.IsLicenseValid = true;
                MessageBox.Show("注册成功，正在重新加载数据…", "AI大本营",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                _ = _viewModel.InitializeAsync();
            }
            else if (licenseWindow.IsLicenseValid)
            {
                App.IsLicenseValid = true;
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
            App.Updater ??= new RegisterSrv.ClientSDK.Services.UpdateClient(
                baseUrl: System.Configuration.ConfigurationManager.AppSettings["LicenseServerUrl"]
                         ?? "http://localhost:5232",
                appId: App.AppId);

            var status = "正在检查更新...";
            Cursor = System.Windows.Input.Cursors.Wait;
            var result = await App.Updater.CheckUpdateAsync(App.AppVersion.ToString());
            Cursor = System.Windows.Input.Cursors.Arrow;

            if (!result.IsSuccess)
            {
                MessageBox.Show($"检查更新失败：{result.Message}", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!result.HasUpdate || result.UpdateInfo == null)
            {
                MessageBox.Show("已是最新版本。", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new UpdateDialog(result.UpdateInfo, App.AppVersion.ToString())
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
