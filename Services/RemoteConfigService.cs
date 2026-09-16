using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiDaBenYing.Services;

/// <summary>
/// 远程配置服务：启动时从固定域名拉取 <c>config.json</c>，
/// 把里面的 licenseServer / marketServer 等字段注入进程环境变量，
/// 让原本直接读 <c>ConfigurationManager.AppSettings</c> / 环境变量的
/// 代码保持不变，远程配置就能透明生效。
///
/// 优先级：
///   远程 config.json（成功且字段非空）
///     ↓ 失败回退
///   已有环境变量 / App.config（保持原状）
/// </summary>
public static class RemoteConfigService
{
    /// <summary>
    /// 远程配置默认 URL。在服务器配置指南.md 里有详细说明。
    /// 改成 https 时记得同步宝塔证书。
    /// </summary>
    public const string DefaultRemoteConfigUrl =
        "http://www.ujuji.cn/hub/api/config.json";

    /// <summary>
    /// App.config 里对应的 key。
    /// </summary>
    public const string AppConfigKey = "RemoteConfigUrl";

    private const string EnvLicenseServer = "AIHUD_LICENSE_SERVER";
    private const string EnvMarketServer   = "AIHUD_MARKET_SERVER";
    private const string EnvConfigApplied  = "AIHUD_REMOTE_CONFIG_APPLIED";
    private const string EnvRemoteConfigUrl = "AIHUD_REMOTE_CONFIG_URL";

    /// <summary>
    /// 当前进程内缓存的最新远程配置（启动后只拉一次）。
    /// </summary>
    public static RemoteConfig? Current { get; private set; }

    /// <summary>
    /// 远程配置 URL 解析（三层优先级）：
    ///   1. 环境变量 AIHUD_REMOTE_CONFIG_URL   ← RemoteConfigService 启动前可设
    ///   2. App.config 的 RemoteConfigUrl
    ///   3. 兜底默认值 DefaultRemoteConfigUrl
    ///
    /// 此属性可在 <see cref="LoadAndApplyAsync"/> 调用前被覆盖（测试场景）。
    /// </summary>
    public static string RemoteConfigUrl => ResolveRemoteConfigUrl();

    private static string ResolveRemoteConfigUrl()
    {
        // 1. 环境变量
        var fromEnv = Environment.GetEnvironmentVariable(EnvRemoteConfigUrl);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.TrimEnd('/');

        // 2. App.config
        try
        {
            var fromConfig = System.Configuration.ConfigurationManager.AppSettings[AppConfigKey];
            if (!string.IsNullOrWhiteSpace(fromConfig))
                return fromConfig.TrimEnd('/');
        }
        catch
        {
            // App.config 缺失时忽略
        }

        // 3. 兜底
        return DefaultRemoteConfigUrl;
    }

    private static readonly HttpClient Http = new()
    {
        // 启动期不能卡太久，5s 兜底超时
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// 启动时调用一次。拉取并应用远程配置。
    /// 任何异常都会被吞掉并返回 false，调用方可以放心忽略返回值。
    /// </summary>
    /// <param name="cancellationToken">可外部取消；通常传 default。</param>
    /// <returns>成功应用远程配置返回 true，否则 false。</returns>
    public static async Task<bool> LoadAndApplyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = RemoteConfigUrl;
            Debug.WriteLine($"[RemoteConfig] GET {url}");

            using var resp = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[RemoteConfig] HTTP {(int)resp.StatusCode}");
                return false;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var cfg = await JsonSerializer.DeserializeAsync<RemoteConfig>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken).ConfigureAwait(false);

            if (cfg is null)
            {
                Debug.WriteLine("[RemoteConfig] 响应为空或解析失败");
                return false;
            }

            Current = cfg;
            ApplyToEnvironment(cfg);

            Debug.WriteLine(
                $"[RemoteConfig] applied v{cfg.Version} licenseServer={cfg.LicenseServer}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteConfig] 加载失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 把 RemoteConfig 的字段写入进程环境变量。
    /// 仅当字段非空时覆盖，保留其它原有变量。
    ///
    /// 重要：RemoteConfig.marketServer 也写入 AIHUD_MARKET_SERVER，
    /// 这样 AppMarketWindow.ResolveMarketUrl() 就能读到远程下发的市场地址。
    /// </summary>
    private static void ApplyToEnvironment(RemoteConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.LicenseServer))
            Environment.SetEnvironmentVariable(EnvLicenseServer, cfg.LicenseServer.Trim());

        // 【新增】远程 config.json 中的 marketServer 字段，通过环境变量透传给 AppMarketWindow
        if (!string.IsNullOrWhiteSpace(cfg.MarketServer))
            Environment.SetEnvironmentVariable(EnvMarketServer, cfg.MarketServer.Trim());

        // 标记本次启动应用过远程配置（便于诊断）
        Environment.SetEnvironmentVariable(EnvConfigApplied, DateTime.UtcNow.ToString("o"));
    }
}
