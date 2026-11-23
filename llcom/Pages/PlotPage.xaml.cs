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

    //自动跟随最新点；滚轮只改变跟随窗口宽度，拖动/查看全局后关闭，双击或重新开始绘制恢复
    private bool _autoFollow = true;

    //自动跟随时显示的窗口点数（滚轮缩放会改变它，默认 1000）
    private int _followWindowPoints = ViewPoints;

    //代码更新滑块时忽略 ValueChanged，避免和坐标轴互相触发
    private bool _updatingScrollBar;

    //左键拖动阈值：单击/双击时也可能收到一次 MouseMove，不能因此停掉跟随
    private Point _dragStart;
    private bool _mouseDown;

    //Y 轴自动适配；手动缩放 Y 后关闭，双击/查看全局/Y 按钮恢复
    private bool _autoFitY = true;
    private bool _updatingAutoYToggle;

    //把右侧刻度同步到左侧（数据）Y 轴时防止重复触发
    private bool _syncingRightAxis;

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

        //ScottPlot 默认双击会切换左下角的渲染耗时基准框，这里关掉
        if (Plot.Configuration != null)
            Plot.Configuration.DoubleClickBenchmark = false;

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

        //滚轮按鼠标位置缩放：绘图区默认只缩 X，底部 X 刻度区缩 X，右侧 Y 刻度区缩 Y；
        //绘图区 Ctrl+滚轮只缩 Y、Alt+滚轮同时缩 X/Y。左键拖动会退出自动跟随/Y 自动适配
        Plot.PreviewMouseWheel += Plot_PreviewMouseWheel;
        Plot.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _mouseDown = true;
            _dragStart = e.GetPosition(Plot);
        };
        Plot.PreviewMouseLeftButtonUp += (_, _) => _mouseDown = false;
        Plot.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _mouseDown = false;
                return;
            }
            if (!_mouseDown)
                return;

            //移动超过 3 像素才算拖动；单击/双击产生的零位移 MouseMove 不改变跟随状态
            var pos = e.GetPosition(Plot);
            if (Math.Abs(pos.X - _dragStart.X) < 3 && Math.Abs(pos.Y - _dragStart.Y) < 3)
                return;

            _autoFollow = false;
            SetAutoFitY(false);
        };
        //双击图表恢复跟随最新点，并恢复 Y 轴自动适配；同时阻止 ScottPlot 自带的双击基准测试
        Plot.PreviewMouseDoubleClick += (_, e) =>
        {
            _autoFollow = true;
            SetAutoFitY(true);
            FitLatestView();
            e.Handled = true;
        };

        llcom.Services.ThemeService.ThemeChanged += (_, _) => ApplyTheme();

        //鼠标拖动/框选缩放后：让右侧刻度跟随数据轴，并同步底部滑块
        Plot.AxesChanged += (_, _) =>
        {
            SyncRightAxisLimits();
            UpdateScrollBarFromView();
        };

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
        _followWindowPoints = ViewPoints;
        _autoFitY = true;
        if (PlotAutoYButton.IsChecked != true)
        {
            _updatingAutoYToggle = true;
            PlotAutoYButton.IsChecked = true;
            _updatingAutoYToggle = false;
        }
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
        ConfigureAxes();
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

    /// <summary>Y 轴刻度移到右侧，左侧只保留网格。</summary>
    private void ConfigureAxes()
    {
        Plot.Plot.LeftAxis.Ticks(false);
        Plot.Plot.RightAxis.Ticks(true);
        Plot.Plot.LeftAxis.Grid(true);
        Plot.Plot.RightAxis.Grid(false);

        var limits = Plot.Plot.GetAxisLimits();
        Plot.Plot.SetAxisLimitsY(
            limits.YMin,
            limits.YMax,
            Plot.Plot.RightAxis.AxisIndex
        );
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
            {
                UpdateRenderIndices();
                UpdateScrollBarFromView();
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
        var window = Math.Max(10, _followWindowPoints);
        double xMin;
        double xMax;
        if (latest < 0)
        {
            xMin = -window + 1;
            xMax = 0;
        }
        else
        {
            //数据不足窗口点数时从最旧点开始，避免左边出现一大块空白
            var dataMin = long.MaxValue;
            for (var line = 0; line < PlotBuffer.LineCount; line++)
            {
                if (_buffer.Counts[line] > 0)
                    dataMin = Math.Min(dataMin, _buffer.OldestX(line));
            }

            xMax = latest;
            xMin = Math.Max(dataMin, latest - window + 1);
        }
        if (xMax <= xMin)
            xMax = xMin + 1;

        Plot.Plot.SetAxisLimitsX(xMin, xMax);
        if (_autoFitY)
            FitY(xMin, xMax);
        UpdateRenderIndices();
        UpdateScrollBarFromView();
        Refresh();
    }

    /// <summary>
    /// 按鼠标所在区域缩放：
    /// 绘图区默认只缩放 X（保持跟随最新），Ctrl 只缩放 Y，Alt 同时缩放 X/Y；
    /// 底部 X 刻度区只缩放 X，右侧 Y 刻度区只缩放 Y（并关闭 Y 自动适配）。
    /// 缩放围绕鼠标位置进行，且限制在已保留的数据范围内。
    /// </summary>
    private void Plot_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_buffer == null || !TryGetDataXRange(out var dataMin, out var dataMax))
            return;

        var zone = GetPlotZone(e.GetPosition(Plot));
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        bool zoomX;
        bool zoomY;
        if (zone == PlotZone.XAxis)
        {
            zoomX = true;
            zoomY = false;
        }
        else if (zone == PlotZone.YAxis)
        {
            zoomX = false;
            zoomY = true;
        }
        else
        {
            //绘图区：默认只缩放 X；Ctrl 只缩放 Y；Alt 同时缩放 X/Y
            zoomX = alt || !ctrl;
            zoomY = alt || ctrl;
        }

        var limits = Plot.Plot.GetAxisLimits();
        var width = limits.XMax - limits.XMin;
        if (width <= 0)
            width = ViewPoints;
        var height = limits.YMax - limits.YMin;
        if (height <= 0)
            height = 1;

        var factor = e.Delta > 0 ? 0.8 : 1.25;
        var mouse = Plot.GetMouseCoordinates();
        double? newXMin = null;
        double? newXMax = null;
        double? newYMin = null;
        double? newYMax = null;

        if (zoomX)
        {
            var minWidth = 5.0;
            var maxWidth = Math.Max(minWidth, dataMax - dataMin);
            var newWidth = Math.Max(minWidth, Math.Min(maxWidth, width * factor));

            double xMin;
            double xMax;
            if (_autoFollow)
            {
                //跟随最新点时，滚轮只改变窗口宽度，最新点仍在右边缘
                xMax = dataMax;
                xMin = xMax - newWidth + 1;
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
            }
            else
            {
                //已手动拖动/查看全局后，围绕鼠标位置缩放
                var ratio = (mouse.x - limits.XMin) / width;
                if (ratio < 0)
                    ratio = 0;
                if (ratio > 1)
                    ratio = 1;

                xMin = mouse.x - newWidth * ratio;
                xMax = xMin + newWidth;
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
            }

            _followWindowPoints = (int)Math.Max(10, Math.Round(xMax - xMin + 1));
            newXMin = xMin;
            newXMax = xMax;
        }

        if (zoomY)
        {
            var newHeight = Math.Max(1e-6, height * factor);
            var ratio = (mouse.y - limits.YMin) / height;
            if (ratio < 0)
                ratio = 0;
            if (ratio > 1)
                ratio = 1;

            var yMin = mouse.y - newHeight * ratio;
            var yMax = yMin + newHeight;
            newYMin = yMin;
            newYMax = yMax;
        }

        if (newXMin.HasValue && newXMax.HasValue)
            Plot.Plot.SetAxisLimitsX(newXMin.Value, newXMax.Value);

        if (newYMin.HasValue && newYMax.HasValue)
        {
            SetYAxisLimits(newYMin.Value, newYMax.Value);
            SetAutoFitY(false);
        }
        else if (newXMin.HasValue && newXMax.HasValue && _autoFitY)
        {
            //X 缩放后让 Y 自动适配新的可见范围（Y 自动开启时）
            FitY(newXMin.Value, newXMax.Value);
        }

        UpdateRenderIndices();
        UpdateScrollBarFromView();
        Refresh();
        e.Handled = true;
    }

    private enum PlotZone
    {
        Data,
        XAxis,
        YAxis,
    }

    /// <summary>
    /// 判断鼠标在绘图区、底部 X 轴区域还是右侧 Y 轴区域。
    /// 用 ScottPlot 的数据区像素边界，并按 DPI 缩放换算鼠标坐标。
    /// </summary>
    private PlotZone GetPlotZone(Point position)
    {
        if (Plot.Plot.Width <= 0 || Plot.ActualWidth <= 0 || Plot.ActualHeight <= 0)
            return PlotZone.Data;

        var scaleX = Plot.Plot.Width / Plot.ActualWidth;
        var scaleY = Plot.Plot.Height / Plot.ActualHeight;
        var px = position.X * scaleX;
        var py = position.Y * scaleY;

        var limits = Plot.Plot.GetAxisLimits();
        var right = Plot.Plot.GetPixelX(limits.XMax);
        var bottom = Plot.Plot.GetPixelY(limits.YMin);

        if (px > right)
            return PlotZone.YAxis;
        if (py > bottom)
            return PlotZone.XAxis;
        return PlotZone.Data;
    }

    private void SetYAxisLimits(double min, double max)
    {
        Plot.Plot.SetAxisLimitsY(min, max, 0);
        Plot.Plot.SetAxisLimitsY(min, max, Plot.Plot.RightAxis.AxisIndex);
    }

    /// <summary>右侧刻度只负责显示：鼠标拖动/框选缩放后让它跟随左侧数据轴的 Y 范围。</summary>
    private void SyncRightAxisLimits()
    {
        if (_syncingRightAxis)
            return;

        var limits = Plot.Plot.GetAxisLimits();
        if (double.IsNaN(limits.YMin) || double.IsNaN(limits.YMax))
            return;

        _syncingRightAxis = true;
        try
        {
            Plot.Plot.SetAxisLimitsY(
                limits.YMin,
                limits.YMax,
                Plot.Plot.RightAxis.AxisIndex
            );
        }
        finally
        {
            _syncingRightAxis = false;
        }
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
        UpdateScrollBarFromView();
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

        SetYAxisLimits(min, max);
    }

    private void PlotAutoY_Checked(object sender, RoutedEventArgs e)
    {
        if (!_updatingAutoYToggle)
            SetAutoFitY(true);
    }

    private void PlotAutoY_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_updatingAutoYToggle)
            SetAutoFitY(false);
    }

    /// <summary>切换 Y 轴自动适配；手动缩放 Y 后自动关闭，按钮/双击/查看全局可恢复。</summary>
    private void SetAutoFitY(bool enabled)
    {
        _autoFitY = enabled;
        if (PlotAutoYButton != null && PlotAutoYButton.IsChecked != enabled)
        {
            _updatingAutoYToggle = true;
            PlotAutoYButton.IsChecked = enabled;
            _updatingAutoYToggle = false;
        }

        if (enabled && _buffer != null)
        {
            var limits = Plot.Plot.GetAxisLimits();
            FitY(limits.XMin, limits.XMax);
            Refresh();
        }
    }

    /// <summary>把当前 X 轴窗口同步到底部历史滑块（滑块表示已保留的全部数据）。</summary>
    private void UpdateScrollBarFromView()
    {
        if (PlotScrollBar == null)
            return;

        _updatingScrollBar = true;
        try
        {
            if (_buffer == null || !TryGetDataXRange(out var dataMin, out var dataMax))
            {
                PlotScrollBar.Minimum = 0;
                PlotScrollBar.Maximum = 0;
                PlotScrollBar.ViewportSize = 0;
                PlotScrollBar.Value = 0;
                PlotScrollBar.IsEnabled = false;
                return;
            }

            var total = Math.Max(1, dataMax - dataMin + 1);
            var limits = Plot.Plot.GetAxisLimits();
            var view = Math.Max(1, limits.XMax - limits.XMin);
            var viewPoints = Math.Min(view, total);
            var maxValue = Math.Max(0, total - viewPoints);

            PlotScrollBar.Minimum = 0;
            PlotScrollBar.Maximum = maxValue;
            PlotScrollBar.ViewportSize = viewPoints;
            PlotScrollBar.LargeChange = Math.Max(1, viewPoints);
            PlotScrollBar.SmallChange = Math.Max(1, viewPoints / 10);
            var left = limits.XMin - dataMin;
            PlotScrollBar.Value = Math.Max(0, Math.Min(maxValue, left));
            PlotScrollBar.IsEnabled = maxValue > 0;
        }
        finally
        {
            _updatingScrollBar = false;
        }
    }

    private void PlotScrollBar_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e
    )
    {
        if (_updatingScrollBar || _buffer == null)
            return;
        if (!TryGetDataXRange(out var dataMin, out var dataMax))
            return;

        var total = Math.Max(1, dataMax - dataMin + 1);
        var limits = Plot.Plot.GetAxisLimits();
        var view = Math.Max(1, limits.XMax - limits.XMin);
        var viewPoints = Math.Min(view, total);
        var maxValue = Math.Max(0, total - viewPoints);
        var value = Math.Max(0, Math.Min(maxValue, e.NewValue));

        double xMin;
        double xMax;
        if (viewPoints >= total)
        {
            xMin = dataMin;
            xMax = dataMax;
        }
        else
        {
            xMin = dataMin + value;
            xMax = xMin + viewPoints;
            if (xMax > dataMax)
            {
                xMax = dataMax;
                xMin = xMax - viewPoints;
            }
        }

        //拖到最右端时恢复自动跟随；拖到左边则停住查看历史
        _autoFollow = value >= maxValue - 0.5;
        _followWindowPoints = (int)Math.Max(10, Math.Round(viewPoints));
        Plot.Plot.SetAxisLimitsX(xMin, xMax);
        if (_autoFitY)
            FitY(xMin, xMax);
        UpdateRenderIndices();
        UpdateScrollBarFromView();
        Refresh();
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
        _followWindowPoints = ViewPoints;
        SetAutoFitY(true);
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
        ConfigureAxes();
        Refresh();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        //查看当前保留的全部历史；之后滚轮/拖动可继续查看局部
        DrainPendingPoints();
        _autoFollow = false;
        SetAutoFitY(true);
        FitAllView();
    }

    private void Plot_MouseMove(object sender, MouseEventArgs e)
    {
        var zone = GetPlotZone(e.GetPosition(Plot));
        Plot.Cursor =
            zone == PlotZone.XAxis ? Cursors.SizeWE
            : zone == PlotZone.YAxis ? Cursors.SizeNS
            : Cursors.Arrow;

        var p = Plot.GetMouseCoordinates();
        if (ch == null)
            return;
        ch.X = p.x;
        ch.Y = p.y;
        Refresh();
    }

    private void Refresh() => NeedRefresh = true;
}