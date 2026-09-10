using System;
using System.IO;
using System.Threading.Tasks;
using AiDaBenYing.Services;
using AiDaBenYing.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiDaBenYing.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _appInstallRoot = string.Empty;

    [ObservableProperty]
    private string _resolvedPreview = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel()
    {
        // 拷贝一份当前设置（避免直接修改静态单例）
        var src = SettingsService.Current;
        _appInstallRoot = src.AppInstallRoot;
        UpdatePreview();
    }

    partial void OnAppInstallRootChanged(string value)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        try
        {
            var preview = new AppSettings { AppInstallRoot = AppInstallRoot }.ResolvedInstallRoot;
            ResolvedPreview = preview;
            StatusMessage = Directory.Exists(preview)
                ? "已就绪（目录存在）"
                : "将自动创建目录";
        }
        catch (Exception ex)
        {
            ResolvedPreview = "(路径无效)";
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Browse()
    {
        try
        {
            // 用 Win32 OpenFolderDialog（.NET 8+），不存在时回退到让用户手填
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择应用安装根目录",
                InitialDirectory = string.IsNullOrWhiteSpace(AppInstallRoot)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : AppInstallRoot
            };
            if (dlg.ShowDialog() == true)
            {
                AppInstallRoot = dlg.FolderName;
            }
        }
        catch
        {
            // 旧版 Windows 没有 OpenFolderDialog 时退化提示
            System.Windows.MessageBox.Show(
                "当前系统不支持目录选择对话框，请手动粘贴路径。",
                "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        AppInstallRoot = string.Empty;
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        try
        {
            var dir = new AppSettings { AppInstallRoot = AppInstallRoot }.ResolvedInstallRoot;
            if (Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            }
            else
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("无法打开目录: " + ex.Message, "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 提交保存。
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        try
        {
            var current = SettingsService.Current;
            current.AppInstallRoot = (AppInstallRoot ?? string.Empty).Trim();
            await SettingsService.SaveAsync();

            // 立即确保目录就绪
            SettingsService.EnsureInstallRootExists();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = "保存失败: " + ex.Message;
            return false;
        }
    }
}
