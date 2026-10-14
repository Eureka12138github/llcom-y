using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace llcom.Model;

/// <summary>
/// 收发数据显示条目（分包模式下列表的一项）。
/// 由 DataShowViewModel 生成，DataShowPage 的 ItemsControl 绑定显示。
/// 显示策略：数据以**单行**呈现，具体是 HEX 还是字符串由设置决定
/// （发送看 <see cref="Settings.sendShowHex"/>，接收看 <see cref="Settings.recvShowHex"/>），
/// 切换设置时通过 <see cref="Refresh"/> 即时重算显示。
/// </summary>
public class DataShowItem : INotifyPropertyChanged
{
    //原始（处理后）字节，切换显示格式时据此重算
    private readonly byte[] _data;
    private readonly bool _sent;
    private readonly bool _generic; //通用通道条目（带标题，如 MQTT/TCP）

    public string TimeText { get; set; }

    /// <summary>方向箭头：发送为“→”，接收为“←”</summary>
    public string ArrowText { get; set; }

    public SolidColorBrush DataTextColor { get; set; }
    public string RawTitle { get; set; }
    public SolidColorBrush RawTextColor { get; set; }

    private string _dataText;
    public string DataText
    {
        get => _dataText;
        private set
        {
            _dataText = value;
            OnPropertyChanged();
        }
    }

    private string _rawText;

    /// <summary>前面要加换行符（通用通道条目用）</summary>
    public string RawText
    {
        get => _rawText;
        private set
        {
            _rawText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// 普通收发数据条目（接收时先经接收转换脚本处理）
    /// </summary>
    public DataShowItem(byte[] data, DateTime time, bool sent)
    {
        if (data == null || data.Length == 0)
            return;
        byte[] temp = data.ToArray();
        //转换下接收数据
        if (!sent)
        {
            try
            {
                temp = LuaEnv.LuaLoader.Run(
                    $"{Tools.Global.setting.recvScript}.lua",
                    new System.Collections.ArrayList { "uartData", temp },
                    "user_script_recv_convert/"
                );
            }
            catch (Exception ex)
            {
                Tools.MessageBox.Show($"receive convert lua script error\r\n" + ex.ToString());
                return;
            }
            if (temp == null)
                return;
        }

        _data = temp;
        _sent = sent;
        _generic = false;

        TimeText = time.ToString("[yyyy/MM/dd HH:mm:ss.fff]");
        ArrowText = sent ? " → " : " ← ";
        DataTextColor = sent ? Brushes.DarkRed : Brushes.DarkGreen;
        Refresh();
    }

    /// <summary>
    /// 通用日志条目（带标题与颜色，如 MQTT/TCP 等通道数据）
    /// </summary>
    public DataShowItem(string title, byte[] data, DateTime time, SolidColorBrush color)
    {
        if (data == null)
            return;
        _data = data.ToArray();
        _generic = true;

        TimeText = time.ToString("[yyyy/MM/dd HH:mm:ss.fff]");
        RawTitle = title;
        RawTextColor = color;
        Refresh();
    }

    /// <summary>
    /// 按当前显示设置重算文本（切换 HEX/字符串时调用）。
    /// </summary>
    public void Refresh()
    {
        if (_data == null)
            return;
        if (_generic)
            RawText = "\n" + Format(_data);
        else
            DataText = Format(_data);
    }

    /// <summary>
    /// 按设置格式化为 HEX 或可读字符串。
    /// </summary>
    private string Format(byte[] data)
    {
        //通用通道条目跟随“接收显示”设置
        var showHex = _generic
            ? Tools.Global.setting.recvShowHex
            : (_sent ? Tools.Global.setting.sendShowHex : Tools.Global.setting.recvShowHex);
        return showHex
            ? Tools.Global.Byte2Hex(data, " ", data.Length)
            : Tools.Global.Byte2Readable(data, data.Length);
    }
}
