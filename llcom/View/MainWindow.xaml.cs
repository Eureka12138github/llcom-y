using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls.WebParts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using FontAwesome.WPF;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using llcom.LuaEnv;
using llcom.Model;
using llcom.Services;
using llcom.Tools;
using llcom.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Color = System.Windows.Media.Color;

namespace llcom;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>串口控制区 ViewModel（Step 5 MVVM 化）</summary>
    private readonly MainViewModel _vm;

    /// <summary>Lua 编辑器折叠刷新定时器（原为后台线程 + 忙等，改 UI 定时器）</summary>
    private readonly System.Windows.Threading.DispatcherTimer _foldingTimer =
        new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Lua 日志缓冲/后台泵（列表批处理与渲染解耦，原为类内字段 + 线程）</summary>
    private readonly LuaLogPump _luaLogPump = new LuaLogPump();

    /// <summary>Lua 日志框保留的最大字符数（超过后从头部裁剪，避免 TextBox 无限膨胀）</summary>
    private const int LuaLogMaxChars = 200_000;

    /// <summary>等待追加到 Lua 日志框的文本（后台线程写入，UI 线程取走）</summary>
    private readonly object _luaLogPendingLock = new object();
    private readonly StringBuilder _luaLogPending = new StringBuilder();
    private bool _luaLogFlushQueued;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        if (
            Tools.Global.setting.windowHeight != 0
            && Tools.Global.setting.windowWidth != 0
            && Tools.Global.setting.windowLeft >= SystemParameters.VirtualScreenLeft
            && Tools.Global.setting.windowTop >= SystemParameters.VirtualScreenTop
            && Tools.Global.setting.windowLeft
                < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && Tools.Global.setting.windowTop
                < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
        )
        {
            this.Left = Tools.Global.setting.windowLeft;
            this.Top = Tools.Global.setting.windowTop;
            this.Width = Tools.Global.setting.windowWidth;
            this.Height = Tools.Global.setting.windowHeight;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        //初始化“串口设置”页的下拉参数
        mainDataBitsComboBox.SelectedIndex = Tools.Global.setting.dataBits - 5;
        mainStopBitComboBox.SelectedIndex = Tools.Global.setting.stopBit - 1;
        mainDataCheckComboBox.SelectedIndex = Tools.Global.setting.parity;
        foreach (var en in new List<EncodingInfo>(Encoding.GetEncodings()).OrderBy(x => x.CodePage))
        {
            var item = new ComboBoxItem { Content = $"[{en.CodePage}] {en.Name}", Tag = en.CodePage };
            var idx = mainEncodingComboBox.Items.Add(item);
            if (Tools.Global.setting.encoding == en.CodePage)
                mainEncodingComboBox.SelectedIndex = idx;
        }

        //延迟启动，加快软件第一屏出现速度
        Task.Run(() =>
        {
            this.Dispatcher.Invoke(
                new Action(
                    delegate
                    {
                        //接收到、发送数据成功回调
                        Tools.Global.uart.UartDataRecived += Uart_UartDataRecived;
                        Tools.Global.uart.UartDataSent += Uart_UartDataSent;

                        //初始化已在 MainWindow 构造函数中完成（需早于 ViewModel 构造）

                        //重写关闭窗口代码
                        this.Closing += MainWindow_Closing;

                        //窗口置顶事件
                        Tools.Global.setting.MainWindowTop += new EventHandler(topEvent);
                        if (Tools.Global.setting.topmost) //设置窗口置顶
                            this.Topmost = true;

                        //收发数据显示页面
                        dataShowFrame.Navigate(
                            new Uri("Pages/DataShowPage.xaml", UriKind.Relative)
                        );

                        // 绑定事件监听,用于监听HID设备插拔
                        //Lua 编辑器文本桥接（AvalonEdit Text 不可绑定，经回调访问）
                        _vm.LuaEditor.SetTextBridge(
                            () => textEditor.Text,
                            t => textEditor.Text = t
                        );

                        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
                        //USB 防抖定时器触发：刷新端口列表（含自动重连）
                        _usbDebounceTimer.Tick += (_, _) =>
                        {
                            _usbDebounceTimer.Stop();
                            _vm.OnUsbDeviceChanged();
                            Logger.AddUartLogInfo($"[USB拔插事件] {DateTime.Now:HH:mm:ss.fff}");
                        };
                        //刷新设备列表
                        _vm.RefreshPorts();

                        //绑定数据（快捷发送/串口区均通过 MainViewModel 绑定）

                        //快速搜索
                        SearchPanel.Install(textEditor.TextArea);

                        var foldingManager = FoldingManager.Install(textEditor.TextArea);
                        var foldingStrategy = new Model.LuaFolding();

                        _foldingTimer.Tick += (_, _) =>
                        {
                            try
                            {
                                foldingStrategy.UpdateFoldings(foldingManager, textEditor.Document);
                            }
                            catch (System.Exception ex) { Tools.Logger.Swallowed("main window ui", ex); }
                        };
                        _foldingTimer.Start();

                        //嵌入资源的逻辑名由 RootNamespace 决定，不随 AssemblyName/exe 名变化
                        string name = "llcom.Lua.xshd";
                        System.Reflection.Assembly assembly =
                            System.Reflection.Assembly.GetExecutingAssembly();
                        using (System.IO.Stream s = assembly.GetManifestResourceStream(name))
                        {
                            using (XmlTextReader reader = new XmlTextReader(s))
                            {
                                var xshd = HighlightingLoader.LoadXshd(reader);
                                textEditor.SyntaxHighlighting = HighlightingLoader.Load(
                                    xshd,
                                    HighlightingManager.Instance
                                );
                            }
                        }

                        //加载lua日志打印事件
                        LuaEnv.LuaApis.PrintLuaLog += LuaApis_PrintLuaLog;
                        _luaLogPump.FlushRequested += OnLuaLogFlush;
                        //lua代码出错/结束运行事件
                        LuaEnv.LuaRunEnv.LuaRunError += LuaRunEnv_LuaRunError;

                        //在线脚本列表
                        OnlineScriptsFrame.Navigate(
                            new Uri("Pages/OnlineScriptsPage.xaml", UriKind.Relative)
                        );

                        //关于页面
                        aboutFrame.Navigate(new Uri("Pages/AboutPage.xaml", UriKind.Relative));

                        //tcp测试页面
                        tcpTestFrame.Navigate(new Uri("Pages/tcpTest.xaml", UriKind.Relative));

                        //tcp客户端页面
                        tcpClientFrame.Navigate(
                            new Uri("Pages/SocketClientPage.xaml", UriKind.Relative)
                        );

                        //本地tcp服务器
                        tcpLocalTestFrame.Navigate(
                            new Uri("Pages/TcpLocalPage.xaml", UriKind.Relative)
                        );

                        //本地udp服务器
                        udpLocalTestFrame.Navigate(
                            new Uri("Pages/UdpLocalPage.xaml", UriKind.Relative)
                        );

                        //mqtt测试页面
                        MqttTestFrame.Navigate(
                            new Uri("Pages/MqttTestPage.xaml", UriKind.Relative)
                        );

                        //编码转换工具页面
                        EncodingToolsFrame.Navigate(
                            new Uri("Pages/ConvertPage.xaml", UriKind.Relative)
                        );

                        //乱码修复
                        EncodingFixFrame.Navigate(
                            new Uri("Pages/EncodingFixPage.xaml", UriKind.Relative)
                        );

                        //串口监听
                        SerialMonitorFrame.Navigate(
                            new Uri("Pages/SerialMonitorPage.xaml", UriKind.Relative)
                        );

                        //绘制曲线
                        PlotFrame.Navigate(new Uri("Pages/PlotPage.xaml", UriKind.Relative));

                        //WinUSB
                        WinUSBFrame.Navigate(new Uri("Pages/WinUSBPage.xaml", UriKind.Relative));
                        //工具选择器默认选中第一个（触发一次可见性切换）
                        ToolSelector.SelectedIndex = 0;

                        this.Title +=
                            $" - {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()}";

                        _luaLogPump.Start();

                        //加载完了，可以允许点击
                        MainGrid.IsEnabled = true;

                        //检查更新
                        if (!Tools.Global.IsMSIX())
                        {
                            AppUpdateService.CheckForUpdate(Dispatcher);
                        }

                        //更换标题栏
                        var title = "";
                        title = this.Title;
                        Tools.Global.ChangeTitleEvent += (n, s) =>
                        {
                            this.Dispatcher.Invoke(() => this.Title = title + s);
                        };

                        //热更，防止恶性bug，及时修复
                        AppUpdateService.RunHotfix();

                        Tools.Global.RefreshLuaScriptListEvent += (n, s) =>
                        {
                            this.Dispatcher.Invoke(() => _vm.LuaEditor.RefreshList());
                        };
                    }
                )
            );
        });
        Tools.Global.recvScriptBackup = Tools.Global.setting.recvScript;
        if (string.IsNullOrEmpty(Tools.Global.recvScriptBackup))
            Tools.Global.recvScriptBackup = "default";
    }

    private bool DoInvoke(Action action)
    {
        if (Tools.Global.isMainWindowsClosed)
            return false;
        Dispatcher.Invoke(action);
        return true;
    }

    private void Uart_UartDataSent(object sender, EventArgs e)
    {
        Tools.Logger.ShowData(sender as byte[], true);
    }

    private void Uart_UartDataRecived(object sender, EventArgs e)
    {
        Tools.Logger.ShowData(sender as byte[], false);
    }

    /// <summary>USB 热插拔防抖定时器（WM_DEVICECHANGE 会连续触发多次）</summary>
    private readonly System.Windows.Threading.DispatcherTimer _usbDebounceTimer =
        new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x219 && !Tools.Global.uart.IsOpen()) // 监听USB设备插拔消息
        {
            //防抖：每次事件都重置定时器，停止触发 800ms 后再刷新端口列表（含自动重连）
            _usbDebounceTimer.Stop();
            _usbDebounceTimer.Start();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 窗口关闭事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Tools.Global.setting.windowLeft = this.Left;
        Tools.Global.setting.windowTop = this.Top;
        Tools.Global.setting.windowWidth = this.Width;
        Tools.Global.setting.windowHeight = this.Height;
        //自动保存脚本
        _vm.LuaEditor.OnAutoSave();
        _foldingTimer.Stop();
        _usbDebounceTimer.Stop();
        Tools.Global.isMainWindowsClosed = true;
        foreach (Window win in App.Current.Windows)
        {
            if (win != this)
            {
                win.Close();
            }
        }
        e.Cancel = false; //正常关闭
    }

    Window settingPage = new SettingWindow();

    private void MoreSettingButton_Click(object sender, RoutedEventArgs e)
    {
        //让“更多设置”成为主窗口的子窗口：始终显示在主窗口之上，避免切换置顶时被压到后面
        settingPage.Owner = this;
        settingPage.Show();
    }

    /// <summary>
    /// 响应其他代码传来的窗口置顶事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void topEvent(object sender, EventArgs e)
    {
        this.Topmost = (bool)sender;
        //主窗口置顶切换后，重新把“更多设置”窗口带回前台，避免它被压到主窗口后面/失焦
        if (settingPage.IsVisible)
            settingPage.Activate();
    }

    /// <summary>日志底栏右端热区：鼠标进入时显示 帮助/设置/语言</summary>
    private void LogCornerZone_MouseEnter(object sender, MouseEventArgs e) =>
        ShowLogCornerTools(true);

    /// <summary>日志底栏右端热区：鼠标离开时隐藏</summary>
    private void LogCornerZone_MouseLeave(object sender, MouseEventArgs e) =>
        ShowLogCornerTools(false);

    /// <summary>淡入/淡出三按钮；隐藏时同时禁用点击，避免误触。</summary>
    private void ShowLogCornerTools(bool show)
    {
        LogCornerTools.IsHitTestVisible = show;
        LogCornerTools.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                show ? 1.0 : 0.0,
                TimeSpan.FromMilliseconds(show ? 120 : 200)
            )
        );
    }

    Window? helpWindow;

    /// <summary>顶栏 “?”：打开使用说明窗口（单实例；关闭后可再次打开）。</summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (helpWindow == null)
        {
            helpWindow = new HelpWindow();
            helpWindow.Closed += (_, _) => helpWindow = null;
        }
        helpWindow.Show();
        helpWindow.Activate();
    }

    /// <summary>工具选择器：切换到对应工具页（各页在 Window_Loaded 已 Navigate 一次，这里只切可见性）。</summary>
    private void ToolSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (tcpTestFrame == null)
            return; // 初始化早期可能尚未就绪
        var frames = new[]
        {
            tcpTestFrame,
            tcpLocalTestFrame,
            udpLocalTestFrame,
            tcpClientFrame,
            EncodingToolsFrame,
            MqttTestFrame,
            EncodingFixFrame,
            SerialMonitorFrame,
            PlotFrame,
            WinUSBFrame,
        };
        int index = ToolSelector.SelectedIndex;
        for (int i = 0; i < frames.Length; i++)
            frames[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApiDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(Tools.Global.apiDocumentUrl);
    }

    private void OpenScriptFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                "explorer.exe",
                Tools.Global.GetTrueProfilePath() + "user_script_run"
            );
        }
        catch
        {
            Tools.MessageBox.Show(
                $"尝试打开文件夹失败，请自行打开该路径：{Tools.Global.GetTrueProfilePath()}user_script_run"
            );
        }
    }

    private void RefreshScriptListButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.LuaEditor.RefreshList();
    }

    /// <summary>Lua 编辑器失焦自动保存</summary>
    private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        _vm.LuaEditor.OnAutoSave();
    }

    /// <summary>窗口切换后台自动保存</summary>
    private void Window_Deactivated(object sender, EventArgs e)
    {
        _vm.LuaEditor.OnAutoSave();
    }

    /// <summary>窗口激活时检测外部修改（其他编辑器改过则重新加载）</summary>
    private void Window_Activated(object sender, EventArgs e)
    {
        _vm.LuaEditor.CheckExternalChange();
    }

    /// <summary>清空发送框内容</summary>
    private void ClearSendBoxButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.SendText = "";
    }

    /// <summary>打开“发送记录”弹层（列表固定高度可滚动）。</summary>
    private void SendHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        sendHistoryPopup.IsOpen = true;
    }

    /// <summary>弹层打开时同步上限输入框内容。</summary>
    private void SendHistoryPopup_Opened(object sender, EventArgs e)
    {
        historyLimitBox.Text = _vm.SendHistoryLimit.ToString();
        sendHistoryList.SelectedIndex = -1;
    }

    /// <summary>点击历史项：填回发送框并关闭弹层。</summary>
    private void SendHistoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;
        if (ItemsControl.ContainerFromElement(sendHistoryList, source) is not ListBoxItem item)
            return;
        if (item.DataContext is not string text || string.IsNullOrEmpty(text))
            return;
        _vm.SendText = text;
        sendHistoryPopup.IsOpen = false;
    }

    /// <summary>发送记录上限的合法范围（与 Settings / 帮助文档保持一致）</summary>
    private const int SendHistoryLimitMin = 5;
    private const int SendHistoryLimitMax = 200;

    /// <summary>上限输入框：只允许数字</summary>
    private void HistoryLimitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>上限输入框：粘贴内容也必须是纯数字</summary>
    private void HistoryLimitBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(typeof(string)) is not string text)
        {
            e.CancelCommand();
            return;
        }
        foreach (var c in text)
        {
            if (!char.IsDigit(c))
            {
                e.CancelCommand();
                return;
            }
        }
    }

    /// <summary>Enter 应用上限，Esc 关闭弹层</summary>
    private void HistoryLimitBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySendHistoryLimit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            sendHistoryPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void HistoryLimitBox_LostFocus(object sender, RoutedEventArgs e) =>
        ApplySendHistoryLimit();

    /// <summary>应用上限：非法输入回退到当前值，越界夹到 5~200</summary>
    private void ApplySendHistoryLimit()
    {
        var value = int.TryParse(historyLimitBox.Text, out var parsed)
            ? parsed
            : _vm.SendHistoryLimit;
        value = Math.Max(SendHistoryLimitMin, Math.Min(SendHistoryLimitMax, value));
        _vm.SendHistoryLimit = value;
        historyLimitBox.Text = value.ToString();
    }

    /// <summary>清空发送记录（列表 + 持久化）</summary>
    private void ClearSendHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearSendHistory();
    }

    // ===== 发送选项（连续发送 / 发送后延时 / 重复次数 / Enter 行为） =====

    private void SendOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
            return;
        var menu = button.ContextMenu;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        Dispatcher.BeginInvoke(
            new Action(() => menu.IsOpen = true),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    private void SendOptionMenu_Opened(object sender, RoutedEventArgs e)
    {
        var s = Tools.Global.setting;
        continuousSendMenuItem.IsChecked = s.continuousSend;
        sendByEnterMenuItem.IsChecked = s.sendByEnter;
        sendByCtrlEnterMenuItem.IsChecked = !s.sendByEnter;
        UpdateContinuousMenuTexts();
    }

    private void UpdateContinuousMenuTexts()
    {
        var s = Tools.Global.setting;
        continuousDelayBox.Text = s.continuousDelayMs.ToString();
        continuousRepeatBox.Text = s.continuousRepeat.ToString();
    }

    private void ContinuousSendMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var s = Tools.Global.setting;
        s.continuousSend = !s.continuousSend;
        continuousSendMenuItem.IsChecked = s.continuousSend;
        if (!s.continuousSend)
            _vm.StopContinuousSending();
    }

    private void ContinuousDelayMinus_Click(object sender, RoutedEventArgs e)
    {
        Tools.Global.setting.continuousDelayMs = MainViewModel.NormalizeInterval(
            Tools.Global.setting.continuousDelayMs - 100
        );
        UpdateContinuousMenuTexts();
    }

    private void ContinuousDelayPlus_Click(object sender, RoutedEventArgs e)
    {
        Tools.Global.setting.continuousDelayMs = MainViewModel.NormalizeInterval(
            Tools.Global.setting.continuousDelayMs + 100
        );
        UpdateContinuousMenuTexts();
    }

    private void ContinuousRepeatMinus_Click(object sender, RoutedEventArgs e)
    {
        Tools.Global.setting.continuousRepeat = Math.Max(
            -1,
            Tools.Global.setting.continuousRepeat - 1
        );
        UpdateContinuousMenuTexts();
    }

    private void ContinuousRepeatPlus_Click(object sender, RoutedEventArgs e)
    {
        Tools.Global.setting.continuousRepeat = Math.Min(
            100000,
            Tools.Global.setting.continuousRepeat + 1
        );
        UpdateContinuousMenuTexts();
    }

    /// <summary>手动输入延时：失焦时校验并夹取到 10~3600000</summary>
    private void ContinuousDelayBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var s = Tools.Global.setting;
        if (int.TryParse(continuousDelayBox.Text, out var v))
            s.continuousDelayMs = MainViewModel.NormalizeInterval(v);
        continuousDelayBox.Text = s.continuousDelayMs.ToString();
    }

    /// <summary>手动输入重复次数：失焦时校验并夹取到 -1~100000</summary>
    private void ContinuousRepeatBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var s = Tools.Global.setting;
        if (int.TryParse(continuousRepeatBox.Text, out var v))
            s.continuousRepeat = v < -1 ? -1 : (v > 100000 ? 100000 : v);
        continuousRepeatBox.Text = s.continuousRepeat.ToString();
    }

    private void SendByEnterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Tools.Global.setting.sendByEnter = true;
        sendByEnterMenuItem.IsChecked = true;
        sendByCtrlEnterMenuItem.IsChecked = false;
    }

    private void SendByCtrlEnterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Tools.Global.setting.sendByEnter = false;
        sendByEnterMenuItem.IsChecked = false;
        sendByCtrlEnterMenuItem.IsChecked = true;
    }

    /// <summary>“按 Enter 发送”模式下 Enter 直接发送（Shift+Enter 仍为换行）</summary>
    private void ToSendDataTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (Tools.Global.setting.sendByEnter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            _vm.SendCommand.Execute(null);
        }
    }

    // ===== 快捷发送每条的“定时”下拉（折叠，和主发送框一样） =====

    /// <summary>当前打开“定时”下拉的那一条（± 需要操作它）</summary>
    private ToSendData _quickTimerItem;

    private void QuickTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
            return;
        _quickTimerItem = button.DataContext as ToSendData;
        var menu = button.ContextMenu;
        menu.DataContext = _quickTimerItem; //菜单内绑定（定时开关/间隔显示）需要该行数据
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        Dispatcher.BeginInvoke(
            new Action(() => menu.IsOpen = true),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    private void QuickTimerIntervalMinus_Click(object sender, RoutedEventArgs e)
    {
        if (_quickTimerItem != null)
            _quickTimerItem.timerIntervalMs = MainViewModel.NormalizeInterval(
                _quickTimerItem.timerIntervalMs - 100
            );
    }

    private void QuickTimerIntervalPlus_Click(object sender, RoutedEventArgs e)
    {
        if (_quickTimerItem != null)
            _quickTimerItem.timerIntervalMs = MainViewModel.NormalizeInterval(
                _quickTimerItem.timerIntervalMs + 100
            );
    }

    /// <summary>手动输入间隔：失焦时校验并夹取到 10~3600000</summary>
    private void QuickTimerIntervalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || _quickTimerItem == null)
            return;
        if (int.TryParse(box.Text, out var v))
            _quickTimerItem.timerIntervalMs = MainViewModel.NormalizeInterval(v);
        box.Text = _quickTimerItem.timerIntervalMs.ToString();
    }

    private void knowSendDataButton_click(object sender, RoutedEventArgs e)
    {
        //未打开串口则不发送（也不切换接收脚本），仅提示
        if (!_vm.EnsurePortOpenForSend())
            return;

        //发送逻辑已下沉到 MainViewModel.SendQuickItem（定时发送复用同一逻辑）
        if (((Button)sender).Tag is ToSendData data)
            _vm.SendQuickItem(data);
    }

    private void Button_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 恢复原有的双击改名功能
        ToSendData data = ((Button)sender).Tag as ToSendData;
        Tuple<bool, string> ret = Tools.InputDialog.OpenDialog(
            TryFindResource("QuickSendSetButton") as string ?? "?!",
            data.commit,
            TryFindResource("QuickSendChangeButton") as string ?? "?!"
        );
        if (ret.Item1)
        {
            ((Button)sender).Content = data.commit = ret.Item2;
        }
    }

    private void NewScriptButton_Click(object sender, RoutedEventArgs e)
    {
        newLuaFileWrapPanel.Visibility = Visibility.Visible;
    }

    private void RunScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.LuaEditor.SelectedFile))
        {
            luaLogTextBox.Clear();
            LuaEnv.LuaRunEnv.New($"user_script_run/{_vm.LuaEditor.SelectedFile}.lua");
            luaScriptEditorGrid.Visibility = Visibility.Collapsed;
            luaLogShowGrid.Visibility = Visibility.Visible;
            luaLogPrintable = true;
        }
        LuaEnv.LuaRunEnv.canRun = true;
    }

    private void NewLuaFilebutton_Click(object sender, RoutedEventArgs e)
    {
        //创建/重名校验与加载都在 VM.CreateNew 内完成
        _vm.LuaEditor.CreateNew(newLuaFileNameTextBox.Text);
        newLuaFileWrapPanel.Visibility = Visibility.Collapsed;
    }

    private void NewLuaFileCancelbutton_Click(object sender, RoutedEventArgs e)
    {
        newLuaFileWrapPanel.Visibility = Visibility.Collapsed;
    }

    //是否可打印标记
    private bool _luaLogPrintable = true;
    private bool luaLogPrintable
    {
        get { return _luaLogPrintable; }
        set
        {
            this.Dispatcher.Invoke(
                new Action(
                    delegate
                    {
                        if (value)
                        {
                            pauseLuaPrintButton.ToolTip =
                                TryFindResource("LuaPause") as string ?? "?!";
                            pauseLuaPrintIcon.Icon = FontAwesomeIcon.Pause;
                        }
                        else
                        {
                            pauseLuaPrintButton.ToolTip =
                                TryFindResource("LuaContinue") as string ?? "?!";
                            pauseLuaPrintIcon.Icon = FontAwesomeIcon.Play;
                        }
                    }
                )
            );
            _luaLogPrintable = value;
            _luaLogPump.Printable = value;
        }
    }

    private void LuaApis_PrintLuaLog(object sender, EventArgs e)
    {
        _luaLogPump.Enqueue(sender as string);
    }

    /// <summary>
    /// Lua 日志一批渲染（由 LuaLogPump 在后台线程触发）。
    /// 先在后台合并多批，再以 Background 优先级异步追加到 UI，
    /// 避免日志刷新和拖拽分隔条/输入抢 UI 线程。
    /// </summary>
    private void OnLuaLogFlush(string logs)
    {
        if (string.IsNullOrEmpty(logs) || Tools.Global.isMainWindowsClosed)
            return;

        lock (_luaLogPendingLock)
        {
            _luaLogPending.Append(logs);
            if (_luaLogFlushQueued)
                return;
            _luaLogFlushQueued = true;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(FlushLuaLogToUi)
        );
    }

    /// <summary>把合并后的 Lua 日志追加到控件（UI 线程执行）。</summary>
    private void FlushLuaLogToUi()
    {
        string logs;
        lock (_luaLogPendingLock)
        {
            logs = _luaLogPending.ToString();
            _luaLogPending.Clear();
            _luaLogFlushQueued = false;
        }
        if (string.IsNullOrEmpty(logs) || Tools.Global.isMainWindowsClosed)
            return;

        //有选中文本时临时禁用，避免 WPF 在追加时反复重排选中区域（原逻辑）
        var hadSelection = luaLogTextBox.SelectionLength > 0;
        if (hadSelection)
            luaLogTextBox.IsEnabled = false;
        try
        {
            if (_luaLogPump.Count >= 1000)
            {
                luaLogTextBox.Clear();
                luaLogTextBox.AppendText(
                    "Lua log too long, auto clear.\r\n" + "more logs see lua log file.\r\n"
                );
                _luaLogPump.Reset();
            }
            luaLogTextBox.AppendText(logs);

            //只在原本就贴着底部时跟随滚动，向上翻看历史时不强行拉回
            if (
                luaLogTextBox.VerticalOffset + luaLogTextBox.ViewportHeight
                >= luaLogTextBox.ExtentHeight - 1
            )
                luaLogTextBox.ScrollToEnd();

            //单行很长的日志（Lua 表等）也要有上限，避免 TextBox 无限膨胀
            if (luaLogTextBox.Text.Length > LuaLogMaxChars)
            {
                var text = luaLogTextBox.Text;
                var keep = LuaLogMaxChars / 2;
                var cut = text.LastIndexOf('\n', text.Length - keep - 1);
                luaLogTextBox.Text =
                    cut >= 0 ? text.Substring(cut + 1) : text.Substring(text.Length - keep);
            }
        }
        finally
        {
            if (hadSelection)
                luaLogTextBox.IsEnabled = true;
        }
    }

    private void luaLogTextBox_MouseLeave(object sender, MouseEventArgs e)
    {
        luaLogTextBox.IsEnabled = true;
    }

    private void StopLuaButton_Click(object sender, RoutedEventArgs e)
    {
        _luaLogPump.Reset();
        if (!LuaEnv.LuaRunEnv.isRunning)
        {
            luaLogTextBox.Clear();
            luaScriptEditorGrid.Visibility = Visibility.Visible;
            luaLogShowGrid.Visibility = Visibility.Collapsed;
            luaLogPrintable = true;

            stopLuaOrExitIcon.Icon = FontAwesomeIcon.Stop;
            stopLuaButton.ToolTip = TryFindResource("LuaStop") as string ?? "?!";
        }
        else
        {
            stopLuaOrExitIcon.Icon = FontAwesomeIcon.SignOut;
            stopLuaButton.ToolTip = TryFindResource("LuaQuit") as string ?? "?!";
        }
        luaLogPrintable = true;
        LuaEnv.LuaRunEnv.StopLua("");

        pauseLuaPrintButton.ToolTip = TryFindResource("LuaOverload") as string ?? "?!";
        pauseLuaPrintIcon.Icon = FontAwesomeIcon.Refresh;
    }

    private void LuaRunEnv_LuaRunError(object sender, EventArgs e)
    {
        luaLogPrintable = true;
    }

    private void PauseLuaPrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (!LuaEnv.LuaRunEnv.isRunning)
        {
            stopLuaOrExitIcon.Icon = FontAwesomeIcon.Stop;
            stopLuaButton.ToolTip = TryFindResource("LuaStop") as string ?? "?!";
            LuaEnv.LuaRunEnv.New($"user_script_run/{_vm.LuaEditor.SelectedFile}.lua");
            LuaEnv.LuaRunEnv.canRun = true;
            luaLogPrintable = true;
        }
        else
        {
            luaLogPrintable = !luaLogPrintable;
        }
    }

    private void SendLuaScriptButton_Click(object sender, RoutedEventArgs e)
    {
        LuaEnv.LuaRunEnv.RunCommand(runOneLineLuaTextBox.Text);
        //runOneLineLuaTextBox.Clear();
    }

    private void RunOneLineLuaTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            LuaEnv.LuaRunEnv.RunCommand(runOneLineLuaTextBox.Text);
    }

    private void ScriptShareButton_Click(object sender, RoutedEventArgs e)
    {
        //切到应用内的「社区脚本」页；页内可切换官方 / 本项目来源
        foreach (TabItem item in mainTabControl.Items)
        {
            if (item.Content == OnlineScriptsFrame)
            {
                mainTabControl.SelectedItem = item;
                return;
            }
        }

        try
        {
            System.Diagnostics.Process.Start(
                "https://github.com/Eureka12138github/llcom-y/tree/master/community-scripts"
            );
        }
        catch (System.Exception ex)
        {
            Tools.Logger.Swallowed("online scripts", ex);
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        Tools.Global.setting.language = ((MenuItem)sender).Tag.ToString();
    }

    //id序号右击事件
    private void TextBlock_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ToSendData data;
        try
        {
            data = ((TextBlock)sender).Tag as ToSendData;
        }
        catch
        {
            data = ((Grid)sender).Tag as ToSendData;
        }
        Tuple<bool, string> ret = Tools.InputDialog.OpenDialog(
            TryFindResource("QuickSendChangeIdButton") as string ?? "?!",
            data.id.ToString(),
            (TryFindResource("QuickSendChangeIdTitle") as string ?? "?!") + data.id.ToString()
        );

        if (!ret.Item1)
            return;
        var qs = _vm.QuickSend;
        qs.CheckIds();
        if (ret.Item2.Trim().Length == 0) //留空删除该项目
        {
            qs.Items.RemoveAt(data.id - 1);
        }
        else
        {
            int index = -1;
            int.TryParse(ret.Item2, out index);
            if (index == data.id || index <= 0 || index > qs.Items.Count)
                return;
            //移动到指定位置
            var item = qs.Items[data.id - 1];
            qs.Items.RemoveAt(data.id - 1);
            qs.Items.Insert(index - 1, item);
        }
        qs.Save();
    }

    private void sentCountTextBlock_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Tools.Global.setting.SentCount = 0;
    }

    private void receivedCountTextBlock_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Tools.Global.setting.ReceivedCount = 0;
    }

    private void QuickListNameStackPanel_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _vm.QuickSend.RenamePageCommand.Execute(null);
    }

    private void pauseLuaPrintButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        luaLogTextBox.Clear();
    }

    private void textEditor_TextChanged(object sender, EventArgs e)
    {
        _vm.LuaEditor.OnTextChanged();
    }

    private void uartDataFlowDocument_GotFocus(object sender, RoutedEventArgs e)
    {
        if (Tools.Global.setting.terminal)
            dataShowFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 148, 0));
    }

    private void uartDataFlowDocument_LostFocus(object sender, RoutedEventArgs e)
    {
        dataShowFrame.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    private void uartDataFlowDocument_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.TextComposition.Text.Length < 1 || !Tools.Global.setting.terminal)
            return;
        if (Tools.Global.uart.IsOpen())
            try
            {
                Tools.Global.uart.SendData(Encoding.ASCII.GetBytes(e.TextComposition.Text));
            }
            catch (System.Exception ex) { Tools.Logger.Swallowed("main window ui", ex); }
    }

    private void uartDataFlowDocument_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (
            !(Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            || !Tools.Global.setting.terminal
        )
            return;
        if (e.Key >= Key.A && e.Key <= Key.Z && Tools.Global.uart.IsOpen())
            try
            {
                Tools.Global.uart.SendData(new byte[] { (byte)((int)e.Key - (int)Key.A + 1) });
            }
            catch (System.Exception ex) { Tools.Logger.Swallowed("main window ui", ex); }
    }

    private void ScriptIcon_Click(object sender, MouseButtonEventArgs e)
    {
        // 点击📜图标时配置接收脚本
        TextBlock icon = sender as TextBlock;
        ToSendData data = icon.Tag as ToSendData;
        recvScriptCombo.ItemsSource = Directory
            .GetFiles(Global.ProfilePath + "user_script_recv_convert", "*.lua")
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .ToList();
        recvScriptPopup.PlacementTarget = icon;
        recvScriptCombo.Tag = data;
        recvScriptCombo.SelectedItem = data.recvScriptPath ?? "";
        recvScriptCombo.IsDropDownOpen = true;
        recvScriptPopup.IsOpen = false;
        recvScriptPopup.IsOpen = true;

        // 打开对话框，选择接收脚本
        //System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog();
        //dialog.Filter = "Lua脚本文件 (*.lua)|*.lua|所有文件 (*.*)|*.*";
        //dialog.InitialDirectory = System.IO.Path.Combine(Tools.Global.ProfilePath, "user_script_recv_convert");

        //if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        //{
        //    data.recvScriptPath = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        //    //SaveSendList(null, EventArgs.Empty);
        //}
    }

    private void ScriptIcon_RightClick(object sender, MouseButtonEventArgs e)
    {
        // 右击📜图标时清除接收脚本
        TextBlock icon = sender as TextBlock;
        ToSendData data = icon.Tag as ToSendData;

        // 清除接收脚本项
        if (!string.IsNullOrEmpty(data.recvScriptPath))
        {
            data.recvScriptPath = "";
            //SaveSendList(null, EventArgs.Empty);
        }
    }

    private void recvScriptCombo_DropDownClosed(object sender, EventArgs e)
    {
        ComboBox me = sender as ComboBox;
        ToSendData data = me.Tag as ToSendData;
        string newItem = me.SelectedItem as string;
        if (data.recvScriptPath != newItem)
            data.recvScriptPath = newItem;
        recvScriptPopup.IsOpen = false;
        me.SelectedItem = null;
    }

    [DllImport("user32")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    private async void ScriptParaIcon_Click(object sender, MouseButtonEventArgs e)
    {
        TextBlock icon = sender as TextBlock;
        ToSendData data = icon.Tag as ToSendData;

        recvScriptParaBox.Tag = data;
        recvScriptParaBox.Text = data.recvScriptPara;
        recvScriptParaBox.ScrollToEnd();
        recvScriptParaPopup.PlacementTarget = icon;
        recvScriptParaPopup.IsOpen = false;
        await Task.Yield();
        recvScriptParaPopup.IsOpen = true;
        await Task.Yield();
        var source = (HwndSource)PresentationSource.FromVisual(recvScriptParaPopup.Child);
        SetFocus(source.Handle);
        await Task.Yield();
        Keyboard.Focus(recvScriptParaBox);
    }

    private void ScriptParaIcon_RightClick(object sender, MouseButtonEventArgs e)
    {
        TextBlock icon = sender as TextBlock;
        ToSendData data = icon.Tag as ToSendData;

        if (!string.IsNullOrEmpty(data.recvScriptPara))
        {
            data.recvScriptPara = "";
            //SaveSendList(null, EventArgs.Empty);
        }
    }

    private void ScriptParaConfirm_Click(object sender, MouseButtonEventArgs e)
    {
        TextBlock icon = sender as TextBlock;
        TextEditor t = icon.Tag as TextEditor;
        ToSendData data = t.Tag as ToSendData;

        data.recvScriptPara = t.Text;
        //SaveSendList(null, EventArgs.Empty);
        recvScriptParaPopup.IsOpen = false;
    }

    private void ScriptParaCancel_Click(object sender, MouseButtonEventArgs e)
    {
        recvScriptParaPopup.IsOpen = false;
    }

    // ===== “串口设置”页参数下拉 =====

    private void MainDataBitsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (mainDataBitsComboBox.SelectedItem != null)
            Tools.Global.setting.dataBits = mainDataBitsComboBox.SelectedIndex + 5;
    }

    private void MainStopBitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (mainStopBitComboBox.SelectedItem != null)
            Tools.Global.setting.stopBit = mainStopBitComboBox.SelectedIndex + 1;
    }

    private void MainDataCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (mainDataCheckComboBox.SelectedItem != null)
            Tools.Global.setting.parity = mainDataCheckComboBox.SelectedIndex;
    }

    private void MainEncodingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (
            mainEncodingComboBox.SelectedItem is ComboBoxItem c
            && c.Tag is int cp
            && cp != Tools.Global.setting.encoding
        )
            Tools.Global.setting.encoding = cp;
    }
}
