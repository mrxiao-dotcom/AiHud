using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiDaBenYing.Services.Abstractions;
using RegisterSrv.ClientSDK.Services;
using SdkUpdateInfo = RegisterSrv.ClientSDK.Services.UpdateInfo;
using AbstractionsUpdateInfo = AiDaBenYing.Services.Abstractions.UpdateInfo;

namespace AiDaBenYing.Services.Adapters;

/// <summary>
/// IUpdateService 的实现，内部使用 RegisterSrv.ClientSDK.Services.UpdateClient。
///
/// 把以下几件原本散在 UpdateDialog 里的事也搬到这里：
///  1. CheckAsync 时自动把相对 DownloadUrl 拼成绝对 URL（避免
///     "An invalid request URI was provided" 错误）。
///  2. 把 SDK 的 ProgressChanged 事件翻译成 IUpdateService.ProgressChanged 回调。
///  3. 提供两阶段更新（DownloadAndPrepareStageAsync）以绕过 SDK 1.3.1
///     CopyFilesAsync 在 exe/dll 被占用时死循环的问题。
/// </summary>
internal sealed class RegisterSrvUpdateService : IUpdateService
{
    private readonly UpdateClient _client;
    private readonly string _serverUrl;
    private readonly HttpClient _http;

    public event Action<UpdateProgress>? ProgressChanged;

    public RegisterSrvUpdateService(string serverUrl, string appId)
    {
        _serverUrl = serverUrl;
        var httpClient = new HttpClient { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/") };
        httpClient.Timeout = TimeSpan.FromMinutes(10);
        _http = httpClient;
        _client = new UpdateClient(
            baseUrl: serverUrl,
            appId: appId,
            httpClient: httpClient);

        _client.ProgressChanged += (_, args) =>
            ProgressChanged?.Invoke(new UpdateProgress
            {
                Progress = args.Progress,
                Message = args.Message,
            });
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        // SDK 当前签名不支持 CancellationToken；ct 留作未来扩展。
        _ = ct;
        var r = await _client.CheckUpdateAsync(currentVersion).ConfigureAwait(false);
        if (!r.IsSuccess || !r.HasUpdate || r.UpdateInfo == null)
        {
            return new UpdateCheckResult
            {
                IsSuccess = r.IsSuccess,
                Message = r.Message,
                HasUpdate = r.HasUpdate,
                Info = null,
            };
        }

        var sdkInfo = r.UpdateInfo;
        var absoluteUrl = NormalizeDownloadUrl(sdkInfo.DownloadUrl);

        return new UpdateCheckResult
        {
            IsSuccess = true,
            Message = r.Message,
            HasUpdate = true,
            Info = new AbstractionsUpdateInfo
            {
                Version = sdkInfo.Version,
                Title = sdkInfo.Title,
                Description = sdkInfo.Description,
                ReleaseDate = sdkInfo.ReleaseDate,
                FileSize = sdkInfo.FileSize,
                DownloadUrl = absoluteUrl,
                IsForceUpdate = sdkInfo.IsForceUpdate,
                FileMD5 = sdkInfo.FileMD5,
            },
        };
    }

    public async Task<UpdateInstallResult> DownloadAndInstallAsync(
        AbstractionsUpdateInfo info, string installPath, CancellationToken ct = default)
    {
        _ = ct;
        // 重新包成 SDK 类型（DownloadUrl 此时已为绝对 URL，可直接用）。
        var sdkInfo = new SdkUpdateInfo
        {
            Version = info.Version,
            Title = info.Title,
            Description = info.Description,
            ReleaseDate = info.ReleaseDate,
            FileSize = info.FileSize,
            DownloadUrl = info.DownloadUrl,
            IsForceUpdate = info.IsForceUpdate,
            FileMD5 = info.FileMD5,
        };

        var r = await _client.DownloadAndInstallAsync(sdkInfo, installPath).ConfigureAwait(false);
        return new UpdateInstallResult
        {
            IsSuccess = r.IsSuccess,
            Message = r.Message,
        };
    }

    /// <summary>
    /// 调用 SDK 的 RestartApplication 启动一个新进程再退出当前进程。
    /// ⚠️ 顺序必须是"先启新进程再 Environment.Exit"——如果先退就找不到要启动的 exe。
    /// </summary>
    public bool RestartAsync(string? arguments = null)
    {
        try
        {
            // 优先用 Environment.ProcessPath（.NET 6+），fallback 到 MainModule.FileName。
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            }
            if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
            {
                return false;
            }

            // SDK 的 RestartApplication 内部已经做了 Process.Start + Environment.Exit。
            // 这里直接转调，让 SDK 兜底启动逻辑（带 UseShellExecute=true）。
            _client.RestartApplication(exePath, arguments);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 两阶段更新的第一阶段：
    ///   1) 用自己的 HttpClient 把更新包下载到 installPath 旁边的 staging\update.zip
    ///      （避免用 SDK 的 DownloadAndInstallAsync，那会立刻解压覆盖 installPath，
    ///       导致 WPF exe/dll 占用 → 全部失败 → 大量 .bak）。
    ///   2) 启动一个中间进程（A iDaBenYing.exe 自身 + --updater-stage 命令行）。
    ///   3) 中间进程会在当前 AiHUD 主进程退出后接管解压+覆盖+启新主程序。
    ///
    /// IsSuccess=true 时，调用方应立即 Shutdown(0) 让中间进程能继续工作。
    /// </summary>
    public async Task<UpdateInstallResult> DownloadAndPrepareStageAsync(
        AbstractionsUpdateInfo info, string installPath, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(info.DownloadUrl))
                return new UpdateInstallResult { IsSuccess = false, Message = "下载链接为空" };

            // 1) 准备 staging 目录（与 installPath 同级，便于清理）
            var installRoot = Path.GetFullPath(installPath);
            var stagingDir = Path.Combine(Path.GetDirectoryName(installRoot) ?? Path.GetTempPath(), "staging");
            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, recursive: true); } catch { /* 残留清理失败不影响本次 */ }
            }
            Directory.CreateDirectory(stagingDir);

            // 2) 下载到 staging\update.zip
            var zipPath = Path.Combine(stagingDir, "update.zip");
            if (!await DownloadToFileAsync(info.DownloadUrl, zipPath, ct))
            {
                return new UpdateInstallResult { IsSuccess = false, Message = "下载失败：详情见 app_startup.log" };
            }

            // 3) 校验 ZIP magic（前 4 字节）
            try
            {
                using var fs = File.OpenRead(zipPath);
                var head = new byte[4];
                if (fs.Read(head, 0, 4) < 4 ||
                    head[0] != 0x50 || head[1] != 0x4B || head[2] != 0x03 || head[3] != 0x04)
                {
                    return new UpdateInstallResult
                    {
                        IsSuccess = false,
                        Message = "下载的不是有效 ZIP 文件（magic bytes 不匹配）。请检查服务器端配置或 CDN。"
                    };
                }
            }
            catch (Exception ex)
            {
                return new UpdateInstallResult { IsSuccess = false, Message = "校验下载文件失败: " + ex.Message };
            }

            // 4) 把新版本号和 installPath 写入 staging\stage-args.json（中间进程读这个文件）
            //    改用 JSON 文件传递参数，规避命令行参数解析歧义
            //    （之前用 `--updater-stage "<zip>" "<install>" "<ver>"` 在 UseShellExecute=true
            //     + 含空格路径下被 ShellExecuteExW 误解析，导致版本号粘在 installPath 后）
            var argsJsonPath = Path.Combine(stagingDir, "stage-args.json");
            var argsJson = JsonSerializer.Serialize(new
            {
                zipPath = zipPath,
                installPath = installPath,
                newVersion = info.Version ?? ""
            });
            File.WriteAllText(argsJsonPath, argsJson);

            // 5) 启动中间进程（命令行只传 2 个 token：--updater-stage <jsonPath>）
            var exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return new UpdateInstallResult
                {
                    IsSuccess = false,
                    Message = "无法定位 AiDaBenYing.exe 路径，无法启动中间进程。"
                };
            }

            // ⚠️ 关键：Arguments 只有两个 token，且 jsonPath 不含空格（staging 目录是 bin 同级，正常）
            //    为了彻底规避引号解析问题，jsonPath 也用绝对路径无空格校验
            if (argsJsonPath.Contains(' '))
            {
                Log($"[Updater] WARNING: stage-args.json 路径含空格，可能仍触发引号问题: {argsJsonPath}");
            }
            var args = "--updater-stage \"" + argsJsonPath + "\"";

            Log($"[Updater] 启动中间进程，raw Arguments = [{args}]");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,   // 与 SDK RestartApplication 一致，避免权限问题
            };
            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                return new UpdateInstallResult
                {
                    IsSuccess = false,
                    Message = "启动中间更新进程失败: " + ex.Message
                };
            }

            // 6) 成功 → 通知调用方立刻 Shutdown(0)，让中间进程接管。
            return new UpdateInstallResult { IsSuccess = true, Message = "已启动中间更新进程，请等待自动重启。" };
        }
        catch (Exception ex)
        {
            return new UpdateInstallResult { IsSuccess = false, Message = "准备更新阶段异常: " + ex.Message };
        }
    }

    private async Task<bool> DownloadToFileAsync(string url, string filePath, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string body = "";
                try
                {
                    body = await response.Content.ReadAsStringAsync();
                    if (body.Length > 1024) body = body.Substring(0, 1024);
                }
                catch { }
                Log($"[Updater] HTTP {(int)response.StatusCode} body={body}");
                return false;
            }

            long total = response.Content.Headers.ContentLength ?? 0;
            long downloaded = 0;
            await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int read;
            while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                downloaded += read;
                if (total > 0)
                {
                    var pct = (int)(downloaded * 100.0 / total);
                    ProgressChanged?.Invoke(new UpdateProgress
                    {
                        Progress = pct,
                        Message = $"下载中 {FormatBytes(downloaded)}/{FormatBytes(total)}"
                    });
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Log($"[Updater] 下载异常: {ex.Message}");
            return false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < units.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {units[order]}";
    }

    private static void Log(string msg)
    {
        try
        {
            var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_startup.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }
    private string NormalizeDownloadUrl(string? downloadUrl)
    {
        if (string.IsNullOrEmpty(downloadUrl))
            return downloadUrl ?? string.Empty;

        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsed)
            && !string.IsNullOrEmpty(parsed.Scheme))
        {
            return downloadUrl;
        }

        return _serverUrl.TrimEnd('/') + "/" + downloadUrl.TrimStart('/');
    }
}
