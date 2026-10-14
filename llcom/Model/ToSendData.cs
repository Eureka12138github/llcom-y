using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace llcom.Model;

public class ToSendData : INotifyPropertyChanged
{
    public static event EventHandler DataChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    private int _id;
    private string _text;
    private bool _hex;
    private string _commit;
    private string _recvScriptPath = "";
    private string _recvScriptPara = "";
    public int id
    {
        get { return _id; }
        set
        {
            _id = value;
            //DataChanged?.Invoke(0, EventArgs.Empty);
        }
    }
    public string text
    {
        get { return _text; }
        set
        {
            _text = value;
            DataChanged?.Invoke(0, EventArgs.Empty);
        }
    }
    public bool hex
    {
        get { return _hex; }
        set
        {
            _hex = value;
            DataChanged?.Invoke(0, EventArgs.Empty);
        }
    }

    public string commit
    {
        get { return _commit; }
        set
        {
            _commit = value;
            DataChanged?.Invoke(0, EventArgs.Empty);
        }
    }

    public string recvScriptPath
    {
        get { return _recvScriptPath; }
        set
        {
            _recvScriptPath = value;
            DataChanged?.Invoke(0, EventArgs.Empty);
            OnPropertyChanged(nameof(recvScriptPath));
        }
    }

    public string recvScriptPara
    {
        get { return _recvScriptPara; }
        set
        {
            _recvScriptPara = value;
            DataChanged?.Invoke(0, EventArgs.Empty);
            OnPropertyChanged(nameof(recvScriptPara));
        }
    }

    protected void OnPropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _timerEnabled;
    private int _timerIntervalMs = 1000;

    /// <summary>是否启用“定时发送”（快捷发送每条独立定时）</summary>
    public bool timerEnabled
    {
        get { return _timerEnabled; }
        set
        {
            if (_timerEnabled == value)
                return;
            _timerEnabled = value;
            DataChanged?.Invoke(0, EventArgs.Empty);
            OnPropertyChanged(nameof(timerEnabled));
        }
    }

    /// <summary>定时发送间隔（毫秒，限制到 10~3600000）</summary>
    public int timerIntervalMs
    {
        get { return _timerIntervalMs; }
        set
        {
            var v = value < 10 ? 10 : (value > 3600000 ? 3600000 : value);
            if (_timerIntervalMs == v)
                return;
            _timerIntervalMs = v;
            DataChanged?.Invoke(0, EventArgs.Empty);
            OnPropertyChanged(nameof(timerIntervalMs));
        }
    }
}
