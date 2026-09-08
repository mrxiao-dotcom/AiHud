using System;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using AiDaBenYing.Services;
using AiDaBenYing.Views;
using RegisterSrv.ClientSDK;
using RegisterSrv.ClientSDK.Services;
// SDK 根命名空间下也有同名 LicenseClient，提供 ValidateLicenseAsync(string)，
// 用 alias 锁定到这一个，避免与 RegisterSrv.ClientSDK.Services.LicenseClient 冲突。
using LicenseClient = RegisterSrv.ClientSDK.LicenseClient;

namespace AiDaBenYing;

/// <summary>
/// 应用入口：负责在主窗口显示前完成 License 验证并触发后台更新检查。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 应用标识，对应 RegisterSrv 中注册的 AppId，用于区分不同客户端。
    /// </summary>
    public const string AppId = "AiHud_184639";

    /// <summary>
    /// 当前应用版本（与 AiHUD.csproj 中的 Version 保持一致）。
    /// </summary>
    public static Version AppVersion { get; } = new Version(1, 0, 0, 0);

    /// <summary>
    /// 是否已通过许可证验证（用于 UI 显示状态）。
    /// </summary>
    public static bool IsLicenseValid { get; internal set; }

    /// <summary>
    /// 后台更新服务（公开以便菜单/按钮手动触发"检查更新"）。
    /// </summary>
    public static UpdateClient? Updater { get; internal set; }

    private static readonly string LogFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");

    /// <summary>
    /// 解析后的 License 服务器地址（启动时确定，后续步骤共用）。
    /// </summary>
    private static string _serverUrl = "http://localhost:5232";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底
        DispatcherUnhandledException += (_, args) =>
        {
            Log($"[UI Unhandled] {args.Exception}");
            MessageBox.Show(
                $"发生未处理异常：{args.Exception.Message}",
                "AI大本营 · 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            // 诊断入口：dotnet bin/.../AiDaBenYing.exe --market-test
            // 跳过 License 弹窗，启动后自动打开应用市场，加载成功立即关闭，回到主界面。
            var isMarketTest = e.Args != null && Array.IndexOf(e.Args, "--market-test") >= 0;

            // 0) 初始化 Neumorphism 主题管理器
            ThemeManager.Initialize();

            // 1) 解析服务器地址（环境变量 > App.config > 默认），并初始化 LicenseManager
            _serverUrl = ConfigurationManager.AppSettings["LicenseServerUrl"]
                         ?? Environment.GetEnvironmentVariable("AIHUD_LICENSE_SERVER")
                         ?? "http://localhost:5232";
            LicenseManager.Initialize(AppId, _serverUrl);

            // 2) 优先用本地缓存的注册码向服务器验证：通过则直接进主窗口，不弹窗
            IsLicenseValid = isMarketTest || await TryValidateCachedLicenseAsync();

            if (!IsLicenseValid && !isMarketTest)
            {
                Log("[Startup] 未通过自动验证，弹出注册窗口供用户手动输入");
                ShowLicenseWindowModal();
            }

            // 3) 主窗口显式启动（已通过 App.xaml 去掉 StartupUri）
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            if (isMarketTest)
            {
                Log("[MarketTest] Auto-opening AppMarketWindow");
                var market = new AppMarketWindow { Owner = mainWindow };

                // 验证成功（页面加载成功）→ 立即关闭验证窗口，回主界面
                // 加 30s 兜底超时，避免页面永远加载不出来卡住自检
                var closed = false;
                void CloseMarket(string reason)
                {
                    if (closed) return;
                    closed = true;
                    Log("[MarketTest] " + reason);
                    Dispatcher.InvokeAsync(() => market.Close());
                }

                market.WebView.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess) CloseMarket("NavigationCompleted success -> close");
                };

                var fallback = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                fallback.Tick += (_, _) =>
                {
                    fallback.Stop();
                    CloseMarket("30s fallback timeout -> close");
                };
                fallback.Start();

                market.Show();
            }

            // 4) 后台异步检查更新（不阻塞 UI）
            if (!isMarketTest)
            {
                _ = CheckUpdateInBackgroundAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"[Startup Fatal] {ex}");
            MessageBox.Show(
                "应用启动失败：" + ex.Message,
                "AI大本营 · 启动错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>
    /// 读取本地缓存的注册码并向服务器验证。本地无缓存 → false；
    /// 服务器校验失败（过期/吊销/机器码不匹配）→ 清缓存并返回 false。
    /// </summary>
    private static async Task<bool> TryValidateCachedLicenseAsync()
    {
        var cached = LicenseCache.Load();
        if (cached == null || string.IsNullOrWhiteSpace(cached.KeyCode))
        {
            Log("[License] 本地无缓存注册码");
            return false;
        }

        var keyPreview = cached.KeyCode.Length > 8
            ? cached.KeyCode.Substring(0, 8) + "..."
            : cached.KeyCode;
        Log($"[License] 读取到本地缓存 KeyCode={keyPreview}");

        try
        {
            // alias 指向 RegisterSrv.ClientSDK.LicenseClient（提供 ValidateLicenseAsync）
            using var client = new LicenseClient(_serverUrl, AppId);
            var result = await client.ValidateLicenseAsync(cached.KeyCode);

            if (result.IsValid)
            {
                Log($"[License] 本地缓存验证通过，到期时间: {result.ExpiresAt:yyyy-MM-dd}");
                return true;
            }

            Log($"[License] 本地缓存验证失败: {result.Message}，清除缓存");
            LicenseCache.Clear();
            return false;
        }
        catch (Exception ex)
        {
            Log($"[License] 验证异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 弹出注册窗口。窗口关闭时若已通过验证，自动把 KeyCode 写入本地缓存，
    /// 下次启动就能免输入直接进主窗口。
    /// </summary>
    private static void ShowLicenseWindowModal()
    {
        try
        {
            // 直接 new LicenseWindow 而非走 LicenseManager，是为了拿到 ValidatedLicenseKey 写本地缓存
            var cachedKey = LicenseCache.Load()?.KeyCode;
            var licenseWindow = new LicenseWindow(AppId, "AI大本营", AppVersion.ToString(), _serverUrl, cachedKey);
            // 注意：OnStartup 中主窗口尚未创建，不要设置 Owner，否则在 async 调度里
            // WPF 会把 LicenseWindow 自己设为 Application.Current.MainWindow，导致
            // Owner == this 触发 InvalidOperationException("无法将 Owner 属性设置为它本身")，
            // 进而 ShowDialog 异常退出、LicenseCache.Save 永不执行。
            licenseWindow.ShowDialog();

            if (licenseWindow.IsLicenseValid && !string.IsNullOrWhiteSpace(licenseWindow.ValidatedLicenseKey))
            {
                LicenseCache.Save(licenseWindow.ValidatedLicenseKey);
                IsLicenseValid = true;
                Log($"[License] 注册成功，已写入本地缓存 ({LicenseCache.CacheFilePath})");
            }
            else
            {
                IsLicenseValid = false;
                Log("[License] 注册窗口关闭，未通过验证");
            }
        }
        catch (Exception ex)
        {
            Log($"[License] 注册窗口异常: {ex.Message}");
            IsLicenseValid = false;
        }
    }

    /// <summary>
    /// 后台检查更新，命中则提示用户。
    /// </summary>
    private static async Task CheckUpdateInBackgroundAsync()
    {
        try
        {
            Updater ??= new UpdateClient(
                baseUrl: ConfigurationManager.AppSettings["LicenseServerUrl"] ?? "http://localhost:5232",
                appId: AppId);

            var result = await Updater.CheckUpdateAsync(AppVersion.ToString());
            if (!result.IsSuccess || !result.HasUpdate || result.UpdateInfo == null)
            {
                Log($"[Update] 无更新或检查失败: success={result.IsSuccess}, has={result.HasUpdate}, msg={result.Message}");
                return;
            }

            // 命中更新 → 调度到 UI 线程弹窗
            Current.Dispatcher.Invoke(() =>
            {
                var win = new UpdateDialog(result.UpdateInfo, AppVersion.ToString())
                {
                    Owner = Current.MainWindow
                };
                win.ShowDialog();
            });
        }
        catch (Exception ex)
        {
            Log($"[Update] 后台检查更新异常: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            File.AppendAllText(LogFile, line + Environment.NewLine);
            System.Diagnostics.Debug.WriteLine(line);
        }
        catch
        {
            // 写日志失败忽略
        }
    }
}
