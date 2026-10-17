using System;
using System.Text;

namespace llcom.Model;

/// <summary>
/// 显示与编码相关设置（Settings 分部类）。
/// </summary>
partial class Settings
{
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
