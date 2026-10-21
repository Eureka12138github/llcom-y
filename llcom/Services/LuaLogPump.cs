using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace llcom.Services;

/// <summary>
/// Lua 日志的缓冲与后台泵（原 <c>MainWindow.LuaLogPrintTask</c> + 相关字段）。
/// 日志由 Lua 线程经 <see cref="Enqueue"/> 入缓冲，后台线程按批经 <see cref="FlushRequested"/>
/// 交给 UI 渲染，避免每条日志都跨线程操作控件。
/// 渲染（清空阈值、滚动）仍留在 View，本类只负责收集/节流。
/// </summary>
internal sealed class LuaLogPump
{
    private readonly AutoResetEvent _queue = new AutoResetEvent(false);
    private readonly List<string> _buffer = new List<string>();
    private readonly object _lock = new object();

    /// <summary>一批日志就绪（参数为已拼接好的文本），由 UI 线程处理。</summary>
    public event Action<string> FlushRequested;

    /// <summary>是否允许输出（暂停时为 false：仍计数，但不渲染）。</summary>
    public bool Printable { get; set; } = true;

    /// <summary>自上次清空以来已输出的行数（供 UI 判断是否需要清空）。</summary>
    public int Count { get; private set; }

    /// <summary>入队一条日志（Lua 线程调用）。缓冲超限时丢弃并提示。</summary>
    public void Enqueue(string log)
    {
        if (log == null)
            return;
        lock (_lock)
        {
            if (_buffer.Count > 500)
            {
                _buffer.Clear();
                _buffer.Add("too many logs!");
                //延时 0.5 秒，防止卡住 UI 线程
                Thread.Sleep(500);
            }
            else
                _buffer.Add(log);
        }
        _queue.Set();
    }

    /// <summary>重置计数与缓冲（停止脚本 / 自动清空时调用）。</summary>
    public void Reset()
    {
        Count = 0;
        lock (_lock)
            _buffer.Clear();
    }

    /// <summary>启动后台泵线程。</summary>
    public void Start() => Task.Run(Pump);

    private void Pump()
    {
        _queue.Reset();
        Tools.Global.ProgramClosedEvent += (_, _) => _queue.Set();
        while (true)
        {
            _queue.WaitOne();
            if (Tools.Global.isMainWindowsClosed)
                return;

            var logsb = new StringBuilder();
            lock (_lock)
            {
                for (int i = 0; i < _buffer.Count; i++)
                {
                    logsb.AppendLine(_buffer[i]);
                    Count++;
                }
                _buffer.Clear();
            }

            if (!Printable)
                continue;
            if (logsb.Length == 0)
                continue;

            FlushRequested?.Invoke(logsb.ToString());
            //合并刷新：50ms 一批，避免日志刷屏和拖拽/输入抢 UI 线程
            Thread.Sleep(50);
        }
    }
}