using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using AiDaBenYing.Services;
using AiDaBenYing.Services.Abstractions;
using AiDaBenYing.Services.Adapters;
using AiDaBenYing.Views;
using SharpCompress.Archives;
using SharpCompress.Common;

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
    /// 当前应用版本。
    ///
    /// 读取策略（按优先级）：
    ///   1. 程序目录下的 "version.txt" 文件（推荐 —— SDK 更新流程会覆盖此文件）
    ///   2. 反射当前 exe 的 AssemblyVersion（兜底 —— 编译时嵌入，常作为 fallback）
    ///   3. 硬编码兜底 1.0.0.0（仅在以上都失败时使用）
    ///
    /// 为什么改：之前硬编码 1,0,0,0 永远不变 → 即使 SDK 把新版本文件拷上去，
    /// AiHUD 自己看到的版本号也没变 → 服务器 CheckUpdateAsync 永远认为有新版本 → 无限循环。
    /// </summary>
    public static Version AppVersion { get; } = ResolveAppVersion();

    private static Version ResolveAppVersion()
    {
        // 1) 优先读 version.txt（每次启动读，所以 SDK 覆盖完即生效）
        try
        {
            var versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
            if (File.Exists(versionFile))
            {
                var raw = File.ReadAllText(versionFile).Trim();
                // 兼容 "1.0.1" / "1.0.1.0" / "v1.0.1"
                if (!string.IsNullOrEmpty(raw))
                {
                    raw = raw.TrimStart('v', 'V', ' ', '\t', '\r', '\n');
                    if (Version.TryParse(raw, out var v))
                    {
                        return v;
                    }
                }
            }
        }
        catch
        {
            // 读 version.txt 失败时静默回退，不影响启动。
        }

        // 2) 反射读 AssemblyVersion（来自 AiHUD.csproj 的 <AssemblyVersion>）
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var asmVer = asm.GetName().Version;
            if (asmVer != null)
            {
                return asmVer;
            }
        }
        catch
        {
        }

        // 3) 硬编码兜底
        return new Version(1, 0, 0, 0);
    }

    /// <summary>
    /// 是否已通过许可证验证（用于 UI 显示状态）。
    /// </summary>
    public static bool IsLicenseValid { get; internal set; }

    /// <summary>
    /// License 子系统入口（业务代码通过此接口与服务器交互，不接触 SDK）。
    /// </summary>
    public static ILicenseService? License { get; private set; }

    /// <summary>
    /// 更新子系统入口（业务代码通过此接口与服务器交互，不接触 SDK）。
    /// </summary>
    public static IUpdateService? Updater { get; private set; }

    private static readonly string LogFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");

    /// <summary>
    /// 全局统一的 License 服务器地址解析入口。
    /// 解析优先级：远程配置（已写入环境变量）→ App.config → 环境变量 → 默认。
    /// 各 View 应统一调用此方法，避免到处重复解析逻辑。
    /// </summary>
    public static string ResolveLicenseServerUrl() => ServerUrlResolver.Resolve();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 早期拦截：--updater-stage 命令行入口。
        // 当 AiHUD 主进程因为更新需要被中间进程接管时，由 UpdateDialog 触发下载并
        // 启动 AiHUD 自身（带上 --updater-stage <zip> <installPath> <newVersion>）。
        // 这个分支执行解压+覆盖+写 version.txt+启新主程序，期间不弹任何窗口。
        if (e.Args != null && Array.IndexOf(e.Args, "--updater-stage") >= 0)
        {
            await RunUpdaterStageAsync(e.Args);
            return;   // 不进正常 UI 流程
        }

        // 默认静默启动：清空 Debug 输出窗口的噪音日志。
        // 仅当设置了环境变量 AIHUD_VERBOSE=1 (或 --verbose 命令行参数) 时才重新挂回。
        // 注意：Release 模式下编译时所有的 Debug.WriteLine 调用本就已被剔除，这里主要影响 Debug 模式。
        ConfigureDebugOutput();

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

            // 0) 加载系统设置（应用安装根目录等），必须在 ThemeManager.Initialize 之前
            await SettingsService.LoadAsync();
            System.Diagnostics.Debug.WriteLine(
                $"[Startup] AppInstallRoot resolved = {SettingsService.Current.ResolvedInstallRoot}");

            // 0.5) 拉取远程配置（http://www.ujuji.cn/hub/api/config.json），
            //      成功则把 licenseServer 写入进程环境变量，覆盖下面的本地解析。
            //      失败/超时静默回退到本地配置，不阻塞启动。
            await RemoteConfigService.LoadAndApplyAsync();
            if (RemoteConfigService.Current is { } rc && !string.IsNullOrWhiteSpace(rc.LicenseServer))
            {
                Log($"[Startup] 远程配置 v{rc.Version} 生效：LicenseServer={rc.LicenseServer}");
            }
            else
            {
                Log("[Startup] 远程配置不可用，使用本地配置");
            }

            // 1) 初始化 Neumorphism 主题管理器
            ThemeManager.Initialize();

            // 1) 解析服务器地址（远程 > App.config > 环境变量 > 默认）
            //    注意：RemoteConfigService 已把 licenseServer 写入 AIHUD_LICENSE_SERVER 环境变量，
            //    所以 ServerUrlResolver.Resolve() 会自动拿到远程值。
            var serverUrl = ServerUrlResolver.Resolve();

            // 构造业务用 Service（Adapter 内部包 SDK，业务代码不再接触 SDK 类型）
            License = new RegisterSrvLicenseService(serverUrl, AppId);
            Updater = new RegisterSrvUpdateService(serverUrl, AppId);

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
            // 走 Abstractions 接口，不再 using SDK
            var result = await License!.ValidateAsync(cached.KeyCode);

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
            // 走 Abstractions 接口，业务代码不接触 SDK 的 LicenseWindow
            var cachedKey = LicenseCache.Load()?.KeyCode;
            var regResult = License!.ShowRegistrationWindow(
                appName: "AI大本营",
                appVersion: AppVersion.ToString(),
                prefillKey: cachedKey);

            if (regResult.IsValid && !string.IsNullOrWhiteSpace(regResult.ValidatedLicenseKey))
            {
                LicenseCache.Save(regResult.ValidatedLicenseKey);
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
            // Updater 在 OnStartup 已初始化；这里只是双保险
            if (Updater == null) return;

            var result = await Updater.CheckAsync(AppVersion.ToString());
            if (!result.IsSuccess || !result.HasUpdate || result.Info == null)
            {
                Log($"[Update] 无更新或检查失败: success={result.IsSuccess}, has={result.HasUpdate}, msg={result.Message}");
                return;
            }

            // 命中更新 → 调度到 UI 线程弹窗
            Current.Dispatcher.Invoke(() =>
            {
                var win = new UpdateDialog(result.Info, AppVersion.ToString())
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

    /// <summary>
    /// 控制 Debug 输出窗口是否显示噪音日志。
    /// 默认：清空所有 listener → Debug.WriteLine 不再写入 Output 窗口。
    /// 开启详细日志：设置环境变量 AIHUD_VERBOSE=1，或命令行参数 --verbose。
    /// </summary>
    private static void ConfigureDebugOutput()
    {
        try
        {
            // 检查是否启用了详细日志
            var verbose = Environment.GetEnvironmentVariable("AIHUD_VERBOSE");
            if (string.IsNullOrEmpty(verbose))
            {
                var args = Environment.GetCommandLineArgs();
                foreach (var a in args)
                {
                    if (string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase))
                    {
                        verbose = "1";
                        break;
                    }
                }
            }

            // 始终先清掉默认 listener（.NET 9 没有 Debug.Listeners，
            // Trace.Listeners 与 Debug 共享，所以清理它就相当于静默 Debug.WriteLine）
            System.Diagnostics.Trace.Listeners.Clear();

            if (!string.IsNullOrEmpty(verbose) && verbose != "0")
            {
                // 重新挂回一个 DefaultTraceListener（仅在显式 verbose 时）
                System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.DefaultTraceListener());
                System.Diagnostics.Debug.AutoFlush = true;
                System.Diagnostics.Trace.AutoFlush = true;
            }
            // 否则彻底静默 —— 没有 listener，Debug.WriteLine 等于空操作
        }
        catch
        {
            // 配置失败不要影响启动
        }
    }

    /// <summary>
    /// 中间更新进程入口（命令行 --updater-stage）。
    ///
    /// 协议（v2：JSON 文件传参）：
    ///   ai_da_ben_ying.exe --updater-stage "&lt;argsJsonPath&gt;"
    ///
    /// argsJsonPath 是一个 JSON 文件，包含：
    ///   { "zipPath": "...", "installPath": "...", "newVersion": "..." }
    ///
    /// ⚠️ 之前用 3 个独立命令行参数传路径+版本号，在 UseShellExecute=true
    ///    + 路径含空格时被 ShellExecuteExW 误解析成畸形值，导致参数解析失败。
    ///    改用单一参数（JSON 绝对路径）彻底规避命令行解析歧义。
    ///
    /// 执行步骤：
    ///   1) 等待旧 AiHUD 主进程退出（最多 10 秒，超时则继续）
    ///   2) SharpCompress 解压 ZIP 到临时目录（支持 ZIP / RAR5 双格式，与 MarketDownloadService 一致）
    ///   3) 把解压结果（去掉顶层单一子目录如 publish/）复制到 installPath
    ///      - 旧文件 Move 到 .bak
    ///      - 占用导致 Move 失败时跳过，不影响其他文件
    ///   4) 写 installPath\version.txt = newVersion
    ///   5) 启动 installPath\AiDaBenYing.exe 新进程
    ///   6) Shutdown(0) 退出当前中间进程
    ///
    /// 此分支不弹任何窗口（无 License 校验、无 Theme、无主窗口）。
    /// </summary>
    private async Task RunUpdaterStageAsync(string[] args)
    {
        int exitCode = 0;
        try
        {
            Log($"[UpdaterStage] 启动中间进程，args.Length={args.Length}, args=[{string.Join(", ", args)}]");

            // 1) 解析参数（v2 协议：--updater-stage <argsJsonPath>）
            string? argsJsonPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--updater-stage", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        argsJsonPath = args[i + 1];
                    }
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(argsJsonPath))
            {
                Log("[UpdaterStage] 缺少 JSON 参数路径，退出");
                exitCode = 2;
                return;
            }
            Log($"[UpdaterStage] JSON 参数路径 = {argsJsonPath}");
            if (!File.Exists(argsJsonPath))
            {
                Log($"[UpdaterStage] 找不到参数 JSON: {argsJsonPath}");
                exitCode = 3;
                return;
            }

            // 2) 解析 JSON（开启大小写不敏感，兼容 Adapter 端用匿名类型的小写驼峰序列化）
            StageArgs stageArgs;
            try
            {
                var json = File.ReadAllText(argsJsonPath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                stageArgs = JsonSerializer.Deserialize<StageArgs>(json, opts) ?? new StageArgs();
                Log($"[UpdaterStage] JSON 内容: zip={stageArgs.ZipPath}, installPath={stageArgs.InstallPath}, newVersion={stageArgs.NewVersion}");
            }
            catch (Exception ex)
            {
                Log($"[UpdaterStage] JSON 解析失败: {ex.Message}");
                exitCode = 7;
                return;
            }

            string zipPath = stageArgs.ZipPath ?? "";
            string installPath = stageArgs.InstallPath ?? "";
            string newVersion = stageArgs.NewVersion ?? "";

            if (string.IsNullOrWhiteSpace(zipPath) || string.IsNullOrWhiteSpace(installPath))
            {
                Log($"[UpdaterStage] JSON 中 zipPath/installPath 为空，退出");
                exitCode = 2;
                return;
            }
            if (!File.Exists(zipPath))
            {
                Log($"[UpdaterStage] 找不到更新包: {zipPath}");
                exitCode = 3;
                return;
            }

            installPath = Path.GetFullPath(installPath);
            Log($"[UpdaterStage] zip={zipPath}, installPath={installPath}, newVersion={newVersion}");

            // 2) 等旧主进程退出 + 文件锁真正释放（最多 30 秒，文件锁检测最可靠）
            //    之前只等 Process.Exited，但 PE 文件句柄由 .NET 运行时延迟释放，
            //    导致 Move(oldExe) 失败 → .bak 堆积。
            try
            {
                var currentPid = Environment.ProcessId;
                var mainExePath = Path.Combine(installPath, "AiDaBenYing.exe");
                Log($"[UpdaterStage] 当前中间进程 PID={currentPid}, 待检测 exe={mainExePath}");

                var waitDeadline = DateTime.UtcNow.AddSeconds(30);
                int attempt = 0;
                bool oldProcessExited = false;
                bool fileLockReleased = false;
                while (DateTime.UtcNow < waitDeadline)
                {
                    attempt++;

                    // 2.1) 进程退出检测
                    if (!oldProcessExited)
                    {
                        var others = System.Diagnostics.Process.GetProcessesByName("AiDaBenYing");
                        bool stillAlive = false;
                        foreach (var p in others)
                        {
                            if (p.Id != currentPid && !p.HasExited)
                            {
                                stillAlive = true;
                                try { p.WaitForExit(500); } catch { }
                            }
                            try { p.Dispose(); } catch { }
                        }
                        if (!stillAlive)
                        {
                            oldProcessExited = true;
                            Log($"[UpdaterStage] 旧主进程已退出（第 {attempt} 次探测）");
                        }
                    }

                    // 2.2) 文件锁检测（最可靠 —— 直接 try-catch FileStream 探测）
                    //      如果 exe/dll 还能被独占打开，说明进程句柄没释放完。
                    if (oldProcessExited && !fileLockReleased)
                    {
                        if (IsFileLockReleased(mainExePath))
                        {
                            fileLockReleased = true;
                            Log($"[UpdaterStage] 主 exe 文件锁已释放（第 {attempt} 次探测）");
                            break;
                        }
                        // 顺便探测几个关键 dll，避免 dll 占用导致覆盖失败
                        var dllNames = new[] { "AiDaBenYing.dll", "MaterialDesignThemes.Wpf.dll" };
                        foreach (var dll in dllNames)
                        {
                            var dllPath = Path.Combine(installPath, dll);
                            if (File.Exists(dllPath) && !IsFileLockReleased(dllPath))
                            {
                                // dll 还被锁，继续等
                                break;
                            }
                        }
                        if (fileLockReleased) break;
                    }

                    System.Threading.Thread.Sleep(300);
                }

                if (!oldProcessExited)
                {
                    Log($"[UpdaterStage] 警告：超时仍未等到旧主进程退出，继续尝试（可能导致失败）");
                }
                else if (!fileLockReleased)
                {
                    Log($"[UpdaterStage] 警告：进程已退出但文件锁未释放（30s 超时），Copy 时可能出现 .bak");
                }
            }
            catch (Exception ex)
            {
                Log($"[UpdaterStage] 等待旧进程退出时异常（忽略继续）: {ex.Message}");
            }

            // 3) 解压到临时目录（用 SharpCompress，与 MarketDownloadService 一致：支持 ZIP / RAR5）
            var tempExtract = Path.Combine(Path.GetTempPath(), "AiHudStage_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempExtract);
            try
            {
                using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(zipPath);
                foreach (var entry in archive.Entries.Where(en => !en.IsDirectory))
                {
                    entry.WriteToDirectory(tempExtract, new SharpCompress.Common.ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
                Log($"[UpdaterStage] 解压完成 → {tempExtract}");
            }
            catch (Exception ex)
            {
                Log($"[UpdaterStage] 解压失败: {ex.Message}");
                exitCode = 4;
                return;
            }

            // 3.5) 扁平化：如果顶层只有一个子目录（如 publish/），则用其内容作为复制源
            var copySourceRoot = tempExtract;
            try
            {
                var topLevelDirs = Directory.GetDirectories(tempExtract);
                var topLevelFiles = Directory.GetFiles(tempExtract);
                if (topLevelDirs.Length == 1 && topLevelFiles.Length == 0)
                {
                    copySourceRoot = topLevelDirs[0];
                    Log($"[UpdaterStage] 检测到顶层单一子目录，扁平化 → {copySourceRoot}");
                }
            }
            catch (Exception ex)
            {
                Log($"[UpdaterStage] 扁平化检测失败（忽略）: {ex.Message}");
            }

            // 4) 复制到 installPath（保留 SDK CopyFilesAsync 的 .bak 备份语义）
            try
            {
                CopyWithBackup(copySourceRoot, installPath);
                Log($"[UpdaterStage] 文件复制完成 → {installPath}");
            }
            catch (Exception ex)
            {
                Log($"[UpdaterStage] 文件复制失败: {ex.Message}");
                exitCode = 5;
                return;
            }

            // 5) 写 installPath\version.txt
            try
            {
                File.WriteAllText(Path.Combine(installPath, "version.txt"), newVersion ?? "");
                Log($"[UpdaterStage] 已写入 version.txt = {newVersion}");
            }
            catch (Exception ex)
            {
                Log($"[UpdaterStage] 写 version.txt 失败: {ex.Message}");
                // 版本号写失败不致命，新进程 fallback 到 AssemblyVersion
            }

            // 6) 启动 installPath\AiDaBenYing.exe 新主进程
            try
            {
                var exePath = Path.Combine(installPath, "AiDaBenYing.exe");
                if (File.Exists(exePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    Log($"[UpdaterStage] 已启动新主进程: {exePath}");
                }
                else
                {
                    Log($"[UpdaterStage] 警告: 找不到新主进程 {exePath}，请用户手动打开");
                }
            }
            catch (Exception ex)
            {
                Log($"[UpdaterStage] 启动新主进程失败: {ex.Message}");
                exitCode = 6;
            }
        }
        catch (Exception ex)
        {
            Log($"[UpdaterStage] 整体异常: {ex.Message}");
            exitCode = 1;
        }
        finally
        {
            Log($"[UpdaterStage] 退出，exitCode={exitCode}");
            Shutdown(exitCode);
        }
    }

    /// <summary>
    /// 中间进程 JSON 参数 v2 协议。
    /// 字段命名兼容 camelCase（与 System.Text.Json 默认大小写规则一致）。
    /// </summary>
    private sealed class StageArgs
    {
        public string? ZipPath { get; set; }
        public string? InstallPath { get; set; }
        public string? NewVersion { get; set; }
    }

    /// <summary>
    /// 复制 sourceDir 下所有文件到 targetDir。
    ///
    /// 策略（v2：覆盖 + 重试 + 详细日志）：
    ///   1. 对每个文件，如果目标存在：
    ///      - 先尝试直接 File.Copy(overwrite:true)（最理想，无 .bak 残留）
    ///      - 如果失败（目标文件被锁），再回退到 Move→.bak→Copy（与 SDK 行为一致）
    ///   2. 每个文件最多重试 3 次，每次间隔 200ms（等文件锁慢慢释放）
    ///   3. 详细日志：每次失败/成功的尝试都记录，便于诊断 .bak 堆积
    /// </summary>
    private static void CopyWithBackup(string sourceDir, string targetDir)
    {
        int totalFiles = 0, successCopies = 0, bakBackups = 0, skipped = 0;
        foreach (var srcFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            totalFiles++;
            var rel = Path.GetRelativePath(sourceDir, srcFile);
            var dest = Path.Combine(targetDir, rel);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            if (!File.Exists(dest))
            {
                // 目标不存在 → 直接 Copy（不会冲突）
                try
                {
                    File.Copy(srcFile, dest, overwrite: false);
                    successCopies++;
                    Log($"[UpdaterStage] [COPY] {rel} (新文件)");
                }
                catch (Exception ex)
                {
                    Log($"[UpdaterStage] [FAIL] {rel} (新文件): {ex.Message}");
                    skipped++;
                }
                continue;
            }

            // 目标已存在 → 优先直接 overwrite，不行再 .bak
            bool copied = false;
            for (int retry = 0; retry < 3 && !copied; retry++)
            {
                if (retry > 0)
                {
                    Log($"[UpdaterStage] [RETRY] {rel} 第 {retry + 1} 次");
                    System.Threading.Thread.Sleep(200);
                }

                // 路径 A：直接 File.Copy(overwrite:true) —— 文件句柄释放后能走通就走这条
                try
                {
                    File.Copy(srcFile, dest, overwrite: true);
                    copied = true;
                    successCopies++;
                    Log($"[UpdaterStage] [COPY] {rel} (覆盖成功)");
                    break;
                }
                catch (IOException) { /* 文件被锁，转路径 B */ }
                catch (UnauthorizedAccessException) { /* 权限问题，转路径 B */ }

                // 路径 B：Move → .bak → Copy（与 SDK 一致）
                try
                {
                    var bak = dest + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(dest, bak);
                    bakBackups++;
                    File.Copy(srcFile, dest, overwrite: true);
                    copied = true;
                    successCopies++;
                    Log($"[UpdaterStage] [COPY] {rel} (旧→.bak 后覆盖)");
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[UpdaterStage] [FAIL] {rel} 第 {retry + 1} 次: {ex.Message}");
                }
            }

            if (!copied)
            {
                skipped++;
                Log($"[UpdaterStage] [SKIP] {rel} 3 次重试均失败，跳过");
            }
        }

        Log($"[UpdaterStage] CopyWithBackup 汇总: 总 {totalFiles}, 成功 {successCopies}, .bak {bakBackups}, 跳过 {skipped}");
    }

    /// <summary>
    /// 检测文件锁是否已释放：尝试以 FileShare.None 独占方式打开。
    /// 返回 true 表示可以独占（无锁）；false 表示被占用。
    /// </summary>
    private static bool IsFileLockReleased(string filePath)
    {
        if (!File.Exists(filePath)) return true;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
