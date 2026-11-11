using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace llcom.Services;

/// <summary>
/// 主题服务：在 AdonisUI 的 Light / Dark 配色方案之间切换，
/// 并在深色模式下合并 Styles/Palette.Dark.xaml 覆盖自定义 Brush.* 颜色。
/// </summary>
internal static class ThemeService
{
    private const string LightScheme =
        "pack://application:,,,/AdonisUI;component/ColorSchemes/Light.xaml";
    private const string DarkScheme =
        "pack://application:,,,/AdonisUI;component/ColorSchemes/Dark.xaml";
    private const string DarkPalettePath = "Styles/Palette.Dark.xaml";
    private const string DarkPaletteUri =
        "pack://application:,,,/Styles/Palette.Dark.xaml";

    /// <summary>主题切换完成（用于无法自动跟随 DynamicResource 的区域，如日志条目颜色）。</summary>
    public static event EventHandler? ThemeChanged;

    public static bool IsDark { get; private set; }

    /// <summary>应用浅色/深色主题；可重复调用。</summary>
    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app == null)
        {
            IsDark = dark;
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;

        // 1) 替换 AdonisUI 的 Light/Dark 配色方案
        var schemeIndex = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source != null && source.IndexOf("/ColorSchemes/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                schemeIndex = i;
                break;
            }
        }
        if (schemeIndex >= 0)
        {
            var current = dictionaries[schemeIndex].Source?.OriginalString ?? "";
            var target = dark ? DarkScheme : LightScheme;
            if (!current.EndsWith(dark ? "/Dark.xaml" : "/Light.xaml", StringComparison.OrdinalIgnoreCase))
                dictionaries[schemeIndex] = new ResourceDictionary { Source = new Uri(target) };
        }

        // 2) 深色调色板必须排在 DesignTokens 之后，才能覆盖自定义 Brush.*
        var paletteIndex = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source != null && source.IndexOf(DarkPalettePath, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                paletteIndex = i;
                break;
            }
        }
        if (dark && paletteIndex < 0)
            dictionaries.Add(new ResourceDictionary { Source = new Uri(DarkPaletteUri) });
        else if (!dark && paletteIndex >= 0)
            dictionaries.RemoveAt(paletteIndex);

        IsDark = dark;
        ApplyWindowChrome(app);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>从当前资源取语义画刷；找不到时使用兜底色。可在后台线程调用。</summary>
    public static SolidColorBrush GetBrush(string key, Color fallback)
    {
        var app = Application.Current;
        if (app == null)
            return new SolidColorBrush(fallback);

        if (!app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => GetBrushOnUiThread(app, key, fallback));

        return GetBrushOnUiThread(app, key, fallback);
    }

    private static SolidColorBrush GetBrushOnUiThread(
        Application app,
        string key,
        Color fallback
    )
    {
        var brush = app.TryFindResource(key) as SolidColorBrush;
        if (brush == null)
            return new SolidColorBrush(fallback);
        if (brush.CanFreeze && !brush.IsFrozen)
            brush.Freeze();
        return brush;
    }

    /// <summary>让原生标题栏跟随深色主题（Windows 10 1809+；失败时静默忽略）。</summary>
    public static void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
            Tools.Win32.SetDarkTitleBar(handle, IsDark);
    }

    private static void ApplyWindowChrome(Application app)
    {
        foreach (Window window in app.Windows)
            ApplyWindowChrome(window);
    }
}
