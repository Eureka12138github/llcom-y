using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using llcom.Tools;

namespace llcom.ViewModels;

/// <summary>
/// 收发数据区 ViewModel（Step 8）。
/// 职责：订阅 Logger.DataShowTask，将串口/TCP/MQTT 等通道数据分发显示——
/// 分包模式（timeout≥0）写入 <see cref="ObservableCollection{T}"/>（绑定列表，虚拟化），
/// 流式模式（timeout&lt;0）追加到流式文本框（经 SetTextBridge 桥接，保持增量追加性能）。
/// 两种模式都有上限（条数/字节、字符数），超过后自动丢弃最早的记录。
/// </summary>
public partial class DataShowViewModel : ObservableObject
{
    /// <summary>全局设置（供 XAML 绑定各显示开关）</summary>
    public Model.Settings Setting { get; }

    /// <summary>串口服务（RTS/DTR 信号线转发；原直接读 <c>Tools.Global.uart</c>）</summary>
    private readonly Services.ISerialPortService _uart;

    /// <summary>日志负载字节预算（与条数上限同时生效，谁先到谁先裁）</summary>
    private const long MaxPayloadBytes = 32L * 1024 * 1024;

    /// <summary>分包模式显示条目（绑定 MainList；超过上限时裁剪最早的记录）</summary>
    public ObservableCollection<Model.DataShowItem> Items { get; } =
        new ObservableCollection<Model.DataShowItem>();

    //当前 Items 中的负载字节总数（裁剪依据）
    private long _payloadBytes;

    /// <summary>锁定滚动（绑定锁定按钮）</summary>
    [ObservableProperty]
    private bool _lockLog;

    /// <summary>RTS 信号线（绑定复选框，转发到串口服务）</summary>
    public bool Rts
    {
        get => _uart.Rts;
        set => _uart.Rts = value;
    }

    /// <summary>DTR 信号线（绑定复选框，转发到串口服务）</summary>
    public bool Dtr
    {
        get => _uart.Dtr;
        set => _uart.Dtr = value;
    }

    //流式模式文本桥（TextBox 增量追加 / 清空 / 从头裁掉 n 个字符）
    private Action<string> _appendStreamBridge = _ => { };
    private Action _clearStreamBridge = () => { };
    private Action<int> _trimStreamBridge = _ => { };
    private readonly StringBuilder _streamBuffer = new StringBuilder();

    //当前显示模式（分包/流式）切换标记
    private bool _lastPackShowMode = true;

    //清空代次：清空后丢弃已排队但尚未执行的旧数据
    private int _generation;

    //滚动请求是否已排队（合并同一帧内的多次滚动）
    private bool _scrollQueued;

    /// <summary>分包模式变化事件（切换 MainList/MainTextBox 显示）</summary>
    public event Action<bool> PackModeChanged;

    /// <summary>分包模式新增条目后触发（View 据此滚动到底部）</summary>
    public event Action ScrollRequested;

    internal DataShowViewModel(Model.Settings setting, Services.ISerialPortService uart)
    {
        Setting = setting;
        _uart = uart;
        Tools.Logger.DataShowTask += Logger_DataShowTask;
        Tools.Logger.DataClearEvent += (_, _) => Clear();
        //显示格式切换（HEX/字符串/不可见字符/编码）时刷新已有条目：
        //只失效缓存并通知绑定，真正的字符串转换推迟到条目可见时
        setting.PropertyChanged += (_, e) =>
        {
            if (
                e.PropertyName == nameof(setting.hexSend)
                || e.PropertyName == nameof(setting.recvShowHex)
                || e.PropertyName == nameof(setting.EnableSymbol)
                || e.PropertyName == nameof(setting.encoding)
            )
            {
                foreach (var it in Items)
                    it.Refresh();
            }
        };
        //上限调小后立即裁剪到新上限（不用等下一条数据）
        setting.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(setting.logMaxItems))
                TrimLogBuffer(force: true);
            else if (e.PropertyName == nameof(setting.streamMaxChars))
                TrimStreamBuffer(force: true);
        };
        Rts = false;
        Dtr = true;
        _lastPackShowMode = setting.timeout >= 0;
    }

    /// <summary>
    /// 由 View 接入流式文本框的追加/清空/头部裁剪（TextBox.AppendText 增量追加）
    /// </summary>
    public void SetTextBridge(Action<string> append, Action clear, Action<int> trimHead)
    {
        _appendStreamBridge = append;
        _clearStreamBridge = clear;
        _trimStreamBridge = trimHead;
    }

    /// <summary>清空显示（清空列表 + 流式缓冲 + 流式文本框）</summary>
    public void Clear()
    {
        //递增代次：丢弃已排队但尚未执行的旧数据，避免清空后又冒出来
        _generation++;
        Items.Clear();
        _payloadBytes = 0;
        _streamBuffer.Clear();
        _clearStreamBridge();
    }

    /// <summary>
    /// 保存显示内容到文件（分包模式遍历 Items，流式模式输出缓冲文本）
    /// </summary>
    public void SaveLog(string path)
    {
        var needPack = Setting.timeout >= 0;
        using (var sw = new System.IO.StreamWriter(path, false, Encoding.UTF8))
        {
            if (!needPack)
            {
                sw.Write(_streamBuffer.ToString());
            }
            else
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    var item = Items[i];
                    if (string.IsNullOrEmpty(item.RawTitle))
                        sw.WriteLine(
                            item.TimeText
                                + (item.ArrowText == " → " ? " [send] " : " [recv] ")
                                + item.DataText
                        );
                    else
                        sw.WriteLine(item.TimeText + " [" + item.RawTitle + "] " + item.RawText);
                }
            }
            sw.Flush();
        }
    }

    /// <summary>
    /// 数据到达事件处理（Logger.DataShowTask）
    /// </summary>
    private void Logger_DataShowTask(object sender, DataShow e)
    {
        //Logger 事件可能来自后台线程（串口接收线程）：
        //异步投递到 UI 线程（Background 优先级，让拖拽/输入优先），避免阻塞接收线程
        var generation = _generation;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                //期间已清空：丢弃排队中的旧数据
                if (generation != _generation)
                    return;

                //先判断下要不要清空/切换显示模式
                var needPack = Setting.timeout >= 0;
                if (_lastPackShowMode != needPack)
                {
                    _lastPackShowMode = needPack;
                    Items.Clear();
                    _payloadBytes = 0;
                    _streamBuffer.Clear();
                    PackModeChanged?.Invoke(needPack);
                }

                //如果不开回显，就别打印
                if (!Setting.showSend && e is DataShowPara para && para.send)
                    return;

                //显示到列表
                if (!needPack && e is not DataShowRaw) //不分包模式
                {
                    var isSend = e is DataShowPara para2 && para2.send;
                    var showHex = isSend ? Setting.hexSend : Setting.recvShowHex;
                    var dataText = showHex
                        ? Tools.Global.Byte2Hex(e.data, " ", e.data.Length) + " "
                        : Tools.Global.Byte2Readable(e.data, e.data.Length);
                    AppendStream(dataText);
                }
                else //分包模式
                {
                    var item = e is DataShowRaw raw
                        ? new Model.DataShowItem(raw.title, e.data, e.time, raw.color)
                        : new Model.DataShowItem(e.data, e.time, (e as DataShowPara).send);
                    if (item != null)
                        AppendPackItem(item);
                }
            }),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    /// <summary>分包模式追加一条日志并按上限裁剪（需在 UI 线程调用）。</summary>
    internal void AppendPackItem(Model.DataShowItem item)
    {
        Items.Add(item);
        _payloadBytes += item.PayloadLength;
        TrimLogBuffer();
        RequestScroll();
    }

    /// <summary>流式模式追加文本并按字符上限裁剪（需在 UI 线程调用）。</summary>
    internal void AppendStreamText(string text) => AppendStream(text);

    /// <summary>合并滚动请求：同一帧内多条数据只触发一次滚动</summary>
    private void RequestScroll()
    {
        if (_scrollQueued || Application.Current == null)
            return;
        _scrollQueued = true;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _scrollQueued = false;
                ScrollRequested?.Invoke();
            }),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    /// <summary>
    /// 流式模式追加文本；超过字符上限时从头部裁掉最早的文本（缓冲与 TextBox 同步）。
    /// </summary>
    private void AppendStream(string text)
    {
        _streamBuffer.Append(text);
        _appendStreamBridge(text);
        TrimStreamBuffer();
    }

    /// <summary>
    /// 流式缓冲按字符上限裁头。
    /// 默认留 10% 余量再裁，避免每包都触发 TextBox 全量重排。
    /// </summary>
    private void TrimStreamBuffer(bool force = false)
    {
        var max = Math.Max(Model.Settings.StreamMaxCharsMin, Setting.streamMaxChars);
        var limit = force ? max : max + Math.Max(10_000, max / 10);
        if (_streamBuffer.Length <= limit)
            return;

        var remove = _streamBuffer.Length - max;
        _streamBuffer.Remove(0, remove);
        _trimStreamBridge(remove);
    }

    /// <summary>
    /// 分包日志裁剪：**条数上限与负载字节预算同时生效，谁先到谁先裁**。
    /// 逐条删除最早的记录（WPF 的 ListCollectionView 不支持“范围删除”通知），
    /// 留少量余量后再裁，避免每来一条都触发删除。
    /// </summary>
    private void TrimLogBuffer(bool force = false)
    {
        var maxItems = Math.Max(Model.Settings.LogMaxItemsMin, Setting.logMaxItems);
        // 余量：攒够 100 条再裁，减少集合变更次数
        var itemLimit = force ? maxItems : maxItems + 100;
        var byteLimit = force ? MaxPayloadBytes : MaxPayloadBytes + MaxPayloadBytes / 20;
        if (Items.Count <= itemLimit && _payloadBytes <= byteLimit)
            return;

        //从头部逐条计算，直到同时满足「条数 ≤ 上限」且「负载 ≤ 预算」
        var remove = 0;
        var bytes = _payloadBytes;
        while (remove < Items.Count && (Items.Count - remove > maxItems || bytes > MaxPayloadBytes))
        {
            bytes -= Items[remove].PayloadLength;
            remove++;
        }
        if (remove <= 0)
            return;

        for (var i = 0; i < remove; i++)
            Items.RemoveAt(0);
        _payloadBytes = bytes;
    }
}
