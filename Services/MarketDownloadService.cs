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
        try
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.DownloadUrl))
                return MarketInstallResult.Fail("下载链接为空");

            // 1. 在安装根目录下分配子目录
            var installDir = SettingsService.ResolveAppInstallDir(request.AppName);
            Report(5, $"已分配目录: {installDir}");

            // 2. 下载
            string pkgPath = Path.Combine(installDir, "package.bin");
            if (!await DownloadToFileAsync(request.DownloadUrl, pkgPath, request.RefererUrl, ct))
            {
                return MarketInstallResult.Fail("下载失败：详情见日志");
            }
            Report(40, "下载完成");

            // 3. 校验文件格式（ZIP 或 RAR5）
            if (!LooksLikeZipOrRar(pkgPath, out var preview, out var fmt))
            {
                try { File.Delete(pkgPath); } catch { }
                return MarketInstallResult.Fail($"下载的文件不是有效的 ZIP/RAR 压缩包，前 4KB 内容:\n{preview}");
            }
            Report(42, $"识别为 {fmt} 格式");

            // 4. 解压
            string extractDir = Path.Combine(installDir, "app");
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }
            Directory.CreateDirectory(extractDir);

            try
            {
                using var archive = ArchiveFactory.OpenArchive(pkgPath);
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    entry.WriteToDirectory(extractDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
            catch (Exception ex)
            {
                return MarketInstallResult.Fail($"解压失败（文件可能被截断或已损坏）: {ex.Message}");
            }

            try { File.Delete(pkgPath); } catch { }
            Report(75, "解压完成，正在识别主程序…");

            // 5. 找主 EXE
            var mainExe = FindMainExecutable(extractDir);
            if (mainExe == null)
            {
                return MarketInstallResult.Fail("解压完成，但目录里没有找到任何 .exe 主程序。请检查包结构。");
            }
            Report(95, $"主程序: {Path.GetFileName(mainExe)}");

            // 6. 尝试从 EXE 版本信息中提取真实产品名（覆盖 request.AppName）
            var resolvedName = ResolveExeDisplayName(mainExe, request.AppName);
            Report(97, "正在注册…");

            // 7. 构造 AppItem
            var item = new AppItem(resolvedName, mainExe)
            {
                Description = request.Description ?? string.Empty,
                Arguments = string.Empty
            };
            // 图标异步加载，调用方在主线程后续会刷新
            _ = Task.Run(() => item.EnsureIconLoaded());

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
                    // 5%~35% 区间映射到下载进度
                    var pct = (int)(5 + (downloaded * 30.0 / total));
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 200)
                    {
                        Report(pct, $"下载中 {FormatBytes(downloaded)}/{FormatBytes(total)}");
                        lastReport = DateTime.UtcNow;
                    }
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MarketDownload] 下载异常: {ex.Message}");
            return false;
        }
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
    /// 从 EXE 文件的版本信息中提取真实产品名/内部名作为显示名称。
    /// 如果无法提取有效名称或名称为 UUID 格式，则使用 fallback。
    /// </summary>
    private static string ResolveExeDisplayName(string exePath, string fallback)
    {
        try
        {
            var ver = FileVersionInfo.GetVersionInfo(exePath);

            // 优先取 InternalName（通常最干净），其次 ProductName，再其次 FileVersion
            var candidates = new[]
            {
                ver.InternalName,
                ver.ProductName,
                ver.FileVersion,
            };

            foreach (var c in candidates)
            {
                if (!string.IsNullOrWhiteSpace(c))
                {
                    var name = Path.GetFileNameWithoutExtension(c);
                    if (IsGoodDisplayName(name))
                        return name;
                }
            }
        }
        catch { }

        // fallback 本身是 UUID 格式时返回"下载的应用"
        if (!IsGoodDisplayName(fallback))
            return "下载的应用";

        return fallback;
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

    public static MarketInstallResult Ok(AppItem item, string installDir) =>
        new() { IsSuccess = true, Message = "安装成功", AppItem = item, InstallDir = installDir };

    public static MarketInstallResult Fail(string message) =>
        new() { IsSuccess = false, Message = message };
}
