using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using llcom.Tools;

namespace llcom.ViewModels;

/// <summary>
/// 收发数据区 ViewModel（Step 8）。
/// 职责：订阅 Logger.DataShowTask，将串口/TCP/MQTT 等通道数据分发显示——
/// 分包模式（timeout≥0）写入 <see cref="Tools.BulkObservableCollection{T}"/>（绑定列表，虚拟化），
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

    /// <summary>分包模式显示条目（绑定 MainList；超过上限时批量裁剪最早的记录）</summary>
    public Tools.BulkObservableCollection<Model.DataShowItem> Items { get; } = new();

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

    //待显示数据队列：把原来“每条数据一个 Dispatcher 任务”改成“一批一个任务”。
    //高频收发（如 200ms 轮询的 Modbus 脚本：发送+接收各一条 = 10 条/秒）时，
    //逐条投递会把 UI 队列塞满，渲染速度跟不上产生速度，主日志/Lua 日志的显示
    //会整体滞后（脚本早已跑完，界面还停在旧进度、提示却已弹出）。
    private readonly ConcurrentQueue<(DataShow show, int generation)> _pendingShows = new();
    private int _showFlushQueued; //0=当前没有待执行的刷新任务，1=已排队

    /// <summary>单批最多处理多少条（时间预算之外的硬上限）。</summary>
    private const int MaxShowsPerFlush = 200;

    /// <summary>单批时间预算（毫秒）：到点先让出 UI 线程，剩余下一批继续。</summary>
    private const int FlushBudgetMs = 10;

    /// <summary>分包模式变化事件（切换 MainList/MainTextBox 显示）</summary>
    public event Action<bool>? PackModeChanged;

    /// <summary>分包模式新增条目后触发（View 据此滚动到底部）</summary>
    public event Action? ScrollRequested;

    /// <summary>裁剪前触发（View 可在此记录滚动锚点与选中项）；参数为本次计划删除的条数。</summary>
    public event Action<int>? TrimStarting;

    /// <summary>裁剪完成（集合已发出一次 Reset）；View 可在此恢复滚动位置。</summary>
    public event Action? Trimmed;

    internal DataShowViewModel(Model.Settings setting, Services.ISerialPortService uart)
    {
        Setting = setting;
        _uart = uart;
        Tools.Logger.DataShowTask += Logger_DataShowTask;
        Tools.Logger.DataClearEvent += (_, _) => Clear();
        Services.ThemeService.ThemeChanged += (_, _) =>
        {
            foreach (var item in Items)
                item.RefreshTheme();
        };
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
        Dtr = false;
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

    /// <summary>当前主日志是否有可导出的内容（分包看条目，流式看缓冲文本）。</summary>
    public bool HasLogContent =>
        _lastPackShowMode ? Items.Count > 0 : _streamBuffer.Length > 0;

    /// <summary>
    /// 保存显示内容到文件：**与主日志逐行一致**（时间 + 方向 + 当前 Hex/字符串表示）。
    /// 分包模式遍历 Items，流式模式输出缓冲文本；模式取实际显示模式，
    /// 避免用户刚改完「串口超时」但还没收到新数据时导出内容和界面对不上。
    /// </summary>
    public void SaveLog(string path)
    {
        using (var sw = new System.IO.StreamWriter(path, false, Encoding.UTF8))
        {
            if (!_lastPackShowMode)
            {
                sw.Write(_streamBuffer.ToString());
            }
            else
            {
                for (int i = 0; i < Items.Count; i++)
                    sw.WriteLine(Items[i].ToLogText());
            }
            sw.Flush();
        }
    }

    /// <summary>
    /// 数据到达事件处理（Logger.DataShowTask）
    /// </summary>
    private void Logger_DataShowTask(object sender, DataShow e)
    {
        //Logger 事件可能来自后台线程（串口接收线程 / Lua 定时器线程）：
        //先入队，再确保只有一个刷新任务在 UI 队列里，避免逐条投递撑爆 Dispatcher
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        _pendingShows.Enqueue((e, _generation));
        if (Interlocked.Exchange(ref _showFlushQueued, 1) == 0)
        {
            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(FlushPendingShows)
            );
        }
    }

    /// <summary>
    /// 在 UI 线程分帧处理待显示数据：一批一个 Dispatcher 任务、限时限量，
    /// 处理完仍有剩余就再排下一批（保持 Background 优先级，不抢输入/渲染）。
    /// </summary>
    private void FlushPendingShows()
    {
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var processed = 0;
            while (
                processed < MaxShowsPerFlush
                && watch.ElapsedMilliseconds < FlushBudgetMs
                && _pendingShows.TryDequeue(out var pending)
            )
            {
                processed++;
                //期间已清空：丢弃排队中的旧数据
                if (pending.generation != _generation)
                    continue;
                ShowPending(pending.show);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _showFlushQueued, 0);

            if (
                !_pendingShows.IsEmpty
                && Interlocked.Exchange(ref _showFlushQueued, 1) == 0
            )
            {
                Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(FlushPendingShows)
                );
            }
        }
    }

    /// <summary>显示一条数据（需在 UI 线程调用）。</summary>
    private void ShowPending(DataShow e)
    {
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
                : new Model.DataShowItem(
                    e.data,
                    e.time,
                    (e as DataShowPara)?.send ?? false
                );
            if (item != null)
                AppendPackItem(item);
        }
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
    /// 超出后一次性删到“低水位”（按 <see cref="Model.Settings.logTrimPercent"/> 决定清掉多少），
    /// 并用一次批量删除（单次 Reset 通知）代替逐条 RemoveAt；
    /// 否则列表贴在上限附近时，每 100 条就要逐条删 100 多条，5 万条规模下会明显卡顿。
    /// </summary>
    private void TrimLogBuffer(bool force = false)
    {
        var maxItems = Math.Max(Model.Settings.LogMaxItemsMin, Setting.logMaxItems);
        var percent = Math.Min(
            Model.Settings.LogTrimPercentMax,
            Math.Max(Model.Settings.LogTrimPercentMin, Setting.logTrimPercent)
        );

        // 余量：轻微超出上限后再裁，减少检查频率；force（用户调小上限）时立即裁。
        var slack = force ? 0 : Math.Min(100, Math.Max(10, maxItems / 100));
        var countLimit = maxItems + slack;
        var byteLimit = force ? MaxPayloadBytes : MaxPayloadBytes + MaxPayloadBytes / 20;
        if (Items.Count <= countLimit && _payloadBytes <= byteLimit)
            return;

        // 目标：按比例清到低水位；100% 表示全部清空。
        var targetCount =
            force ? maxItems : (int)((long)maxItems * (100 - percent) / 100);

        // 字节也留 10% 余量，避免大包数据每来一条都触发裁剪。
        var targetBytes = force ? MaxPayloadBytes : MaxPayloadBytes / 10 * 9;

        //从头部逐条计算，直到同时满足「条数 ≤ 目标」且「负载 ≤ 目标预算」
        var remove = 0;
        var bytes = _payloadBytes;
        while (remove < Items.Count && (Items.Count - remove > targetCount || bytes > targetBytes))
        {
            bytes -= Items[remove].PayloadLength;
            remove++;
        }
        if (remove <= 0)
            return;

        TrimStarting?.Invoke(remove);
        Items.TrimStart(remove);
        _payloadBytes = bytes;
        Trimmed?.Invoke();
    }
}
