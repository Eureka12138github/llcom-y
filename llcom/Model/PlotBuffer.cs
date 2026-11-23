using System;

namespace llcom.Model;

/// <summary>
/// 曲线数据缓冲：每条线保留最近 <see cref="Capacity"/> 个点，按从旧到新排列。
/// X 使用绝对样本序号（<see cref="BaseX"/> + 索引）；满容量时一次丢弃 5% 的最旧点，
/// 避免每来一个点就搬移整个数组。
/// </summary>
internal sealed class PlotBuffer
{
    /// <summary>曲线工具支持的线数量。</summary>
    public const int LineCount = 10;

    //满容量时一次丢弃的比例（1/20 = 5%）
    private const int TrimDivisor = 20;

    /// <summary>每条线最多保留多少个点。</summary>
    public int Capacity { get; }

    /// <summary>每条线的数据，长度均为 Capacity；有效数据在索引 0..Counts[line)-1。</summary>
    public double[][] Values { get; }

    /// <summary>每条线当前有效点数。</summary>
    public int[] Counts { get; }

    /// <summary>索引 0 对应的绝对样本序号。</summary>
    public long[] BaseX { get; }

    /// <summary>所有线中最大的绝对样本序号；没有数据时为 -1。</summary>
    public long LatestX { get; private set; } = -1;

    public PlotBuffer(int capacity)
    {
        Capacity = Math.Max(2, capacity);
        Values = new double[LineCount][];
        Counts = new int[LineCount];
        BaseX = new long[LineCount];
        for (var i = 0; i < LineCount; i++)
            Values[i] = new double[Capacity];
    }

    /// <summary>追加一个点；NaN/Infinity 和非法线号会被忽略。</summary>
    public void Add(int line, double value)
    {
        if (line < 0 || line >= LineCount)
            return;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;

        var values = Values[line];
        if (Counts[line] == Capacity)
        {
            var drop = Math.Max(1, Capacity / TrimDivisor);
            Array.Copy(values, drop, values, 0, Capacity - drop);
            Counts[line] -= drop;
            BaseX[line] += drop;
        }

        var index = Counts[line]++;
        values[index] = value;
        var x = BaseX[line] + index;
        if (x > LatestX)
            LatestX = x;
    }

    /// <summary>清空所有线的数据和样本序号。</summary>
    public void Clear()
    {
        for (var line = 0; line < LineCount; line++)
        {
            Counts[line] = 0;
            BaseX[line] = 0;
            Array.Clear(Values[line], 0, Values[line].Length);
        }
        LatestX = -1;
    }

    /// <summary>
    /// 取某条线在给定 X 范围内的有效索引区间（闭区间）。
    /// 没有数据或范围外没有点时返回 false。
    /// </summary>
    public bool TryGetVisibleRange(
        int line,
        double xMin,
        double xMax,
        out int start,
        out int end
    )
    {
        start = 0;
        end = -1;
        if (line < 0 || line >= LineCount || Counts[line] == 0)
            return false;

        var first = (int)Math.Ceiling(xMin - BaseX[line]);
        var last = (int)Math.Floor(xMax - BaseX[line]);
        start = Math.Max(0, first);
        end = Math.Min(Counts[line] - 1, last);
        return start <= end;
    }

    /// <summary>某条线当前最旧的绝对样本序号；没有数据时返回 0。</summary>
    public long OldestX(int line) =>
        line < 0 || line >= LineCount || Counts[line] == 0 ? 0 : BaseX[line];

    /// <summary>某条线当前最新的绝对样本序号；没有数据时返回 -1。</summary>
    public long NewestX(int line) =>
        line < 0 || line >= LineCount || Counts[line] == 0
            ? -1
            : BaseX[line] + Counts[line] - 1;
}