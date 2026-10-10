using System.Windows;

namespace llcom;

/// <summary>
/// 使用说明窗口（由主窗口顶栏的 “?” 打开）。
/// 内容为各功能的使用说明，静态文本。
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }
}