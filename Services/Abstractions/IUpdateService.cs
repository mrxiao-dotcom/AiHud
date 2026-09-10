using System;
using System.Threading;
using System.Threading.Tasks;

namespace AiDaBenYing.Services.Abstractions;

/// <summary>
/// AiHUD 与"应用更新"子系统交互的统一抽象。
/// Adapter 层内部使用 RegisterSrv.ClientSDK.Services.UpdateClient 实现，
/// 但 SDK 类型对业务代码不可见。
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// 下载 / 安装进度回调。Adapter 负责把 SDK 的事件翻译到此回调。
    /// </summary>
    event Action<UpdateProgress>? ProgressChanged;

    /// <summary>
    /// 向服务器询问是否有新版本。
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default);

    /// <summary>
    /// 下载并安装更新包。
    /// 注意：这是 SDK 的直接转发版本，会在调用方进程中同步解压覆盖 installPath。
    /// 对于 WPF 桌面应用，几乎一定会因为 exe/dll 被占用而失败（SDK 1.3.1 的
    /// CopyFilesAsync 在 File.Move 失败时直接 continue 跳过覆盖）。
    /// 推荐使用 <see cref="DownloadAndPrepareStageAsync"/> 走中间进程路径。
    /// </summary>
    Task<UpdateInstallResult> DownloadAndInstallAsync(UpdateInfo info, string installPath, CancellationToken ct = default);

    /// <summary>
    /// 启动一个新进程并退出当前进程。
    /// </summary>
    bool RestartAsync(string? arguments = null);

    /// <summary>
    /// 两阶段更新的第一阶段：仅下载更新包到临时目录，**不解压、不覆盖**，
    /// 然后启动一个中间进程（以 AiDaBenYing.exe 自身 + --updater-stage 命令），
    /// 让中间进程在当前进程退出后接管解压+覆盖工作。
    ///
    /// 实现要点：
    ///   * 必须在 IsSuccess=true 后立刻 Shutdown（关主进程 + 窗口），
    ///     否则中间进程等待旧 exe 句柄释放会卡死。
    ///   * SDK 1.3.1 的 DownloadAndInstallAsync 不能用（会在主进程同步解压覆盖，
    ///     触发 .bak 堆积死循环），所以此方法应直接用 HttpClient 下载。
    /// </summary>
    /// <param name="info">服务器返回的更新元数据（含绝对 DownloadUrl）。</param>
    /// <param name="installPath">AiHUD 当前安装目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// IsSuccess=true 时，调用方应立即执行 Shutdown(0)；
    /// IsSuccess=false 时，调用方应保持现状并提示用户。
    /// </returns>
    Task<UpdateInstallResult> DownloadAndPrepareStageAsync(UpdateInfo info, string installPath, CancellationToken ct = default);
}
