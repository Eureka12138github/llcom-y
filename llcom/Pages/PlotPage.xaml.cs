using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ScottPlot;

namespace llcom.Pages;

/// <summary>
/// 曲线页：接收 Lua 的 apiAddPoint 数据并绘制。
/// 数据先在并发队列中累积，再由 UI 定时器批量写入并刷新，避免 Lua 线程直接修改绘图数组。
/// 数据不足 1000 点时按实际点数自动缩放 X 轴，填满后变为最近 1000 点的滚动窗口。
/// </summary>
public partial class PlotPage : Page
{
    //最多十个图像
    private readonly double[][] Data = new double[10][];
    private double[]? DataX = null;

    //最大点数量
    private const int MaxPoints = 1000;

    //每条线已加入的点数（用于 X 轴缩放 / Y 轴只看有效数据）
    private readonly int[] _pointCounts = new int[10];
    private int _maxPointCount;
    private bool _autoFitDone;

    //Lua 线程只入队，UI 定时器统一消费
    private readonly ConcurrentQueue<(double Value, int Line)> _pendingPoints = new();

    private ScottPlot.Plottable.Crosshair? ch = null;

    private readonly ScottPlot.Styles.IStyle[] Styles = ScottPlot.Style.GetStyles();
    private int StyleNow = -1;

    private bool NeedRefresh = true;

    //定时刷新（原为后台线程 + Sleep 忙等，改为 UI 定时器）
    private readonly DispatcherTimer _refreshTimer =
        new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };

    private bool first = true;

    public PlotPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!first)
            return;
        first = false;

        DataX = new double[MaxPoints];
        for (int i = 0; i < MaxPoints; i++)
            DataX[i] = i - MaxPoints + 1;
        for (int i = 0; i < Data.Length; i++)
        {
            if (Data[i] == null)
                Data[i] = new double[MaxPoints];
            Plot.Plot.AddSignalXY(DataX, Data[i]);
        }

        ch = Plot.Plot.AddCrosshair(0, 0);
        ch.Color = System.Drawing.Color.LightGray;
        ch.LineWidth = 2;

        ApplyTheme();
        FitView();

        llcom.Services.ThemeService.ThemeChanged += (_, _) => ApplyTheme();

        _refreshTimer.Tick += (_, _) => RefreshTick();
        _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();

        //Lua 回调可能来自脚本线程：只入队，不在回调里碰 UI/绘图数组
        LuaEnv.LuaApis.LinePlotAdd += (_, e) => _pendingPoints.Enqueue((e.N, e.Line));
    }

    /// <summary>让 ScottPlot 跟随应用主题（深色用 Black 风格）。</summary>
    private void ApplyTheme()
    {
        var dark = llcom.Services.ThemeService.IsDark;
        Plot.Plot.Style(dark ? ScottPlot.Style.Black : ScottPlot.Style.Default);
        if (ch != null)
        {
            ch.Color = dark
                ? System.Drawing.Color.FromArgb(140, 230, 237, 243)
                : System.Drawing.Color.LightGray;
        }
        try
        {
            Plot.Render();
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("plot theme", ex);
        }
    }

    private void RefreshTick()
    {
        var added = DrainPendingPoints();
        if (added > 0)
        {
            NeedRefresh = true;

            //数据填充阶段自动缩放；填满 1000 点后固定窗口，避免持续抖动
            if (!_autoFitDone)
            {
                FitView();
                if (_maxPointCount >= MaxPoints)
                    _autoFitDone = true;
            }
        }

        if (!NeedRefresh)
            return;
        NeedRefresh = false;
        try
        {
            Plot.Render();
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("plot render", ex);
        }
    }

    /// <summary>把 Lua 线程排队的点批量写入绘图数组，返回本次加入的点数。</summary>
    private int DrainPendingPoints()
    {
        var added = 0;
        while (_pendingPoints.TryDequeue(out var point))
        {
            AddPointCore(point.Value, point.Line);
            added++;
        }
        return added;
    }

    private void AddPointCore(double value, int line)
    {
        if (line < 0 || line >= Data.Length)
            return;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;

        var data = Data[line];
        for (int i = 0; i < MaxPoints - 1; i++)
            data[i] = data[i + 1];
        data[MaxPoints - 1] = value;

        if (_pointCounts[line] < MaxPoints)
            _pointCounts[line]++;
        if (_pointCounts[line] > _maxPointCount)
            _maxPointCount = _pointCounts[line];
    }

    /// <summary>
    /// 按实际数据适配 X/Y：
    /// 不足 1000 点时 X 轴按已加入点数展开（至少 10 个样本），填满后为 -999~0。
    /// Y 轴只统计有数据的线，并留 10% 边距。
    /// </summary>
    private void FitView()
    {
        if (_maxPointCount <= 0)
        {
            Plot.Plot.SetAxisLimitsX(-MaxPoints, 0);
            Plot.Plot.SetAxisLimitsY(-1, 1);
            Refresh();
            return;
        }

        var visibleCount = Math.Max(10, Math.Min(_maxPointCount, MaxPoints));
        Plot.Plot.SetAxisLimitsX(-visibleCount + 1, 0);

        var min = double.MaxValue;
        var max = double.MinValue;
        for (int line = 0; line < Data.Length; line++)
        {
            var count = Math.Min(_pointCounts[line], MaxPoints);
            if (count <= 0)
                continue;

            var data = Data[line];
            for (int i = MaxPoints - count; i < MaxPoints; i++)
            {
                var value = data[i];
                if (value < min)
                    min = value;
                if (value > max)
                    max = value;
            }
        }

        if (min > max)
        {
            min = -1;
            max = 1;
        }
        else if (min == max)
        {
            var padding = Math.Max(1.0, Math.Abs(min) * 0.1);
            min -= padding;
            max += padding;
        }
        else
        {
            var padding = (max - min) * 0.1;
            min -= padding;
            max += padding;
        }

        Plot.Plot.SetAxisLimitsY(min, max);
        Refresh();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        while (_pendingPoints.TryDequeue(out _)) { }
        for (int i = 0; i < Data.Length; i++)
        {
            Array.Clear(Data[i], 0, Data[i].Length);
            _pointCounts[i] = 0;
        }

        _maxPointCount = 0;
        _autoFitDone = false;
        if (ch != null)
        {
            ch.X = 0;
            ch.Y = 0;
        }
        FitView();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        StyleNow++;
        if (StyleNow >= Styles.Length)
            StyleNow = 0;
        Plot.Plot.Style(Styles[StyleNow]);
        Refresh();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        //先把队列里尚未绘制的点写入，再按完整数据适配
        DrainPendingPoints();
        FitView();
    }

    private void Plot_MouseMove(object sender, MouseEventArgs e)
    {
        var p = Plot.GetMouseCoordinates();
        if (ch == null)
            return;
        ch.X = p.x;
        ch.Y = p.y;
        Refresh();
    }

    private void Refresh() => NeedRefresh = true;
}