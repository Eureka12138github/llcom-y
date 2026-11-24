using System;
using System.Text;

namespace llcom.Model;

/// <summary>
/// 显示与编码相关设置（Settings 分部类）。
/// </summary>
partial class Settings
{
    /// <summary>深色模式（OpenCode 风格深色调色板 + AdonisUI Dark）。</summary>
    private bool _darkMode;
    public bool darkMode
    {
        get => _darkMode;
        set
        {
            if (SetProperty(ref _darkMode, value))
            {
                llcom.Services.ThemeService.Apply(value);
                Save();
            }
        }
    }

    /// <summary>收发数据区字号下限</summary>
    public const int LogFontSizeMin = 10;

    /// <summary>收发数据区字号上限</summary>
    public const int LogFontSizeMax = 40;

    private int _logFontSize = 15;

    /// <summary>
    /// 收发数据区（日志）显示字号，可在“更多设置”里调整（范围 10~40）。
    /// </summary>
    public int logFontSize
    {
        get => _logFontSize;
        set
        {
            //限制到合理范围，避免过大撑爆布局、过小不可读
            var v =
                value < LogFontSizeMin ? LogFontSizeMin
                : value > LogFontSizeMax ? LogFontSizeMax
                : value;
            if (SetProperty(ref _logFontSize, v))
                Save();
        }
    }

    /// <summary>日志（分包模式）保留条数下限</summary>
    public const int LogMaxItemsMin = 1000;

    /// <summary>日志（分包模式）保留条数上限</summary>
    public const int LogMaxItemsMax = 200000;

    private int _logMaxItems = 50000;

    /// <summary>
    /// 日志（分包模式）最多保留多少条，超过后自动丢弃最早的记录（范围 1000~200000）。
    /// </summary>
    public int logMaxItems
    {
        get => _logMaxItems;
        set
        {
            var v =
                value < LogMaxItemsMin ? LogMaxItemsMin
                : value > LogMaxItemsMax ? LogMaxItemsMax
                : value;
            if (SetProperty(ref _logMaxItems, v))
                Save();
        }
    }

    /// <summary>达到上限后清理比例下限（百分比）</summary>
    public const int LogTrimPercentMin = 10;

    /// <summary>达到上限后清理比例上限（百分比，100 = 全部清空）</summary>
    public const int LogTrimPercentMax = 100;

    private int _logTrimPercent = 50;

    /// <summary>
    /// 分包日志达到上限后一次性清理多少（百分比，范围 10~100）。
    /// 默认 50：一次删掉一半，避免“刚删完又立刻到上限”的频繁裁剪。
    /// 100 = 全部清空。
    /// </summary>
    public int logTrimPercent
    {
        get => _logTrimPercent;
        set
        {
            var v =
                value < LogTrimPercentMin ? LogTrimPercentMin
                : value > LogTrimPercentMax ? LogTrimPercentMax
                : value;
            if (SetProperty(ref _logTrimPercent, v))
                Save();
        }
    }

    /// <summary>流式模式（分包超时&lt;0）保留字符数下限</summary>
    public const int StreamMaxCharsMin = 100000;

    /// <summary>流式模式（分包超时&lt;0）保留字符数上限</summary>
    public const int StreamMaxCharsMax = 10000000;

    private int _streamMaxChars = 2000000;

    /// <summary>
    /// 流式模式最多保留多少字符，超过后从头部丢弃最早的文本（范围 100000~10000000）。
    /// </summary>
    public int streamMaxChars
    {
        get => _streamMaxChars;
        set
        {
            var v =
                value < StreamMaxCharsMin ? StreamMaxCharsMin
                : value > StreamMaxCharsMax ? StreamMaxCharsMax
                : value;
            if (SetProperty(ref _streamMaxChars, v))
                Save();
        }
    }

    /// <summary>曲线工具保留点数下限</summary>
    public const int PlotHistoryPointsMin = 10000;

    /// <summary>曲线工具保留点数上限</summary>
    public const int PlotHistoryPointsMax = 100000;

    private int _plotHistoryPoints = 20000;

    /// <summary>
    /// 曲线工具每条线保留多少个历史点（范围 10000~100000，默认 20000）。
    /// 修改后曲线页会清空并按新容量重建。
    /// </summary>
    public int plotHistoryPoints
    {
        get => _plotHistoryPoints;
        set
        {
            var v =
                value < PlotHistoryPointsMin ? PlotHistoryPointsMin
                : value > PlotHistoryPointsMax ? PlotHistoryPointsMax
                : value;
            if (SetProperty(ref _plotHistoryPoints, v))
                Save();
        }
    }

    /// <summary>曲线交点数值标签字号下限</summary>
    public const int PlotValueFontSizeMin = 12;

    /// <summary>曲线交点数值标签字号上限</summary>
    public const int PlotValueFontSizeMax = 20;

    private int _plotValueFontSize = 12;

    /// <summary>曲线交点数值标签的字号（12~20，默认 12）；修改后即时生效。</summary>
    public int plotValueFontSize
    {
        get => _plotValueFontSize;
        set
        {
            var v =
                value < PlotValueFontSizeMin ? PlotValueFontSizeMin
                : value > PlotValueFontSizeMax ? PlotValueFontSizeMax
                : value;
            if (SetProperty(ref _plotValueFontSize, v))
                Save();
        }
    }

    private bool _plotTimeAxis;

    /// <summary>曲线 X 轴是否按时间显示（false = 样本序号）。</summary>
    public bool plotTimeAxis
    {
        get => _plotTimeAxis;
        set
        {
            if (SetProperty(ref _plotTimeAxis, value))
                Save();
        }
    }

    private bool _recvShowHex = true;

    /// <summary>
    /// 接收的数据是否以 HEX 显示（不勾选则以字符串显示）。
    /// </summary>
    public bool recvShowHex
    {
        get => _recvShowHex;
        set
        {
            if (SetProperty(ref _recvShowHex, value))
                Save();
        }
    }

    private bool _bitDelay = true;
    public bool bitDelay
    {
        get => _bitDelay;
        set
        {
            if (SetProperty(ref _bitDelay, value))
                Save();
        }
    }

    private bool _autoUpdate = true;

    /// <summary>
    /// 是否开启自动升级
    /// </summary>
    public bool autoUpdate
    {
        get => _autoUpdate;
        set
        {
            if (SetProperty(ref _autoUpdate, value))
                Save();
        }
    }

    private bool _scriptNotifyEnabled = true;

    /// <summary>
    /// 脚本结束/报错时是否在系统托盘弹出气泡提示（同时决定托盘图标是否常驻）。
    /// </summary>
    public bool scriptNotifyEnabled
    {
        get => _scriptNotifyEnabled;
        set
        {
            if (SetProperty(ref _scriptNotifyEnabled, value))
                Save();
        }
    }

    private bool _scriptNotifyFlash = true;

    /// <summary>
    /// 未读时是否让托盘图标闪烁 30 秒（关闭后只保留彩色角标）。
    /// </summary>
    public bool scriptNotifyFlash
    {
        get => _scriptNotifyFlash;
        set
        {
            if (SetProperty(ref _scriptNotifyFlash, value))
                Save();
        }
    }

    private string _logEditorPath = "";

    /// <summary>
    /// 「导出并打开当前日志」使用的默认编辑器路径；留空时使用系统「打开方式」对话框。
    /// </summary>
    public string logEditorPath
    {
        get => _logEditorPath;
        set
        {
            if (SetProperty(ref _logEditorPath, value ?? ""))
                Save();
        }
    }

    /// <summary>
    /// 串口接收每包最大长度
    /// </summary>
    private uint _maxLength = 10240;
    public uint maxLength
    {
        get => _maxLength;
        set
        {
            if (SetProperty(ref _maxLength, value))
                Save();
        }
    }
}
