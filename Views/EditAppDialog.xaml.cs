using System;
using System.Windows;
using System.Windows.Controls;
using AiDaBenYing.Models;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace AiDaBenYing.Views;

public partial class EditAppDialog : UserControl
{
    public AppItem? AppItem { get; private set; }

    public EditAppDialog()
    {
        InitializeComponent();
    }

    public void SetAppItem(AppItem appItem)
    {
        AppItem = appItem;
        AppNameTextBox.Text = appItem.Name;
        ExecutablePathTextBox.Text = appItem.ExecutablePath;
        ArgumentsTextBox.Text = appItem.Arguments;
        DescriptionTextBox.Text = appItem.Description;
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            ExecutablePathTextBox.Text = dialog.FileName;

            if (string.IsNullOrEmpty(AppNameTextBox.Text))
            {
                AppNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (AppItem == null) return;

        if (string.IsNullOrWhiteSpace(AppNameTextBox.Text))
        {
            MessageBox.Show("请输入应用名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AppItem.Name = AppNameTextBox.Text.Trim();
        AppItem.ExecutablePath = ExecutablePathTextBox.Text.Trim();
        AppItem.Arguments = ArgumentsTextBox.Text.Trim();
        AppItem.Description = DescriptionTextBox.Text.Trim();

        // 重新加载图标
        AppItem.RefreshIcon();

        DialogHost.CloseDialogCommand.Execute(this, this);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogHost.CloseDialogCommand.Execute(null, this);
    }
}
