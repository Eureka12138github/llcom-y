using System;
using System.Collections.Generic;

namespace llcom.Services.Dlt645;

/// <summary>
/// 串口字节流 → 645 帧切分。
/// 处理前导 FE、粘包、分包、噪声；校验失败的帧会丢弃并继续找下一帧。
/// </summary>
public sealed class Dlt645FrameAssembler
{
    private readonly List<byte> _buffer = new List<byte>();
    private const int MaxBuffer = 16384;

    public IReadOnlyList<Dlt645Frame> Append(byte[]? data)
    {
        var frames = new List<Dlt645Frame>();
        if (data != null && data.Length > 0)
            _buffer.AddRange(data);

        while (TryTakeFrame(out var frame))
        {
            if (frame != null)
                frames.Add(frame);
        }

        if (_buffer.Count > MaxBuffer)
            _buffer.Clear();

        return frames;
    }

    public void Reset() => _buffer.Clear();

    private bool TryTakeFrame(out Dlt645Frame? frame)
    {
        frame = null;

        while (true)
        {
            var start = _buffer.IndexOf(Dlt645Codec.StartByte);
            if (start < 0)
            {
                // 还没有起始符：保留末尾连续的 FE（可能是下一帧前导），丢弃其余噪声。
                var keepFrom = _buffer.Count;
                while (keepFrom > 0 && _buffer[keepFrom - 1] == Dlt645Codec.PreambleByte)
                    keepFrom--;
                if (keepFrom > 0)
                    _buffer.RemoveRange(0, keepFrom);
                if (_buffer.Count > 16)
                    _buffer.Clear();
                return false;
            }

            // 保留紧邻 68 的前导 FE，丢弃更早的噪声。
            var preambleStart = start;
            while (preambleStart > 0 && _buffer[preambleStart - 1] == Dlt645Codec.PreambleByte)
                preambleStart--;
            if (preambleStart > 0)
            {
                _buffer.RemoveRange(0, preambleStart);
                start -= preambleStart;
            }

            // 68 + 6 字节地址 + 68 + C + L = start+9 至少要到 L 字节。
            if (_buffer.Count < start + 10)
                return false;

            var length = _buffer[start + 9];
            var totalLength = start + 12 + length;
            if (_buffer.Count < totalLength)
                return false;

            var raw = new byte[totalLength];
            _buffer.CopyTo(0, raw, 0, totalLength);
            _buffer.RemoveRange(0, totalLength);

            if (Dlt645Codec.TryParse(raw, out var parsed, out _) && parsed != null)
            {
                frame = parsed;
                return true;
            }

            // 校验/格式错误：丢弃该帧，继续在同一缓冲里找下一帧。
        }
    }
}
