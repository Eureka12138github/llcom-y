using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using llcom.LuaEnv;
using llcom.Services;

namespace llcom.ViewModels;

/// <summary>
/// 串口控制区条目：显示名（含设备描述）与端口名（COMx）。
/// </summary>
public class PortInfo
{
    public string DisplayName { get; }
    public string PortName { get; }

    public PortInfo(string displayName)
    {
        DisplayName = displayName;
        var m = Regex.Match(displayName, @"\(COM\d+\)");
        PortName = m.Success ? m.Value.Trim('(', ')') : displayName;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// 波特率下拉条目：Value 为 null 时表示"自定义"入口。
/// </summary>
public class BaudRateItem
{
    public string Display { get; }
    public int? Value { get; }
    public bool IsCustom => Value == null;

    public BaudRateItem(string display, int? value)
    {
        Display = display;
        Value = value;
    }

    public override string ToString() => Display;
}

/// <summary>
/// 主窗口串口控制区 ViewModel（Step 5）。
/// 职责：端口列表刷新（WMI + 注册表权威过滤）、打开/关闭串口（含缓存发送与自动重连）、
/// 波特率选择（含自定义）、数据发送管线（Lua 发送脚本 → appendNewline → SendData）、
/// 收发计数、USB 热插拔刷新。
/// 逻辑从 MainWindow code-behind 逐字迁移，行为保持一致。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ISerialPortService _uart;

    // ===== 定时发送（快捷发送逐条定时 + 主发送框连续发送） =====
    /// <summary>定时发送基准定时器（10ms tick，扫描到期项）</summary>
    private readonly System.Windows.Threading.DispatcherTimer _timedSendTimer =
        new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };

    /// <summary>快捷发送每条的下次到期时间</summary>
    private readonly Dictionary<Model.ToSendData, DateTime> _quickNextDue = new();

    /// <summary>主发送框“连续发送”是否正在运行</summary>
    private bool _continuousRunning;

    /// <summary>连续发送剩余次数（-1 表示无限）</summary>
    private int _continuousRemaining;

    private DateTime _continuousNextDue;

    //串口控制状态
    private bool _isOpeningPort; //打开串口进行中

    //初始为 true：禁止启动时自动重连（serial.PortName 默认 "COM1"，
    //若本机恰有 COM1 设备，启动刷新会误判为上次端口而自动打开）；
    //用户手动打开串口（OpenPort）后置 false，才允许后续拔插自动重连
    private bool _forcusClosePort = true;
    private bool _refreshLock; //端口列表刷新锁
    private bool _skipSearch; //WMI 查询连续失败后跳过
    private int _searchCount;
    private string _lastPortName; //上次使用的端口名（自动重连/恢复选中用）

    /// <summary>端口下拉列表</summary>
    public ObservableCollection<PortInfo> Ports { get; } = new ObservableCollection<PortInfo>();

    /// <summary>波特率下拉列表（末尾为"自定义"入口）</summary>
    public ObservableCollection<BaudRateItem> BaudRates { get; } =
        new ObservableCollection<BaudRateItem>();

    [ObservableProperty]
    private PortInfo? _selectedPort;

    [ObservableProperty]
    private BaudRateItem? _selectedBaudRate;

    [ObservableProperty]
    private bool _isPortOpen;

    [ObservableProperty]
    private bool _isPortComboEnabled = true;

    [ObservableProperty]
    private bool _canOpenClosePort;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _openCloseButtonText = "";

    /// <summary>发送框文本（绑定到 setting.dataToSend 保持持久化）</summary>
    public string SendText
    {
        get => Tools.Global.setting.dataToSend;
        set
        {
            if (Tools.Global.setting.dataToSend != value)
                Tools.Global.setting.dataToSend = value;
        }
    }

    /// <summary>快捷发送区（10 页列表管理）</summary>
    public QuickSendViewModel QuickSend { get; }

    /// <summary>Lua 脚本编辑器（user_script_run 目录，主窗口 Lua Tab）</summary>
    public LuaEditorViewModel LuaEditor { get; }

    /// <summary>已发送字节计数（来自设置，仅 UI 通知）</summary>
    public int SentCount => Tools.Global.setting.SentCount;

    /// <summary>已接收字节计数（来自设置，仅 UI 通知）</summary>
    public int ReceivedCount => Tools.Global.setting.ReceivedCount;

    /// <summary>全局设置（供“串口设置”页/发送框字号等 XAML 绑定）</summary>
    public Model.Settings Setting => Tools.Global.setting;

    /// <summary>RTS 信号线（串口硬件控制线，绑定「串口设置」页复选框）</summary>
    public bool Rts
    {
        get => _uart.Rts;
        set => _uart.Rts = value;
    }

    /// <summary>DTR 信号线（串口硬件控制线，绑定「串口设置」页复选框）</summary>
    public bool Dtr
    {
        get => _uart.Dtr;
        set => _uart.Dtr = value;
    }

    /// <summary>发送记录（最近发送过的内容，最新在前；绑定发送记录下拉）</summary>
    public ObservableCollection<string> SendHistory { get; } = new ObservableCollection<string>();

    /// <summary>发送记录条数上限（写入设置并裁剪现有记录）</summary>
    public int SendHistoryLimit
    {
        get => Tools.Global.setting.sendHistoryLimit;
        set
        {
            Tools.Global.setting.sendHistoryLimit = value;
            TrimSendHistory();
        }
    }

    /// <summary>记录一次发送内容（去重后置顶，并按上限裁剪）</summary>
    public void RecordSendHistory(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        SendHistory.Remove(text);
        SendHistory.Insert(0, text);
        TrimSendHistory();
        Tools.Global.setting.sendHistory = SendHistory.ToList();
    }

    /// <summary>按上限裁剪发送记录</summary>
    private void TrimSendHistory()
    {
        var limit = Tools.Global.setting.sendHistoryLimit;
        while (SendHistory.Count > limit)
            SendHistory.RemoveAt(SendHistory.Count - 1);
    }

    /// <summary>按设置生成末尾追加的换行字节：0=无，1=\n，2=\r，3=\n\r，4=\r\n</summary>
    internal static byte[] AppendNewlineBytes(int mode) =>
        mode switch
        {
            1 => new byte[] { 0x0a },
            2 => new byte[] { 0x0d },
            3 => new byte[] { 0x0a, 0x0d },
            4 => new byte[] { 0x0d, 0x0a },
            _ => new byte[0],
        };

    /// <summary>
    /// “发送Hex”模式下的实际字节：直接把输入文本按十六进制解析，**不做任何“像不像十六进制”的判断**。
    /// 发字符串还是发 Hex 完全由「Abc/Hex」按钮决定：
    /// 字符串模式原样发送；Hex 模式则按十六进制解析（非十六进制字符会被 Hex2Byte 过滤掉）。
    /// </summary>
    internal static byte[] HexModeBytes(byte[] data) =>
        Tools.Global.Hex2Byte(Tools.Global.Byte2String(data));

    public IRelayCommand RefreshPortsCommand { get; }
    public IRelayCommand OpenClosePortCommand { get; }
    public IRelayCommand SendCommand { get; }

    internal MainViewModel(
        ISerialPortService uart,
        Model.Settings setting,
        QuickSendViewModel quickSend
    )
    {
        _uart = uart;
        QuickSend = quickSend;
        LuaEditor = new LuaEditorViewModel(
            "user_script_run/",
            () => setting.runScript,
            v => setting.runScript = v
        );

        RefreshPortsCommand = new RelayCommand(() => RefreshPorts());
        OpenClosePortCommand = new RelayCommand(OpenClosePort);
        SendCommand = new RelayCommand(SendData);

        //波特率列表初始化（预设 + 自定义入口）
        foreach (
            var rate in new[]
            {
                110,
                330,
                600,
                1200,
                2400,
                4800,
                9600,
                14400,
                19200,
                38400,
                56000,
                57600,
                115200,
                128000,
                230400,
                256000,
                460800,
                500000,
                128000,
                512000,
                600000,
                750000,
                921600,
                1000000,
                1500000,
                2000000,
                3000000,
            }
        )
        {
            BaudRates.Add(new BaudRateItem(rate.ToString(), rate));
        }
        BaudRates.Add(CustomRateEntry);
        //选中当前设置中的波特率；不在预设中则值化自定义入口
        var cur = setting.baudRate;
        SelectedBaudRate = BaudRates.FirstOrDefault(x => x.Value == cur);
        if (SelectedBaudRate == null)
        {
            BaudRates[BaudRates.Count - 1] = new BaudRateItem(cur.ToString(), cur);
            BaudRates.Add(CustomRateEntry);
            SelectedBaudRate = BaudRates[BaudRates.Count - 2];
        }

        //计数变化通知
        setting.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(setting.SentCount))
                OnPropertyChanged(nameof(SentCount));
            else if (e.PropertyName == nameof(setting.ReceivedCount))
                OnPropertyChanged(nameof(ReceivedCount));
            else if (e.PropertyName == nameof(setting.dataToSend))
                OnPropertyChanged(nameof(SendText)); //发送框文本可能在代码里被清空/填回
        };

        //初始状态文本
        StatusText = Localize("OpenPort_close");
        OpenCloseButtonText = Localize("OpenPort_open");

        //载入发送记录
        if (Tools.Global.setting.sendHistory != null)
        {
            foreach (var h in Tools.Global.setting.sendHistory)
                SendHistory.Add(h);
        }
        TrimSendHistory();

        //定时发送：定时器 tick；任何快捷发送条目/设置变化时，确保定时器在需要时启动
        _timedSendTimer.Tick += (_, _) => OnTimedSendTick();
        Model.ToSendData.DataChanged += (_, _) => EnsureTimedSendRunning();
    }

    private static readonly BaudRateItem CustomRateEntry = new BaudRateItem(null, null);

    /// <summary>
    /// 取本地化资源字符串（等价于 Window.TryFindResource）
    /// </summary>
    private static string Localize(string key) =>
        Application.Current?.TryFindResource(key) as string ?? "?!";

    /// <summary>
    /// 刷新串口设备列表。
    /// 列表来源：Win32_PnPEntity WMI 查询（含设备描述名），并用注册表权威
    /// SerialPort.GetPortNames() 过滤掉已拔掉的设备（WMI 有延迟/缓存）。
    /// </summary>
    /// <param name="lastPort">需要恢复选中的端口名，null 时用上次使用的端口</param>
    public void RefreshPorts(string lastPort = null)
    {
        if (_refreshLock)
            return;
        _refreshLock = true;
        Ports.Clear();
        var strs = new List<string>();
        _searchCount = 0;
        Task.Run(() =>
        {
            while (!_skipSearch)
            {
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "root\\CIMV2",
                        "SELECT * FROM Win32_PnPEntity"
                    );
                    var regExp = new Regex("\\(COM\\d+\\)");
                    foreach (ManagementObject queryObj in searcher.Get())
                    {
                        if (
                            (queryObj["Caption"] != null)
                            && regExp.IsMatch(queryObj["Caption"].ToString())
                        )
                        {
                            strs.Add(queryObj["Caption"].ToString());
                        }
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (++_searchCount >= 3)
                    {
                        _skipSearch = true;
                        Tools.MessageBox.Show(ex.Message);
                    }
                    else
                        Task.Delay(500).Wait();
                }
            }

            // WMI 枚举可能有延迟/缓存（设备刚拔掉时仍返回旧数据），
            // 以注册表权威端口列表 SerialPort.GetPortNames() 为准，过滤掉已拔掉的设备
            try
            {
                var currentPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in SerialPort.GetPortNames())
                {
                    //有些人遇到了微软库的bug，所以需要手动从0x00截断
                    currentPorts.Add(p.IndexOf("\0") > 0 ? p.Substring(0, p.IndexOf("\0")) : p);
                }
                strs.RemoveAll(s =>
                {
                    var m = Regex.Match(s, @"\(COM\d+\)");
                    return m.Success && !currentPorts.Contains(m.Value.Trim('(', ')'));
                });
            }
            catch (Exception ex)
            {
                Tools.Logger.Swallowed("filter ports by registry", ex);
            }

            //加上缺少的com口
            try
            {
                foreach (string p in SerialPort.GetPortNames())
                {
                    var pp = p;
                    if (p.IndexOf("\0") > 0)
                        pp = p.Substring(0, p.IndexOf("\0"));
                    bool notMatch = true;
                    foreach (string n in strs)
                    {
                        if (n.Contains($"({pp})"))
                        {
                            notMatch = false;
                            break;
                        }
                    }
                    if (notMatch)
                        strs.Add($"Serial Port {pp} ({pp})");
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.Swallowed("add missing ports", ex);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var i in strs)
                    Ports.Add(new PortInfo(i));
                CanOpenClosePort = Ports.Count >= 1;
                _refreshLock = false;

                if (string.IsNullOrEmpty(lastPort))
                    //当前串口对象上设置的端口名（即使串口已断开/被拔掉仍存在），
                    //用于设备重新插回时恢复选中并触发自动重连
                    lastPort = _uart.GetName();
                //恢复选中：优先上次端口，否则第一个
                var target = Ports.FirstOrDefault(p => p.PortName == lastPort);
                SelectedPort = target ?? Ports.FirstOrDefault();
                if (target != null)
                {
                    //自动重连：非主动关闭且开启自动重连时尝试重新打开
                    if (!_forcusClosePort && Tools.Global.setting.autoReconnect && !_isOpeningPort)
                    {
                        Task.Run(() =>
                        {
                            _isOpeningPort = true;
                            try
                            {
                                _uart.Open();
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    OpenCloseButtonText = Localize("OpenPort_close");
                                    IsPortComboEnabled = false;
                                    StatusText = Localize("OpenPort_open");
                                });
                            }
                            catch
                            {
                                //MessageBox.Show("串口打开失败！");
                            }
                            _isOpeningPort = false;
                        });
                    }
                }
            });
        });
    }

    /// <summary>
    /// USB 设备拔插事件处理（由窗口 WndProc 转发调用）
    /// </summary>
    public void OnUsbDeviceChanged()
    {
        if (_uart.IsOpen())
        {
            RefreshPorts(_uart.GetName());
        }
        else
        {
            OpenCloseButtonText = Localize("OpenPort_open");
            IsPortComboEnabled = true;
            StatusText = Localize("OpenPort_close");
            RefreshPorts();
        }
    }

    /// <summary>
    /// 打开/关闭串口（按钮命令）
    /// </summary>
    private void OpenClosePort()
    {
        if (!_uart.IsOpen())
            OpenPort();
        else
            ClosePort();
    }

    /// <summary>
    /// 关闭串口
    /// </summary>
    private void ClosePort()
    {
        try
        {
            _forcusClosePort = true; //不再自动重连
            _lastPortName = _uart.GetName();
            _uart.Close();
        }
        catch
        {
            Tools.MessageBox.Show(Localize("ErrorClosePort"));
        }
        OpenCloseButtonText = Localize("OpenPort_open");
        IsPortComboEnabled = true;
        StatusText = Localize("OpenPort_close");
        RefreshPorts(_lastPortName);
    }

    /// <summary>
    /// 打开串口（异步；成功后若有缓存发送数据则补发）
    /// </summary>
    private void OpenPort()
    {
        if (_isOpeningPort)
            return;
        if (SelectedPort == null)
            return;

        string[] ports;
        try
        {
            ports = SerialPort.GetPortNames();
        }
        catch
        {
            ports = new string[0];
        }

        string port = "";
        foreach (var p in ports)
        {
            //有些人遇到了微软库的bug，所以需要手动从0x00截断
            var pp = p;
            if (p.IndexOf("\0") > 0)
                pp = p.Substring(0, p.IndexOf("\0"));
            if (SelectedPort.DisplayName.Contains($"({pp})"))
            {
                port = pp;
                break;
            }
        }

        if (port == "")
            return;

        Task.Run(() =>
        {
            _isOpeningPort = true;
            try
            {
                _forcusClosePort = false; //不再强制关闭串口
                _uart.SetName(port);
                _uart.Open();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OpenCloseButtonText = Localize("OpenPort_close");
                    IsPortComboEnabled = false;
                    StatusText = Localize("OpenPort_open");
                });
            }
            catch (Exception)
            {
                //串口打开失败！
                Tools.MessageBox.Show(Localize("ErrorOpenPort"));
            }
            _isOpeningPort = false;
        });
    }

    /// <summary>
    /// 发送框发送命令（Ctrl+Enter / 发送按钮）
    /// </summary>
    private void SendData()
    {
        //未打开串口则不发送（也不跑脚本/不自动选端口），仅提示
        if (!EnsurePortOpenForSend())
            return;

        //连续发送模式：第一次点击开始，再次点击停止
        if (Tools.Global.setting.continuousSend)
        {
            if (_continuousRunning)
            {
                StopContinuousSending();
                return;
            }
            SendMainBoxOnce();
            if (Tools.Global.setting.continuousRepeat == 0)
                return;
            _continuousRunning = true;
            _continuousRemaining =
                Tools.Global.setting.continuousRepeat < 0 ? -1 : Tools.Global.setting.continuousRepeat;
            _continuousNextDue = DateTime.UtcNow.AddMilliseconds(
                NormalizeInterval(Tools.Global.setting.continuousDelayMs)
            );
            _timedSendTimer.Start();
            return;
        }

        SendMainBoxOnce();
    }

    /// <summary>发送主发送框内容一次（切换默认接收脚本 + 记录发送历史）</summary>
    private void SendMainBoxOnce()
    {
        Tools.Global.setting.recvScript = Tools.Global.recvScriptBackup;
        var text = SendText;
        var data = Tools.Global.GetEncoding().GetBytes(text);
        SendUartData(data);
        RecordSendHistory(text);
    }

    /// <summary>
    /// 发送前检查串口是否已打开；未打开时给出文字提示并返回 false。
    /// 供主发送框与快捷发送共用——不再自动打开串口、也不再自动选择端口。
    /// </summary>
    internal bool EnsurePortOpenForSend()
    {
        if (_uart.IsOpen())
            return true;
        Tools.MessageBox.Show(Localize("ErrorPortNotOpen"));
        return false;
    }

    // ===== 定时发送 =====

    /// <summary>把所有间隔限制到 10ms ~ 1h</summary>
    internal static int NormalizeInterval(int ms) => ms < 10 ? 10 : (ms > 3600000 ? 3600000 : ms);

    /// <summary>有定时任务时确保定时器在运行</summary>
    private void EnsureTimedSendRunning()
    {
        if (_timedSendTimer.IsEnabled)
            return;
        if (_continuousRunning || QuickSend.Items.Any(i => i.timerEnabled))
            _timedSendTimer.Start();
    }

    /// <summary>停止主发送框的“连续发送”</summary>
    internal void StopContinuousSending()
    {
        _continuousRunning = false;
        _continuousRemaining = 0;
        if (!QuickSend.Items.Any(i => i.timerEnabled))
            _timedSendTimer.Stop();
    }

    /// <summary>停止所有定时发送（快捷发送逐条定时 + 连续发送）</summary>
    internal void StopAllTimedSending()
    {
        _continuousRunning = false;
        _continuousRemaining = 0;
        _quickNextDue.Clear();
        foreach (var item in QuickSend.Items)
            item.timerEnabled = false;
        _timedSendTimer.Stop();
    }

    /// <summary>
    /// 定时器 tick：按各自间隔发送到期的快捷发送条目，并处理主发送框的连续发送。
    /// 若有到期任务而串口未打开，则提示并停止全部定时。
    /// </summary>
    private void OnTimedSendTick()
    {
        var now = DateTime.UtcNow;

        var due = new List<Model.ToSendData>();
        var anyQuick = false;
        foreach (var item in QuickSend.Items)
        {
            if (!item.timerEnabled)
            {
                _quickNextDue.Remove(item);
                continue;
            }
            anyQuick = true;
            var interval = NormalizeInterval(item.timerIntervalMs);
            if (!_quickNextDue.TryGetValue(item, out var next))
            {
                //首次启用：延迟一个间隔后再发
                _quickNextDue[item] = now.AddMilliseconds(interval);
                continue;
            }
            if (now >= next)
            {
                _quickNextDue[item] = now.AddMilliseconds(interval);
                due.Add(item);
            }
        }

        var continuousDue = _continuousRunning && now >= _continuousNextDue;

        if ((due.Count > 0 || continuousDue) && !_uart.IsOpen())
        {
            StopAllTimedSending();
            Tools.MessageBox.Show(Localize("ErrorPortNotOpen"));
            return;
        }

        foreach (var item in due)
            SendQuickItem(item);

        if (continuousDue)
        {
            SendMainBoxOnce();
            _continuousNextDue = now.AddMilliseconds(
                NormalizeInterval(Tools.Global.setting.continuousDelayMs)
            );
            if (_continuousRemaining > 0 && --_continuousRemaining <= 0)
                _continuousRunning = false;
        }

        if (!anyQuick && !_continuousRunning)
            _timedSendTimer.Stop();
    }

    /// <summary>发送一条“快捷发送”条目（含该条的接收脚本切换与 Hex/字符串解析）</summary>
    internal void SendQuickItem(Model.ToSendData data)
    {
        //如果该条指定了接收脚本，则切换；否则用默认接收脚本
        if (!string.IsNullOrEmpty(data.recvScriptPath))
        {
            if (
                !System.IO.File.Exists(
                    Tools.Global.ProfilePath
                        + $"user_script_recv_convert/{data.recvScriptPath}.lua"
                )
            )
            {
                Tools.Global.setting.recvScript = "default";
                data.recvScriptPath = "";
                if (
                    !System.IO.File.Exists(
                        Tools.Global.ProfilePath
                            + $"user_script_recv_convert/{Tools.Global.setting.recvScript}.lua"
                    )
                )
                {
                    System.IO.File.Create(
                            Tools.Global.ProfilePath
                                + $"user_script_recv_convert/{Tools.Global.setting.recvScript}.lua"
                        )
                        .Close();
                }
            }
            else
            {
                Tools.Global.setting.recvScript = data.recvScriptPath;
            }
        }
        else
        {
            Tools.Global.setting.recvScript = Tools.Global.recvScriptBackup;
        }

        var sendData = data.hex
            ? Tools.Global.Hex2Byte(data.text)
            : Tools.Global.GetEncoding().GetBytes(data.text);
        SendUartData(sendData, true);
    }

    /// <summary>
    /// 发送串口数据（完整管线：Lua 发送脚本 → appendNewline → SendData → 回显）。
    /// 未打开串口时：不自动打开、不自动选端口、不缓存，仅提示用户先打开串口。
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <param name="is_hex">强制 hex 模式标志</param>
    internal void SendUartData(byte[] data, bool? is_hex = null)
    {
        if (data == null)
            return;
        if (!EnsurePortOpenForSend())
            return;

        if (_uart.IsOpen())
        {
            byte[] dataConvert;
            try
            {
                dataConvert = LuaEnv.LuaLoader.Run(
                    $"{Tools.Global.setting.sendScript}.lua",
                    new System.Collections.ArrayList
                    {
                        "uartData",
                        is_hex == null
                            ? (
                                Tools.Global.setting.hexSend
                                    ? HexModeBytes(data)
                                    : data
                            )
                            : data,
                    }
                );
            }
            catch (Exception ex)
            {
                Tools.MessageBox.Show($"{Localize("ErrorScript")}\r\n" + ex.ToString());
                return;
            }
            if (dataConvert == null) //脚本可能没有 return / 返回 nil
                dataConvert = new byte[0];
            try
            {
                var suffix = AppendNewlineBytes(Tools.Global.setting.appendNewline);
                if (suffix.Length > 0)
                {
                    var temp = dataConvert.ToList();
                    temp.AddRange(suffix);
                    dataConvert = temp.ToArray();
                }
                _uart.SendData(dataConvert, data);
            }
            catch (Exception ex)
            {
                Tools.MessageBox.Show($"{Localize("ErrorSendFail")}\r\n" + ex.ToString());
                return;
            }
        }
    }

    /// <summary>
    /// 选中的波特率变化：预设直接应用；"自定义"弹输入框
    /// </summary>
    partial void OnSelectedBaudRateChanged(BaudRateItem? value)
    {
        if (value == null)
            return;
        if (!value.IsCustom)
        {
            Tools.Global.setting.baudRate = value.Value.Value;
        }
        else
        {
            var ret = Tools.InputDialog.OpenDialog(
                Localize("ShowBaudRate"),
                "115200",
                Localize("OtherRate")
            );
            if (ret.Item1 && int.TryParse(ret.Item2, out var br))
            {
                //把"自定义"入口项替换为实际波特率并选中，末尾补充新的自定义入口
                var idx = BaudRates.IndexOf(value);
                var item = new BaudRateItem(br.ToString(), br);
                BaudRates[idx] = item;
                if (!BaudRates.Any(x => x.IsCustom))
                    BaudRates.Add(CustomRateEntry);
                SelectedBaudRate = item;
                Tools.Global.setting.baudRate = br;
            }
            else
            {
                //取消/输入非法：提示并回到当前实际波特率对应项
                Tools.MessageBox.Show(Localize("OtherRateFail"));
                SelectedBaudRate =
                    BaudRates.FirstOrDefault(x => x.Value == Tools.Global.setting.baudRate)
                    ?? BaudRates.First(x => x.IsCustom);
            }
        }
    }
}
