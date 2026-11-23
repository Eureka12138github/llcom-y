using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FontAwesomeIcon = FontAwesome.WPF.FontAwesomeIcon;
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

    //只有收到数据的线才显示，避免 10 条空线一起参与渲染
    private readonly ScottPlot.Plottable.SignalPlotXY?[] _signals =
        new ScottPlot.Plottable.SignalPlotXY?[10];

    //每条线已加入的点数（用于 X 轴缩放 / Y 轴只看有效数据）
    private readonly int[] _pointCounts = new int[10];
    private int _maxPointCount;
    private bool _autoFitDone;
    private DateTime _lastFitUtc = DateTime.MinValue;

    //Lua 线程只入队，UI 定时器统一消费；限制积压量，避免脚本异常刷点时把 UI 拖死
    private const int MaxPendingPoints = 4096;
    private ConcurrentQueue<(double Value, int Line)> _pendingPoints = new();

    //只有曲线页可见时才接收点；切走期间的数据直接丢弃，不做回放
    private volatile bool _acceptPoints;

    //曲线页自己的开始/停止绘制开关；停止时冻结当前画面，但不影响脚本运行
    private bool _plotEnabled = true;

    private ScottPlot.Plottable.Crosshair? ch = null;

    private readonly ScottPlot.Styles.IStyle[] Styles = ScottPlot.Style.GetStyles();
    private int StyleNow = -1;

    private bool NeedRefresh = true;

    //定时刷新（原为后台线程 + Sleep 忙等，改为 UI 定时器）
    private readonly DispatcherTimer _refreshTimer =
        new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };

    private bool first = true;

    public PlotPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!first)
        {
            //页面被重新加载时恢复刷新；之前 Unloaded 会停掉定时器
            if (IsVisible)
            {
                _acceptPoints = _plotEnabled;
                if (!_refreshTimer.IsEnabled)
                    _refreshTimer.Start();
                FitView();
            }
            return;
        }
        first = false;

        DataX = new double[MaxPoints];
        for (int i = 0; i < MaxPoints; i++)
            DataX[i] = i - MaxPoints + 1;
        for (int i = 0; i < Data.Length; i++)
        {
            if (Data[i] == null)
                Data[i] = new double[MaxPoints];
            var signal = Plot.Plot.AddSignalXY(DataX, Data[i]);
            signal.Label = "Line " + i;
            signal.IsVisible = false;
            _signals[i] = signal;
        }

        ch = Plot.Plot.AddCrosshair(0, 0);
        ch.Color = System.Drawing.Color.LightGray;
        ch.LineWidth = 2;

        ApplyTheme();
        FitView();

        //窗口最大化 / 拖动分隔条改变绘图区大小时，按当前数据重新适配，避免波形缩成一小块
        Plot.SizeChanged += (_, _) =>
        {
            if (DataX != null && IsVisible)
                FitView();
        };

        llcom.Services.ThemeService.ThemeChanged += (_, _) => ApplyTheme();

        _refreshTimer.Tick += (_, _) => RefreshTick();
        if (IsVisible)
            _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();

        //只有曲线页可见且处于“开始绘制”状态时才记录；切走时丢弃排队点，回来也不补画
        _acceptPoints = IsVisible && _plotEnabled;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                //先丢掉切走期间可能残留的点，再开始接收当前数据
                _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
                _acceptPoints = _plotEnabled;
                if (!_refreshTimer.IsEnabled)
                    _refreshTimer.Start();
                FitView();
                _lastFitUtc = DateTime.UtcNow;
                if (_maxPointCount >= MaxPoints)
                    _autoFitDone = true;
            }
            else
            {
                _acceptPoints = false;
                _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
                _refreshTimer.Stop();
            }
        };

        //Lua 回调可能来自脚本线程：只入队，不在回调里碰 UI/绘图数组
        LuaEnv.LuaApis.LinePlotAdd += (_, e) =>
        {
            if (!_acceptPoints || _pendingPoints.Count >= MaxPendingPoints)
                return;
            _pendingPoints.Enqueue((e.N, e.Line));
        };

        //重新运行脚本时从空白曲线开始，避免旧数据和新数据混在一起
        LuaEnv.LuaApis.LinePlotClear += (_, _) =>
            Dispatcher.BeginInvoke(new Action(ClearPlot));
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
        //页面不可见时丢弃残留数据，不记录也不渲染；切回来时从当前点重新开始
        if (!IsVisible)
        {
            _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
            NeedRefresh = false;
            return;
        }

        var added = DrainPendingPoints();
        if (added > 0)
            NeedRefresh = true;

        //数据填充阶段自动缩放，但限制为每 250ms 一次，避免坐标轴持续抖动
        if (
            added > 0
            && !_autoFitDone
            && (DateTime.UtcNow - _lastFitUtc).TotalMilliseconds >= 250
        )
        {
            FitView();
            _lastFitUtc = DateTime.UtcNow;
            if (_maxPointCount >= MaxPoints)
                _autoFitDone = true;
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

        var signal = _signals[line];
        if (_pointCounts[line] == 0 && signal != null)
            signal.IsVisible = true;

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

    private void ClearButton_Click(object sender, RoutedEventArgs e) => ClearPlot();

    private void ClearPlot()
    {
        _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
        for (int i = 0; i < Data.Length; i++)
        {
            Array.Clear(Data[i], 0, Data[i].Length);
            _pointCounts[i] = 0;
            var signal = _signals[i];
            if (signal != null)
                signal.IsVisible = false;
        }

        _maxPointCount = 0;
        _autoFitDone = false;
        _lastFitUtc = DateTime.MinValue;
        if (ch != null)
        {
            ch.X = 0;
            ch.Y = 0;
        }
        FitView();
        if (IsVisible && !_refreshTimer.IsEnabled)
            _refreshTimer.Start();
        NeedRefresh = true;
    }

    private void PlotToggleButton_Click(object sender, RoutedEventArgs e) =>
        SetPlotEnabled(!_plotEnabled);

    private void SetPlotEnabled(bool enabled)
    {
        _plotEnabled = enabled;
        var text =
            TryFindResource(enabled ? "PlotStop" : "PlotStart") as string
            ?? (enabled ? "Stop plotting" : "Start plotting");
        PlotToggleIcon.Icon = enabled ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        PlotToggleButton.ToolTip = text;
        System.Windows.Automation.AutomationProperties.SetName(PlotToggleButton, text);

        if (enabled)
        {
            _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
            _acceptPoints = IsVisible;
            if (IsVisible && !_refreshTimer.IsEnabled)
                _refreshTimer.Start();
            FitView();
        }
        else
        {
            _acceptPoints = false;
            _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
            NeedRefresh = true;
        }
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
        _lastFitUtc = DateTime.UtcNow;
        if (_maxPointCount >= MaxPoints)
            _autoFitDone = true;
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