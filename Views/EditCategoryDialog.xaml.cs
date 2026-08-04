using System.Windows;
using System.Windows.Controls;
using AiDaBenYing.Models;
using MaterialDesignThemes.Wpf;

namespace AiDaBenYing.Views;

public partial class EditCategoryDialog : UserControl
{
    public string CategoryName { get; private set; } = string.Empty;
    public string CategoryColor { get; private set; } = "#7C4DFF";
    public string CategoryIcon { get; private set; } = "FolderOutline";

    private readonly Category? _category;

    public EditCategoryDialog() : this(null)
    {
    }

    public EditCategoryDialog(Category? category)
    {
        InitializeComponent();
        _category = category;

        if (category != null)
        {
            CategoryNameTextBox.Text = category.Name;
            SetSelectedColor(category.Color);
            SetSelectedIcon(category.Icon);
        }
    }

    private void SetSelectedColor(string color)
    {
        switch (color.ToUpperInvariant())
        {
            case "#7C4DFF": ColorPurple.IsChecked = true; break;
            case "#4CAF50": ColorGreen.IsChecked = true; break;
            case "#2196F3": ColorBlue.IsChecked = true; break;
            case "#FF9800": ColorOrange.IsChecked = true; break;
            case "#F44336": ColorRed.IsChecked = true; break;
            case "#E91E63": ColorPink.IsChecked = true; break;
            default: ColorPurple.IsChecked = true; break;
        }
    }

    private void SetSelectedIcon(string icon)
    {
        switch (icon)
        {
            case "FolderOutline": IconFolder.IsChecked = true; break;
            case "ChartLine": IconChart.IsChecked = true; break;
            case "Robot": IconRobot.IsChecked = true; break;
            case "Wrench": IconWrench.IsChecked = true; break;
            case "Code": IconCode.IsChecked = true; break;
            case "Brain": IconBrain.IsChecked = true; break;
            case "GamepadVariant": IconGamepad.IsChecked = true; break;
            case "BookOpenPageVariant": IconBook.IsChecked = true; break;
            default: IconFolder.IsChecked = true; break;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        CategoryName = CategoryNameTextBox.Text.Trim();

        if (string.IsNullOrEmpty(CategoryName))
        {
            MessageBox.Show("请输入分类名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CategoryColor = GetSelectedColor();
        CategoryIcon = GetSelectedIcon();

        // 如果传入了 category 对象，直接更新
        if (_category != null)
        {
            _category.Name = CategoryName;
            _category.Color = CategoryColor;
            _category.Icon = CategoryIcon;
        }

        DialogHost.CloseDialogCommand.Execute(this, this);
    }

    private string GetSelectedColor()
    {
        if (ColorPurple.IsChecked == true) return "#7C4DFF";
        if (ColorGreen.IsChecked == true) return "#4CAF50";
        if (ColorBlue.IsChecked == true) return "#2196F3";
        if (ColorOrange.IsChecked == true) return "#FF9800";
        if (ColorRed.IsChecked == true) return "#F44336";
        if (ColorPink.IsChecked == true) return "#E91E63";
        return "#7C4DFF";
    }

    private string GetSelectedIcon()
    {
        if (IconFolder.IsChecked == true) return "FolderOutline";
        if (IconChart.IsChecked == true) return "ChartLine";
        if (IconRobot.IsChecked == true) return "Robot";
        if (IconWrench.IsChecked == true) return "Wrench";
        if (IconCode.IsChecked == true) return "Code";
        if (IconBrain.IsChecked == true) return "Brain";
        if (IconGamepad.IsChecked == true) return "GamepadVariant";
        if (IconBook.IsChecked == true) return "BookOpenPageVariant";
        return "FolderOutline";
    }
}
