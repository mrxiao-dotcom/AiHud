using System;
using System.Configuration;

namespace AiDaBenYing.Services;

/// <summary>
/// License/Update 服务器地址解析器。
///
/// 解析优先级（与原 App.ResolveLicenseServerUrl 一致，保持行为兼容）：
///   1. 进程环境变量 AIHUD_LICENSE_SERVER   ← RemoteConfigService 写入
///   2. App.config 的 LicenseServerUrl
///   3. 兜底默认值 http://localhost:5232
///
/// 设计原则：
///   * 不持有 WPF/SDK 依赖，可被任意模块调用
///   * 解析结果在启动早期一次性缓存，后续模块（Adapter / App）共用同一份
/// </summary>
public static class ServerUrlResolver
{
    public const string DefaultUrl = "http://localhost:5232";
    public const string EnvVarName = "AIHUD_LICENSE_SERVER";
    public const string AppConfigKey = "LicenseServerUrl";

    private static string? _cached;

    /// <summary>
    /// 当前已解析出的服务器地址。未解析时返回 DefaultUrl。
    /// </summary>
    public static string Current => _cached ?? Resolve();

    /// <summary>
    /// 强制重新解析（一般在启动早期 RemoteConfigService 写完环境变量之后调用）。
    /// </summary>
    public static string Resolve()
    {
        try
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                _cached = fromEnv.TrimEnd('/');
                return _cached;
            }

            var fromConfig = ConfigurationManager.AppSettings[AppConfigKey];
            if (!string.IsNullOrWhiteSpace(fromConfig))
            {
                _cached = fromConfig.TrimEnd('/');
                return _cached;
            }
        }
        catch
        {
            // App.config 缺失时 ConfigurationManager 会抛异常，吞掉走默认
        }
        _cached = DefaultUrl;
        return _cached;
    }

    /// <summary>
    /// 重置缓存（用于单元测试 / 强制刷新）。
    /// </summary>
    public static void Reset() => _cached = null;
}
