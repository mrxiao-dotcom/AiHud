/*
  ThemeManager.cs
  ===============
  主题管理器：支持亮/暗 Neumorphism 主题动态切换。

  实现要点：
   1. 两套资源字典 NeumorphismLight.xaml / NeumorphismDark.xaml 通过
     Application.Resources.MergedDictionaries 动态替换。
  2. 关键修复：MaterialDesign 的 BundledTheme 在切换 BaseTheme 时会把全局
     TextElement.Foreground 重新绑定到 MaterialDesignBody 等内置键，把我们
     的 NeuroTextBrush 顶下去，导致"主题不能切换/文字色不变"。这里在替换
     字典后强制刷新所有窗口的 TextElement 绑定，并清掉 BundledTheme 的影响。
  3. 切换时同步刷新所有顶层 Window 的 Foreground / Background，使已经显示的
     控件立即重新解析 DynamicResource。
*/
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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

        // 1. MaterialDesign BundledTheme 同步（仍保留 PackIcon 等控件需要的资源），
        //    但不依赖它的前景色覆盖我们的 DynamicResource。
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

        // 2. 切换 Neumorphism 资源字典：先 RemoveAt，再 Insert，
        //    这样 MergedDictionaries 会触发所有 DynamicResource 重新解析。
        var themeFileName = theme == AppTheme.Dark
            ? "NeumorphismDark.xaml"
            : "NeumorphismLight.xaml";

        var newDict = new ResourceDictionary
        {
            Source = new Uri(themeFileName, UriKind.Relative)
        };

        ResourceDictionary? existingNeuro = null;
        int existingIndex = -1;
        for (int i = 0; i < app.Resources.MergedDictionaries.Count; i++)
        {
            var dict = app.Resources.MergedDictionaries[i];
            if (dict.Source != null &&
                (dict.Source.ToString().EndsWith("NeumorphismLight.xaml", StringComparison.OrdinalIgnoreCase) ||
                 dict.Source.ToString().EndsWith("NeumorphismDark.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                existingNeuro = dict;
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            app.Resources.MergedDictionaries.RemoveAt(existingIndex);
            app.Resources.MergedDictionaries.Insert(existingIndex, newDict);
        }
        else
        {
            app.Resources.MergedDictionaries.Add(newDict);
        }

        // 3. MaterialDesign 的 BundledTheme 会在 BaseTheme 切换时把 TextElement.Foreground
        //    改写成 MaterialDesignBody *，但 DynamicResource 解析发生在字典树匹配阶段，
        //    而 MaterialDesign 的内置色键优先于我们自定义的 NeuroTextBrush。
        //    修复：主动把所有顶层 Window 的 TextElement.Foreground / Background
        //    重新指向我们的 DynamicResource，确保 Neumorphism 颜色优先。
        foreach (Window window in app.Windows)
        {
            try
            {
                window.SetResourceReference(TextElement.ForegroundProperty, "NeuroTextBrush");
                window.SetValue(TextElement.FontWeightProperty, FontWeights.Medium);

                // 重新设置 Background 为 DynamicResource，触发视觉刷新
                window.SetResourceReference(Control.BackgroundProperty, "NeuroBackgroundBrush");
            }
            catch
            {
                // 单个窗口刷新失败不影响其它窗口
            }
        }
    }

    public static void Initialize()
    {
        ApplyTheme(_currentTheme);
    }
}
