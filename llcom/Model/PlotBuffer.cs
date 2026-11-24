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

    /// <summary>每条线对应的样本时间（秒，自曲线会话开始）；与 Values 同步搬移。</summary>
    public double[][] Times { get; }

    /// <summary>每条线当前有效点数。</summary>
    public int[] Counts { get; }

    /// <summary>索引 0 对应的绝对样本序号。</summary>
    public long[] BaseX { get; }

    /// <summary>所有线中最大的绝对样本序号；没有数据时为 -1。</summary>
    public long LatestX { get; private set; } = -1;

    /// <summary>所有线中最大的样本时间（秒）；没有数据时为 -1。</summary>
    public double LatestTime { get; private set; } = -1;

    public PlotBuffer(int capacity)
    {
        Capacity = Math.Max(2, capacity);
        Values = new double[LineCount][];
        Times = new double[LineCount][];
        Counts = new int[LineCount];
        BaseX = new long[LineCount];
        for (var i = 0; i < LineCount; i++)
        {
            Values[i] = new double[Capacity];
            Times[i] = new double[Capacity];
        }
    }

    /// <summary>追加一个点；NaN/Infinity 和非法线号会被忽略。时间为自动递增秒数（测试/索引模式）。</summary>
    public void Add(int line, double value) =>
        Add(
            line,
            value,
            line >= 0 && line < LineCount && Counts[line] > 0
                ? Times[line][Counts[line] - 1] + 1
                : 0
        );

    /// <summary>追加一个点并记录样本时间（秒）；时间会保证每条线严格递增。</summary>
    public void Add(int line, double value, double timeSeconds)
    {
        if (line < 0 || line >= LineCount)
            return;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;

        var values = Values[line];
        var times = Times[line];
        if (Counts[line] == Capacity)
        {
            var drop = Math.Max(1, Capacity / TrimDivisor);
            Array.Copy(values, drop, values, 0, Capacity - drop);
            Array.Copy(times, drop, times, 0, Capacity - drop);
            Counts[line] -= drop;
            BaseX[line] += drop;
        }

        if (double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds))
            timeSeconds = Counts[line] > 0 ? times[Counts[line] - 1] + 1e-6 : 0;
        else if (Counts[line] > 0 && timeSeconds <= times[Counts[line] - 1])
            timeSeconds = times[Counts[line] - 1] + 1e-6;
        if (timeSeconds < 0)
            timeSeconds = 0;

        var index = Counts[line]++;
        values[index] = value;
        times[index] = timeSeconds;
        var x = BaseX[line] + index;
        if (x > LatestX)
            LatestX = x;
        if (timeSeconds > LatestTime)
            LatestTime = timeSeconds;
    }

    /// <summary>清空所有线的数据和样本序号。</summary>
    public void Clear()
    {
        for (var line = 0; line < LineCount; line++)
        {
            Counts[line] = 0;
            BaseX[line] = 0;
            Array.Clear(Values[line], 0, Values[line].Length);
            Array.Clear(Times[line], 0, Times[line].Length);
        }
        LatestX = -1;
        LatestTime = -1;
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

    /// <summary>取某条线在给定时间范围（秒）内的有效索引区间（闭区间）。</summary>
    public bool TryGetVisibleTimeRange(
        int line,
        double timeMin,
        double timeMax,
        out int start,
        out int end
    )
    {
        start = 0;
        end = -1;
        if (line < 0 || line >= LineCount || Counts[line] == 0)
            return false;

        var times = Times[line];
        var count = Counts[line];
        start = LowerBound(times, count, timeMin);
        end = UpperBound(times, count, timeMax) - 1;
        if (start < 0)
            start = 0;
        if (end >= count)
            end = count - 1;
        return start <= end;
    }

    /// <summary>某条线当前最旧的样本时间（秒）；没有数据时返回 0。</summary>
    public double OldestTime(int line) =>
        line < 0 || line >= LineCount || Counts[line] == 0 ? 0 : Times[line][0];

    /// <summary>某条线当前最新的样本时间（秒）；没有数据时返回 -1。</summary>
    public double NewestTime(int line) =>
        line < 0 || line >= LineCount || Counts[line] == 0
            ? -1
            : Times[line][Counts[line] - 1];

    /// <summary>
    /// 估算“最近 points 个点”对应的时间宽度（秒），用于时间轴默认窗口。
    /// 取当前点数最多的那条线，避免稀疏线的取样跨度失真。
    /// </summary>
    public double GetTimeWindowSeconds(int points)
    {
        var line = -1;
        var best = 0;
        for (var i = 0; i < LineCount; i++)
        {
            if (Counts[i] > best)
            {
                best = Counts[i];
                line = i;
            }
        }
        if (line < 0)
            return 1;

        var count = Counts[line];
        var take = Math.Min(Math.Max(2, points), count);
        if (take <= 1)
            return 0.5;

        var window = Times[line][count - 1] - Times[line][count - take];
        return window > 1e-6 ? window : 0.5;
    }

    private static int LowerBound(double[] values, int count, double target)
    {
        var lo = 0;
        var hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] < target)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static int UpperBound(double[] values, int count, double target)
    {
        var lo = 0;
        var hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] <= target)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
}