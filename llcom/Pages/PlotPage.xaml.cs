using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private ConcurrentQueue<(double Value, int Line, double Time)> _pendingPoints = new();

    //只有曲线页可见且处于「开始绘制」状态时才接收点
    private volatile bool _acceptPoints;
    private bool _plotEnabled = true;

    //自动跟随最新点；滚轮只改变跟随窗口宽度，拖动/查看全局后关闭，双击或重新开始绘制恢复
    private bool _autoFollow = true;

    //自动跟随时显示的窗口点数（滚轮缩放会改变它，默认 1000）
    private int _followWindowPoints = ViewPoints;

    //时间轴模式：X 仍为样本序号，刻度标签按平均采样间隔换算成时间
    private bool _timeAxis;
    private double _timeAnchorX;
    private double _timeAnchorTime;
    private double _timeScale = 0.01;
    private DateTime _timeOrigin = DateTime.Now;

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

    //每条线是否显示（菜单勾选）；隐藏只影响绘制和 Y 自动适配，数据仍保留
    private readonly bool[] _lineVisible = new bool[PlotBuffer.LineCount];
    private readonly MenuItem[] _lineMenuItems = new MenuItem[PlotBuffer.LineCount];
    private MenuItem? _showAllLinesItem;
    private MenuItem? _timeAxisMenuItem;
    private MenuItem? _exportPngItem;
    private MenuItem? _exportCsvItem;

    private ScottPlot.Plottable.Crosshair? ch = null;
    private ScottPlot.Renderable.Legend? _legend;
    private bool _legendVisible = true;
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
        for (var i = 0; i < _lineVisible.Length; i++)
            _lineVisible[i] = true;
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

        //ScottPlot 自带右键菜单是英文，且导出/查看全局与「曲线选项」重复：
        //默认菜单不是通过 ContextMenu 属性打开的，而是构造函数里挂了 RightClicked -> DefaultRightClickEvent，
        //这里把这个处理器移除，右键就不会再弹菜单。
        Plot.RemoveHandler(
            ScottPlot.WpfPlot.RightClickedEvent,
            new RoutedEventHandler(Plot.DefaultRightClickEvent)
        );
        Plot.ContextMenu = null;
        //右键直接切换图例显示/隐藏，不再弹菜单
        Plot.RightClicked += (_, _) => ToggleLegend();

        _timeAxis = Tools.Global.setting.plotTimeAxis;

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
                _pendingPoints = new ConcurrentQueue<(double Value, int Line, double Time)>();
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
                _pendingPoints = new ConcurrentQueue<(double Value, int Line, double Time)>();
                _refreshTimer.Stop();
            }
        };

        //Lua 回调可能来自脚本线程：只入队，不在回调里碰 UI/绘图数组
        LuaEnv.LuaApis.LinePlotAdd += (_, e) =>
        {
            if (!_acceptPoints || _pendingPoints.Count >= MaxPendingPoints)
                return;
            var timeSeconds = (DateTime.Now - _timeOrigin).TotalSeconds;
            _pendingPoints.Enqueue((e.N, e.Line, timeSeconds));
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
        _timeOrigin = DateTime.Now;
        _timeAnchorX = 0;
        _timeAnchorTime = 0;
        _timeScale = 0.01;

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

        //图例跟随线条颜色；隐藏的线由 ScottPlot 自动从图例中排除
        ConfigureLegend();

        ApplyXAxisMode();
        if (_timeAxisMenuItem != null)
            _timeAxisMenuItem.IsChecked = _timeAxis;

        for (var i = 0; i < _lineVisible.Length; i++)
        {
            _lineVisible[i] = true;
            if (_lineMenuItems[i] != null)
                _lineMenuItems[i].IsChecked = true;
        }

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

    /// <summary>曲线工具栏的“显示/隐藏”菜单（Line 0~9）。</summary>
    private void BuildLineMenu()
    {
        var menu = new ContextMenu();
        _showAllLinesItem = new MenuItem
        {
            Header = TryFindResource("PlotShowAllLines") as string ?? "Show all",
        };
        _showAllLinesItem.Click += (_, _) => SetAllLinesVisible(true);
        menu.Items.Add(_showAllLinesItem);
        menu.Items.Add(new Separator());

        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            var item = new MenuItem
            {
                Header = "Line " + line,
                IsCheckable = true,
                IsChecked = true,
                Tag = line,
            };
            item.Click += LineMenuItem_Click;
            _lineMenuItems[line] = item;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        _timeAxisMenuItem = new MenuItem
        {
            Header = TryFindResource("PlotTimeAxisMenu") as string ?? "Show time on X axis",
            IsCheckable = true,
            IsChecked = _timeAxis,
        };
        _timeAxisMenuItem.Click += (_, _) => SetTimeAxis(_timeAxisMenuItem.IsChecked);
        menu.Items.Add(_timeAxisMenuItem);

        menu.Items.Add(new Separator());
        _exportPngItem = new MenuItem
        {
            Header = TryFindResource("PlotExportPng") as string ?? "Export PNG…",
        };
        _exportPngItem.Click += PlotExportPng_Click;
        menu.Items.Add(_exportPngItem);

        _exportCsvItem = new MenuItem
        {
            Header = TryFindResource("PlotExportCsv") as string ?? "Export CSV…",
        };
        _exportCsvItem.Click += PlotExportCsv_Click;
        menu.Items.Add(_exportCsvItem);

        PlotLinesButton.ContextMenu = menu;
    }

    private void PlotLinesButton_Click(object sender, RoutedEventArgs e)
    {
        //每次打开时重建菜单，保证运行时切换语言后菜单文字跟着更新
        BuildLineMenu();

        var menu = PlotLinesButton.ContextMenu;
        if (menu == null)
            return;

        var hasHiddenWithData = false;
        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            var hasData = _buffer != null && _buffer.Counts[line] > 0;
            _lineMenuItems[line].IsEnabled = hasData;
            _lineMenuItems[line].IsChecked = _lineVisible[line];
            if (hasData && !_lineVisible[line])
                hasHiddenWithData = true;
        }
        if (_showAllLinesItem != null)
            _showAllLinesItem.IsEnabled = hasHiddenWithData;
        if (_timeAxisMenuItem != null)
            _timeAxisMenuItem.IsChecked = _timeAxis;

        var hasAnyData = _buffer != null && TryGetDataXRange(out _, out _);
        if (_exportPngItem != null)
            _exportPngItem.IsEnabled = hasAnyData;
        if (_exportCsvItem != null)
            _exportCsvItem.IsEnabled = hasAnyData;

        menu.PlacementTarget = PlotLinesButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void LineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not int line)
            return;
        if (line < 0 || line >= _lineVisible.Length)
            return;

        _lineVisible[line] = item.IsChecked;
        ApplyLineVisibility();
    }

    private void SetAllLinesVisible(bool visible)
    {
        for (var line = 0; line < _lineVisible.Length; line++)
        {
            _lineVisible[line] = visible;
            _lineMenuItems[line].IsChecked = visible;
        }
        ApplyLineVisibility();
    }

    /// <summary>按菜单勾选更新线条可见性，并在 Y 自动适配时排除隐藏线。</summary>
    private void ApplyLineVisibility()
    {
        if (_buffer == null)
        {
            Refresh();
            return;
        }

        UpdateRenderIndices();
        if (_autoFitY)
        {
            var limits = Plot.Plot.GetAxisLimits();
            FitY(limits.XMin, limits.XMax);
        }
        Refresh();
    }

    /// <summary>让 ScottPlot 跟随应用主题（深色用 Black 风格）。</summary>
    private void ApplyTheme()
    {
        var dark = llcom.Services.ThemeService.IsDark;
        Plot.Plot.Style(dark ? ScottPlot.Style.Black : ScottPlot.Style.Default);
        ConfigureAxes();
        ConfigureLegend();
        ApplyXAxisMode();
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

    private void ToggleLegend()
    {
        _legendVisible = !_legendVisible;
        if (_legend != null)
            _legend.IsVisible = _legendVisible;
        Refresh();
    }

    /// <summary>图例背景/文字颜色跟随深浅主题，避免深色下出现白底黑字。</summary>
    private void ConfigureLegend()
    {
        //Style() 可能会重建图例对象，这里每次都取当前图例
        _legend = Plot.Plot.Legend(true, Alignment.UpperLeft);
        if (_legend == null)
            return;
        _legend.FontSize = 10;
        _legend.FontBold = false;

        var dark = llcom.Services.ThemeService.IsDark;
        _legend.FillColor = dark
            ? System.Drawing.Color.FromArgb(220, 30, 30, 30)
            : System.Drawing.Color.FromArgb(230, 255, 255, 255);
        _legend.FontColor = dark
            ? System.Drawing.Color.FromArgb(230, 237, 243)
            : System.Drawing.Color.Black;
        _legend.OutlineColor = dark
            ? System.Drawing.Color.FromArgb(90, 230, 237, 243)
            : System.Drawing.Color.FromArgb(160, 0, 0, 0);
        _legend.IsVisible = _legendVisible;
    }

    /// <summary>
    /// 切换 X 轴刻度：样本序号，或按平均采样间隔换算成时间。
    /// 只改刻度标签，几何仍按样本序号排布，避免批量投递的点在时间轴上挤成台阶。
    /// </summary>
    private void ApplyXAxisMode()
    {
        if (_timeAxis)
        {
            UpdateTimeLabelMapping();
            Plot.Plot.BottomAxis.TickLabelFormat(FormatTimeLabel);
        }
        else
        {
            Plot.Plot.BottomAxis.TickLabelFormat(x => x.ToString("0"));
        }
    }

    /// <summary>按当前可见时间跨度选择时间格式：短跨度带毫秒，长跨度只到秒。</summary>
    private string FormatTimeLabel(double x)
    {
        var limits = Plot.Plot.GetAxisLimits();
        var span = Math.Abs(limits.XMax - limits.XMin) * Math.Abs(_timeScale);
        var format = span < 10 ? "HH:mm:ss.fff" : span < 120 ? "HH:mm:ss.f" : "HH:mm:ss";
        return _timeOrigin
            .AddSeconds(_timeAnchorTime + (x - _timeAnchorX) * _timeScale)
            .ToString(format);
    }

    /// <summary>
    /// 用点数最多的线估算“每个样本多少秒”，把样本序号映射到时间标签。
    /// 采样间隔均匀时准确；批量投递/间隔变化时是近似值。
    /// </summary>
    private void UpdateTimeLabelMapping()
    {
        if (_buffer == null)
            return;

        var line = -1;
        var count = 0;
        for (var i = 0; i < PlotBuffer.LineCount; i++)
        {
            if (_buffer.Counts[i] > count)
            {
                count = _buffer.Counts[i];
                line = i;
            }
        }

        if (line < 0 || count < 2)
        {
            _timeAnchorX = 0;
            _timeAnchorTime = 0;
            _timeScale = 0.01;
            return;
        }

        var times = _buffer.Times[line];
        var span = times[count - 1] - times[0];
        _timeScale = span > 0 ? span / (count - 1) : 0.01;
        _timeAnchorX = _buffer.BaseX[line];
        _timeAnchorTime = times[0];
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
            _pendingPoints = new ConcurrentQueue<(double Value, int Line, double Time)>();
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
            if (_timeAxis)
                UpdateTimeLabelMapping();
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
            AddPointCore(point.Value, point.Line, point.Time);
            added++;
        }
        return added;
    }

    private void AddPointCore(double value, int line, double timeSeconds)
    {
        if (_buffer == null || line < 0 || line >= PlotBuffer.LineCount)
            return;

        _buffer.Add(line, value, timeSeconds);
        var signal = _signals[line];
        if (signal == null)
            return;

        signal.OffsetX = _buffer.BaseX[line];
        signal.IsVisible = _lineVisible[line];
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
                TryGetVisibleRange(
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
                signal.IsVisible = _lineVisible[line];
            }
            else
            {
                signal.MinRenderIndex = 0;
                signal.MaxRenderIndex = 0;
                signal.IsVisible = false;
            }
        }
    }

    /// <summary>视图跟随最新数据：默认显示最近 ViewPoints 个点（时间轴模式下换算为对应秒数）。</summary>
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

    /// <summary>所有线当前保留数据的最旧/最新样本序号。</summary>
    private bool TryGetDataXRange(out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
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

    /// <summary>取某条线在给定 X 范围内的索引区间。</summary>
    private bool TryGetVisibleRange(
        int line,
        double xMin,
        double xMax,
        out int start,
        out int end
    )
    {
        if (_buffer == null)
        {
            start = 0;
            end = -1;
            return false;
        }
        return _buffer.TryGetVisibleRange(line, xMin, xMax, out start, out end);
    }

    /// <summary>显示当前保留的全部历史。</summary>
    private void FitAllView()
    {
        if (_buffer == null)
            return;

        if (!TryGetDataXRange(out var min, out var max))
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
                if (!_lineVisible[line])
                    continue;
                if (
                    !TryGetVisibleRange(line, xMin, xMax, out var start, out var end)
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

    /// <summary>切换 X 轴刻度为时间/样本序号；只改标签，不改变当前视图。</summary>
    private void SetTimeAxis(bool enabled)
    {
        _timeAxis = enabled;
        Tools.Global.setting.plotTimeAxis = enabled;
        if (_timeAxisMenuItem != null)
            _timeAxisMenuItem.IsChecked = enabled;
        ApplyXAxisMode();
        Refresh();
    }

    private void PlotExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (_buffer == null || !TryGetDataXRange(out _, out _))
        {
            ShowPlotExportEmpty();
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"plot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
        };
        var owner = Window.GetWindow(this);
        var ok = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (ok != true)
            return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            //导出时隐藏十字准线，避免把鼠标位置和坐标标签画进图片
            var crosshairWasVisible = ch?.IsVisible ?? false;
            if (ch != null)
                ch.IsVisible = false;
            try
            {
                //按当前绘图区尺寸的 2 倍导出，避免 SaveFig 缩放时坐标轴被裁切
                var width = Math.Max(800, (int)Math.Round(Plot.ActualWidth * 2));
                var height = Math.Max(600, (int)Math.Round(Plot.ActualHeight * 2));
                Plot.Plot.SaveFig(dialog.FileName, width, height);
            }
            finally
            {
                if (ch != null)
                    ch.IsVisible = crosshairWasVisible;
            }
            Plot.Render();
        }
        catch (Exception ex)
        {
            ReportPlotExportFailed(ex);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void PlotExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_buffer == null || !TryGetDataXRange(out _, out _))
        {
            ShowPlotExportEmpty();
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"plot_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        var owner = Window.GetWindow(this);
        var ok = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (ok != true)
            return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var activeLines = new List<int>();
            for (var line = 0; line < PlotBuffer.LineCount; line++)
            {
                if (_buffer.Counts[line] > 0)
                    activeLines.Add(line);
            }

            var startIndex = long.MaxValue;
            var endIndex = long.MinValue;
            var referenceLine = activeLines[0];
            for (var i = 0; i < activeLines.Count; i++)
            {
                var line = activeLines[i];
                startIndex = Math.Min(startIndex, _buffer.BaseX[line]);
                endIndex = Math.Max(
                    endIndex,
                    _buffer.BaseX[line] + _buffer.Counts[line] - 1
                );
                if (_buffer.Counts[line] > _buffer.Counts[referenceLine])
                    referenceLine = line;
            }

            using var sw = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
            sw.Write("index,elapsed_s,time");
            foreach (var line in activeLines)
            {
                sw.Write(",line");
                sw.Write(line);
            }
            sw.WriteLine();

            for (var index = startIndex; index <= endIndex; index++)
            {
                double? elapsed = null;
                //优先用点数最多的线作为时间参考；同一 index 的曲线在界面上也是对齐的
                if (
                    index >= _buffer.BaseX[referenceLine]
                    && index < _buffer.BaseX[referenceLine] + _buffer.Counts[referenceLine]
                )
                {
                    elapsed = _buffer.Times[referenceLine][
                        (int)(index - _buffer.BaseX[referenceLine])
                    ];
                }
                else
                {
                    foreach (var line in activeLines)
                    {
                        if (
                            index >= _buffer.BaseX[line]
                            && index < _buffer.BaseX[line] + _buffer.Counts[line]
                        )
                        {
                            elapsed = _buffer.Times[line][(int)(index - _buffer.BaseX[line])];
                            break;
                        }
                    }
                }

                sw.Write(index);
                sw.Write(',');
                if (elapsed.HasValue)
                {
                    sw.Write(elapsed.Value.ToString("0.000000", CultureInfo.InvariantCulture));
                }
                sw.Write(',');
                if (elapsed.HasValue)
                {
                    sw.Write(
                        _timeOrigin
                            .AddSeconds(elapsed.Value)
                            .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    );
                }

                foreach (var line in activeLines)
                {
                    sw.Write(',');
                    if (
                        index >= _buffer.BaseX[line]
                        && index < _buffer.BaseX[line] + _buffer.Counts[line]
                    )
                    {
                        sw.Write(
                            _buffer.Values[line][(int)(index - _buffer.BaseX[line])]
                                .ToString("R", CultureInfo.InvariantCulture)
                        );
                    }
                }
                sw.WriteLine();
            }
        }
        catch (Exception ex)
        {
            ReportPlotExportFailed(ex);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ShowPlotExportEmpty() =>
        Tools.MessageBox.Show(
            TryFindResource("PlotExportEmpty") as string
                ?? "There is no curve data to export."
        );

    private void ReportPlotExportFailed(Exception ex)
    {
        Tools.Logger.Swallowed("plot export", ex);
        Tools.MessageBox.Show(
            (TryFindResource("PlotExportFailed") as string ?? "Export failed:")
                + "\r\n"
                + ex.Message
        );
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
        _pendingPoints = new ConcurrentQueue<(double Value, int Line, double Time)>();
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

        _timeOrigin = DateTime.Now;
        _autoFollow = true;
        _followWindowPoints = ViewPoints;
        SetAutoFitY(true);
        ApplyXAxisMode();
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
            _pendingPoints = new ConcurrentQueue<(double Value, int Line, double Time)>();
            _acceptPoints = IsVisible;
            _autoFollow = true;
            if (IsVisible && !_refreshTimer.IsEnabled)
                _refreshTimer.Start();
            FitLatestView();
        }
        else
        {
            _acceptPoints = false;
            _pendingPoints = new ConcurrentQueue<(double Value, int Line, double Time)>();
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
        ConfigureLegend();
        ApplyXAxisMode();
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