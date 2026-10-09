using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ScottPlot;

namespace llcom.Pages;

/// <summary>
/// PlotPage.xaml 的交互逻辑
/// </summary>
public partial class PlotPage : Page
{
    public PlotPage()
    {
        InitializeComponent();
    }

    //最多十个图像
    private double[][] Data = new double[10][];
    private double[] DataX = null;

    //最大点数量
    private static int MaxPoints = 1000;

    private ScottPlot.Plottable.Crosshair ch = null;

    private ScottPlot.Styles.IStyle[] Styles = ScottPlot.Style.GetStyles();
    private int StyleNow = -1;

    private bool NeedRefresh = true;

    //定时刷新（原为后台线程 + Sleep 忙等，改为 UI 定时器）
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer =
        new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

    bool first = true;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!first)
            return;
        first = false;
        //暂时先定1000个点吧
        DataX = new double[MaxPoints];
        for (int i = 0; i < MaxPoints; i++)
            DataX[i] = i - MaxPoints + 1;
        for (int i = 0; i < Data.Length; i++)
        {
            if (Data[i] == null)
                Data[i] = new double[MaxPoints];
            Plot.Plot.AddSignalXY(DataX, Data[i]);
        }
        Plot.Plot.SetAxisLimitsX(-MaxPoints, 0);
        ch = Plot.Plot.AddCrosshair(0, 0);

        ch.Color = System.Drawing.Color.LightGray;
        ch.LineWidth = 2;

        //定时刷吧，要不然卡（改为 UI 定时器，避免后台线程 + Dispatcher.Invoke 忙等）
        _refreshTimer.Tick += (_, _) =>
        {
            if (!NeedRefresh)
                return;
            NeedRefresh = false;
            try
            {
                Plot.Render();
            }
            catch (System.Exception ex) { Tools.Logger.Swallowed("plot render", ex); }
        };
        _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();

        LuaEnv.LuaApis.LinePlotAdd += (s, e) => AddPoint(e.N, e.Line);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < Data.Length; i++)
        for (int j = 0; j < Data[i].Length; j++)
            Data[i][j] = 0;
        Refresh();
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
        Plot.Plot.SetAxisLimitsX(-MaxPoints, 0);
        //防止最大值最小值错误
        var min = Data.Min(x => x.Min());
        var max = Data.Max(x => x.Max());
        if (min < max)
            Plot.Plot.SetAxisLimitsY(min, max);
        Refresh();
    }

    private void Plot_MouseMove(object sender, MouseEventArgs e)
    {
        var p = Plot.GetMouseCoordinates();
        ch.X = p.x;
        ch.Y = p.y;
        Refresh();
    }

    private void Refresh() => NeedRefresh = true;

    private void AddPoint(double d, int line)
    {
        if (line >= 10)
            return;
        if (Data[line] == null)
            Data[line] = new double[MaxPoints];
        for (int i = 0; i < MaxPoints - 1; i++)
            Data[line][i] = Data[line][i + 1];
        Data[line][MaxPoints - 1] = d;
        Refresh();
    }
}
