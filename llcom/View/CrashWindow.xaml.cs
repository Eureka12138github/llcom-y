using System;
using System.Windows;

namespace llcom;

/// <summary>
/// 崩溃提示窗口：只提供“关闭程序”一个操作（不提供“继续运行”）。
/// 展示错误详情（可选中复制）与日志文件路径。
/// </summary>
public partial class CrashWindow : Window
{
    public CrashWindow(Exception exception, string logPath)
    {
        InitializeComponent();
        detailTextBox.Text = exception?.ToString() ?? "unknown error";
        logPathTextBlock.Text = logPath ?? "";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
