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

    private bool _sendShowHex = true;

    /// <summary>
    /// 发送的数据是否以 HEX 显示（不勾选则以字符串显示）。
    /// </summary>
    public bool sendShowHex
    {
        get => _sendShowHex;
        set
        {
            if (SetProperty(ref _sendShowHex, value))
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
