using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiDaBenYing.Models;

public partial class AppData : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Category> _categories = new();

    [ObservableProperty]
    private AppSettings _settings = new();
}

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string _themeMode = "Dark";

    [ObservableProperty]
    private string _primaryColor = "DeepPurple";

    [ObservableProperty]
    private string _secondaryColor = "Cyan";

    [ObservableProperty]
    private double _windowWidth = 1200;

    [ObservableProperty]
    private double _windowHeight = 800;

    [ObservableProperty]
    private bool _rememberWindowPosition = true;

    [ObservableProperty]
    private double _left;

    [ObservableProperty]
    private double _top;

    /// <summary>
    /// 应用安装/下载根目录。
    /// 留空时回退到 <c>AppDomain.CurrentDomain.BaseDirectory\Apps</c>。
    /// 在设置窗口里可手动改成任意本地路径。
    /// </summary>
    [ObservableProperty]
    private string _appInstallRoot = string.Empty;

    /// <summary>
    /// 解析后的安装根目录（保证已存在的绝对路径）。
    /// </summary>
    [JsonIgnore]
    public string ResolvedInstallRoot
    {
        get
        {
            var raw = (AppInstallRoot ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw))
            {
                raw = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Apps");
            }
            try
            {
                if (!System.IO.Path.IsPathRooted(raw))
                {
                    raw = System.IO.Path.GetFullPath(raw, AppDomain.CurrentDomain.BaseDirectory);
                }
                System.IO.Directory.CreateDirectory(raw);
                return raw;
            }
            catch
            {
                // 路径非法时回退默认
                var fallback = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Apps");
                System.IO.Directory.CreateDirectory(fallback);
                return fallback;
            }
        }
    }
}
