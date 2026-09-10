using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using AiDaBenYing.Services;
using AiDaBenYing.Services.Abstractions;
using Microsoft.Win32;

namespace AiDaBenYing.Views;

/// <summary>
/// 新版本提示与下载对话框。
///
/// 依赖项：
///   * UpdateInfo —— Abstractions 层定义的"业务模型"（Adapter 已经把
///     DownloadUrl 拼成绝对 URL，并把 SDK 模型翻译到这里）。
///   * App.Updater —— IUpdateService，业务代码不接触 SDK。
/// </summary>
public partial class UpdateDialog : Window
{
    private readonly UpdateInfo _info;
    private readonly string _currentVersion;

    public UpdateDialog(UpdateInfo info, string currentVersion)
    {
        InitializeComponent();

        _info = info ?? throw new ArgumentNullException(nameof(info));
        _currentVersion = currentVersion;

        TxtVersionInfo.Text = $"当前版本 v{_currentVersion} → 新版本 v{_info.Version}";
        TxtNewVersion.Text = _info.Version;
        TxtReleaseDate.Text = _info.ReleaseDate.ToString("yyyy-MM-dd HH:mm");
        TxtFileSize.Text = FormatSize(_info.FileSize);
        TxtDescription.Text = string.IsNullOrWhiteSpace(_info.Description)
            ? "暂无更新说明"
            : _info.Description;

        if (_info.IsForceUpdate)
        {
            ForcePanel.Visibility = Visibility.Visible;
            BtnLater.IsEnabled = false;
            BtnLater.Opacity = 0.5;
        }
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_info.DownloadUrl))
        {
            MessageBox.Show("此版本没有提供下载地址，请前往官网手动下载。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnUpdate.IsEnabled = false;
        BtnLater.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        try
        {
            // Adapter 已负责 DownloadUrl 拼接；这里只管订阅进度回调。
            void OnProgress(UpdateProgress p)
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressBarFill.Width = Math.Max(0, ProgressBarTrack.ActualWidth * p.Progress / 100.0);
                    TxtProgress.Text = p.Message;
                });
            }
            App.Updater!.ProgressChanged += OnProgress;
            try
            {
                var installPath = AppDomain.CurrentDomain.BaseDirectory;
                // 两阶段更新：先下载到 staging\，启动中间进程，再 Shutdown(0)。
                // 走这条路避免 SDK 在主进程同步解压覆盖 installPath，导致 exe/dll
                // 被自身占用而失败 → .bak 死循环。
                var result = await App.Updater.DownloadAndPrepareStageAsync(_info, installPath);

                if (result.IsSuccess)
                {
                    // 中间进程已启动，立即关闭当前应用。
                    // 中间进程会等待本进程退出 → 解压覆盖 → 写 version.txt → 启新主进程。
                    Application.Current.Shutdown(0);
                    return;
                }

                // 下载/启动中间进程失败 → 兜底走 SDK 原生流程
                // （虽然几乎必然失败，但给用户一次机会，万一 staging 满了之类的）
                var fallback = await App.Updater.DownloadAndInstallAsync(_info, installPath);
                if (fallback.IsSuccess)
                {
                    var restarted = App.Updater!.RestartAsync();
                    if (restarted)
                    {
                        return;
                    }

                    MessageBox.Show(
                        "更新已下载完成，但自动重启失败，请手动关闭程序后重新打开。",
                        "更新完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show($"更新失败：{result.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    BtnUpdate.IsEnabled = true;
                    BtnLater.IsEnabled = !_info.IsForceUpdate;
                }
            }
            finally
            {
                App.Updater!.ProgressChanged -= OnProgress;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"更新过程中发生异常：{ex.Message}", "异常",
                MessageBoxButton.OK, MessageBoxImage.Error);
            BtnUpdate.IsEnabled = true;
            BtnLater.IsEnabled = !_info.IsForceUpdate;
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnLater_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "未知";
        string[] sizes = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int order = 0;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private static void LogUpdate(string msg)
    {
        try
        {
            var logFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");
            File.AppendAllText(logFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Update] {msg}\n");
        }
        catch { /* 忽略日志失败 */ }
    }
}
