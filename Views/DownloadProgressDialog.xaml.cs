using System;
using System.Windows;

namespace AiDaBenYing.Views;

public partial class DownloadProgressDialog : Window
{
    public DownloadProgressDialog(string appName)
    {
        InitializeComponent();
        TxtAppName.Text = string.IsNullOrWhiteSpace(appName) ? "应用" : appName;
    }

    /// <summary>
    /// 由调用方在进度回调里调用。percent 0~100，message 任意说明文字。
    /// </summary>
    public void UpdateProgress(int percent, string message)
    {
        Dispatcher.Invoke(() =>
        {
            percent = Math.Max(0, Math.Min(100, percent));
            // 自定义进度条宽度（按当前 Border.ActualWidth 比例）
            ProgressFill.Width = Math.Max(0, (ActualWidthSafe() - 56) * percent / 100.0);
            TxtProgress.Text = string.IsNullOrEmpty(message) ? $"进度 {percent}%" : message;
        });
    }

    private double ActualWidthSafe()
    {
        try
        {
            // 实际宽度未知时，给个保守值（避免进度条出框）
            return ActualWidth > 0 ? ActualWidth : 520;
        }
        catch
        {
            return 520;
        }
    }
}
