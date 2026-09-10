using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using AiDaBenYing.Models;
using AiDaBenYing.Services;

namespace AiDaBenYing.Views;

/// <summary>
/// 会员应用市场：内嵌浏览器访问会员应用下载页，并拦截页面里的下载链接，
/// 让客户端下载到 <see cref="SettingsService.Current"/> 的 ResolvedInstallRoot，
/// 自动解压并把主 EXE 注册为新的 AppItem。
///
/// 设计要点：
///  * 不暴露目标 URL（XAML 里没有任何地址栏 / URL 输入框），
///    底部状态栏只显示"正在加载/已加载"等通用文案。
///  * 拦截 WebView2 的 DownloadStarting 事件，把下载重定向给客户端。
///  * 下载/解压/注册过程通过 DownloadProgressDialog 实时反馈。
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
    // 预留：标记是否已拦截过下载，避免重复弹窗
#pragma warning disable CS0414
    private bool _downloadInterceptedOnce;
#pragma warning restore CS0414

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

            var settings = WebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            // 必须开启默认右键菜单，否则有些下载入口会要求"另存为"
            settings.AreDefaultContextMenusEnabled = true;
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

            // 关键：拦截页面发起的下载（用户点击页面下载按钮触发）。
            // 把 Handled 置 true 并把 ResultFilePath 指向 AppInstallRoot，
            // 浏览器自身就会把文件落到我们指定的位置。
            // 但我们更需要"自动解压 + 自动注册"，所以这里把 Handled 置 true 并
            // 取消浏览器内置下载，改为客户端拉取（这样进度、解压、注册都能接管）。
            WebView.CoreWebView2.DownloadStarting += WebView_DownloadStarting;

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

    /// <summary>
    /// 拦截浏览器发起的下载：取消浏览器内置下载，转交客户端的 MarketDownloadService。
    /// </summary>
    private void WebView_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var originalUri = e.DownloadOperation.Uri;
            // 注意：CoreWebView2DownloadOperation.SuggestedFileName 在某些 SDK 版本不存在，
            // 这里直接从 URL 推断一个文件名作为兜底展示。
            string suggestedName = "";
            try
            {
                var u = new Uri(originalUri);
                var seg = u.Segments.Length > 0 ? u.Segments[^1] : "";
                suggestedName = Uri.UnescapeDataString(seg);
            }
            catch { /* 忽略 */ }

            Log($"DownloadStarting: uri={originalUri}, name={suggestedName}");

            // 阻止浏览器自身的下载流程
            e.Handled = true;
            e.Cancel = true;
            _downloadInterceptedOnce = true;

            // 切回 UI 线程弹进度窗
            var dispatcher = Dispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                await HandleClientDownloadAsync(originalUri, suggestedName);
            });
        }
        catch (Exception ex)
        {
            Log("DownloadStarting handler error: " + ex);
            MessageBox.Show("下载拦截失败: " + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 客户端接管下载：弹进度 → 落盘 → 解压 → 注册到 AiHUD。
    /// </summary>
    private async Task HandleClientDownloadAsync(string url, string? suggestedName)
    {
        // 从 URL 或建议文件名提取原始包名，并做清洗（去掉版本号、平台后缀等）
        var rawName = !string.IsNullOrWhiteSpace(suggestedName)
            ? suggestedName
            : Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(rawName)) rawName = "未命名应用";

        // 清洗：去掉 _v1.0、_win64、_x64、_amd64、_20240101 等常见噪音后缀
        var appName = CleanAppName(rawName);

        var req = new MarketDownloadRequest
        {
            AppName = appName,
            DownloadUrl = url,
            RefererUrl = WebView.Source?.ToString()
        };

        var progress = new DownloadProgressDialog(appName)
        {
            Owner = this
        };
        progress.Show();

        var svc = new MarketDownloadService();
        svc.ProgressChanged += (_, args) =>
        {
            progress.UpdateProgress(args.Percent, args.Message);
        };

        try
        {
            var result = await svc.DownloadAndInstallAsync(req);
            progress.Close();

            if (!result.IsSuccess || result.AppItem == null)
            {
                Log("Install fail: " + result.Message);
                MessageBox.Show(
                    "下载或安装失败：\n" + result.Message,
                    "应用市场",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 自动添加到"最新下载"分类
            var category = GetOrCreateLatestDownloadCategory();
            category.AppItems.Add(result.AppItem);
            await SaveDataAsync();

            Log($"Install ok: {result.AppItem.ExecutablePath} -> {category.Name}");
            MessageBox.Show(
                $"应用 \"{result.AppItem.Name}\" 已下载并自动安装到 {result.InstallDir}\n" +
                $"已添加到分类 \"{category.Name}\"（可在左侧查看）。",
                "应用市场",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            try { progress.Close(); } catch { }
            Log("HandleClientDownloadAsync exception: " + ex);
            MessageBox.Show("下载过程异常: " + ex.Message, "应用市场",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 获取或创建"最新下载"分类。始终使用该分类承载下载的应用，不会回退到其他分类。
    /// </summary>
    private Category GetOrCreateLatestDownloadCategory()
    {
        var vm = ((MainWindow)Owner)?.DataContext as ViewModels.MainViewModel;
        if (vm == null)
        {
            // 主窗口拿不到（不应该发生），创建一个临时分类
            return new Category { Name = "最新下载", Icon = "DownloadOutline", Color = "#7C3AED" };
        }

        // 已存在 "最新下载"
        var existing = vm.Categories.FirstOrDefault(c => c.Name == "最新下载");
        if (existing != null) return existing;

        // 新建 "最新下载"
        var cat = new Category
        {
            Name = "最新下载",
            Icon = "DownloadOutline",
            Color = "#7C3AED",
            Order = vm.Categories.Count + 1,
            AppItems = new System.Collections.ObjectModel.ObservableCollection<AppItem>()
        };
        vm.Categories.Add(cat);
        return cat;
    }

    private async Task SaveDataAsync()
    {
        try
        {
            var vm = ((MainWindow)Owner)?.DataContext as ViewModels.MainViewModel;
            if (vm != null)
            {
                await vm.SaveDataAsync();
            }
        }
        catch (Exception ex)
        {
            Log("SaveDataAsync error: " + ex.Message);
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

    /// <summary>
    /// 清洗包名，去掉版本号、平台后缀、日期、UUID 等常见噪音，得到干净的软件名。
    /// 例如：Pot_1.2.3_win64.exe -> Pot
    ///       XXX_v1.0.0_x64 -> XXX
    ///       139fb2da-0c38-4971-a3b4-ad8ad3dc43ab -> 原始文件名
    /// </summary>
    private static string CleanAppName(string raw)
    {
        var name = raw;

        // 如果是纯 UUID（如 139fb2da-0c38-4971-a3b4-ad8ad3dc43ab），直接返回原始文件名（说明没有真实包名）
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            // 尝试从 SourceContext 看看有没有更好的名称，或者直接用"下载的应用"
            return "下载的应用";
        }

        // 去掉常见的版本号模式：_v1.2.3、_1.2.3、-1.2.3、v1.2.3
        name = System.Text.RegularExpressions.Regex.Replace(name,
            @"[-_]?(v?\d+(\.\d+){1,3}([-_]?(x64|win64|win|x86|amd64|arm64|linux|mac|ios|android|setup|portable|beta|alpha|rc))?)$",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 去掉 _x64、_win64、_amd64、_arm64 后缀
        name = System.Text.RegularExpressions.Regex.Replace(name,
            @"[-_](x64|win64|win(x86|32)?|amd64|arm64|x86(32)?|linux|mac|osx|android|ios|web|portable|setup|standalone|full|bin|radius)$",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 去掉日期后缀：_20240101、_2024_01_01、_20240101_win
        name = System.Text.RegularExpressions.Regex.Replace(name,
            @"[-_]?\d{8}(_\d{4})?$", "");

        // 去掉下划线/连字符开头的残余（如 tool__ -> tool）
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[-_]+$", "");

        // 去掉 _beta、_alpha、_rc、_final 后缀
        name = System.Text.RegularExpressions.Regex.Replace(name,
            @"[-_]?(beta|alpha|rc|final|release|stable|update|patch|build)\d*$",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 如果全空则返回原始值
        if (string.IsNullOrWhiteSpace(name)) return raw;

        // 恢复首字母大写
        if (name.Length > 1)
            name = char.ToUpper(name[0]) + name[1..];

        return name;
    }
}
