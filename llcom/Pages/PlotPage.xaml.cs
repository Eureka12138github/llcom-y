using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FontAwesomeIcon = FontAwesome.WPF.FontAwesomeIcon;
using llcom.Model;
using ScottPlot;

namespace llcom.Pages;

/// <summary>
/// 曲线页：接收 Lua 的 apiAddPoint 数据并绘制。
/// 每条线保留一段较长的历史（默认 2 万点，可配置），默认只显示最近 1000 点并跟随最新数据滚动；
/// 鼠标缩放/拖动后可查看历史，双击图表或重新开始绘制可恢复跟随。
/// 数据先在并发队列中累积，再由 UI 定时器批量写入，避免脚本线程直接修改绘图数组。
/// </summary>
public partial class PlotPage : Page
{
    //默认显示的窗口点数；历史保留量由设置 plotHistoryPoints 决定
    private const int ViewPoints = 1000;

    //待绘制队列上限：脚本异常刷点时保护 UI
    private const int MaxPendingPoints = 4096;

    private PlotBuffer? _buffer;
    private double[]? _xs;
    private readonly ScottPlot.Plottable.SignalPlotXY?[] _signals =
        new ScottPlot.Plottable.SignalPlotXY?[PlotBuffer.LineCount];

    //Lua 线程只入队，UI 定时器统一消费
    private ConcurrentQueue<(double Value, int Line)> _pendingPoints = new();

    //只有曲线页可见且处于「开始绘制」状态时才接收点
    private volatile bool _acceptPoints;
    private bool _plotEnabled = true;

    //自动跟随最新点；鼠标滚轮/拖动后关闭，双击或重新开始绘制恢复
    private bool _autoFollow = true;

    private ScottPlot.Plottable.Crosshair? ch = null;
    private readonly ScottPlot.Styles.IStyle[] Styles = ScottPlot.Style.GetStyles();
    private int StyleNow = -1;

    private bool NeedRefresh = true;

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
                if (_autoFollow)
                    FitLatestView();
                else
                {
                    UpdateRenderIndices();
                    Refresh();
                }
            }
            return;
        }
        first = false;

        InitializePlotData();

        //窗口最大化 / 拖动分隔条改变绘图区大小
        Plot.SizeChanged += (_, _) =>
        {
            if (_buffer == null || !IsVisible)
                return;
            if (_autoFollow)
                FitLatestView();
            else
            {
                UpdateRenderIndices();
                Refresh();
            }
        };

        //鼠标滚轮只缩放 X 轴（查看历史），不改 Y 轴；左键拖动会退出自动跟随
        Plot.PreviewMouseWheel += Plot_PreviewMouseWheel;
        Plot.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                _autoFollow = false;
        };
        //双击图表恢复跟随最新点
        Plot.PreviewMouseDoubleClick += (_, _) =>
        {
            _autoFollow = true;
            FitLatestView();
        };

        llcom.Services.ThemeService.ThemeChanged += (_, _) => ApplyTheme();

        _refreshTimer.Tick += (_, _) => RefreshTick();
        if (IsVisible)
            _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();

        //只有曲线页可见且处于「开始绘制」状态时才记录；切走时丢弃排队点
        _acceptPoints = IsVisible && _plotEnabled;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
                _acceptPoints = _plotEnabled;
                if (!_refreshTimer.IsEnabled)
                    _refreshTimer.Start();
                if (_autoFollow)
                    FitLatestView();
                else
                {
                    UpdateRenderIndices();
                    Refresh();
                }
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

        //重新运行脚本时从空白曲线开始
        LuaEnv.LuaApis.LinePlotClear += (_, _) => Dispatcher.BeginInvoke(new Action(ClearPlot));

        //保留点数设置变化时按新容量重建（会清空曲线）
        Tools.Global.setting.PropertyChanged += PlotSettingChanged;
    }

    /// <summary>按当前设置创建/重建曲线数据与 ScottPlot 图元。</summary>
    private void InitializePlotData()
    {
        var requested = Tools.Global.setting.plotHistoryPoints;
        var capacity = Math.Max(
            Model.Settings.PlotHistoryPointsMin,
            Math.Min(Model.Settings.PlotHistoryPointsMax, requested)
        );
        _buffer = new PlotBuffer(capacity);
        _xs = new double[capacity];
        for (var i = 0; i < capacity; i++)
            _xs[i] = i;

        Plot.Plot.Clear();
        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            var signal = Plot.Plot.AddSignalXY(_xs, _buffer.Values[line]);
            signal.Label = "Line " + line;
            signal.IsVisible = false;
            _signals[line] = signal;
        }

        ch = Plot.Plot.AddCrosshair(0, 0);
        ch.Color = System.Drawing.Color.LightGray;
        ch.LineWidth = 2;

        _autoFollow = true;
        ApplyTheme();
        FitLatestView();
    }

    private void PlotSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Model.Settings.plotHistoryPoints))
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_buffer == null)
                    return;
                var capacity = Math.Max(
                    Model.Settings.PlotHistoryPointsMin,
                    Math.Min(Model.Settings.PlotHistoryPointsMax, Tools.Global.setting.plotHistoryPoints)
                );
                if (capacity == _buffer.Capacity)
                    return;

                //容量变化时清空重建，避免搬移一半数据造成显示错位
                InitializePlotData();
                _acceptPoints = IsVisible && _plotEnabled;
                if (IsVisible)
                    FitLatestView();
            })
        );
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
        //页面不可见时丢弃残留数据，不记录也不渲染
        if (!IsVisible)
        {
            _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
            NeedRefresh = false;
            return;
        }

        var added = DrainPendingPoints();
        if (added > 0)
        {
            NeedRefresh = true;
            if (_autoFollow)
                FitLatestView();
            else
                UpdateRenderIndices();
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

    /// <summary>把 Lua 线程排队的点批量写入缓冲，返回本次加入的点数。</summary>
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
        if (_buffer == null || line < 0 || line >= PlotBuffer.LineCount)
            return;

        _buffer.Add(line, value);
        var signal = _signals[line];
        if (signal == null)
            return;

        signal.OffsetX = _buffer.BaseX[line];
        signal.IsVisible = true;
    }

    /// <summary>
    /// 让每条线只渲染当前视图内的点。
    /// 历史很长时，缩放到局部也不会把全部点送给渲染器。
    /// </summary>
    private void UpdateRenderIndices()
    {
        if (_buffer == null)
            return;

        var limits = Plot.Plot.GetAxisLimits();
        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            var signal = _signals[line];
            if (signal == null)
                continue;

            if (
                _buffer.TryGetVisibleRange(
                    line,
                    limits.XMin,
                    limits.XMax,
                    out var start,
                    out var end
                )
            )
            {
                signal.MinRenderIndex = start;
                signal.MaxRenderIndex = end;
                signal.IsVisible = true;
            }
            else
            {
                signal.MinRenderIndex = 0;
                signal.MaxRenderIndex = 0;
                signal.IsVisible = false;
            }
        }
    }

    /// <summary>视图跟随最新数据：只显示最近 ViewPoints 个点，最新点在右边缘。</summary>
    private void FitLatestView()
    {
        if (_buffer == null)
            return;

        var latest = _buffer.LatestX;
        double xMin;
        double xMax;
        if (latest < 0)
        {
            xMin = -ViewPoints + 1;
            xMax = 0;
        }
        else
        {
            //数据不足 ViewPoints 时从最旧点开始，避免左边出现一大块空白
            var dataMin = long.MaxValue;
            for (var line = 0; line < PlotBuffer.LineCount; line++)
            {
                if (_buffer.Counts[line] > 0)
                    dataMin = Math.Min(dataMin, _buffer.OldestX(line));
            }

            xMax = latest;
            xMin = Math.Max(dataMin, latest - ViewPoints + 1);
        }
        if (xMax <= xMin)
            xMax = xMin + 1;

        Plot.Plot.SetAxisLimitsX(xMin, xMax);
        FitY(xMin, xMax);
        UpdateRenderIndices();
        Refresh();
    }

    /// <summary>
    /// 滚轮只缩放 X 轴，并限制在已保留的数据范围内：
    /// 既能向左查看历史，也不会把 Y 轴一起缩小导致波形变成一小块。
    /// </summary>
    private void Plot_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_buffer == null || !TryGetDataXRange(out var dataMin, out var dataMax))
            return;

        var limits = Plot.Plot.GetAxisLimits();
        var width = limits.XMax - limits.XMin;
        if (width <= 0)
            width = ViewPoints;

        var factor = e.Delta > 0 ? 0.8 : 1.25;
        var minWidth = 5.0;
        var maxWidth = Math.Max(minWidth, dataMax - dataMin);
        var newWidth = Math.Max(minWidth, Math.Min(maxWidth, width * factor));

        var mouse = Plot.GetMouseCoordinates();
        var ratio = (mouse.x - limits.XMin) / width;
        if (ratio < 0)
            ratio = 0;
        if (ratio > 1)
            ratio = 1;

        var xMin = mouse.x - newWidth * ratio;
        var xMax = xMin + newWidth;
        if (xMin < dataMin)
        {
            xMin = dataMin;
            xMax = xMin + newWidth;
        }
        if (xMax > dataMax)
        {
            xMax = dataMax;
            xMin = xMax - newWidth;
        }

        _autoFollow = false;
        Plot.Plot.SetAxisLimitsX(xMin, xMax);
        UpdateRenderIndices();
        Refresh();
        e.Handled = true;
    }

    /// <summary>所有线当前保留数据的最旧/最新绝对样本序号。</summary>
    private bool TryGetDataXRange(out long min, out long max)
    {
        min = long.MaxValue;
        max = long.MinValue;
        if (_buffer == null)
            return false;

        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            if (_buffer.Counts[line] <= 0)
                continue;
            min = Math.Min(min, _buffer.OldestX(line));
            max = Math.Max(max, _buffer.NewestX(line));
        }
        return min <= max;
    }

    /// <summary>显示当前保留的全部历史。</summary>
    private void FitAllView()
    {
        if (_buffer == null)
            return;

        var min = long.MaxValue;
        var max = long.MinValue;
        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            if (_buffer.Counts[line] <= 0)
                continue;
            min = Math.Min(min, _buffer.OldestX(line));
            max = Math.Max(max, _buffer.NewestX(line));
        }

        if (min > max)
        {
            FitLatestView();
            return;
        }

        double xMin = min;
        double xMax = max;
        if (xMax <= xMin)
            xMax = xMin + 1;

        Plot.Plot.SetAxisLimitsX(xMin, xMax);
        FitY(xMin, xMax);
        UpdateRenderIndices();
        Refresh();
    }

    /// <summary>按当前 X 范围内的可见点计算 Y 轴范围，并留 10% 边距。</summary>
    private void FitY(double xMin, double xMax)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        if (_buffer != null)
        {
            for (var line = 0; line < PlotBuffer.LineCount; line++)
            {
                if (
                    !_buffer.TryGetVisibleRange(line, xMin, xMax, out var start, out var end)
                )
                    continue;

                var values = _buffer.Values[line];
                for (var i = start; i <= end; i++)
                {
                    var value = values[i];
                    if (value < min)
                        min = value;
                    if (value > max)
                        max = value;
                }
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
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) => ClearPlot();

    private void ClearPlot()
    {
        _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
        _buffer?.Clear();
        for (var i = 0; i < PlotBuffer.LineCount; i++)
        {
            var signal = _signals[i];
            if (signal == null)
                continue;
            signal.IsVisible = false;
            signal.MinRenderIndex = 0;
            signal.MaxRenderIndex = 0;
        }

        if (ch != null)
        {
            ch.X = 0;
            ch.Y = 0;
        }

        _autoFollow = true;
        FitLatestView();
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
            _autoFollow = true;
            if (IsVisible && !_refreshTimer.IsEnabled)
                _refreshTimer.Start();
            FitLatestView();
        }
        else
        {
            _acceptPoints = false;
            _pendingPoints = new ConcurrentQueue<(double Value, int Line)>();
            _autoFollow = false;
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
        //查看当前保留的全部历史；之后滚轮/拖动可继续查看局部
        DrainPendingPoints();
        _autoFollow = false;
        FitAllView();
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