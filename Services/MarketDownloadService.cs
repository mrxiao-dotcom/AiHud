using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Diagnostics.FileVersionInfo;
using AiDaBenYing.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace AiDaBenYing.Services;

/// <summary>
/// 会员应用市场下载服务：
///   1. 把网页上的下载链接拦截下来，用客户端 HttpClient 拉到
///      <see cref="SettingsService.Current"/> 的 ResolvedInstallRoot；
///   2. 自动识别并解压 ZIP / RAR5 压缩包；
///   3. 启发式寻找主 EXE，构造 <see cref="AppItem"/> 返回给调用方。
///
/// 主 EXE 启发式（按优先级）：
///   a) 解压根目录下唯一的 .exe；
///   b) 解压根目录下 .exe 中体积最大的；
///   c) 任意一层目录下名字带 "app"、"main"、"launcher"、"client" 的 .exe；
///   d) 兜底：递归找体积最大的 .exe。
/// </summary>
public class MarketDownloadService
{
    private readonly HttpClient _http;

    /// <summary>
    /// 进度事件：百分比 (0~100) + 文本说明。
    /// </summary>
    public event EventHandler<(int Percent, string Message)>? ProgressChanged;

    public MarketDownloadService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>
    /// 下载并安装一个应用。结果里 <see cref="MarketInstallResult.AppItem"/> 是可直接
    /// 写入 DataService 的 Category.AppItems 的实例。
    /// </summary>
    public async Task<MarketInstallResult> DownloadAndInstallAsync(
        MarketDownloadRequest request,
        CancellationToken ct = default)
    {
        // 诊断日志：记录原始请求
        Debug.WriteLine($"[MarketDownload] === ENTRY ===");
        Debug.WriteLine($"[MarketDownload] AppName   = '{request?.AppName}'");
        Debug.WriteLine($"[MarketDownload] URL       = '{request?.DownloadUrl}'");
        Debug.WriteLine($"[MarketDownload] Referer   = '{request?.RefererUrl}'");
        Debug.WriteLine($"[MarketDownload] Desc      = '{request?.Description}'");
        try
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.DownloadUrl))
                return MarketInstallResult.Fail("下载链接为空");

            var normalizedUrl = SettingsService.NormalizeSourceUrl(request.DownloadUrl);
            Debug.WriteLine($"[MarketDownload] normalizedUrl = '{normalizedUrl}'");

            // SHA256 在下载完成后计算；先占位，避免作用域问题（缓存命中分支需要）
            string pkgSha256 = "";

            // 1. 在安装根目录下分配子目录
            var installDir = SettingsService.ResolveAppInstallDir(request.AppName, request.AppName);
            Debug.WriteLine($"[MarketDownload] installDir = '{installDir}'");
            Report(5, $"已分配目录: {installDir}");

            // 检测是否为升级场景：已存在 marker 且来源 URL 匹配
            bool isUpgrade = false;
            var existingMarker = SettingsService.TryReadInstallMarker(installDir);
            if (existingMarker != null && existingMarker.IsValid)
            {
                var normalizedUrlForCompare = SettingsService.NormalizeSourceUrl(request.DownloadUrl);
                if (string.Equals(existingMarker.SourceUrl, normalizedUrlForCompare, StringComparison.OrdinalIgnoreCase))
                {
                    isUpgrade = true;
                    Debug.WriteLine($"[MarketDownload] UPGRADE scenario detected for '{request.AppName}'");
                }
            }

            // 2. 下载到系统临时目录，避免后续清理 installDir 时误删压缩包
            string pkgPath = Path.Combine(
                Path.GetTempPath(),
                $"aihud_pkg_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.bin");
            try
            {
                if (!await DownloadToFileAsync(request.DownloadUrl, pkgPath, request.RefererUrl, ct))
                {
                    try { File.Delete(pkgPath); } catch { }
                    return MarketInstallResult.Fail("下载失败：详情见日志");
                }
                Report(40, "下载完成");

                // 2.5 算 SHA256，用于"重复下载同包"快速跳过
                try
                {
                    pkgSha256 = await ComputeFileSha256Async(pkgPath, ct);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MarketDownload] SHA256 计算失败: {ex.Message}");
                    pkgSha256 = "";
                }
                Debug.WriteLine($"[MarketDownload] package SHA256 = '{pkgSha256}'");

                // 2.6 命中缓存：URL 相同 + SHA256 相同 + 旧 install 还在 → 完全跳过解压
                if (!string.IsNullOrEmpty(pkgSha256))
                {
                    var normUrl = SettingsService.NormalizeSourceUrl(request.DownloadUrl);
                    if (existingMarker != null
                        && existingMarker.IsValid
                        && string.Equals(existingMarker.SourceUrl, normUrl, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existingMarker.PackageSha256, pkgSha256, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(existingMarker.MainExePath)
                        && File.Exists(existingMarker.MainExePath))
                    {
                        Debug.WriteLine($"[MarketDownload] CACHE HIT, skipping extraction.");
                        Report(80, "检测到相同包，跳过解压直接复用");

                        var cachedItem = new AppItem(
                            string.IsNullOrWhiteSpace(existingMarker.DisplayName) ? request.AppName : existingMarker.DisplayName,
                            existingMarker.MainExePath)
                        {
                            Description = request.Description ?? existingMarker.AppName ?? "",
                            Arguments = string.Empty
                        };
                        // 缓存命中不重新加载图标，保持原有图标
                        // 图标已在首次加载时缓存到文件

                        try { File.Delete(pkgPath); } catch { }
                        Report(100, "完成（命中缓存）");
                        return MarketInstallResult.Ok(cachedItem, installDir, isUpgrade: true);
                    }
                }

                // 3. 校验文件格式（ZIP 或 RAR5）
                if (!LooksLikeZipOrRar(pkgPath, out var preview, out var fmt))
                {
                    try { File.Delete(pkgPath); } catch { }
                    return MarketInstallResult.Fail($"下载的文件不是有效的 ZIP/RAR 压缩包，前 4KB 内容:\n{preview}");
                }
                Report(42, $"识别为 {fmt} 格式");

                // 4. 解压：直接放到 installDir，不额外包 app/ 子目录。
                //    升级场景：只清理旧文件，保留 install.json 以便更新 marker
                if (Directory.Exists(installDir))
                {
                    foreach (var sub in Directory.GetDirectories(installDir))
                        Directory.Delete(sub, recursive: true);
                    foreach (var f in Directory.GetFiles(installDir))
                        // 保留 install.json，只更新其内容
                        if (!f.EndsWith("install.json", StringComparison.OrdinalIgnoreCase))
                            try { File.Delete(f); } catch { }
                }
                else
                {
                    Directory.CreateDirectory(installDir);
                }

                try
                {
                    using var archive = ArchiveFactory.OpenArchive(pkgPath);

                    // 收集所有文件路径，算出公共顶级目录前缀
                    var allPaths = archive.Entries
                        .Where(e => !e.IsDirectory && e.Key != null)
                        .Select(e => e.Key!.Replace('\\', '/'))
                        .ToList();

                    // 诊断日志：打印所有 entry
                    Debug.WriteLine($"[MarketDownload] === ARCHIVE ENTRIES ({allPaths.Count}) ===");
                    foreach (var p in allPaths.Take(50))
                        Debug.WriteLine($"[MarketDownload]   entry: {p}");
                    if (allPaths.Count > 50)
                        Debug.WriteLine($"[MarketDownload]   ... and {allPaths.Count - 50} more");

                    if (allPaths.Count == 0)
                        return MarketInstallResult.Fail("压缩包为空，未找到任何文件。");

                    // 如果所有文件都共享同一个顶级目录（如 public/app.exe），
                    // 把这个顶级目录拍平：去掉公共前缀，让 exe 直接出现在 installDir 下。
                    var commonPrefix = allPaths
                        .Aggregate((string?)null, (acc, p) => GetCommonDir(acc, p));
                    Debug.WriteLine($"[MarketDownload] commonPrefix = '{commonPrefix ?? "(null)"}'");

                    // 解压到 installDir
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        var rawPath = entry.Key?.Replace('\\', '/') ?? "";

                        if (!string.IsNullOrEmpty(commonPrefix)
                            && rawPath.StartsWith(commonPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            rawPath = rawPath.Substring(commonPrefix.Length).TrimStart('/');
                        }

                        var dest = Path.Combine(installDir, rawPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? "");

                        var tmpDest = Path.Combine(installDir, Path.GetFileName(rawPath));
                        entry.WriteToDirectory(installDir, new ExtractionOptions
                        {
                            ExtractFullPath = false,
                            Overwrite = true
                        });

                        if (!string.Equals(tmpDest, dest, StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(dest)) File.Delete(dest);
                            if (File.Exists(tmpDest)) File.Move(tmpDest, dest);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return MarketInstallResult.Fail($"解压失败（文件可能被截断或已损坏）: {ex.Message}");
                }
                finally
                {
                    try { File.Delete(pkgPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { File.Delete(pkgPath); } catch { }
                return MarketInstallResult.Fail("下载/安装过程异常: " + ex.Message);
            }

            Report(75, "解压完成，正在识别主程序…");

            // 5. 找主 EXE（直接在 installDir 下找，不用再进 app/ 子目录）
            var mainExe = FindMainExecutable(installDir);
            if (mainExe == null)
            {
                return MarketInstallResult.Fail("解压完成，但目录里没有找到任何 .exe 主程序。请检查包结构。");
            }
            Report(95, $"主程序: {Path.GetFileName(mainExe)}");

            // 6. 决定显示名称（优先用压缩包名，即 request.AppName）。
            //
            // 关键约定：上传到应用市场的压缩包名 = 用户在 AiHUD 里看到的应用名。
            // 例如 批量生图.rar → 显示为 "批量生图"，而不是 exe 内部挖出来的英文名。
            //
            // 但如果压缩包名恰好是 exe 的脏名（UUID / 全是版本号），允许回退到
            // exe 文件版本信息中的 ProductName / InternalName。
            //
            // 调用约定：request.AppName 必须是"用户期望看到的名字"，绝对不能再去
            // exe 里找一个英文名把它盖掉。
            var resolvedName = ResolveDisplayName(request.AppName, mainExe);
            Report(97, $"显示名称: {resolvedName}");

            // 7. 构造 AppItem
            var item = new AppItem(resolvedName, mainExe)
            {
                Description = request.Description ?? string.Empty,
                Arguments = string.Empty
            };
            // 新下载场景：异步加载图标；升级场景：跳过，保持原有图标
            if (!isUpgrade)
            {
                _ = Task.Run(() => item.EnsureIconLoaded());
            }

            // 8. 写 install marker：让该目录被识别为"该应用的合法安装"，
            //    下次同名应用下载时，能正确进入"升级"分支而不是"撞名"。
            //    install marker 里要保留"显示名 + exe 名 + 源 URL + 包哈希"，
            //    后面做"已安装版本列表"和"重复下载检测"都能直接用。
            SettingsService.WriteInstallMarker(
                installDir: installDir,
                displayName: resolvedName,
                appName: request.AppName,
                version: request.AppName, // 这里先用 appName 当 hint，UI 可以读 exe 的版本信息
                mainExePath: mainExe,
                sourceUrl: normalizedUrl,
                packageSha256: pkgSha256);

            Report(100, "安装完成");
            return MarketInstallResult.Ok(item, installDir);
        }
        catch (Exception ex)
        {
            return MarketInstallResult.Fail("下载/安装过程异常: " + ex.Message);
        }
    }

    private async Task<bool> DownloadToFileAsync(string url, string filePath, string? referer, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(referer))
            {
                req.Headers.Referrer = new Uri(referer);
            }

            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                string body = "";
                try
                {
                    body = await response.Content.ReadAsStringAsync();
                    if (body.Length > 1024) body = body.Substring(0, 1024);
                }
                catch { }
                Debug.WriteLine($"[MarketDownload] HTTP {(int)response.StatusCode} {response.ReasonPhrase} body={body}");
                return false;
            }

            long total = response.Content.Headers.ContentLength ?? 0;
            long downloaded = 0;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int read;
            var lastReport = DateTime.UtcNow;
            while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await dst.WriteAsync(buffer, 0, read, ct);
                downloaded += read;
                if (total > 0)
                {
                    var pct = (int)(5 + (downloaded * 30.0 / total));
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 200)
                    {
                        Report(pct, $"下载中 {FormatBytes(downloaded)}/{FormatBytes(total)}");
                        lastReport = DateTime.UtcNow;
                    }
                }
            }
            // 同步落盘，下面的 SHA256 计算必须读到完整文件
            await dst.FlushAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MarketDownload] 下载异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 计算文件的 SHA256（小写 hex）。读大文件足够快：604KB 约几毫秒，几 MB 几十毫秒。
    /// </summary>
    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        await using var fs = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 检测文件是否为 ZIP 或 RAR5 格式。
    /// ZIP:   0x50 0x4B 0x03 0x04
    /// RAR5:  0x52 0x61 0x72 0x21 0x1A 0x07
    /// </summary>
    private static bool LooksLikeZipOrRar(string path, out string preview, out string format)
    {
        preview = string.Empty;
        format = "未知";
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[6];
            var read = fs.Read(head, 0, 6);
            if (read < 4)
            {
                ReadPreview(fs, path, out preview);
                return false;
            }

            // ZIP (PK\x03\x04)
            if (head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
            {
                format = "ZIP";
                return true;
            }

            // RAR5 (Rar!\x1A\x07)
            if (read >= 6 &&
                head[0] == 0x52 &&  // R
                head[1] == 0x61 &&  // a
                head[2] == 0x72 &&  // r
                head[3] == 0x21 &&  // !
                head[4] == 0x1A &&
                head[5] == 0x07)
            {
                format = "RAR5";
                return true;
            }

            ReadPreview(fs, path, out preview);
            return false;
        }
        catch (Exception ex)
        {
            preview = "无法读取文件: " + ex.Message;
            return false;
        }
    }

    private static void ReadPreview(FileStream fs, string path, out string preview)
    {
        preview = string.Empty;
        try
        {
            fs.Position = 0;
            using var sr = new StreamReader(fs, Encoding.UTF8);
            preview = sr.ReadToEnd();
            if (preview.Length > 4096) preview = preview.Substring(0, 4096);
        }
        catch { }
    }

    private static string? FindMainExecutable(string rootDir)
    {
        // a) 根目录唯一 .exe
        var rootExes = Directory.GetFiles(rootDir, "*.exe", SearchOption.TopDirectoryOnly);
        if (rootExes.Length == 1) return rootExes[0];

        // b) 根目录 .exe 取最大
        if (rootExes.Length > 1)
        {
            return rootExes.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        // c) 任意 .exe 按名字排序
        var allExes = Directory.GetFiles(rootDir, "*.exe", SearchOption.AllDirectories);
        if (allExes.Length == 0) return null;

        string[] preferredKeywords = { "app", "main", "launcher", "client", "starter", "run" };
        foreach (var kw in preferredKeywords)
        {
            var hit = allExes.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Contains(kw, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }

        // d) 兜底取最大
        return allExes.OrderByDescending(f => new FileInfo(f).Length).First();
    }

    /// <summary>
    /// 决定要显示给用户的应用名。
    /// 优先级：
    ///   1. <paramref name="archiveDisplayName"/>（压缩包名 = 卖家上传时人工起的中文名）
    ///      —— 若它是有效名（非空、非 UUID、非纯数字），就直接用它，**不再用 exe 名字覆盖**。
    ///   2. 兜底：当压缩包名无效（UUID / 全是版本号），从 exe 版本信息里读 InternalName/ProductName。
    ///   3. 终极兜底：用 exe 文件名去掉扩展名。
    /// </summary>
    /// <remarks>
    /// 为什么不让 exe 版本信息覆盖压缩包名：
    ///   exe 内部写的 InternalName/ProductName 通常是英文（开发者语言），
    ///   但卖家在市场上传时给的中文名是给最终用户看的，UI 上必须用中文名。
    ///   例：批量生图.rar 解出 BatchImageGen.exe，
    ///      exe 内部 ProductName=BatchImageGen（英文），
    ///      我们最终显示 "批量生图"，而不是 "BatchImageGen"。
    /// </remarks>
    private static string ResolveDisplayName(string archiveDisplayName, string exePath)
    {
        // 1. 压缩包名有效就直接用
        if (IsGoodDisplayName(archiveDisplayName))
            return archiveDisplayName.Trim();

        // 2. 兜底：读 exe 版本信息
        try
        {
            var ver = FileVersionInfo.GetVersionInfo(exePath);
            foreach (var c in new[] { ver.InternalName, ver.ProductName, ver.FileVersion })
            {
                if (!string.IsNullOrWhiteSpace(c) && IsGoodDisplayName(c))
                {
                    // 注意：exe 版本信息可能带 ".exe" 扩展名（极少见），剥掉
                    var name = Path.GetFileNameWithoutExtension(c);
                    if (IsGoodDisplayName(name))
                        return name;
                }
            }
        }
        catch { /* 继续兜底 */ }

        // 3. 用 exe 文件名（去扩展名）
        var fallback = Path.GetFileNameWithoutExtension(exePath);
        return IsGoodDisplayName(fallback) ? fallback : "未命名应用";
    }

    private static bool IsGoodDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length <= 2) return false;
        // 排除 UUID 格式
        if (System.Text.RegularExpressions.Regex.IsMatch(name,
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
        // 排除纯数字
        if (name.All(char.IsDigit)) return false;
        return true;
    }

    private void Report(int percent, string msg)
    {
        ProgressChanged?.Invoke(this, (percent, msg));
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    /// <summary>
    /// 计算两个路径的公共目录前缀。
    /// null 表示"第一个路径尚未确定"。
    /// </summary>
    private static string? GetCommonDir(string? acc, string? next)
    {
        if (string.IsNullOrEmpty(acc)) return next;
        if (string.IsNullOrEmpty(next)) return acc;

        var accParts = acc.Split('/');
        var nextParts = next.Split('/');
        var minLen = Math.Min(accParts.Length, nextParts.Length);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < minLen; i++)
        {
            if (string.Equals(accParts[i], nextParts[i], StringComparison.OrdinalIgnoreCase))
                sb.Append(accParts[i]).Append('/');
            else
                break;
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}

public class MarketDownloadRequest
{
    public string AppName { get; set; } = "App";
    public string DownloadUrl { get; set; } = string.Empty;
    public string? RefererUrl { get; set; }
    public string? Description { get; set; }
}

public class MarketInstallResult
{
    public bool IsSuccess { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public AppItem? AppItem { get; private set; }
    public string? InstallDir { get; private set; }

    /// <summary>
    /// 标识是否为升级场景（true）或新下载场景（false）。
    /// </summary>
    public bool IsUpgrade { get; private set; }

    public static MarketInstallResult Ok(AppItem item, string installDir, bool isUpgrade = false) =>
        new() { IsSuccess = true, Message = "安装成功", AppItem = item, InstallDir = installDir, IsUpgrade = isUpgrade };

    public static MarketInstallResult Fail(string message) =>
        new() { IsSuccess = false, Message = message };
}
