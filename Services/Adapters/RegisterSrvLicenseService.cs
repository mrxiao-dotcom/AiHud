using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AiDaBenYing.Services.Abstractions;
using RegisterSrv.ClientSDK;
using RegisterSrv.ClientSDK.Services;
using AbstractionsLicenseValidationResult = AiDaBenYing.Services.Abstractions.LicenseValidationResult;
using AbstractionsTrialApplyResult = AiDaBenYing.Services.Abstractions.TrialApplyResult;

namespace AiDaBenYing.Services.Adapters;

/// <summary>
/// ILicenseService 的实现，内部使用 RegisterSrv.ClientSDK.LicenseClient
/// （以及 SDK 自带的 LicenseWindow 弹窗）。
///
/// 此类是 AiHUD 项目中**唯一**可以 using RegisterSrv.ClientSDK 的地方；
/// 其他业务代码一律只引用 Abstractions。
/// </summary>
internal sealed class RegisterSrvLicenseService : ILicenseService
{
    private readonly LicenseClient _client;
    private readonly string _serverUrl;
    private readonly string _appId;

    public RegisterSrvLicenseService(string serverUrl, string appId)
    {
        _serverUrl = serverUrl;
        _appId = appId;
        _client = new LicenseClient(serverUrl, appId);
    }

    public async Task<AbstractionsLicenseValidationResult> ValidateAsync(string keyCode, CancellationToken ct = default)
    {
        var r = await _client.ValidateLicenseAsync(keyCode).ConfigureAwait(false);
        return new AbstractionsLicenseValidationResult
        {
            IsValid = r.IsValid,
            Message = r.Message,
            ExpiresAt = r.ExpiresAt,
            LicenseType = r.LicenseType,
            RemainingDays = r.RemainingDays,
            IsExpiringSoon = r.IsExpiringSoon,
            CustomerInfo = null, // SDK 公开 API 未暴露
        };
    }

    public async Task<LicenseContactInfo?> GetContactInfoAsync(CancellationToken ct = default)
    {
        var c = await _client.GetContactInfoAsync(ct).ConfigureAwait(false);
        if (c == null) return null;
        return new LicenseContactInfo
        {
            TrialEnabled = c.TrialEnabled,
            TrialDays = c.TrialDays,
            ContactHint = c.ContactHint,
            TrialTip = c.TrialTip,
            HasWechatQrCode = c.HasWechatQrCode,
            WechatQrCodeUrl = c.WechatQrCodeUrl,
        };
    }

    public async Task<AbstractionsTrialApplyResult> RequestTrialAsync(
        string contactInfo, string? appVersion, CancellationToken ct = default)
    {
        var r = await _client.RequestTrialAsync(contactInfo, appVersion, ct).ConfigureAwait(false);
        return new AbstractionsTrialApplyResult
        {
            Success = r.Success,
            Message = r.Message,
            LicenseKey = r.LicenseKey,
            ExpiresAt = r.ExpiresAt,
            DaysRemaining = r.DaysRemaining,
            ReusedExisting = r.ReusedExisting,
            FailureCode = r.FailureCode,
            ContactHint = r.ContactHint,
            TrialTip = r.TrialTip,
            WechatQrCodeUrl = r.WechatQrCodeUrl,
        };
    }

    public RegistrationResult ShowRegistrationWindow(
        string appName, string appVersion, string? prefillKey = null)
    {
        // 这里直接 new SDK 的 LicenseWindow 是 Adapter 的职责：
        // 业务代码拿到的是 RegistrationResult（不依赖 WPF/SDK 类型）。
        try
        {
            var win = new LicenseWindow(
                appId: _appId,
                appName: appName,
                appVersion: appVersion,
                serverUrl: _serverUrl,
                prefillLicenseKey: prefillKey);

            // 不在这里设 Owner，由调用方 (App.ShowLicenseWindowModal) 单独处理。
            win.ShowDialog();

            if (win.IsLicenseValid && !string.IsNullOrWhiteSpace(win.ValidatedLicenseKey))
            {
                return new RegistrationResult(true, win.ValidatedLicenseKey);
            }
            return RegistrationResult.None;
        }
        catch
        {
            return RegistrationResult.None;
        }
    }
}
