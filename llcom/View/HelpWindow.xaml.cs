using System;
using System.Windows;

namespace llcom;

/// <summary>
/// 使用说明窗口（由主窗口顶栏的 “?” 打开）。
/// 中文显示完整说明；英文显示对应的英文快速指南。
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();

        var language = Tools.Global.setting.language ?? "";
        var english = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        HelpContentZh.Visibility = english ? Visibility.Collapsed : Visibility.Visible;
        HelpContentEn.Visibility = english ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        llcom.Services.ThemeService.ApplyWindowChrome(this);
        llcom.Services.ThemeService.ThemeChanged += (_, _) =>
            llcom.Services.ThemeService.ApplyWindowChrome(this);
    }
}