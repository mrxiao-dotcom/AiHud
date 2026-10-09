using System;
using System.Collections.Generic;
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
    /// 给"主应用安装目录"用的子目录名生成器（按应用名 + 唯一 ID + 时间戳）。
    ///
    /// 设计原则（防冲突三层）：
    ///   1. 优先尝试使用应用目录名（appName 清洗后）。
    ///   2. 如该目录已存在（同应用已下载过）→ 自动追加版本号/时间戳变成 v2 子目录，
    ///      不再覆盖旧版本。
    ///   3. 如果经过清洗后两个不同 appName 仍碰撞（如 "My App" 与 "My-App"），
    ///      也用版本号/时间戳作唯一化。
    ///
    /// 返回值形如：
    ///   C:\Installed\MyApp\                  ← 首次安装
    ///   C:\Installed\MyApp\MyApp_v2_20261012\ ← 同应用二次安装（多版本共存）
    /// </summary>
    public static string ResolveAppInstallDir(string appName, string? versionHint = null)
    {
        var root = EnsureInstallRootExists();
        var safe = SanitizeFolderName(appName);
        if (string.IsNullOrWhiteSpace(safe)) safe = "App";

        // 1) 首次尝试：使用应用名同名子目录（最直观、便于用户查找）
        var primaryDir = Path.Combine(root, safe);
        if (!Directory.Exists(primaryDir))
        {
            Directory.CreateDirectory(primaryDir);
            return primaryDir;
        }

        // 2) 同名目录已存在 → 检查里面的 marker，确认是否属于"同一应用"
        //    marker 写在 primaryDir\install.json 里，包含 DisplayName / AppName / 安装时间 / exe 路径。
        //    优先按 DisplayName 判断（用户视角的"同一个应用"），
        //    DisplayName 为空时回退到 AppName。
        //    不匹配（例如另一个被清洗后撞名的 app），或目录里已经有 .exe 在跑，
        //    就走第 3 步分版本。
        var marker = TryReadInstallMarker(primaryDir);
        bool sameApp = marker != null
            && marker.IsValid
            && NamesMatch(marker, appName);

        if (sameApp)
        {
            // 升级场景：直接返回主目录，由下载服务覆盖内容（不生成 v2 子目录）
            return primaryDir;
        }

        // 3) 撞名（不同 appName 清洗后相同）→ 直接用时间戳目录
        var fallback = Path.Combine(root,
            safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>
    /// 把"刚安装完成的目录"标记为有效应用目录，写入 install.json。
    /// 由 MarketDownloadService 在下载+解压成功后调用。
    ///
    /// 参数约定：
    ///   - displayName：用户在市场上一眼看到的名字（=压缩包名），即 AppItem.Name 的来源。
    ///   - appName：压缩包原始文件名清洗后得到的技术名（用于升级匹配、UUID 兜底）。
    /// </summary>
    public static void WriteInstallMarker(string installDir,
        string displayName, string appName, string? version, string mainExePath,
        string? sourceUrl = null, string? packageSha256 = null)
    {
        try
        {
            // 先尝试读取已有的 marker，保留手动设置过的字段
            var existing = TryReadInstallMarker(installDir);

            var marker = new InstallMarker
            {
                DisplayName = displayName,   // UI 展示的名字（中文，比如"批量生图"）
                AppName = appName,           // 技术名（用于匹配、合并、升级判断）
                Version = version ?? "",
                MainExePath = mainExePath,
                InstalledAt = existing?.InstalledAt ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                IsValid = true,
                SourceUrl = sourceUrl ?? existing?.SourceUrl ?? "",
                PackageSha256 = packageSha256 ?? existing?.PackageSha256 ?? ""
            };
            File.WriteAllText(Path.Combine(installDir, "install.json"),
                JsonSerializer.Serialize(marker, JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] WriteInstallMarker 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 把 URL 归一化（去 query / fragment），便于重复下载时匹配。
    /// http://host/path?x=y#z  →  http://host/path
    /// </summary>
    public static string NormalizeSourceUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.GetLeftPart(UriPartial.Query);
        }
        catch
        {
            return (url ?? "").Split('?', '#')[0];
        }
    }

    /// <summary>
    /// 列出某应用下所有已归档版本目录（最新→最旧）。
    /// 用于 UI 展示"已安装 N 个版本"、或升级时清理最老的旧版。
    /// </summary>
    public static List<string> ListVersionedDirs(string appDir)
    {
        var result = new List<string>();
        try
        {
            if (!Directory.Exists(appDir)) return result;
            var parent = Path.GetDirectoryName(appDir) ?? appDir;
            var baseName = Path.GetFileName(appDir);
            foreach (var d in Directory.GetDirectories(parent, baseName + "_v*"))
            {
                result.Add(d);
            }
            // 倒序：版本号最大的在前
            result.Sort((a, b) => string.Compare(b, a, StringComparison.OrdinalIgnoreCase));
        }
        catch { }
        return result;
    }

    internal static InstallMarker? TryReadInstallMarker(string dir)
    {
        try
        {
            var p = Path.Combine(dir, "install.json");
            if (!File.Exists(p)) return null;
            var json = File.ReadAllText(p);
            return JsonSerializer.Deserialize<InstallMarker>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 比较两个名字是否"同一个应用"。
    /// 优先按 DisplayName 比较（旧版本可能没有 DisplayName，回退到 AppName）。
    /// </summary>
    private static bool NamesMatch(InstallMarker marker, string appName)
    {
        var candidates = new[]
        {
            marker.DisplayName,
            marker.AppName,
        };
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)
                && string.Equals(c.Trim(), appName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string ResolveNextVersionString(string? currentVersion)
    {
        // 优先递增 currentVersion 里的数字部分（如 1.0.0 → 1.0.1）
        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                currentVersion, @"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?");
            if (m.Success)
            {
                int major = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
                int minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                int build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                return $"v{major}.{minor}.{build + 1}";
            }
        }
        // 兜底：递增扫描同名目录的最高 v 数
        try
        {
            var parent = Directory.GetParent(SettingsDir)?.FullName ?? string.Empty;
            var appDirs = Directory.GetDirectories(parent); // 兜底不严谨，但极少触发
            int maxN = 0;
            foreach (var d in appDirs)
            {
                var name = Path.GetFileName(d);
                var match = System.Text.RegularExpressions.Regex.Match(
                    name, @"_v(\d+)(?:\.\d+){0,3}_?$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int n) && n > maxN)
                    maxN = n;
            }
            return "v" + (maxN + 1);
        }
        catch
        {
            return "v2";
        }
    }

    private static void WriteVersionArchiveMeta(string archiveDir, InstallMarker oldMarker)
    {
        try
        {
            var meta = new
            {
                ArchivedFrom = oldMarker.MainExePath,
                PreviousAppName = oldMarker.AppName,
                PreviousVersion = oldMarker.Version,
                ArchivedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Note = "新版本已安装到同级父目录，此目录保留为历史版本快照（可手动删除）。"
            };
            File.WriteAllText(Path.Combine(archiveDir, "version_meta.json"),
                JsonSerializer.Serialize(meta, JsonOptions));
        }
        catch { }
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

/// <summary>
/// 安装目录的元数据标记，写在 install.json 里。
/// 用于识别"该子目录是不是某个特定 appName 的有效安装"，
/// 避免同名子目录被其他 appName 误识别为冲突。
/// </summary>
public class InstallMarker
{
    /// <summary>
    /// 用户在 AiHUD 上看到的应用名（=压缩包名，可能是中文，如"批量生图"）。
    /// 优先用这个字段做"已安装应用"的展示。
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 压缩包名清洗后的技术名（用于升级匹配、撞名检测、版本号子目录生成）。
    /// 例：批量生图_v1.0.0_x64.rar → "批量生图"。
    /// </summary>
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("mainExePath")]
    public string MainExePath { get; set; } = "";

    [JsonPropertyName("installedAt")]
    public string InstalledAt { get; set; } = "";

    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// 应用的下载来源 URL（去 query string，留 scheme+host+path 便于匹配）。
    /// 用于"重复下载同包"的快速判定。
    /// </summary>
    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = "";

    /// <summary>
    /// 压缩包完整内容的 SHA256（十六进制小写）。
    /// 用于"重复下载同包"时跳过解压、直接复用安装目录。
    /// </summary>
    [JsonPropertyName("packageSha256")]
    public string PackageSha256 { get; set; } = "";
}
