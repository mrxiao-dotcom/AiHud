using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    /// 默认市场地址兜底（当所有配置源都不可用时的最后防线）。
    /// 改为不带 IP 的占位符，实际运行时由配置层提供真实地址。
    /// </summary>
    public const string FallbackMarketUrl = "http://localhost:8888/";

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
        await InitializeAsync();
    }

    /// <summary>
    /// 初始化 WebView2 并加载市场。可重入——"重新检测"按钮会再次调用。
    ///   1) HttpClient 探测目标 URL
    ///   2) 探测 WebView2 Runtime（找不到时弹引导窗）
    ///   3) 创建 CoreWebView2Environment + EnsureCoreWebView2Async
    ///   4) CoreWebView2 初始化完成时由事件驱动导航（避免与 Source 属性的竞态）
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            Log($"Initialize start. url='{_pendingUrl}'");

            // ★ 启动前先用 HttpClient 直连一次目标地址：先做轻量 HTTP 探测，
            //   若目标站点连 TCP/HEAD 都不通，就别浪费用户时间等 WebView2 报 ConnectionAborted。
            await ProbeMarketUrlAsync(_pendingUrl);

            var userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AiHUD", "WebView2");
            Directory.CreateDirectory(userDataDir);

            // 清掉历史锁文件（崩溃残留），避免 WebView2 启动失败
            ClearLockFiles(userDataDir);

            // ★ 提前探测 WebView2 Runtime —— 避免走到 EnsureCoreWebView2Async 才报错，
            //   那时报的是"系统找不到指定的文件"，对用户无意义。
            var runtimePath = TryDetectWebView2RuntimePath();
            if (string.IsNullOrEmpty(runtimePath))
            {
                Log("WebView2 Runtime not detected. Showing install guide dialog.");
                // 给用户一个可操作的引导，而不是冷冰冰的一行字
                var dlg = new WebView2MissingDialog { Owner = this };
                dlg.ShowDialog();
                if (dlg.IsResolved)
                {
                    // 用户点了"重新检测"且这次找到了
                    runtimePath = TryDetectWebView2RuntimePath();
                    if (string.IsNullOrEmpty(runtimePath))
                    {
                        TxtStatus.Text = "WebView2 仍未检测到，请完成安装后重试。";
                        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
                        StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
                        return;
                    }
                }
                else
                {
                    TxtStatus.Text = "加载失败：WebView2 运行时未安装（已打开安装引导）";
                    StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Download;
                    StatusIcon.Foreground = System.Windows.Media.Brushes.Orange;
                    return;
                }
            }
            Log($"WebView2 Runtime: {runtimePath}");

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
            Log("Initialize error: " + ex);
            TxtStatus.Text = "加载失败：" + FriendlyHint(ex);
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    /// <summary>
    /// "重新检测"按钮：用于 WebView2 装好后手动触发重新初始化。
    /// 不会重复弹引导窗——如果还是没装好就静默回到原状态。
    /// </summary>
    private async void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("BtnRetry clicked. Re-running detection + init.");
            var runtimePath = TryDetectWebView2RuntimePath();
            if (string.IsNullOrEmpty(runtimePath))
            {
                TxtStatus.Text = "WebView2 仍未检测到，请先完成安装。";
                return;
            }

            // 重新走 InitializeAsync 的后半段（跳过引导窗）
            TxtStatus.Text = "正在重新加载市场…";
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudSyncOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.SkyBlue;

            var userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AiHUD", "WebView2");
            Directory.CreateDirectory(userDataDir);
            ClearLockFiles(userDataDir);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDir,
                options: null);
            await WebView.EnsureCoreWebView2Async(env);
            Log("BtnRetry: EnsureCoreWebView2Async done.");
        }
        catch (Exception ex)
        {
            Log("BtnRetry error: " + ex);
            TxtStatus.Text = "重试失败：" + FriendlyHint(ex);
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    /// <summary>
    /// 探测 WebView2 Runtime 是否安装以及安装路径。找不到返回 null。
    /// 同时检测 Evergreen (Microsoft Edge) 和 Fixed (用户自定义) 两种部署方式。
    /// </summary>
    private static string? TryDetectWebView2RuntimePath()
    {
        try
        {
            // 1) 注册表查询 Evergreen 版本（标准安装路径）
            //    Edge 实际写的是 location 字段；老版本可能写 path。pv 是版本号字符串（如 "153.0.4234.48"），
            //    不能当路径用。新版 EdgeWebView 会在 <location>\<pv>\msedgewebview2.exe 这种子目录里放 exe。
            //    HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}
            const string evergreenClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            string[] regPaths =
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + evergreenClientGuid,
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + evergreenClientGuid,
            };
            foreach (var sub in regPaths)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(sub);
                    if (key == null) continue;

                    // Edge 现代版本写的是 location（指向 Application 目录）
                    var location = key.GetValue("location") as string;
                    if (ResolveWebView2Exe(location, out var exeFromReg))
                        return Path.GetDirectoryName(exeFromReg);

                    // 老版本可能写 path（可能是完整 exe 路径或目录）
                    var path = key.GetValue("path") as string;
                    if (ResolveWebView2Exe(path, out exeFromReg))
                        return Path.GetDirectoryName(exeFromReg);
                }
                catch { /* HKLM 子键不存在/无权限，继续尝试下一个 */ }
            }

            // 2) 兜底：扫描常见安装目录里的 <version>\msedgewebview2.exe
            //    注意 Application 目录本身没有 exe，必须进版本号子目录。
            string[] roots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "EdgeWebView", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application"),
            };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                // 遍历版本号子目录（如 153.0.4234.48），找到 msedgewebview2.exe
                try
                {
                    foreach (var sub in Directory.GetDirectories(root))
                    {
                        var exe = Path.Combine(sub, "msedgewebview2.exe");
                        if (File.Exists(exe)) return sub; // ★ 返回版本号子目录
                    }
                }
                catch { /* 单个目录扫不动就跳过 */ }
            }

            // 3) 最后：尝试启动 msedgewebview2.exe --version 探测 PATH
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "msedgewebview2.exe",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    p.WaitForExit(2000);
                    if (p.ExitCode == 0 || p.ExitCode == 1) return "PATH-detected";
                }
            }
            catch { }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把注册表读到的"目录"或"完整 exe 路径"归一化，验证里面真的有 msedgewebview2.exe。
    /// EdgeWebView Application 目录下没有 exe，必须进版本号子目录。
    /// </summary>
    private static bool ResolveWebView2Exe(string? raw, out string exePath)
    {
        exePath = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            // raw 可能是完整 exe 路径
            if (File.Exists(raw) && raw.EndsWith("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
            {
                exePath = raw;
                return true;
            }
            // raw 是 Application 目录
            if (Directory.Exists(raw))
            {
                foreach (var sub in Directory.GetDirectories(raw))
                {
                    var exe = Path.Combine(sub, "msedgewebview2.exe");
                    if (File.Exists(exe)) { exePath = exe; return true; }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 启动前用 HttpClient 做一次轻量探测，提前发现"端口通但 HTTP 没响应"这类隐形故障。
    /// 不影响正式加载：探测超时/失败只更新状态栏提示，不抛异常。
    /// </summary>
    private static async Task ProbeMarketUrlAsync(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await probe.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            Log($"Probe HTTP {(int)resp.StatusCode} {resp.StatusCode} for {url}");
        }
        catch (TaskCanceledException)
        {
            Log($"Probe TIMEOUT (6s) for {url}");
        }
        catch (HttpRequestException ex)
        {
            Log($"Probe HTTP error for {url}: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log($"Probe unexpected for {url}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 把常见网络/IO 异常翻译成中文友好提示，让左下角不再显示裸 ConnectionAborted。
    /// </summary>
    private static string FriendlyHint(Exception ex)
    {
        var inner = ex.InnerException ?? ex;
        var msg = inner.Message ?? ex.Message ?? "";

        // ★ Win32 / 文件相关错误优先识别 —— "系统找不到指定的文件" 是 Windows 原生消息，
        //   出现在 IO/Win32 异常里。最常见原因是 WebView2 Runtime 缺失或二进制损坏。
        var lower = msg.ToLowerInvariant();
        if (lower.Contains("系统找不到指定的文件")
            || lower.Contains("系统找不到指定的路径")
            || lower.Contains("cannot find the file")
            || lower.Contains("could not find file")
            || lower.Contains("the system cannot find")
            || lower.Contains("error_file_not_found"))
        {
            // 进一步定位：是不是 WebView2 相关？
            var fullText = (ex.ToString() ?? "").ToLowerInvariant();
            if (fullText.Contains("webview2") || fullText.Contains("msedgewebview2") || fullText.Contains("edge"))
            {
                return "WebView2 运行时缺失或已损坏（请安装或重装 Microsoft Edge WebView2 Runtime）";
            }
            return "系统找不到指定的文件（请检查文件路径是否正确，或重新安装应用）";
        }

        // 常见坑：WebView2 还没装好
        if (lower.Contains("could not load")
            || lower.Contains("webview2"))
        {
            return "WebView2 运行时未安装（请安装 Microsoft Edge WebView2 Runtime）";
        }

        if (ex is HttpRequestException || lower.Contains("connectionaborted")
            || lower.Contains("connection reset"))
        {
            return "服务器无响应或连接被重置（请检查市场服务器 8888 端口是否启动，或联系客服）";
        }

        if (lower.Contains("tls") || lower.Contains("ssl"))
        {
            return "TLS/证书协商失败（请检查服务器证书是否过期）";
        }

        // 截断过长堆栈文本，避免状态栏撑爆
        return msg.Length > 80 ? msg.Substring(0, 80) + "…" : msg;
    }

    /// <summary>
    /// 把 WebView2 报上来的 CoreWebView2WebErrorStatus 枚举翻译成中文，
    /// 让左下角能直观看到"为什么失败"，而不是 ConnectionAborted 这种鬼话。
    /// </summary>
    private static string TranslateWebError(CoreWebView2WebErrorStatus status)
    {
        return status switch
        {
            CoreWebView2WebErrorStatus.ConnectionAborted       => "连接被中止（服务器主动断开或中间网络 RST）",
            CoreWebView2WebErrorStatus.ConnectionReset         => "连接被重置",
            CoreWebView2WebErrorStatus.CannotConnect           => "无法连接（服务器未监听或被防火墙拦截）",
            CoreWebView2WebErrorStatus.Disconnected            => "连接已断开",
            CoreWebView2WebErrorStatus.Timeout                 => "连接超时",
            CoreWebView2WebErrorStatus.HostNameNotResolved     => "域名无法解析（DNS 问题）",
            CoreWebView2WebErrorStatus.OperationCanceled       => "操作已取消",
            CoreWebView2WebErrorStatus.RedirectFailed          => "重定向失败",
            CoreWebView2WebErrorStatus.ServerUnreachable       => "服务器不可达",
            CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse => "HTTP 响应无效",
            CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired => "需要身份验证（401）",
            CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired     => "需要代理身份验证",
            CoreWebView2WebErrorStatus.CertificateExpired      => "证书已过期",
            CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect => "证书域名不匹配",
            CoreWebView2WebErrorStatus.CertificateIsInvalid    => "证书无效",
            CoreWebView2WebErrorStatus.CertificateRevoked      => "证书已被吊销",
            CoreWebView2WebErrorStatus.ClientCertificateContainsErrors => "客户端证书错误",
            _ => status.ToString()
        };
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
            _navRetryCount = 0; // 成功，重置计数
        }
        else
        {
            var msg = TranslateWebError(e.WebErrorStatus);
            TxtStatus.Text = "加载失败：" + msg;
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudOffOutline;
            StatusIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
            Log($"Navigation failed: status={e.WebErrorStatus} ({e.WebErrorStatus.ToString()})");

            // 仅对"瞬时网络错误"做有限次数自动重试，避免用户面对反复卡顿
            if (IsTransientNavError(e.WebErrorStatus) && _navRetryCount < MaxNavRetry)
            {
                _navRetryCount++;
                var n = _navRetryCount;
                Log($"Auto-retry navigation #{n}/{MaxNavRetry} after {n * 2}s ...");
                TxtStatus.Text = $"连接中断，{n * 2}s 后自动重试 ({n}/{MaxNavRetry})…";
                StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudSyncOutline;
                StatusIcon.Foreground = System.Windows.Media.Brushes.Orange;
                _ = RetryNavigateAsync(n * 2);
            }
        }
    }

    private int _navRetryCount;
    private const int MaxNavRetry = 2; // 最多重试 2 次，共 3 次尝试

    private static bool IsTransientNavError(CoreWebView2WebErrorStatus s)
    {
        // 这些错误通常是网络抖动/服务器重启导致，重试大概率能成功
        return s == CoreWebView2WebErrorStatus.ConnectionAborted
            || s == CoreWebView2WebErrorStatus.ConnectionReset
            || s == CoreWebView2WebErrorStatus.Disconnected
            || s == CoreWebView2WebErrorStatus.Timeout
            || s == CoreWebView2WebErrorStatus.CannotConnect
            || s == CoreWebView2WebErrorStatus.HostNameNotResolved
            || s == CoreWebView2WebErrorStatus.Unknown;
    }

    private async Task RetryNavigateAsync(int delaySec)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySec));
            // 优先用 CoreWebView2.Reload（若已初始化），否则用 WebView.Source 触发重新导航
            if (WebView.CoreWebView2 != null)
                WebView.CoreWebView2.Reload();
            else
                WebView.Source = new Uri(_pendingUrl);
        }
        catch (Exception ex)
        {
            Log($"Retry navigate failed: {ex.Message}");
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

            // 诊断：完整列出 URL 段
            Debug.WriteLine($"[MarketUI] === DownloadStarting ===");
            Debug.WriteLine($"[MarketUI] URI = {originalUri}");
            string suggestedName = "";
            try
            {
                var u = new Uri(originalUri);
                var segs = u.Segments;
                Debug.WriteLine($"[MarketUI] Segments.Count = {segs.Length}");
                for (int i = 0; i < segs.Length; i++)
                    Debug.WriteLine($"[MarketUI]   [{i}] = '{segs[i].TrimEnd('/')}' (IsLikelyHash={IsLikelyHash(segs[i].TrimEnd('/'))})");

                // 从后往前遍历，跳过哈希/纯数字段
                for (int i = segs.Length - 2; i >= 0; i--)
                {
                    var candidate = Uri.UnescapeDataString(segs[i].TrimEnd('/'));
                    if (!IsLikelyHash(candidate) && !string.IsNullOrWhiteSpace(candidate))
                    {
                        suggestedName = candidate;
                        break;
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[MarketUI] parse error: {ex.Message}"); }

            Debug.WriteLine($"[MarketUI] suggestedName = '{suggestedName}'");
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
    private async Task HandleClientDownloadAsync(string url, string? urlSuggestedName)
    {
        // 策略优先级：
        // 1. HEAD 请求 Content-Disposition 头（服务器返回的真实文件名）✅ 优先
        // 2. GET Range: bytes=0-0 兜底（很多服务器禁了 HEAD，但支持 Range）
        // 3. URL 路径段解析（兜底，但 UUID/数字 ID 会被跳过）
        // 4. URL 文件名（最后兜底）
        string rawName = urlSuggestedName ?? "";
        string source = "(none)";

        // 1. 先发 HEAD 请求，从 Content-Disposition 头取真实文件名
        var referer = WebView.Source?.ToString();
        try
        {
            var headerName = await FetchFileNameFromHeaderAsync(url, referer, useHead: true);
            if (!string.IsNullOrWhiteSpace(headerName))
            {
                rawName = headerName;
                source = "HEAD";
                Log($"[MarketUI] Picked HEAD name: '{rawName}'");
            }
            else
            {
                Log($"[MarketUI] HEAD returned no Content-Disposition. Trying GET Range...");
            }
        }
        catch (Exception ex)
        {
            Log($"[MarketUI] HEAD threw: {ex.GetType().Name}: {ex.Message}. Trying GET Range...");
        }

        // 2. GET Range 兜底
        if (source == "(none)")
        {
            try
            {
                var headerName = await FetchFileNameFromHeaderAsync(url, referer, useHead: false);
                if (!string.IsNullOrWhiteSpace(headerName))
                {
                    rawName = headerName;
                    source = "GET-Range";
                    Log($"[MarketUI] Picked GET-Range name: '{rawName}'");
                }
                else
                {
                    Log($"[MarketUI] GET-Range returned no Content-Disposition.");
                }
            }
            catch (Exception ex)
            {
                Log($"[MarketUI] GET-Range threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 3. URL 路径解析兜底（urlSuggestedName）
        if (source == "(none)" && !string.IsNullOrWhiteSpace(urlSuggestedName))
        {
            rawName = urlSuggestedName;
            source = "URL-segment";
            Log($"[MarketUI] Picked URL-segment name: '{rawName}'");
        }

        // 4. 最后兜底
        if (source == "(none)")
        {
            try { rawName = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath); } catch { }
            source = "URL-filename";
            if (string.IsNullOrWhiteSpace(rawName)) rawName = "未命名应用";
        }

        Log($"[MarketUI] Final appName source='{source}', rawName='{rawName}'");

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

            // 根据是否为升级场景，决定是否添加到"最新下载"分类
            if (result.IsUpgrade)
            {
                // 升级场景：更新现有 AppItem（如果存在），不添加到最新下载
                Log($"Upgraded app '{result.AppItem.Name}' in place");

                // 尝试更新主窗口中该应用的信息
                try
                {
                    var mainVm = TryGetMainViewModel();
                    if (mainVm != null)
                    {
                        // 在所有分类中查找同名应用并更新其 exe 路径 / 提取的图标
                        // 重要：如果 existingItem.IconPath 已经有值（用户手动换过），
                        // 一定不要覆盖；如果它是 null（新安装还没图标），
                        // 用本次下载保存的 icon.png 补上。
                        string? newIconPath = result.AppItem?.IconPath;
                        foreach (var cat in mainVm.Categories)
                        {
                            foreach (var existingItem in cat.AppItems.Where(
                                a => string.Equals(a.Name, result.AppItem?.Name, StringComparison.OrdinalIgnoreCase)).ToList())
                            {
                                existingItem.ExecutablePath = result.AppItem!.ExecutablePath;
                                if (string.IsNullOrEmpty(existingItem.IconPath) && !string.IsNullOrEmpty(newIconPath))
                                {
                                    existingItem.IconPath = newIconPath;
                                    existingItem.SetIcon(IconExtractor.LoadIconFile(newIconPath));
                                    Log($"[升级补图标] '{existingItem.Name}' 之前无图标，已设置 IconPath={newIconPath}");
                                }
                                else
                                {
                                    Log($"Updated existing app '{existingItem.Name}' in category '{cat.Name}' (IconPath preserved)");
                                }
                            }
                        }
                        await mainVm.SaveDataAsync();
                    }
                }
                catch (Exception updateEx)
                {
                    Log("Update existing app failed (non-fatal): " + updateEx.Message);
                }

                MessageBox.Show(
                    $"应用 \"{result.AppItem.Name}\" 已更新到新版本。",
                    "应用市场",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                // 新下载场景：自动添加到"最新下载"分类。
                // 但要先做一次"去重检查"——避免升级判断漏判时
                // （例如市场端改了压缩包名但其实是同一应用），
                // 同一应用在"最新下载"里出现两个 AppItem、一个带原图标、一个带系统默认。
                var category = GetOrCreateLatestDownloadCategory();
                var existingInCategory = category.AppItems.FirstOrDefault(
                    a => string.Equals(a.Name, result.AppItem!.Name, StringComparison.OrdinalIgnoreCase));
                if (existingInCategory != null)
                {
                    // 已有同名 → 转成"原地升级"：更新 exe 路径、保留/补全 IconPath
                    existingInCategory.ExecutablePath = result.AppItem!.ExecutablePath;
                    if (string.IsNullOrEmpty(existingInCategory.IconPath) && !string.IsNullOrEmpty(result.AppItem.IconPath))
                    {
                        existingInCategory.IconPath = result.AppItem.IconPath;
                        existingInCategory.SetIcon(IconExtractor.LoadIconFile(result.AppItem.IconPath));
                        Log($"[去重+补图标] '{result.AppItem.Name}' 之前无图标，已设置");
                    }
                    Log($"[去重] '{result.AppItem.Name}' 已存在于最新下载，保留原图标仅更新 exe 路径");
                }
                else
                {
                    category.AppItems.Add(result.AppItem);
                    Log($"Added app '{result.AppItem.Name}' to category '{category.Name}'. AppItems.Count={category.AppItems.Count}");
                }
                await SaveDataAsync();

                // 主动把主窗口切到"最新下载"分类，让用户立刻能看到
                try
                {
                    var mainVm = TryGetMainViewModel();
                    if (mainVm != null && mainVm.SelectedCategory != category)
                    {
                        mainVm.SelectedCategory = category;
                        Log($"Switched SelectedCategory to '{category.Name}'");
                    }
                }
                catch (Exception selEx)
                {
                    Log("Set SelectedCategory failed (non-fatal): " + selEx.Message);
                }

                Log($"Install ok: {result.AppItem.ExecutablePath} -> {category.Name}");
                MessageBox.Show(
                    $"应用 \"{result.AppItem.Name}\" 已下载并自动安装到 {result.InstallDir}\n" +
                    $"已添加到分类 \"{category.Name}\"（可在左侧查看）。",
                    "应用市场",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
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
    /// 注意：AppMarketWindow 可能没有 Owner（为了避免关闭时主窗口失焦），
    /// 所以这里走 Application.Current.Windows 来反查主窗口。
    /// </summary>
    private Category GetOrCreateLatestDownloadCategory()
    {
        var vm = TryGetMainViewModel();
        if (vm == null)
        {
            // 主窗口拿不到（不应该发生），创建一个临时分类
            Log("WARN: MainViewModel 拿不到，下载结果无法显示在主界面");
            return new Category { Name = "最新下载", Icon = "DownloadOutline", Color = "#7C3AED" };
        }

        Log($"GetOrCreateLatestDownloadCategory: vm.Categories.Count={vm.Categories.Count}");

        // 已存在 "最新下载"
        var existing = vm.Categories.FirstOrDefault(c => c.Name == "最新下载");
        if (existing != null)
        {
            Log($"Found existing '最新下载' category with {existing.AppItems.Count} apps");
            return existing;
        }

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
        Log($"Created new '最新下载' category. vm.Categories.Count={vm.Categories.Count}");

        // ★ 主动通知 UI：Categories 自身是 ObservableCollection，但保险起见
        //   通过 OnPropertyChanged 强制刷新一次绑定（部分 ItemsControl 在数据竞争时不会自动刷新）
        //   注：MainViewModel.Categories 是 [ObservableProperty] 生成的，但这里是外部修改 Collection 内容，理论上 ObservableCollection.Add 已触发 CollectionChanged
        //   这里打日志方便排查，不重复触发 PropertyChanged
        return cat;
    }

    /// <summary>
    /// 在所有打开的窗口里找 MainWindow，拿到它的 DataContext (MainViewModel)。
    /// 不依赖 Owner：因为我们故意没设 Owner（避免关闭时主窗口失焦）。
    /// </summary>
    private ViewModels.MainViewModel? TryGetMainViewModel()
    {
        // 1) 优先用 Owner（兜底，多数调用路径上还是有 Owner 的）
        try
        {
            if (Owner is MainWindow mw1 && mw1.DataContext is ViewModels.MainViewModel vm1)
                return vm1;
        }
        catch { }

        // 2) 退而求其次：在所有 Application 窗口里查找 MainWindow
        try
        {
            var allWindows = System.Windows.Application.Current?.Windows;
            if (allWindows != null)
            {
                foreach (var win in allWindows)
                {
                    if (win is MainWindow mw && mw.DataContext is ViewModels.MainViewModel vm)
                        return vm;
                }
            }
        }
        catch { }

        return null;
    }

    private async Task SaveDataAsync()
    {
        try
        {
            var vm = TryGetMainViewModel();
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
    /// 解析市场地址。优先级：
    ///   1. 环境变量 AIHUD_MARKET_SERVER   ← RemoteConfigService 写入
    ///   2. App.config 的 AppMarketUrl
    ///   3. 兜底默认值 FallbackMarketUrl
    ///
    /// 这样服务器只需在 config.json 返回 marketServer 字段，客户端自动切换。
    /// </summary>
    public static string ResolveMarketUrl()
    {
        // 1. 环境变量（由 RemoteConfigService 在启动时写入，优先级最高）
        var env = Environment.GetEnvironmentVariable("AIHUD_MARKET_SERVER");
        if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/') + "/";

        // 2. App.config
        try
        {
            var configured = System.Configuration.ConfigurationManager.AppSettings["AppMarketUrl"];
            if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/') + "/";
        }
        catch
        {
            // 无 App.config 时忽略
        }

        return FallbackMarketUrl;
    }

    /// <summary>
    /// 清洗包名，去掉版本号、平台后缀、日期、UUID 等常见噪音，得到干净的软件名。
    /// 例如：Pot_1.2.3_win64.exe -> Pot
    ///       XXX_v1.0.0_x64 -> XXX
    ///       139fb2da-0c38-4971-a3b4-ad8ad3dc43ab -> 原始文件名
    /// </summary>
    private static string CleanAppName(string raw)
    {
        // 统一剥掉文件扩展名（.rar / .zip / .7z 等），
        // 让后面的版本号/平台后缀清洗逻辑不受扩展名干扰。
        var name = Path.GetFileNameWithoutExtension(raw).Trim();
        if (string.IsNullOrWhiteSpace(name)) name = raw;

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

        // 恢复首字母大写（仅 ASCII 字母）
        // 注意：不对中文字符做大写转换（C# 的 ToUpper 对非 ASCII 字符是恒等，
        //       但加上这段更显式，未来如果有人改成 ToUpperInvariant 也不会翻车）。
        if (name.Length > 1 && name[0] < 128)
            name = char.ToUpper(name[0]) + name[1..];

        return name;
    }

    /// <summary>判断字符串是否像哈希值/纯数字 ID（不适合做应用名）。</summary>
    private static bool IsLikelyHash(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        // 纯 UUID（带连字符）
        if (System.Text.RegularExpressions.Regex.IsMatch(s,
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        // 纯数字（长数字串，如 2035602777027130259）
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{6,}$")) return true;
        // 纯 hex 字符串（不带连字符，长度 >= 8）
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[0-9a-f]{8,}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 向下载 URL 发请求，尝试从 Content-Disposition 头取出真实文件名。
    /// 模式 1: HEAD（最干净，零下载）。
    /// 模式 2: GET Range bytes=0-0（兜底，多数服务器支持 Range 但禁了 HEAD）。
    /// </summary>
    private static async Task<string?> FetchFileNameFromHeaderAsync(
        string url, string? referer, bool useHead)
    {
        var method = useHead ? "HEAD" : "GET-Range";
        Log($"[MarketUI] {method} attempt: {url}");
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        using var request = useHead
            ? new HttpRequestMessage(HttpMethod.Head, url)
            : new HttpRequestMessage(HttpMethod.Get, url);
        if (!useHead)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        }
        if (!string.IsNullOrEmpty(referer))
        {
            try { request.Headers.Referrer = new Uri(referer); } catch { }
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Log($"[MarketUI] {method} status={(int)response.StatusCode} {response.StatusCode}");
            foreach (var h in response.Headers)
                Log($"[MarketUI] {method} hdr {h.Key} = {string.Join(", ", h.Value)}");
            if (response.Content != null)
                foreach (var h in response.Content.Headers)
                    Log($"[MarketUI] {method} content-hdr {h.Key} = {string.Join(", ", h.Value)}");

            if (!response.IsSuccessStatusCode) return null;

            if (response.Content.Headers.ContentDisposition != null)
            {
                var cd = response.Content.Headers.ContentDisposition;
                var raw = cd.FileNameStar ?? cd.FileName;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var trimmed = raw.Trim('"', '\'');
                    // 兼容非标准的 URL-encoded filename：
                    // 服务器返回 "filename=%E6%A8%A1%E7%89%B9...rar" 但没用 RFC 5987 的 filename*= 格式，
                    // .NET 不会自动解码，需要手动 UnescapeDataString。
                    string decoded;
                    try { decoded = Uri.UnescapeDataString(trimmed); }
                    catch { decoded = trimmed; }
                    Log($"[MarketUI] {method} extracted filename: '{trimmed}' → decoded: '{decoded}'");
                    return Path.GetFileNameWithoutExtension(decoded);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[MarketUI] {method} failed: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }
}
