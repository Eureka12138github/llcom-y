using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace llcom.Model;

/// <summary>
/// 串口相关设置（Settings 分部类）。
/// 属性 setter 中涉及串口参数（波特率/校验位/数据位/停止位）的会同步应用到
/// Global.uart.serial，与重构前行为一致。
/// </summary>
partial class Settings
{
    private string _dataToSend = "uart data";
    public string dataToSend
    {
        get => _dataToSend;
        set
        {
            if (SetProperty(ref _dataToSend, value))
                Save();
        }
    }

    private int _baudRate = 115200;
    public int baudRate
    {
        get => _baudRate;
        set
        {
            try
            {
                Tools.Global.uart.serial.BaudRate = value;
                SetProperty(ref _baudRate, value);
                Save();
            }
            catch (Exception e)
            {
                Tools.MessageBox.Show(e.Message);
            }
        }
    }

    private bool _autoReconnect = true;
    public bool autoReconnect
    {
        get => _autoReconnect;
        set
        {
            if (SetProperty(ref _autoReconnect, value))
                Save();
        }
    }

    private bool _autoSaveLog = true;
    public bool autoSaveLog
    {
        get => _autoSaveLog;
        set
        {
            if (SetProperty(ref _autoSaveLog, value))
                Save();
        }
    }

private bool _hexSend = false;

    /// <summary>
    /// 主数据发送框是否发hex
    /// </summary>
    public bool hexSend
    {
        get => _hexSend;
        set
        {
            if (SetProperty(ref _hexSend, value))
                Save();
        }
    }

    private bool _continuousSend = false;

    /// <summary>主发送框是否启用“连续发送”（点击发送按钮开始 / 再次点击停止）</summary>
    public bool continuousSend
    {
        get => _continuousSend;
        set
        {
            if (SetProperty(ref _continuousSend, value))
                Save();
        }
    }

    private int _continuousDelayMs = 1000;

    /// <summary>连续发送间隔（毫秒，使用时限制到 10~3600000）</summary>
    public int continuousDelayMs
    {
        get => _continuousDelayMs;
        set
        {
            if (SetProperty(ref _continuousDelayMs, value))
                Save();
        }
    }

    private int _continuousRepeat = -1;

    /// <summary>连续发送的重复次数（-1=一直重复直到再次点发送停止；0=只发一次）</summary>
    public int continuousRepeat
    {
        get => _continuousRepeat;
        set
        {
            if (SetProperty(ref _continuousRepeat, value))
                Save();
        }
    }

    private bool _sendByEnter = false;

    /// <summary>true=按 Enter 发送；false=按 Ctrl+Enter 发送</summary>
    public bool sendByEnter
    {
        get => _sendByEnter;
        set
        {
            if (SetProperty(ref _sendByEnter, value))
                Save();
        }
    }

    private bool _showSend = true;

    public bool showSend
    {
        get => _showSend;
        set
        {
            if (SetProperty(ref _showSend, value))
                Save();
        }
    }

private int _parity = 0;
    public int parity
    {
        get => _parity;
        set
        {
            try
            {
                _parity = value;
                Tools.Global.uart.serial.Parity = (Parity)value;
                Save();
            }
            catch (Exception e)
            {
                Tools.MessageBox.Show(e.Message);
            }
        }
    }

    private int _timeout = 50;
    public int timeout
    {
        get => _timeout;
        set
        {
            if (SetProperty(ref _timeout, value))
                Save();
        }
    }

    private int _dataBits = 8;
    public int dataBits
    {
        get => _dataBits;
        set
        {
            try
            {
                _dataBits = value;
                Tools.Global.uart.serial.DataBits = value;
                Save();
            }
            catch (Exception e)
            {
                Tools.MessageBox.Show(e.Message);
            }
        }
    }

    private int _stopBit = 1;
    public int stopBit
    {
        get => _stopBit;
        set
        {
            try
            {
                _stopBit = value;
                Tools.Global.uart.serial.StopBits = (StopBits)value;
                Save();
            }
            catch (Exception e)
            {
                Tools.MessageBox.Show(e.Message);
            }
        }
    }

    private string _sendScript = "default";
    public string sendScript
    {
        get => _sendScript;
        set
        {
            if (SetProperty(ref _sendScript, value))
                Save();
        }
    }

    private string _recvScript = "default";
    public string recvScript
    {
        get => _recvScript;
        set
        {
            if (SetProperty(ref _recvScript, value))
                Save();
        }
    }

    private string _runScript = "example";
    public string runScript
    {
        get => _runScript;
        set
        {
            if (SetProperty(ref _runScript, value))
                Save();
        }
    }

    private bool _topmost = false;
    public bool topmost
    {
        get => _topmost;
        set
        {
            if (SetProperty(ref _topmost, value))
            {
                try
                {
                    MainWindowTop?.Invoke(value, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Tools.Logger.Swallowed("topmost event", ex);
                }
                Save();
            }
        }
    }

    private bool _terminal = true;
    public bool terminal
    {
        get => _terminal;
        set
        {
            if (SetProperty(ref _terminal, value))
                Save();
        }
    }

    private int _encoding = 65001;
    public int encoding
    {
        get => _encoding;
        set
        {
            try
            {
                Encoding.GetEncoding(value);
                SetProperty(ref _encoding, value);
                Save();
            }
            catch { /* 忽略：GetEncoding 抛异常说明编码无效，保持原值 */ }
        }
    }

    private int _appendNewline = 0;

    /// <summary>
    /// 发送数据末尾追加的换行类型：0=无追加，1=\n，2=\r，3=\n\r，4=\r\n
    /// </summary>
    public int appendNewline
    {
        get => _appendNewline;
        set
        {
            var v = value < 0 ? 0 : (value > 4 ? 4 : value);
            if (SetProperty(ref _appendNewline, v))
                Save();
        }
    }

    private List<string> _sendHistory = new List<string>();

    /// <summary>发送记录（最近发送过的内容，最新在前）</summary>
    public List<string> sendHistory
    {
        get => _sendHistory;
        set
        {
            if (SetProperty(ref _sendHistory, value))
                Save();
        }
    }

    private int _sendHistoryLimit = 30;

    /// <summary>发送记录条数上限（5~200）</summary>
    public int sendHistoryLimit
    {
        get => _sendHistoryLimit;
        set
        {
            var v = value < 5 ? 5 : (value > 200 ? 200 : value);
            if (SetProperty(ref _sendHistoryLimit, v))
                Save();
        }
    }

    private bool _enableSymbol = true;
    public bool EnableSymbol
    {
        get => _enableSymbol;
        set
        {
            if (SetProperty(ref _enableSymbol, value))
                Save();
        }
    }
}
