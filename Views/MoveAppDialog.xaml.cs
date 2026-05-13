using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AiHUD.Models;
using MaterialDesignThemes.Wpf;

namespace AiHUD.Views;

public partial class MoveAppDialog : UserControl
{
    private readonly AppItem _appItem;
    private readonly ObservableCollection<Category> _categories;

    public MoveAppDialog(ObservableCollection<Category> categories, AppItem appItem)
    {
        InitializeComponent();
        _categories = categories;
        _appItem = appItem;

        AppNameTextBlock.Text = $"将 \"{appItem.Name}\" 移动到：";
        CategoriesItemsControl.ItemsSource = categories;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Category targetCategory)
        {
            DialogHost.CloseDialogCommand.Execute(targetCategory, this);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogHost.CloseDialogCommand.Execute(null, this);
    }
}
