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

    /// <summary>目录跳转：按钮的 Tag 是目标章节的 x:Name，滚动到该章节顶部。</summary>
    private void SectionLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name })
            return;
        if (FindName(name) is not FrameworkElement target)
            return;

        //按章节相对滚动容器的位置计算偏移，留 8 像素避免标题贴边
        var offset = target.TransformToAncestor(HelpScroll).Transform(new Point(0, 0)).Y;
        HelpScroll.ScrollToVerticalOffset(Math.Max(0, HelpScroll.VerticalOffset + offset - 8));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        llcom.Services.ThemeService.ApplyWindowChrome(this);
        llcom.Services.ThemeService.ThemeChanged += (_, _) =>
            llcom.Services.ThemeService.ApplyWindowChrome(this);
    }
}