using System;
using System.Collections.Generic;

namespace llcom.Services.Dlt645;

/// <summary>主日志的一条 645 解码结果。</summary>
public sealed class Dlt645LogEntry
{
    public Dlt645LogEntry(byte[] data, bool decoded)
    {
        Data = data;
        Decoded = decoded;
    }

    public byte[] Data { get; }

    /// <summary>true 表示已减 33H 并重算 CS；false 表示原样输出。</summary>
    public bool Decoded { get; }
}

/// <summary>
/// 把串口原始字节转换成主日志可显示的 645 帧：
/// 完整且校验通过的帧 → 数据域减 33H、重算 CS；
/// 非 645/半帧/坏帧 → 原样输出（半帧由调用方超时 Flush）。
/// </summary>
public sealed class Dlt645LogDecoder
{
    private readonly List<byte> _buffer = new List<byte>();
    private const int MaxBuffer = 16384;

    public bool HasPending => _buffer.Count > 0;

    public IReadOnlyList<Dlt645LogEntry> Append(byte[]? data)
    {
        var output = new List<Dlt645LogEntry>();
        if (data != null && data.Length > 0)
            _buffer.AddRange(data);

        while (TryExtract(output)) { }

        // 没有起始符时保留末尾 FE（可能是下一帧前导），其他噪声原样输出。
        if (_buffer.IndexOf(Dlt645Codec.StartByte) < 0)
            FlushNoiseBeforePreamble(output);

        if (_buffer.Count > MaxBuffer)
            FlushAll(output);

        return output;
    }

    /// <summary>超时/关闭解码时把剩余半帧原样输出。</summary>
    public IReadOnlyList<Dlt645LogEntry> Flush()
    {
        var output = new List<Dlt645LogEntry>();
        FlushAll(output);
        return output;
    }

    public void Reset() => _buffer.Clear();

    private bool TryExtract(List<Dlt645LogEntry> output)
    {
        var start = _buffer.IndexOf(Dlt645Codec.StartByte);
        if (start < 0)
            return false;

        var preambleStart = start;
        while (preambleStart > 0 && _buffer[preambleStart - 1] == Dlt645Codec.PreambleByte)
            preambleStart--;

        if (preambleStart > 0)
        {
            output.Add(new Dlt645LogEntry(_buffer.GetRange(0, preambleStart).ToArray(), false));
            _buffer.RemoveRange(0, preambleStart);
            start -= preambleStart;
        }

        if (_buffer.Count < start + 10)
            return false;

        var length = _buffer[start + 9];
        var totalLength = start + 12 + length;
        if (_buffer.Count < totalLength)
            return false;

        var raw = _buffer.GetRange(0, totalLength).ToArray();
        _buffer.RemoveRange(0, totalLength);

        if (Dlt645Codec.TryParse(raw, out var frame, out _) && frame != null)
            output.Add(new Dlt645LogEntry(DecodeFrame(raw, frame), true));
        else
            output.Add(new Dlt645LogEntry(raw, false));

        return true;
    }

    private static byte[] DecodeFrame(byte[] raw, Dlt645Frame frame)
    {
        var decoded = (byte[])raw.Clone();
        var dataStart = frame.PreambleCount + 10;
        Array.Copy(frame.Data, 0, decoded, dataStart, frame.Data.Length);

        var checksumIndex = dataStart + frame.Data.Length;
        var checksum = 0;
        for (var i = frame.PreambleCount; i < checksumIndex; i++)
            checksum += decoded[i];
        decoded[checksumIndex] = unchecked((byte)checksum);
        return decoded;
    }

    private void FlushNoiseBeforePreamble(List<Dlt645LogEntry> output)
    {
        var keepFrom = _buffer.Count;
        while (keepFrom > 0 && _buffer[keepFrom - 1] == Dlt645Codec.PreambleByte)
            keepFrom--;

        if (keepFrom > 0)
        {
            output.Add(new Dlt645LogEntry(_buffer.GetRange(0, keepFrom).ToArray(), false));
            _buffer.RemoveRange(0, keepFrom);
        }
    }

    private void FlushAll(List<Dlt645LogEntry> output)
    {
        if (_buffer.Count == 0)
            return;
        output.Add(new Dlt645LogEntry(_buffer.ToArray(), false));
        _buffer.Clear();
    }
}
