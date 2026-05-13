using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiHUD.Models;

public partial class AppItem : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _launchCount;

    [ObservableProperty]
    private DateTime? _lastLaunchTime;

    [ObservableProperty]
    private string? _iconPath;

    private bool _isDeserializing;

    private System.Windows.Media.Imaging.BitmapImage? _icon;

    [JsonIgnore] // 重要：BitmapImage 不可序列化，且会在反序列化时重新加载
    public System.Windows.Media.Imaging.BitmapImage? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon != null;

    public string Initials
    {
        get
        {
            if (string.IsNullOrEmpty(Name)) return "?";

            // 获取首字母缩写
            var words = Name.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                // 多词取首字母，如 "Visual Studio Code" -> "VSC"
                return string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])));
            }
            else if (Name.Length >= 2)
            {
                // 单词取前两位，如 "Notepad" -> "No"
                return Name.Substring(0, Math.Min(2, Name.Length)).ToUpperInvariant();
            }
            return Name.ToUpperInvariant();
        }
    }

    public bool IsDeserializing
    {
        get => _isDeserializing;
        set => _isDeserializing = value;
    }

    public AppItem()
    {
    }

    public AppItem(string name, string executablePath, string? iconPath = null)
    {
        Name = name;
        ExecutablePath = executablePath;
        IconPath = iconPath;
        // 不再自动加载图标
    }

    private void LoadIcon()
    {
        try
        {
            // IconPath 为 null 时使用 ExecutablePath
            string? pathToLoad = !string.IsNullOrEmpty(IconPath) ? IconPath : ExecutablePath;

            if (string.IsNullOrEmpty(pathToLoad))
            {
                Icon = null;
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[AppItem.LoadIcon] 加载图标: {pathToLoad}, IconPath={IconPath}, ExePath={ExecutablePath}");

            if (System.IO.File.Exists(pathToLoad))
            {
                var extension = System.IO.Path.GetExtension(pathToLoad).ToLowerInvariant();
                System.Diagnostics.Debug.WriteLine($"[AppItem.LoadIcon] 文件存在, 扩展名: {extension}");
                if (extension == ".ico" || extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp")
                {
                    Icon = IconExtractor.LoadIconFile(pathToLoad);
                }
                else
                {
                    Icon = IconExtractor.ExtractIconFromFile(pathToLoad);
                }
            }
            else if (System.IO.File.Exists(ExecutablePath))
            {
                System.Diagnostics.Debug.WriteLine($"[AppItem.LoadIcon] 主路径不存在，尝试 ExePath: {ExecutablePath}");
                Icon = IconExtractor.ExtractIconFromFile(ExecutablePath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AppItem.LoadIcon] 文件都不存在");
                Icon = null;
            }

            System.Diagnostics.Debug.WriteLine($"[AppItem.LoadIcon] 图标加载结果: {(Icon != null ? "成功" : "失败")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppItem.LoadIcon] 异常: {ex.Message}");
            Icon = null;
        }
    }

    public void RefreshIcon()
    {
        LoadIcon();
    }

    public void EnsureIconLoaded()
    {
        if (Icon == null && !string.IsNullOrEmpty(ExecutablePath))
        {
            LoadIcon();
        }
    }

    partial void OnIconPathChanged(string? value)
    {
        // 不再自动加载图标
    }

    partial void OnExecutablePathChanged(string value)
    {
        // 不再自动加载图标
    }

    public void ClearIcon()
    {
        _icon = null;
        IconPath = null;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(IconPath));
    }

    public void SetIcon(System.Windows.Media.Imaging.BitmapImage? icon)
    {
        Icon = icon;
    }
}
