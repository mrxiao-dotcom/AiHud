using System.Threading;
using System.Threading.Tasks;

namespace AiDaBenYing.Services.Abstractions;

/// <summary>
/// AiHUD 与"许可证 / 注册"子系统交互的统一抽象。
/// 由 Adapters 层实现（当前唯一实现是 RegisterSrv.ClientSDK），
/// 业务代码只依赖此接口，SDK 类型不得泄漏。
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// 向服务器校验一个注册码。
    /// </summary>
    Task<LicenseValidationResult> ValidateAsync(string keyCode, CancellationToken ct = default);

    /// <summary>
    /// 拉取服务器公开的联系信息（试用开关、二维码等）。
    /// </summary>
    Task<LicenseContactInfo?> GetContactInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// 申请试用，返回试用注册码（若有）。
    /// </summary>
    Task<TrialApplyResult> RequestTrialAsync(string contactInfo, string? appVersion, CancellationToken ct = default);

    /// <summary>
    /// 显示注册窗口（弹窗）。返回是否成功以及验证通过的 KeyCode。
    /// </summary>
    RegistrationResult ShowRegistrationWindow(
        string appName,
        string appVersion,
        string? prefillKey = null);
}

/// <summary>
/// 注册窗口返回结果。
/// </summary>
public readonly record struct RegistrationResult(bool IsValid, string? ValidatedLicenseKey)
{
    public static RegistrationResult None { get; } = new(false, null);
}
