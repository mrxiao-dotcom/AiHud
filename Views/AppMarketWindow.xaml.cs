using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AiDaBenYing.Views;

/// <summary>
/// 会员应用市场：内嵌浏览器访问会员应用下载页。
///
/// 设计要点：
///  * 不暴露目标 URL（XAML 里没有任何地址栏 / URL 输入框），
///    底部状态栏只显示"正在加载/已加载"等通用文案。
///  * 禁用右键菜单和 F12 开发者工具，避免用户从浏览器入口看到地址。
///  * 默认导航地址来自配置 -> 环境变量 -> 写死默认，
///    与 LicenseServerUrl 同一套优先级，避免重复硬编码。
/// </summary>
public partial class AppMarketWindow : Window
{
    /// <summary>
    /// 默认市场地址（写死兜底）
    /// </summary>
    public const string DefaultMarketUrl = "http://82.157.32.44:8888/";

    private static readonly string StatusDefault = "已加载";
    private static readonly string StatusLoading = "正在加载…";

    private static readonly string LogFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");

    public AppMarketWindow() : this(ResolveMarketUrl())
    {
    }

    public AppMarketWindow(string url)
    {
        InitializeComponent();

        _pendingUrl = url;
        DataContext = this;

        // 用事件驱动：避免 Source 属性与 EnsureCoreWebView2Async 之间的竞态
        WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        WebView.NavigationStarting += WebView_NavigationStarting;
        WebView.NavigationCompleted += WebView_NavigationCompleted;

        Loaded += AppMarketWindow_Loaded;
    }

    private readonly string _pendingUrl;

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [AppMarket] {msg}\n");
        }
        catch { }
    }

    private async void AppMarketWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= AppMarketWindow_Loaded;
        try
        {
            Log($"Loaded. url='{_pendingUrl}'");

            var userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AiHUD", "WebView2");
            Directory.CreateDirectory(userDataDir);

            // 清掉历史锁文件（崩溃残留），避免 WebView2 启动失败
            ClearLockFiles(userDataDir);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDir,
                options: null);
            Log("CoreWebView2Environment created.");

            await WebView.EnsureCoreWebView2Async(env);
            Log("EnsureCoreWebView2Async done.");
        }
        catch (Exception ex)
        {
            Log("Loaded error: " + ex);
            TxtStatus.Text = "加载失败：" + ex.Message;
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private static void ClearLockFiles(string root)
    {
        try
        {
            foreach (var name in new[] { "lockfile", "SingletonLock", "SingletonCookie" })
            {
                foreach (var f in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }

    private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        try
        {
            if (!e.IsSuccess || WebView.CoreWebView2 == null)
            {
                Log("CoreWebView2 init failed: " + e.InitializationException);
                TxtStatus.Text = "加载失败：" + (e.InitializationException?.Message ?? "WebView2 初始化失败");
                StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
                StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
                return;
            }

            Log("CoreWebView2 initialized.");

            // 安全加固：隐藏开发者工具与右键菜单，避免从入口窥到 URL
            var settings = WebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsBuiltInErrorPageEnabled = true;

            // 监听新窗口请求：默认强制在当前窗口打开，避免浏览器弹外部窗口
            WebView.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (!string.IsNullOrWhiteSpace(args.Uri))
                {
                    try { WebView.CoreWebView2.Navigate(args.Uri); } catch { }
                }
            };

            // CoreWebView2 已就绪，立刻发起导航
            if (!string.IsNullOrWhiteSpace(_pendingUrl))
            {
                WebView.CoreWebView2.Navigate(_pendingUrl);
                Log("Navigate issued: " + _pendingUrl);
            }
        }
        catch (Exception ex)
        {
            Log("CoreWebView2 init handler error: " + ex);
            TxtStatus.Text = "加载失败：" + ex.Message;
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        TxtStatus.Text = StatusLoading;
        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudSyncOutline;
        StatusIcon.Foreground = System.Windows.Media.Brushes.SkyBlue;
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            TxtStatus.Text = StatusDefault;
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudCheckOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
        }
        else
        {
            var msg = e.WebErrorStatus.ToString();
            TxtStatus.Text = "加载失败：" + msg;
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
            Log("Navigation failed: " + msg);
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CanGoBack) WebView.GoBack();
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CanGoForward) WebView.GoForward();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        try { WebView.Reload(); }
        catch { try { WebView.CoreWebView2?.Reload(); } catch { } }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 解析市场地址：配置 > 环境变量 > 默认值（与 LicenseServerUrl 同思路）。
    /// </summary>
    public static string ResolveMarketUrl()
    {
        try
        {
            var configured = System.Configuration.ConfigurationManager.AppSettings["AppMarketUrl"];
            if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/') + "/";
        }
        catch
        {
            // 无 App.config 时忽略
        }

        var env = Environment.GetEnvironmentVariable("AIHUD_APP_MARKET_URL");
        if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/') + "/";

        return DefaultMarketUrl;
    }
}
