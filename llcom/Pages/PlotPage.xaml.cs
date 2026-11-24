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

    //十字参考：一条竖直点线 + 每条可见线在交点处的圆点和数值
    private ScottPlot.Plottable.VLine? _crossLine;
    private ScottPlot.Plottable.Text? _crossXText;
    private readonly ScottPlot.Plottable.MarkerPlot?[] _crossMarkers =
        new ScottPlot.Plottable.MarkerPlot?[PlotBuffer.LineCount];
    private readonly ScottPlot.Plottable.Text?[] _crossTexts =
        new ScottPlot.Plottable.Text?[PlotBuffer.LineCount];

    //示波器式测量线：幅值（水平）和间隔（垂直）
    private static readonly System.Drawing.Color MeasureYColor =
        System.Drawing.Color.FromArgb(0, 188, 212);
    private static readonly System.Drawing.Color MeasureXColor =
        System.Drawing.Color.FromArgb(213, 0, 249);
    private ScottPlot.Plottable.HLine? _measureY1;
    private ScottPlot.Plottable.HLine? _measureY2;
    private ScottPlot.Plottable.VLine? _measureX1;
    private ScottPlot.Plottable.VLine? _measureX2;
    private bool _measureYEnabled;
    private bool _measureXEnabled;
    private bool _measureSavedAutoFollow;
    private bool _measureSavedAutoFitY;
    private MenuItem? _measureYMenuItem;
    private MenuItem? _measureXMenuItem;
    private ScottPlot.Renderable.Legend? _legend;
    private bool _legendVisible = true;
    private bool _mouseOverPlot;
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

        //十字准星跟随鼠标像素：视图滚动时在 RefreshTick 里用当前鼠标位置重算，
        //否则数据坐标固定会让竖线随曲线一起往左漂。
        Plot.MouseEnter += (_, _) =>
        {
            _mouseOverPlot = true;
            UpdateCrosshair();
            Refresh();
        };
        Plot.MouseLeave += (_, _) =>
        {
            _mouseOverPlot = false;
            HideCrosshair();
            Refresh();
        };

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
            UpdateMeasurementDragLimits();
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
        ResetMeasurementState();

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

        //十字参考：竖直点线 + 交点圆点/数值（水平线和左侧 Y 标签不再使用）
        InitializeCrosshair();
        InitializeMeasurementLines();

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
        _measureYMenuItem = new MenuItem
        {
            Header = TryFindResource("PlotMeasureAmplitude") as string ?? "Amplitude measurement",
            IsCheckable = true,
            IsChecked = _measureYEnabled,
        };
        _measureYMenuItem.Click += (_, _) =>
            SetMeasurementEnabled(_measureYMenuItem.IsChecked, _measureXEnabled);
        menu.Items.Add(_measureYMenuItem);

        _measureXMenuItem = new MenuItem
        {
            Header = TryFindResource("PlotMeasureInterval") as string ?? "Interval measurement",
            IsCheckable = true,
            IsChecked = _measureXEnabled,
        };
        _measureXMenuItem.Click += (_, _) =>
            SetMeasurementEnabled(_measureYEnabled, _measureXMenuItem.IsChecked);
        menu.Items.Add(_measureXMenuItem);

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
        if (_measureYMenuItem != null)
            _measureYMenuItem.IsChecked = _measureYEnabled;
        if (_measureXMenuItem != null)
            _measureXMenuItem.IsChecked = _measureXEnabled;

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
        ConfigureCrosshair();
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

    /// <summary>创建竖直参考线和每条线的交点圆点/数值（默认隐藏）。</summary>
    private void InitializeCrosshair()
    {
        _crossLine = Plot.Plot.AddVerticalLine(
            0,
            System.Drawing.Color.Gray,
            1,
            LineStyle.Dot
        );
        _crossLine.IgnoreAxisAuto = true;
        _crossLine.PositionLabel = false;
        _crossLine.IsVisible = false;

        //底部 X 标签：放在绘图区内部、贴近底边，比轴上的小灰字更醒目
        _crossXText = Plot.Plot.AddText(
            "-",
            0,
            0,
            11,
            System.Drawing.Color.White
        );
        _crossXText.BackgroundFill = true;
        _crossXText.BackgroundColor = System.Drawing.Color.FromArgb(235, 0, 120, 215);
        _crossXText.BorderSize = 1;
        _crossXText.BorderColor = System.Drawing.Color.FromArgb(200, 255, 255, 255);
        _crossXText.FontBold = true;
        _crossXText.FontName = "Consolas";
        _crossXText.Alignment = Alignment.LowerCenter;
        _crossXText.PixelOffsetY = 6;
        _crossXText.IsVisible = false;

        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            var color = _signals[line]?.Color ?? System.Drawing.Color.Gray;
            var marker = Plot.Plot.AddMarker(
                0,
                0,
                MarkerShape.filledCircle,
                7,
                color
            );
            marker.IsVisible = false;
            _crossMarkers[line] = marker;

            var text = Plot.Plot.AddText("-", 0, 0, 10, color);
            text.BackgroundFill = true;
            text.BorderSize = 1;
            text.BorderColor = color;
            text.IsVisible = false;
            _crossTexts[line] = text;
        }

        ConfigureCrosshair();
    }

    /// <summary>参考线/交点标签的颜色跟随深浅主题。</summary>
    private void ConfigureCrosshair()
    {
        var dark = llcom.Services.ThemeService.IsDark;
        if (_crossLine != null)
        {
            _crossLine.Color = dark
                ? System.Drawing.Color.FromArgb(170, 220, 220, 220)
                : System.Drawing.Color.FromArgb(170, 60, 60, 60);
            _crossLine.PositionLabelBackground = dark
                ? System.Drawing.Color.FromArgb(220, 30, 30, 30)
                : System.Drawing.Color.FromArgb(230, 255, 255, 255);
            _crossLine.PositionLabelFont.Color = dark
                ? System.Drawing.Color.WhiteSmoke
                : System.Drawing.Color.Black;
        }

        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            var color = _signals[line]?.Color ?? System.Drawing.Color.Gray;
            if (_crossMarkers[line] != null)
                _crossMarkers[line]!.Color = color;
            if (_crossTexts[line] is { } text)
            {
                text.Color = color;
                text.BorderColor = color;
                text.BackgroundColor = dark
                    ? System.Drawing.Color.FromArgb(200, 20, 20, 20)
                    : System.Drawing.Color.FromArgb(220, 255, 255, 255);
            }
        }
    }

    /// <summary>
    /// 鼠标在数据区时：竖线吸附到最近样本点，并在每条可见线的交点处显示圆点和该点 Y 值。
    /// </summary>
    private void UpdateCrosshair()
    {
        if (_measureYEnabled || _measureXEnabled)
        {
            HideCrosshair();
            return;
        }

        if (!_mouseOverPlot || _buffer == null || _crossLine == null)
        {
            HideCrosshair();
            return;
        }

        var limits = Plot.Plot.GetAxisLimits();
        var mouse = Plot.GetMouseCoordinates();
        if (
            mouse.x < limits.XMin
            || mouse.x > limits.XMax
            || mouse.y < limits.YMin
            || mouse.y > limits.YMax
        )
        {
            HideCrosshair();
            return;
        }

        var snap = (long)Math.Round(mouse.x);
        if (!TryGetDataXRange(out var dataMin, out var dataMax) || snap < dataMin || snap > dataMax)
        {
            HideCrosshair();
            return;
        }

        _crossLine.X = snap;
        _crossLine.IsVisible = true;

        if (_crossXText != null)
        {
            _crossXText.Label = _timeAxis
                ? FormatTimeLabel(snap)
                : snap.ToString(CultureInfo.InvariantCulture);
            _crossXText.X = snap;
            _crossXText.Y = limits.YMin;
            _crossXText.IsVisible = true;
        }

        var leftSide = snap > (limits.XMin + limits.XMax) / 2;
        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            var marker = _crossMarkers[line];
            var text = _crossTexts[line];
            if (marker == null || text == null)
                continue;

            var index = snap - _buffer.BaseX[line];
            if (
                !_lineVisible[line]
                || _buffer.Counts[line] <= 0
                || index < 0
                || index >= _buffer.Counts[line]
            )
            {
                marker.IsVisible = false;
                text.IsVisible = false;
                continue;
            }

            var value = _buffer.Values[line][(int)index];
            marker.X = snap;
            marker.Y = value;
            marker.IsVisible = true;

            text.Label = value.ToString("0.###", CultureInfo.InvariantCulture);
            text.X = snap;
            text.Y = value;
            text.Alignment = leftSide ? Alignment.MiddleRight : Alignment.MiddleLeft;
            text.PixelOffsetX = leftSide ? -8 : 8;
            text.IsVisible = true;
        }
    }

    private void HideCrosshair()
    {
        if (_crossLine != null)
            _crossLine.IsVisible = false;
        if (_crossXText != null)
            _crossXText.IsVisible = false;
        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            if (_crossMarkers[line] != null)
                _crossMarkers[line]!.IsVisible = false;
            if (_crossTexts[line] != null)
                _crossTexts[line]!.IsVisible = false;
        }
    }

    /// <summary>创建示波器式测量线（默认隐藏，启用时再显示/允许拖动）。</summary>
    private void InitializeMeasurementLines()
    {
        _measureY1 = Plot.Plot.AddHorizontalLine(
            0,
            MeasureYColor,
            2,
            LineStyle.Solid
        );
        _measureY2 = Plot.Plot.AddHorizontalLine(
            0,
            MeasureYColor,
            2,
            LineStyle.Dash
        );
        _measureX1 = Plot.Plot.AddVerticalLine(
            0,
            MeasureXColor,
            2,
            LineStyle.Solid
        );
        _measureX2 = Plot.Plot.AddVerticalLine(
            0,
            MeasureXColor,
            2,
            LineStyle.Dash
        );

        foreach (var line in new ScottPlot.Plottable.AxisLine?[]
        {
            _measureY1,
            _measureY2,
            _measureX1,
            _measureX2,
        })
        {
            if (line == null)
                continue;
            line.IgnoreAxisAuto = true;
            line.DragEnabled = false;
            line.IsVisible = false;
        }

        //间隔测量吸附到整数样本点
        _measureX1.DragSnap = new SnapXToSample();
        _measureX2.DragSnap = new SnapXToSample();
    }

    private sealed class SnapXToSample : ScottPlot.SnapLogic.ISnap2D
    {
        public ScottPlot.Coordinate Snap(ScottPlot.Coordinate original) =>
            new(Math.Round(original.X), original.Y);
    }

    /// <summary>清空/重建时直接关闭测量，不恢复旧的跟随状态。</summary>
    private void ResetMeasurementState()
    {
        _measureYEnabled = false;
        _measureXEnabled = false;
        foreach (var line in new ScottPlot.Plottable.AxisLine?[]
        {
            _measureY1,
            _measureY2,
            _measureX1,
            _measureX2,
        })
        {
            if (line == null)
                continue;
            line.IsVisible = false;
            line.DragEnabled = false;
        }

        if (MeasurePanel != null)
            MeasurePanel.Visibility = Visibility.Collapsed;
        if (MeasureYRow != null)
            MeasureYRow.Visibility = Visibility.Collapsed;
        if (MeasureXRow != null)
            MeasureXRow.Visibility = Visibility.Collapsed;
        if (_measureYMenuItem != null)
            _measureYMenuItem.IsChecked = false;
        if (_measureXMenuItem != null)
            _measureXMenuItem.IsChecked = false;
    }

    private void SetMeasurementEnabled(bool enableY, bool enableX)
    {
        var wasActive = _measureYEnabled || _measureXEnabled;
        var turnOnY = enableY && !_measureYEnabled;
        var turnOnX = enableX && !_measureXEnabled;

        //第一次进入测量：冻结视图，但数据仍继续记录
        if (!wasActive && (enableY || enableX))
        {
            //冻结时间映射：测量读数与 X 轴标签使用同一套换算，测量期间不再刷新，
            //避免数据增长导致平均采样间隔变化、A/B 读数自己漂移。
            UpdateTimeLabelMapping(force: true);
            _measureSavedAutoFollow = _autoFollow;
            _measureSavedAutoFitY = _autoFitY;
            _autoFollow = false;
            SetAutoFitY(false);
        }

        _measureYEnabled = enableY;
        _measureXEnabled = enableX;

        if (_measureY1 != null)
        {
            _measureY1.IsVisible = enableY;
            _measureY1.DragEnabled = enableY;
        }
        if (_measureY2 != null)
        {
            _measureY2.IsVisible = enableY;
            _measureY2.DragEnabled = enableY;
        }
        if (_measureX1 != null)
        {
            _measureX1.IsVisible = enableX;
            _measureX1.DragEnabled = enableX;
        }
        if (_measureX2 != null)
        {
            _measureX2.IsVisible = enableX;
            _measureX2.DragEnabled = enableX;
        }

        if (turnOnY)
            AlignMeasurementYExtremes();
        if (turnOnX)
        {
            AlignMeasurementXDefault();
        }

        UpdateMeasurementDragLimits();
        UpdateMeasurementPanel();

        if (!_measureYEnabled && !_measureXEnabled)
        {
            RestoreMeasurementView();
            UpdateCrosshair();
        }
        else
        {
            HideCrosshair();
        }

        Refresh();
    }

    private void RestoreMeasurementView()
    {
        _autoFollow = _measureSavedAutoFollow;
        SetAutoFitY(_measureSavedAutoFitY);
        if (_autoFollow)
        {
            FitLatestView();
            return;
        }

        UpdateRenderIndices();
        UpdateScrollBarFromView();
        Refresh();
    }

    private void UpdateMeasurementPanel()
    {
        var visible = _measureYEnabled || _measureXEnabled;
        if (MeasurePanel != null)
            MeasurePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (MeasureYRow != null)
            MeasureYRow.Visibility = _measureYEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (MeasureXRow != null)
            MeasureXRow.Visibility = _measureXEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (MeasureHint != null)
            MeasureHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateMeasurementReadout();
    }

    private void UpdateMeasurementReadout()
    {
        if (_measureYEnabled && _measureY1 != null && _measureY2 != null)
        {
            var a = _measureY1.Y;
            var b = _measureY2.Y;
            var format = TryFindResource("PlotMeasureYReadout") as string
                ?? "Amplitude: A={0}  B={1}  |Δ|={2}";
            MeasureYText.Text = string.Format(
                CultureInfo.InvariantCulture,
                format,
                a.ToString("0.###", CultureInfo.InvariantCulture),
                b.ToString("0.###", CultureInfo.InvariantCulture),
                Math.Abs(a - b).ToString("0.###", CultureInfo.InvariantCulture)
            );
        }

        if (_measureXEnabled && _measureX1 != null && _measureX2 != null)
        {
            var a = _measureX1.X;
            var b = _measureX2.X;
            var samples = Math.Abs(a - b);
            //与 X 轴标签使用同一套（测量开始时冻结的）时间映射，保证读数和视觉一致
            var seconds = samples * Math.Abs(_timeScale);
            var format = TryFindResource("PlotMeasureXReadout") as string
                ?? "Interval: A={0}  B={1}  Δ={2} samples / {3} s";
            MeasureXText.Text = string.Format(
                CultureInfo.InvariantCulture,
                format,
                FormatMeasureX(a),
                FormatMeasureX(b),
                samples.ToString("0.###", CultureInfo.InvariantCulture),
                seconds.ToString("0.000", CultureInfo.InvariantCulture)
            );
        }
    }

    private string FormatMeasureX(double x)
    {
        if (_timeAxis)
        {
            return _timeOrigin
                .AddSeconds(_timeAnchorTime + (x - _timeAnchorX) * _timeScale)
                .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
        return x.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void UpdateMeasurementDragLimits()
    {
        var limits = Plot.Plot.GetAxisLimits();
        if (_measureY1 != null)
        {
            _measureY1.DragLimitMin = limits.YMin;
            _measureY1.DragLimitMax = limits.YMax;
        }
        if (_measureY2 != null)
        {
            _measureY2.DragLimitMin = limits.YMin;
            _measureY2.DragLimitMax = limits.YMax;
        }
        if (_measureX1 != null)
        {
            _measureX1.DragLimitMin = limits.XMin;
            _measureX1.DragLimitMax = limits.XMax;
        }
        if (_measureX2 != null)
        {
            _measureX2.DragLimitMin = limits.XMin;
            _measureX2.DragLimitMax = limits.XMax;
        }
    }

    /// <summary>把幅值测量线对齐到当前可见曲线的最高/最低点。</summary>
    private void AlignMeasurementYExtremes()
    {
        if (_buffer == null || _measureY1 == null || _measureY2 == null)
            return;

        var limits = Plot.Plot.GetAxisLimits();
        var min = double.MaxValue;
        var max = double.MinValue;
        for (var line = 0; line < PlotBuffer.LineCount; line++)
        {
            if (!_lineVisible[line] || _buffer.Counts[line] <= 0)
                continue;
            if (!TryGetVisibleRange(line, limits.XMin, limits.XMax, out var start, out var end))
                continue;

            var values = _buffer.Values[line];
            for (var i = start; i <= end; i++)
            {
                if (values[i] < min)
                    min = values[i];
                if (values[i] > max)
                    max = values[i];
            }
        }

        if (min > max)
        {
            min = limits.YMin;
            max = limits.YMax;
        }
        if (max <= min)
            max = min + 1;

        _measureY1.Y = max;
        _measureY2.Y = min;
        UpdateMeasurementDragLimits();
        UpdateMeasurementReadout();
        Refresh();
    }

    /// <summary>间隔测量默认放在可见 X 范围的 1/3 和 2/3。</summary>
    private void AlignMeasurementXDefault()
    {
        if (_measureX1 == null || _measureX2 == null)
            return;

        var limits = Plot.Plot.GetAxisLimits();
        var width = limits.XMax - limits.XMin;
        if (width <= 0)
            width = 1;
        _measureX1.X = Math.Round(limits.XMin + width / 3.0);
        _measureX2.X = Math.Round(limits.XMin + width * 2.0 / 3.0);
        if (_measureX2.X <= _measureX1.X)
            _measureX2.X = _measureX1.X + 1;
        UpdateMeasurementReadout();
    }

    private void AlignMeasurementXRange()
    {
        if (_measureX1 == null || _measureX2 == null)
            return;

        var limits = Plot.Plot.GetAxisLimits();
        _measureX1.X = Math.Round(limits.XMin);
        _measureX2.X = Math.Round(limits.XMax);
        if (_measureX2.X <= _measureX1.X)
            _measureX2.X = _measureX1.X + 1;
        UpdateMeasurementDragLimits();
        UpdateMeasurementReadout();
        Refresh();
    }

    private void MeasureYSwap_Click(object sender, RoutedEventArgs e)
    {
        if (_measureY1 == null || _measureY2 == null)
            return;
        (_measureY1.Y, _measureY2.Y) = (_measureY2.Y, _measureY1.Y);
        UpdateMeasurementReadout();
        Refresh();
    }

    private void MeasureYAlign_Click(object sender, RoutedEventArgs e) =>
        AlignMeasurementYExtremes();

    private void MeasureYClose_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementEnabled(false, _measureXEnabled);

    private void MeasureXSwap_Click(object sender, RoutedEventArgs e)
    {
        if (_measureX1 == null || _measureX2 == null)
            return;
        (_measureX1.X, _measureX2.X) = (_measureX2.X, _measureX1.X);
        UpdateMeasurementReadout();
        Refresh();
    }

    private void MeasureXAlign_Click(object sender, RoutedEventArgs e) =>
        AlignMeasurementXRange();

    private void MeasureXClose_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementEnabled(_measureYEnabled, false);

    /// <summary>
    /// 切换 X 轴刻度：样本序号，或按平均采样间隔换算成时间。
    /// 只改刻度标签，几何仍按样本序号排布，避免批量投递的点在时间轴上挤成台阶。
    /// </summary>
    private void ApplyXAxisMode()
    {
        if (_crossLine != null)
        {
            if (_timeAxis)
                _crossLine.PositionFormatter = FormatTimeLabel;
            else
                _crossLine.PositionFormatter = x => x.ToString("0");
        }

        if (_timeAxis)
        {
            if (!(_measureYEnabled || _measureXEnabled))
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
    private void UpdateTimeLabelMapping(bool force = false)
    {
        //测量期间冻结映射：读数和 X 轴标签使用同一套换算，避免读数和视觉位置对不上
        if (!force && (_measureYEnabled || _measureXEnabled))
            return;
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

        if (_timeAxis && !(_measureYEnabled || _measureXEnabled))
            UpdateTimeLabelMapping();
        if (_measureYEnabled || _measureXEnabled)
        {
            UpdateMeasurementDragLimits();
            UpdateMeasurementReadout();
        }

        if (!NeedRefresh)
            return;
        NeedRefresh = false;
        try
        {
            //数据滚动时十字准星保持在鼠标当前像素位置，而不是停在旧的数据坐标
            if (_mouseOverPlot)
                UpdateCrosshair();
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
            //导出时隐藏参考线和交点标签
            var crosshairWasVisible = _crossLine?.IsVisible ?? false;
            HideCrosshair();
            try
            {
                //按当前绘图区尺寸的 2 倍导出，避免 SaveFig 缩放时坐标轴被裁切
                var width = Math.Max(800, (int)Math.Round(Plot.ActualWidth * 2));
                var height = Math.Max(600, (int)Math.Round(Plot.ActualHeight * 2));
                Plot.Plot.SaveFig(dialog.FileName, width, height);
            }
            finally
            {
                if (crosshairWasVisible)
                    UpdateCrosshair();
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

        HideCrosshair();
        ResetMeasurementState();

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
        //停止/重新开始绘制时自动退出测量，避免旧测量线与新数据错位
        if (_measureYEnabled || _measureXEnabled)
        {
            var restoreAutoY = _measureSavedAutoFitY;
            ResetMeasurementState();
            SetAutoFitY(restoreAutoY);
        }

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
        _mouseOverPlot = true;
        if (!_measureYEnabled && !_measureXEnabled)
        {
            var zone = GetPlotZone(e.GetPosition(Plot));
            Plot.Cursor =
                zone == PlotZone.XAxis ? Cursors.SizeWE
                : zone == PlotZone.YAxis ? Cursors.SizeNS
                : Cursors.Arrow;
        }

        UpdateCrosshair();
        Refresh();
    }

    private void Refresh() => NeedRefresh = true;
}