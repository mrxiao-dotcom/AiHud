using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace AiDaBenYing.Views;

/// <summary>
/// 引导用户安装 Microsoft Edge WebView2 Runtime。
/// 设计目标：把"一行红字"升级为可操作引导。
///
/// IsResolved = true  表示用户在引导窗里成功让检测通过（重试后命中），
/// 主窗口应当继续走 EnsureCoreWebView2Async 流程。
/// </summary>
public partial class WebView2MissingDialog : Window
{
    /// <summary>用户安装并重新检测后，WebView2 已被识别 → 主窗口应继续加载。</summary>
    public bool IsResolved { get; private set; }

    public WebView2MissingDialog()
    {
        InitializeComponent();
    }

    private void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://developer.microsoft.com/zh-cn/microsoft-edge/webview2/",
                UseShellExecute = true
            });
            TxtLog.Text = "已打开微软官方下载页，请下载 \"Evergreen Standalone Installer\" 安装包并双击运行。";
        }
        catch (Exception ex)
        {
            TxtLog.Text = "打开浏览器失败：" + ex.Message;
        }
    }

    private void BtnOpenLocal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Filter = "WebView2 安装包 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择 MicrosoftEdgeWebview2Setup.exe",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;

            // /silent /install：静默安装 Evergreen 运行时
            // Verb = "runas"：以管理员权限运行（安装器需要写 Program Files）
            var psi = new ProcessStartInfo
            {
                FileName = dlg.FileName,
                Arguments = "/silent /install",
                Verb = "runas",
                UseShellExecute = true
            };
            Process.Start(psi);
            TxtLog.Text = "已启动安装器。安装完成后点 ③ 重新检测。";
        }
        catch (Exception ex)
        {
            TxtLog.Text = "启动安装器失败：" + ex.Message;
        }
    }

    private void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        // 反射调主窗口（Owner）上的 TryDetectWebView2RuntimePath
        // 避免在 Views 里反向依赖 AppMarketWindow 的内部细节
        var ownerType = Owner?.GetType();
        if (ownerType == null)
        {
            TxtLog.Text = "未找到主窗口，无法重新检测。";
            return;
        }

        var m = ownerType.GetMethod(
            "TryDetectWebView2RuntimePath",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null)
        {
            TxtLog.Text = "未找到检测方法 TryDetectWebView2RuntimePath。";
            return;
        }

        try
        {
            var path = m.Invoke(null, null) as string;
            if (!string.IsNullOrEmpty(path))
            {
                IsResolved = true;
                DialogResult = true;
                Close();
            }
            else
            {
                TxtLog.Text = "仍未检测到 WebView2 Runtime，请先完成安装，或检查权限。";
            }
        }
        catch (Exception ex)
        {
            TxtLog.Text = "重新检测失败：" + ex.Message;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
