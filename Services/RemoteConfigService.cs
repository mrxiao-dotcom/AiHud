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

    private const string EnvLicenseServer = "AIHUD_LICENSE_SERVER";
    private const string EnvMarketServer   = "AIHUD_MARKET_SERVER";
    private const string EnvConfigApplied  = "AIHUD_REMOTE_CONFIG_APPLIED";

    /// <summary>
    /// 当前进程内缓存的最新远程配置（启动后只拉一次）。
    /// </summary>
    public static RemoteConfig? Current { get; private set; }

    /// <summary>
    /// 当前生效的远程配置 URL（可在 main 启动前被覆盖用于测试）。
    /// </summary>
    public static string RemoteConfigUrl { get; set; } = DefaultRemoteConfigUrl;

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
    /// </summary>
    private static void ApplyToEnvironment(RemoteConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.LicenseServer))
            Environment.SetEnvironmentVariable(EnvLicenseServer, cfg.LicenseServer.Trim());

        if (!string.IsNullOrWhiteSpace(cfg.MarketServer))
            Environment.SetEnvironmentVariable(EnvMarketServer, cfg.MarketServer.Trim());

        // 标记本次启动应用过远程配置（便于诊断）
        Environment.SetEnvironmentVariable(EnvConfigApplied, DateTime.UtcNow.ToString("o"));
    }
}
