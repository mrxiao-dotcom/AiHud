using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AiDaBenYing.Services;

/// <summary>
/// 本地注册码缓存：把通过服务器验证的 KeyCode 写到
/// <c>%LOCALAPPDATA%\AiHUD\license.json</c>，下次启动直接读出来再向服务器
/// 校验，校验通过则不再弹注册窗口。
///
/// 设计要点：
///  * 始终以服务端为准 —— 本地缓存只存 KeyCode，不存"是否有效"标志，
///    避免机器码变更 / 密钥被吊销后还能离线跑。
///  * 原子写入（先 .tmp 再 Replace），防止写一半被强杀导致下次解析失败。
///  * 任何 IO 异常都不向调用方抛出，只写 Debug —— 缓存失败不影响主流程。
/// </summary>
public static class LicenseCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiHUD");

    private static readonly string CachePath = Path.Combine(CacheDir, "license.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 已缓存的注册码信息。
    /// </summary>
    public sealed class CachedLicense
    {
        public string KeyCode { get; set; } = string.Empty;
        public string? LicenseType { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime SavedAt { get; set; }
    }

    /// <summary>
    /// 缓存文件路径（供调试 / UI 显示用）。
    /// </summary>
    public static string CacheFilePath => CachePath;

    /// <summary>
    /// 读取本地缓存。无文件或解析失败返回 null。
    /// </summary>
    public static CachedLicense? Load()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;

            var json = File.ReadAllText(CachePath);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var data = JsonSerializer.Deserialize<CachedLicense>(json, JsonOptions);
            if (data == null || string.IsNullOrWhiteSpace(data.KeyCode)) return null;
            return data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicenseCache] 读取失败: {ex.Message}");
            // 解析失败时把损坏文件备份一下，方便排查，但不抛
            TryBackupCorruptedFile(ex);
            return null;
        }
    }

    /// <summary>
    /// 写入本地缓存（原子写入）。参数都可空，仅 KeyCode 是必填的。
    /// </summary>
    public static void Save(string keyCode, string? licenseType = null, DateTime? expiresAt = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyCode))
                throw new ArgumentException("KeyCode 不能为空", nameof(keyCode));

            Directory.CreateDirectory(CacheDir);

            var data = new CachedLicense
            {
                KeyCode = keyCode.Trim(),
                LicenseType = licenseType,
                ExpiresAt = expiresAt,
                SavedAt = DateTime.Now
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            var tmp = CachePath + ".tmp";
            File.WriteAllText(tmp, json);

            if (File.Exists(CachePath))
            {
                File.Replace(tmp, CachePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, CachePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicenseCache] 写入失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清除本地缓存（用于验证失败或用户主动注销）。
    /// </summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(CachePath)) File.Delete(CachePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicenseCache] 删除失败: {ex.Message}");
        }
    }

    private static void TryBackupCorruptedFile(Exception ex)
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var backup = CachePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(CachePath, backup);
            Debug.WriteLine($"[LicenseCache] 已备份损坏缓存到 {backup}（原异常：{ex.Message}）");
        }
        catch
        {
            // 备份失败也不重要，下次启动再覆盖即可
        }
    }
}
