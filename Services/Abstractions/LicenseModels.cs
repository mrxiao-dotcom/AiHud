using System;

namespace AiDaBenYing.Services.Abstractions;

/// <summary>
/// 验证注册码的结果（与 RegisterSrv.ClientSDK.LicenseValidationResult 字段一致，
/// 但独立命名，避免 SDK 类型穿透到业务层）。
/// </summary>
public sealed class LicenseValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public string? LicenseType { get; init; }
    public int? RemainingDays { get; init; }
    public bool IsExpiringSoon { get; init; }
    public string? CustomerInfo { get; init; }
}

/// <summary>
/// 服务器联系信息：试用开关、二维码 URL 等公开数据。
/// </summary>
public sealed class LicenseContactInfo
{
    public bool TrialEnabled { get; init; }
    public int TrialDays { get; init; }
    public string? ContactHint { get; init; }
    public string? TrialTip { get; init; }
    public bool HasWechatQrCode { get; init; }
    public string? WechatQrCodeUrl { get; init; }
}

/// <summary>
/// 试用申请结果。
/// </summary>
public sealed class TrialApplyResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? LicenseKey { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int? DaysRemaining { get; init; }
    public bool ReusedExisting { get; init; }
    public string? FailureCode { get; init; }
    public string? ContactHint { get; init; }
    public string? TrialTip { get; init; }
    public string? WechatQrCodeUrl { get; init; }
}
