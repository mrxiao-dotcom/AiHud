using System.Text.Json.Serialization;

namespace AiDaBenYing.Services;

/// <summary>
/// 远程配置 DTO（对应 hub/api/config.json 的字段）。
/// 使用 snake_case 命名策略，与 JSON 文件保持一致。
/// </summary>
public sealed class RemoteConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("notice")]
    public string? Notice { get; set; }

    /// <summary>
    /// 许可证/验证服务器地址。最关键的字段。
    /// </summary>
    [JsonPropertyName("licenseServer")]
    public string? LicenseServer { get; set; }

    /// <summary>
    /// 应用市场下载地址（预留字段，暂未启用）。
    /// </summary>
    [JsonPropertyName("marketServer")]
    public string? MarketServer { get; set; }
}
