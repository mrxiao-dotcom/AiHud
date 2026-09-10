using System;

namespace AiDaBenYing.Services.Abstractions;

/// <summary>
/// 应用更新包信息（与 RegisterSrv.ClientSDK.Services.UpdateInfo 字段一致，
/// 由 Adapter 在 CheckAsync 时翻译）。
/// </summary>
public sealed class UpdateInfo
{
    public string Version { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime ReleaseDate { get; init; }
    public long FileSize { get; init; }
    /// <summary>
    /// 已经由 Adapter 解析为"绝对可访问 URL"（带 serverUrl 拼接）。
    /// </summary>
    public string? DownloadUrl { get; init; }
    public bool IsForceUpdate { get; init; }
    public string? FileMD5 { get; init; }
}

/// <summary>
/// CheckAsync 返回结构。
/// </summary>
public sealed class UpdateCheckResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool HasUpdate { get; init; }
    public UpdateInfo? Info { get; init; }

    public static UpdateCheckResult Fail(string msg) =>
        new() { IsSuccess = false, Message = msg };
}

/// <summary>
/// DownloadAndInstallAsync 返回结构。
/// </summary>
public sealed class UpdateInstallResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 下载进度。
/// </summary>
public sealed class UpdateProgress
{
    /// <summary>0-100</summary>
    public int Progress { get; init; }
    public string Message { get; init; } = string.Empty;
}
