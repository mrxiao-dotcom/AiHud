/*
  ThemeManager.cs
  ===============
  主题管理器：支持亮/暗 Neumorphism 主题动态切换。
  通过管理两套 ResourceDictionary（NeumorphismLight.xaml / NeumorphismDark.xaml）
  的加载/卸载，配合 WPF 的 DynamicResource 机制实现运行时主题热切换。
  所有 Neumorphism 颜色均以 DynamicResource 引用主题字典中的键，
  切换时自动响应，无需重新构建 UI。
*/
using System;
using System.Windows;
using MaterialDesignThemes.Wpf;

namespace AiDaBenYing;

public enum AppTheme { Light, Dark }

public static class ThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Dark;

    public static AppTheme CurrentTheme => _currentTheme;

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static void ToggleTheme()
    {
        _currentTheme = _currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ApplyTheme(_currentTheme);
        ThemeChanged?.Invoke(null, _currentTheme);
    }

    public static void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        _currentTheme = theme;
        ApplyTheme(_currentTheme);
        ThemeChanged?.Invoke(null, _currentTheme);
    }

    private static void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null) return;

        // 1. 同步更新 Material Design 的 BundledTheme
        try
        {
            var paletteHelper = new MaterialDesignThemes.Wpf.PaletteHelper();
            var mdTheme = paletteHelper.GetTheme();
            mdTheme.SetBaseTheme(theme == AppTheme.Dark
                ? MaterialDesignThemes.Wpf.BaseTheme.Dark
                : MaterialDesignThemes.Wpf.BaseTheme.Light);
            paletteHelper.SetTheme(mdTheme);
        }
        catch
        {
            // 非关键，忽略
        }

        // 2. 切换 Neumorphism 资源字典
        var themeFileName = theme == AppTheme.Dark
            ? "NeumorphismDark.xaml"
            : "NeumorphismLight.xaml";

        var newDict = new ResourceDictionary
        {
            Source = new Uri(themeFileName, UriKind.Relative)
        };

        // 找到并替换 Neumorphism 字典
        ResourceDictionary? existingNeuro = null;
        foreach (var dict in app.Resources.MergedDictionaries)
        {
            if (dict.Source != null &&
                (dict.Source.ToString().Contains("NeumorphismLight") ||
                 dict.Source.ToString().Contains("NeumorphismDark")))
            {
                existingNeuro = dict;
                break;
            }
        }

        if (existingNeuro != null)
        {
            var index = app.Resources.MergedDictionaries.IndexOf(existingNeuro);
            app.Resources.MergedDictionaries.RemoveAt(index);
            app.Resources.MergedDictionaries.Insert(index, newDict);
        }
        else
        {
            app.Resources.MergedDictionaries.Add(newDict);
        }
    }

    public static void Initialize()
    {
        ApplyTheme(_currentTheme);
    }
}
