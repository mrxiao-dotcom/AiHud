using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using RegisterSrv.ClientSDK.Services;

namespace AiDaBenYing.Views;

/// <summary>
/// 新版本提示与下载对话框。
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
            var serverUrl = ConfigurationToServerUrl();

            // 修复下载失败："An invalid request URI was provided. Either the request URI must
            // be an absolute URI or BaseAddress must be set"。
            // 原因：RegisterSrv 服务端 update 接口有时只返回 path-absolute 路径（如
            // "/downloads/AiDaBenYing_xxx.zip"），以 "/" 开头但没有 scheme；HttpClient
            // 既不视其为可独立访问的绝对 URI 也不会去用任何 BaseAddress 拼接，于是抛错。
            // 处理：若 URL 没有 scheme，就在客户端用 serverUrl 拼成完整绝对 URL 再回写
            // 到 _info.DownloadUrl；同时给 SDK 传一个带 BaseAddress 的 HttpClient 做双保险。
            var downloadUrl = _info.DownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl) ||
                !(Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Scheme)))
            {
                _info.DownloadUrl = serverUrl.TrimEnd('/') + "/" + downloadUrl.TrimStart('/');
                LogUpdate($"DownloadUrl 拼接: '{downloadUrl}' -> '{_info.DownloadUrl}'");
            }
            else
            {
                LogUpdate($"DownloadUrl 已是绝对: '{downloadUrl}'");
            }

            App.Updater ??= new UpdateClient(
                serverUrl,
                App.AppId,
                httpClient: new HttpClient { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/") });
            App.Updater.ProgressChanged += (_, args) =>
                Dispatcher.Invoke(() =>
                {
                    // 更新自定义进度条宽度
                    ProgressBarFill.Width = Math.Max(0, ProgressBarTrack.ActualWidth * args.Progress / 100.0);
                    TxtProgress.Text = args.Message;
                });

            var installPath = AppDomain.CurrentDomain.BaseDirectory;
            var result = await App.Updater.DownloadAndInstallAsync(_info, installPath);

            if (result.IsSuccess)
            {
                MessageBox.Show(
                    "更新已下载完成，关闭程序后将替换文件（请手动重新打开）。",
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

    private static string ConfigurationToServerUrl()
    {
        try
        {
            var url = System.Configuration.ConfigurationManager.AppSettings["LicenseServerUrl"];
            if (!string.IsNullOrWhiteSpace(url)) return url.TrimEnd('/');
        }
        catch
        {
            // 忽略
        }

        return "http://localhost:5232";
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
            var logFile = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");
            System.IO.File.AppendAllText(logFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Update] {msg}\n");
        }
        catch { /* 忽略日志失败 */ }
    }
}
