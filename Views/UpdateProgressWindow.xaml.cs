using System;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AiDaBenYing.Views;

/// <summary>
/// 中间进程升级提示窗口。
/// 在 RunUpdaterStageAsync 中实例化此窗口，实时展示升级进度、倒计时和日志，
/// 解决"升级后主窗口关闭、很久无反应"的用户困惑问题。
/// </summary>
public partial class UpdateProgressWindow : Window
{
    private readonly StringBuilder _logBuilder = new();
    private readonly DispatcherTimer _countdownTimer;
    private int _countdownSeconds;
    private bool _isCompleted;

    public UpdateProgressWindow()
    {
        InitializeComponent();

        // 初始倒计时 30 秒（每步操作完成后会重设）
        _countdownSeconds = 30;
        TxtCountdown.Text = _countdownSeconds.ToString();

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) =>
        {
            if (_isCompleted) return;
            _countdownSeconds--;
            TxtCountdown.Text = Math.Max(0, _countdownSeconds).ToString();
            if (_countdownSeconds <= 0)
            {
                // 超时后停止倒计时闪烁（避免误导），但不关闭窗口
                _countdownTimer.Stop();
            }
        };
        _countdownTimer.Start();
    }

    /// <summary>
    /// 更新阶段文字（e.g. "正在准备升级…"/"正在等待旧进程退出…"/"正在解压…"/"正在复制文件…"/"升级完成！")
    /// </summary>
    public void SetStage(string stage)
    {
        Dispatcher.Invoke(() => { TxtStage.Text = stage; });
    }

    /// <summary>
    /// 更新版本号文字
    /// </summary>
    public void SetVersion(string version)
    {
        Dispatcher.Invoke(() =>
        {
            TxtVersion.Text = string.IsNullOrWhiteSpace(version) ? "" : $"目标版本 {version}";
        });
    }

    /// <summary>
    /// 更新进度条（percent 0~100）和说明文字
    /// </summary>
    public void SetProgress(int percent, string message)
    {
        Dispatcher.Invoke(() =>
        {
            percent = Math.Max(0, Math.Min(100, percent));
            ProgressFill.Width = Math.Max(0, (ActualWidthSafe() - 56) * percent / 100.0);
            TxtProgress.Text = string.IsNullOrEmpty(message) ? $"进度 {percent}%" : message;
        });
    }

    /// <summary>
    /// 追加一行日志（自动换行）
    /// </summary>
    public void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            if (_logBuilder.Length > 0)
                _logBuilder.AppendLine();
            _logBuilder.Append($"[{DateTime.Now:HH:mm:ss}] {line}");
            TxtLog.Text = _logBuilder.ToString();

            // 自动滚动到底部
            var scroll = TxtLog.Parent as System.Windows.Controls.ScrollViewer
                ?? (TxtLog.Parent as FrameworkElement)?.Parent as System.Windows.Controls.ScrollViewer;
            scroll?.ScrollToEnd();
        });
    }

    /// <summary>
    /// 重设倒计时（每次操作开始时调用）
    /// </summary>
    public void ResetCountdown(int seconds = 30)
    {
        Dispatcher.Invoke(() =>
        {
            _countdownSeconds = seconds;
            TxtCountdown.Text = _countdownSeconds.ToString();
            if (!_countdownTimer.IsEnabled) _countdownTimer.Start();
        });
    }

    /// <summary>
    /// 标记升级完成（停止倒计时，显示"完成"状态）
    /// </summary>
    public void MarkComplete(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _isCompleted = true;
            _countdownTimer.Stop();
            ProgressFill.Width = Math.Max(0, (ActualWidthSafe() - 56)); // 满进度
            TxtProgress.Text = message;
            TxtStage.Text = "升级完成！";
            TxtCountdown.Text = "✓";
            AppendLog("升级完成！新版本即将启动。");
        });
    }

    private double ActualWidthSafe()
    {
        try { return ActualWidth > 0 ? ActualWidth : 500; }
        catch { return 500; }
    }
}
