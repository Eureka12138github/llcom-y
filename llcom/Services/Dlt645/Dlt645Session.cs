using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using llcom.Tools;

namespace llcom.Services.Dlt645;

/// <summary>
/// 645 串口会话：订阅原始串口数据、切帧、发送请求并收集应答。
/// 只观察数据，不修改主日志；UI 订阅 <see cref="FrameReceived"/> 后自行切回 UI 线程。
/// </summary>
public sealed class Dlt645Session : IDisposable
{
    private readonly Dlt645FrameAssembler _assembler = new Dlt645FrameAssembler();
    private readonly object _bufferLock = new object();
    private bool _started;

    /// <summary>收到一个完整且校验通过的 645 帧（可能来自串口线程）。</summary>
    public event Action<Dlt645Frame>? FrameReceived;

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        Global.uart.UartDataRecived += OnUartData;
    }

    public void Stop()
    {
        if (!_started)
            return;
        _started = false;
        Global.uart.UartDataRecived -= OnUartData;
        lock (_bufferLock)
            _assembler.Reset();
    }

    public void Dispose() => Stop();

    private void OnUartData(object sender, EventArgs e)
    {
        if (sender is not byte[] data || data.Length == 0)
            return;

        IReadOnlyList<Dlt645Frame> frames;
        lock (_bufferLock)
            frames = _assembler.Append(data);

        foreach (var frame in frames)
            FrameReceived?.Invoke(frame);
    }

    /// <summary>
    /// 发送请求并等待匹配的应答；超时返回空列表。
    /// 收到首个匹配后继续收集一小段时间，用于处理多表同时应答。
    /// </summary>
    public async Task<IReadOnlyList<Dlt645Frame>> RequestAsync(
        byte[] request,
        Func<Dlt645Frame, bool> match,
        int timeoutMs = 1200,
        CancellationToken cancellationToken = default
    )
    {
        var responses = new List<Dlt645Frame>();

        void OnFrame(Dlt645Frame frame)
        {
            if (!match(frame))
                return;
            lock (responses)
                responses.Add(frame);
        }

        FrameReceived += OnFrame;
        try
        {
            Global.uart.SendData(request);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                lock (responses)
                {
                    if (responses.Count > 0)
                        break;
                }
            }

            // 多表同时应答时，给后续帧一点时间。
            lock (responses)
            {
                if (responses.Count > 0)
                {
                    // 继续等待 120ms 收集同批应答。
                }
                else
                {
                    return Array.Empty<Dlt645Frame>();
                }
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            lock (responses)
                return responses.ToArray();
        }
        finally
        {
            FrameReceived -= OnFrame;
        }
    }
}
