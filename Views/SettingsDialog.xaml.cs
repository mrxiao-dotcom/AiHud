using System.Windows;
using AiDaBenYing.ViewModels;

namespace AiDaBenYing.Views;

/// <summary>
/// 系统设置对话框。
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsDialog()
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel();
        DataContext = _viewModel;
    }

    public bool IsSaved { get; private set; }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var ok = await _viewModel.SaveAsync();
        if (ok)
        {
            IsSaved = true;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
