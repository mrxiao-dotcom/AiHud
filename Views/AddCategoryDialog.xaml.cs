using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace AiHUD.Views;

public partial class AddCategoryDialog : UserControl
{
    public string CategoryName { get; private set; } = string.Empty;
    public string CategoryColor { get; private set; } = "#7C4DFF";
    public string CategoryIcon { get; private set; } = "FolderOutline";

    public AddCategoryDialog()
    {
        InitializeComponent();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        CategoryName = CategoryNameTextBox.Text.Trim();

        if (string.IsNullOrEmpty(CategoryName))
        {
            MessageBox.Show("请输入分类名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CategoryColor = GetSelectedColor();
        CategoryIcon = GetSelectedIcon();

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
