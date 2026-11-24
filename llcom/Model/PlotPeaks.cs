using System;
using System.Collections.Generic;

namespace llcom.Model;

/// <summary>
/// 曲线工具的简单峰值查找，用于「间隔测量 → 对齐相邻峰」。
/// 只处理当前可见区间：轻度平滑、幅度阈值和最小间距，避免把噪声当成峰；
/// 不追求通用信号分析，也暂不处理复杂调制/平台信号。
/// </summary>
internal static class PlotPeaks
{
    /// <summary>
    /// 在可见索引区间内找一对相邻峰，优先选择中点离 <paramref name="targetIndex"/> 最近的一对。
    /// 返回的索引是相对 <paramref name="values"/> 的数组下标。
    /// </summary>
    public static bool TryFindAdjacentPeaks(
        double[] values,
        int start,
        int end,
        long targetIndex,
        out int first,
        out int second
    )
    {
        first = -1;
        second = -1;
        if (values == null || start < 0 || end >= values.Length || end - start + 1 < 5)
            return false;

        var count = end - start + 1;
        //平滑半径随可见点数增加，但设上限，避免把真实峰抹平
        var radius = Math.Max(1, Math.Min(10, count / 200));
        var smooth = new double[count];
        for (var i = 0; i < count; i++)
        {
            var lo = Math.Max(0, i - radius);
            var hi = Math.Min(count - 1, i + radius);
            var sum = 0.0;
            var n = 0;
            for (var j = lo; j <= hi; j++)
            {
                var value = values[start + j];
                if (double.IsNaN(value) || double.IsInfinity(value))
                    continue;
                sum += value;
                n++;
            }
            smooth[i] = n > 0 ? sum / n : double.NaN;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        for (var i = 0; i < count; i++)
        {
            var value = smooth[i];
            if (double.IsNaN(value))
                continue;
            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }
        if (min > max || max - min <= 1e-12)
            return false;

        //只接受明显高于底部的局部极大值，过滤小噪声
        var threshold = min + (max - min) * 0.25;
        var peaks = new List<int>();
        for (var i = radius; i < count - radius; i++)
        {
            var value = smooth[i];
            if (double.IsNaN(value) || value < threshold)
                continue;

            var isPeak = true;
            for (var j = i - radius; j <= i + radius; j++)
            {
                if (j == i)
                    continue;
                if (smooth[j] > value)
                {
                    isPeak = false;
                    break;
                }
            }
            if (!isPeak)
                continue;

            //平台顶部只保留最后一个点，避免同一周期被算成多个峰
            if (smooth[i + 1] >= value)
                continue;
            peaks.Add(i);
        }

        if (peaks.Count < 2)
            return false;

        //相邻峰之间至少隔开一小段，太近的只保留更高的那个
        var minDistance = Math.Max(2, count / 50);
        var filtered = new List<int>();
        foreach (var peak in peaks)
        {
            if (filtered.Count == 0)
            {
                filtered.Add(peak);
                continue;
            }

            var last = filtered[filtered.Count - 1];
            if (peak - last >= minDistance)
            {
                filtered.Add(peak);
            }
            else if (smooth[peak] > smooth[last])
            {
                filtered[filtered.Count - 1] = peak;
            }
        }

        if (filtered.Count < 2)
            return false;

        //选相邻峰里中点最接近当前测量中心（或视图中心）的一对，避免线总落在画面边缘
        var relativeTarget = targetIndex - start;
        var target = (int)Math.Max(0, Math.Min(count - 1, relativeTarget));
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < filtered.Count - 1; i++)
        {
            var midpoint = (filtered[i] + filtered[i + 1]) / 2.0;
            var distance = Math.Abs(midpoint - target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        first = start + filtered[best];
        second = start + filtered[best + 1];
        return second > first;
    }
}