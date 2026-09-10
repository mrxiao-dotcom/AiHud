using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AiDaBenYing.Models;

namespace AiDaBenYing.Services;

/// <summary>
/// 系统设置服务：把 <see cref="AppSettings"/> 单独持久化到
/// <c>%LOCALAPPDATA%\AiHUD\settings.json</c>，并对外暴露事件让
/// 其它模块（市场下载、安装路径变更）实时响应。
///
/// 与 DataService 的关系：DataService 同时也会把 AppSettings 写进
/// data.json 的字段里，这里再独立写一份"权威"副本，启动时优先
/// 读取 settings.json，缺失字段再从 data.json 兼容迁移。
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiHUD");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static AppSettings _current = new();

    /// <summary>
    /// 当前生效的设置实例（单例）。
    /// </summary>
    public static AppSettings Current => _current;

    /// <summary>
    /// 设置加载完成后触发（启动时调一次，之后只在 Reload 后触发）。
    /// </summary>
    public static event EventHandler? SettingsChanged;

    /// <summary>
    /// 加载本地设置；文件不存在或损坏时返回默认实例。
    /// </summary>
    public static async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        _current = loaded;
                        EnsureInstallRootExists();
                        return _current;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Load 失败: {ex.Message}");
        }

        _current = new AppSettings();
        EnsureInstallRootExists();
        return _current;
    }

    /// <summary>
    /// 把当前设置写回磁盘（原子：tmp → Replace）。
    /// </summary>
    public static async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(_current, JsonOptions);
            var tmp = SettingsFilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(SettingsFilePath))
            {
                File.Replace(tmp, SettingsFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, SettingsFilePath);
            }
            SettingsChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Save 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 立刻把安装根目录创建出来，避免下游下载时再判断。
    /// </summary>
    public static string EnsureInstallRootExists()
    {
        return _current.ResolvedInstallRoot;
    }

    /// <summary>
    /// 给"主应用安装目录"用的子目录名生成器（按应用名/时间戳）。
    /// </summary>
    public static string ResolveAppInstallDir(string appName)
    {
        var root = EnsureInstallRootExists();
        // 把 appName 中非法字符替换掉，避免 Windows 路径报错
        var safe = SanitizeFolderName(appName);
        var dir = Path.Combine(root, safe);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private static string SanitizeFolderName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "App_" + DateTime.Now.ToString("yyyyMMddHHmmss");
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ')
            {
                chars[i] = '_';
            }
        }
        var name = new string(chars).Trim('.', '_');
        if (string.IsNullOrEmpty(name)) name = "App";
        return name.Length > 60 ? name.Substring(0, 60) : name;
    }
}
