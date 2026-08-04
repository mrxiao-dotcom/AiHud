using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AiDaBenYing.Models;
using AiDaBenYing.ViewModels;
using MaterialDesignThemes.Wpf;

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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}
