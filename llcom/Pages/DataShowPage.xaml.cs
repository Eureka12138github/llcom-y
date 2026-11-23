using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using llcom.Model;
using llcom.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Clipboard = System.Windows.Clipboard;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace llcom.Pages;

/// <summary>
/// DataShowPage.xaml 的交互逻辑（Step 8 MVVM 化）。
/// 显示逻辑全部移入 DataShowViewModel；本页只保留 UI 桥接
/// （流式文本框增量追加、滚动、分包/流式切换、保存日志对话框）。
/// </summary>
public partial class DataShowPage : Page
{
    private DataShowViewModel _vm = null!;
    private bool loaded = false;
    private bool _logRowSelectionActive;

    // 日志底栏低于该宽度时，常显图标折叠成「⋯」菜单（右下角悬停工具组约 80px）
    private const double LogBarCollapseMinWidth = 400;

    public DataShowPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded)
            return;
        loaded = true;

        //显示逻辑全部在 VM（订阅 Logger.DataShowTask 等）
        _vm = Tools.AppHost.Services.GetRequiredService<DataShowViewModel>();
        DataContext = _vm;

        //流式文本框桥接：增量追加保持性能，未锁定时滚动到底部
        _vm.SetTextBridge(
            t =>
            {
                MainTextBox.AppendText(t);
                if (!_vm.LockLog)
                    MainTextBox.ScrollToEnd();
            },
            () => MainTextBox.Clear(),
            //流式缓冲超过字符上限：从文本框头部删掉同样的字符数
            n =>
            {
                var text = MainTextBox.Text;
                MainTextBox.Text = n >= text.Length ? "" : text.Substring(n);
                if (!_vm.LockLog)
                    MainTextBox.ScrollToEnd();
            }
        );

        //分包/流式显示切换
        _vm.PackModeChanged += pack =>
        {
            MainList.Visibility = pack ? Visibility.Visible : Visibility.Collapsed;
            MainTextBox.Visibility = pack ? Visibility.Collapsed : Visibility.Visible;
        };
        //分包模式新增条目后滚动到底部（虚拟化列表直接按索引滚动，避免按对象查找的 O(n) 开销）
        _vm.ScrollRequested += () =>
        {
            if (!_vm.LockLog)
                ScrollListToEnd();
        };

        //批量裁剪会触发一次 Reset：锁定视图时尽量恢复滚动位置与选中项
        _vm.TrimStarting += _ => CaptureViewStateBeforeTrim();
        _vm.Trimmed += () => RestoreViewStateAfterTrim();

        //初始显示模式（timeout≥0 分包，否则流式）
        var needPack = _vm.Setting.timeout >= 0;
        MainList.Visibility = needPack ? Visibility.Visible : Visibility.Collapsed;
        MainTextBox.Visibility = needPack ? Visibility.Collapsed : Visibility.Visible;

        UpdateLogBarLayout();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateLogBarLayout();

    /// <summary>
    /// 日志区变窄时，把底栏常显图标折叠成「⋯」菜单，避免和右下角（悬停显示）的工具组重叠。
    /// 宽度恢复后展开。
    /// </summary>
    private void UpdateLogBarLayout()
    {
        if (!loaded || ActualWidth <= 0)
            return;

        var collapse = ActualWidth < LogBarCollapseMinWidth;
        var visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        if (SendHexToggle.Visibility == visibility)
            return;

        SendHexToggle.Visibility = visibility;
        RecvHexToggle.Visibility = visibility;
        ShowSymbolToggle.Visibility = visibility;
        DisableLogToggle.Visibility = visibility;
        LockLogButton.Visibility = visibility;
        ClearLogButton.Visibility = visibility;
        ExportLogButton.Visibility = visibility;
        LogBarMoreButton.Visibility = collapse ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== 批量裁剪（Reset）时的视图状态保持 =====
    private DataShowItem? _anchorBeforeTrim;
    private List<DataShowItem>? _selectionBeforeTrim;

    /// <summary>裁剪前记录首个可见条目与选中项（Reset 会清空这两者）。</summary>
    private void CaptureViewStateBeforeTrim()
    {
        _anchorBeforeTrim = GetFirstVisibleItem();
        // 选中项过多（例如全选）时不恢复，避免一次裁剪做上万次线性查找
        var selected = MainList.SelectedItems.OfType<DataShowItem>().ToList();
        _selectionBeforeTrim = selected.Count is > 0 and <= 500 ? selected : null;
    }

    /// <summary>裁剪后恢复选中项；锁定视图时再滚回裁剪前看到的位置。</summary>
    private void RestoreViewStateAfterTrim()
    {
        var anchor = _anchorBeforeTrim;
        var selection = _selectionBeforeTrim;
        _anchorBeforeTrim = null;
        _selectionBeforeTrim = null;

        if (anchor == null && selection == null)
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (selection != null)
                {
                    // 一次构建现存项集合，避免每个选中项都做一次 O(n) 查找
                    var present = new HashSet<DataShowItem>(MainList.Items.OfType<DataShowItem>());
                    foreach (var item in selection)
                    {
                        if (present.Contains(item))
                            MainList.SelectedItems.Add(item);
                    }
                    if (MainList.SelectedItems.Count > 0)
                        _logRowSelectionActive = true;
                }

                if (!_vm.LockLog || anchor == null)
                    return;

                if (MainList.Items.Contains(anchor))
                    MainList.ScrollIntoView(anchor);
                else if (MainList.Items.Count > 0)
                    MainList.ScrollIntoView(MainList.Items[0]);
            }),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    /// <summary>按索引滚动到列表末尾（避免 ScrollIntoView 按对象查找的 O(n) 开销）。</summary>
    private void ScrollListToEnd()
    {
        if (MainList.Items.Count == 0)
            return;

        if (FindVisualChild<VirtualizingStackPanel>(MainList) is { } panel)
        {
            try
            {
                panel.BringIndexIntoViewPublic(MainList.Items.Count - 1);
                return;
            }
            catch (ArgumentOutOfRangeException) { }
        }
        MainList.ScrollIntoView(MainList.Items[MainList.Items.Count - 1]);
    }

    /// <summary>当前视口内最靠上的日志条目（裁剪后据此恢复位置）。</summary>
    private DataShowItem? GetFirstVisibleItem()
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(MainList);
        var panel = FindVisualChild<VirtualizingStackPanel>(MainList);
        if (scrollViewer == null || panel == null)
            return null;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(panel);
        for (var i = 0; i < count; i++)
        {
            if (System.Windows.Media.VisualTreeHelper.GetChild(panel, i) is not FrameworkElement child)
                continue;

            double top;
            try
            {
                top = child.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (top + child.ActualHeight <= 0)
                continue; // 完全在视口上方

            var index = MainList.ItemContainerGenerator.IndexFromContainer(child);
            if (index >= 0 && index < MainList.Items.Count)
                return MainList.Items[index] as DataShowItem;
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;
            if (FindVisualChild<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    /// <summary>「⋯」按钮：打开折叠后的底栏功能菜单。</summary>
    private void LogBarMoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = LogBarMoreButton.ContextMenu;
        if (menu == null)
            return;

        menu.PlacementTarget = LogBarMoreButton;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void LockLogButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.LockLog = !_vm.LockLog;
    }

    /// <summary>清空日志显示（与主窗口 ☰ 里原来的“清空日志”同一动作）</summary>
    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        Tools.Logger.ClearData();
    }

    /// <summary>导出日志（唯一入口）：左键导出为新文件并用默认编辑器打开。</summary>
    private void ExportLogButton_Click(object sender, RoutedEventArgs e) => OpenCurrentLogFile();

    /// <summary>右键菜单 /「⋯」菜单：另存到指定位置（由用户选择路径）。</summary>
    private void SaveLogAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasLogContent)
        {
            ShowLogMissing();
            return;
        }

        //默认文件名与「导出并打开当前日志」一致，放在 logs 目录
        var suggested = Tools.Logger.GetExportLogPath(DateTime.Now);
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Filter = "Log files(*.txt)|*.txt|All files(*.*)|*.*",
            InitialDirectory = System.IO.Path.GetDirectoryName(suggested),
            FileName = System.IO.Path.GetFileName(suggested),
        };
        if (saveFileDialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            _vm.SaveLog(saveFileDialog.FileName);
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("save log", ex);
            Tools.MessageBox.Show(
                (TryFindResource("OpenLogFailed") as string ?? "Failed to save the log:")
                    + "\r\n"
                    + ex.Message
            );
        }
    }

    /// <summary>导出当前主日志，用系统默认程序（文件关联）打开。</summary>
    private void OpenLogWithSystemButton_Click(object sender, RoutedEventArgs e)
    {
        var path = ExportCurrentLog();
        if (path == null)
            return;

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }
            );
        }
        catch (Exception ex)
        {
            ReportOpenLogFailed(ex);
        }
    }

    /// <summary>先选程序，再导出并用它打开一次（不保存为默认）。</summary>
    private void OpenLogWithButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = PickLogEditor();
        if (editor == null)
            return;

        var path = ExportCurrentLog();
        if (path != null)
            LaunchLogEditor(editor, path);
    }

    /// <summary>只选择并保存默认编辑器，不要求主日志已有内容。</summary>
    private void SetLogEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = PickLogEditor();
        if (editor != null)
            Tools.Global.setting.logEditorPath = editor;
    }

    private void ClearLogEditorButton_Click(object sender, RoutedEventArgs e) =>
        Tools.Global.setting.logEditorPath = "";

    /// <summary>导出当前主日志并打开：有默认编辑器就直接用，否则让用户选一次。</summary>
    private void OpenCurrentLogFile()
    {
        var editor = Tools.Global.setting.logEditorPath ?? "";
        if (string.IsNullOrWhiteSpace(editor))
        {
            //未设置默认编辑器：先让用户选一次（取消则什么都不做）
            editor = PickLogEditor();
            if (editor == null)
                return;
        }
        else if (!System.IO.File.Exists(editor))
        {
            Tools.MessageBox.Show(
                TryFindResource("LogEditorMissing") as string
                    ?? "The default editor no longer exists; please choose it again."
            );
            return;
        }

        var path = ExportCurrentLog();
        if (path != null)
            LaunchLogEditor(editor, path);
    }

    /// <summary>
    /// 把当前主日志导出为 `logs/logyyyyMMdd_HHmmss.txt`（内容与界面逐行一致）并返回路径。
    /// 日志不在后台持续写入，只在用户点击导出/保存时生成。
    /// </summary>
    private string? ExportCurrentLog()
    {
        if (!_vm.HasLogContent)
        {
            ShowLogMissing();
            return null;
        }

        try
        {
            //全限定：本文件同时引用了 System.Windows.Forms，Cursors 存在同名类型
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var path = Tools.Logger.GetExportLogPath(DateTime.Now);
            _vm.SaveLog(path);
            return path;
        }
        catch (Exception ex)
        {
            ReportOpenLogFailed(ex);
            return null;
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
        }
    }

    /// <summary>选择用于打开日志的程序；取消时返回 null。</summary>
    private string? PickLogEditor()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = TryFindResource("LogEditorDialogTitle") as string ?? "Select editor",
            Filter = "Program (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        var owner = Window.GetWindow(this);
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private void LaunchLogEditor(string editor, string path)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editor,
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = false,
                }
            );
        }
        catch (Exception ex)
        {
            ReportOpenLogFailed(ex);
        }
    }

    private void ReportOpenLogFailed(Exception ex)
    {
        Tools.Logger.Swallowed("open log file", ex);
        var message = TryFindResource("OpenLogFailed") as string ?? "Failed to open the log file.";
        Tools.MessageBox.Show(message + "\r\n" + ex.Message);
    }

    private void ShowLogMissing() =>
        Tools.MessageBox.Show(
            TryFindResource("LogFileMissing") as string
                ?? "The main log is empty; send or receive some data first."
        );

    /// <summary>Ctrl+C 复制选中的多行；没有选中行时保留 RichTextBox 的原生文本复制。</summary>
    private void MainList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Key == Key.C && MainList.SelectedItems.Count > 0 && _logRowSelectionActive)
        {
            CopySelectedLines();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl+左键切换行选中，支持跨行多选；
    /// 不带修饰键时仍交给 RichTextBox 选择行内文字。
    /// </summary>
    private void LogItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            _logRowSelectionActive = false;
            MainList.SelectedItems.Clear();
            return;
        }
        if (sender is not RichTextBox box)
            return;
        if (ItemsControl.ContainerFromElement(MainList, box) is not ListBoxItem item)
            return;

        item.IsSelected = !item.IsSelected;
        _logRowSelectionActive = true;
        MainList.Focus();
        e.Handled = true;
    }

    private void CopySelectedLines_Click(object sender, RoutedEventArgs e) => CopySelectedLines();

    private void SelectAllLog_Click(object sender, RoutedEventArgs e)
    {
        MainList.SelectAll();
        _logRowSelectionActive = true;
    }

    private void CopyAllLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(MainTextBox.Text))
            CopyText(MainTextBox.Text);
    }

    private void CopyAllList_Click(object sender, RoutedEventArgs e)
    {
        CopyLines(MainList.Items.OfType<DataShowItem>().Select(GetItemText));
    }

    private void CopySelectedLines()
    {
        var selected = new HashSet<DataShowItem>();
        foreach (var item in MainList.SelectedItems)
        {
            if (item is DataShowItem data)
                selected.Add(data);
        }
        if (selected.Count == 0)
            return;

        // 按界面显示顺序复制，而不是按点击顺序
        CopyLines(
            MainList
                .Items.OfType<DataShowItem>()
                .Where(item => selected.Contains(item))
                .Select(GetItemText)
        );
    }

    private static string GetItemText(DataShowItem item) => item.ToLogText();

    private static void CopyLines(IEnumerable<string> lines)
    {
        var text = string.Join(
            Environment.NewLine,
            lines.Where(line => !string.IsNullOrEmpty(line))
        );
        if (text.Length > 0)
            CopyText(text);
    }

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("copy log", ex);
        }
    }
}
