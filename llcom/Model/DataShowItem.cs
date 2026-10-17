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
/// （发送看 <see cref="Settings.hexSend"/>，接收看 <see cref="Settings.recvShowHex"/>），
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

    //显示文本按需计算并缓存：切换格式时只失效缓存，不对所有条目做字符串转换
    private string _cachedText = "";
    private bool _cacheValid;
    private bool _cacheShowHex;
    private bool _cacheSymbol;
    private int _cacheEncoding;

    /// <summary>收发数据文本（普通条目；通用通道条目为 null）</summary>
    public string? DataText => _generic ? null : GetText(false);

    /// <summary>通用通道条目的文本（前面加换行；普通条目为 null）</summary>
    public string? RawText => _generic ? GetText(true) : null;

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
    }

    /// <summary>
    /// 按当前显示设置取文本（首次访问或设置变化后重算，其余走缓存）。
    /// </summary>
    private string GetText(bool leadingNewline)
    {
        var setting = Tools.Global.setting;
        var showHex = ShouldShowHex();
        if (
            !_cacheValid
            || _cacheShowHex != showHex
            || _cacheSymbol != setting.EnableSymbol
            || _cacheEncoding != setting.encoding
        )
        {
            _cachedText = Format(_data);
            _cacheShowHex = showHex;
            _cacheSymbol = setting.EnableSymbol;
            _cacheEncoding = setting.encoding;
            _cacheValid = true;
        }
        return leadingNewline ? "\n" + _cachedText : _cachedText;
    }

    private bool ShouldShowHex()
    {
        var setting = Tools.Global.setting;
        //通用通道条目跟随“接收显示”设置
        return _generic ? setting.recvShowHex : (_sent ? setting.hexSend : setting.recvShowHex);
    }

    /// <summary>
    /// 显示设置变化后调用：**只失效缓存并通知绑定**，字符串转换推迟到真正读取时（虚拟化后只算可见条目）。
    /// </summary>
    public void Refresh()
    {
        if (_data == null)
            return;
        _cacheValid = false;
        OnPropertyChanged(nameof(DataText));
        OnPropertyChanged(nameof(RawText));
    }

    /// <summary>
    /// 按设置格式化为 HEX 或可读字符串。
    /// </summary>
    private string Format(byte[] data) =>
        ShouldShowHex()
            ? Tools.Global.Byte2Hex(data, " ", data.Length)
            : Tools.Global.Byte2Readable(data, data.Length);
}
