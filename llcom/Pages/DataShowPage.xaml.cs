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

    // 日志底栏低于该宽度时，常显图标折叠成「⋯」菜单（右下角悬停工具组约 144px）
    private const double LogBarCollapseMinWidth = 430;

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
        SaveLogButton.Visibility = visibility;
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

    private void SaveLogButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "Log files(*.log)|*.log";
        if (saveFileDialog.ShowDialog() == DialogResult.OK)
        {
            _vm.SaveLog(saveFileDialog.FileName);
        }
    }

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
