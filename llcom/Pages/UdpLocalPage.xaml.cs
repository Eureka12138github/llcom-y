using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
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
using llcom.Model;

namespace llcom.Pages;

/// <summary>
/// UdpLocalPage.xaml 的交互逻辑
/// </summary>
public partial class UdpLocalPage : Page, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// 设置属性值，值变化时触发 PropertyChanged（XAML 生成的 g.cs 基类固定为 Page，
    /// 无法继承 ObservableObject/ObservablePage，故类内自带此帮助方法）
    /// </summary>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string propertyName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public UdpLocalPage()
    {
        InitializeComponent();
    }

    private bool _isConnected = false;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private static bool loaded = false;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded)
            return;
        loaded = true;
        RefreshIp();
        //绑定
        MainGrid.DataContext = this;
        IpPortTextBox.DataContext = Tools.Global.setting;
    }

    /// <summary>
    /// 刷新本机ip列表
    /// </summary>
    private void RefreshIp() =>
        Tools.NetworkHelper.FillLocalIpComboBox(IpListComboBox, "udp server");

    private void ShowData(string title, byte[] data = null, bool send = false) =>
        Tools.NetworkLog.Show("🗑 local udp server: ", title, data, send);

    private UdpClient Server = null;

    /// <summary>
    /// 开始监听服务器
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <returns></returns>
    private bool StartServer(string ip, int port)
    {
        if (Server != null)
            return false;
        IPAddress localAddr = IPAddress.Parse(ip);
        IPEndPoint IpEndPoint = new IPEndPoint(localAddr, port);
        Server = new UdpClient(IpEndPoint);

        ShowData($"🗑 {Tools.NetworkHelper.FormatHostPort(ip, port)}");

        AsyncCallback newConnectionCb = null;
        newConnectionCb = new AsyncCallback(
            (ar) =>
            {
                try
                {
                    UdpClient u = ((UdpState)(ar.AsyncState)).u;
                    IPEndPoint e = ((UdpState)(ar.AsyncState)).e;

                    byte[] receiveBytes = u.EndReceive(ar, ref e);
                    ShowData(
                        Tools.NetworkHelper.FormatHostPort(e.Address.ToString(), e.Port),
                        receiveBytes
                    );
                    Server.BeginReceive(newConnectionCb, ar.AsyncState);
                }
                catch (System.Exception ex) { Tools.Logger.Swallowed("udp server", ex); }
            }
        );
        UdpState s = new UdpState();
        s.e = IpEndPoint;
        s.u = Server;
        try
        {
            Server.BeginReceive(newConnectionCb, s);
        }
        catch (Exception ex)
        {
            ShowData($"❗ Server create error {ex.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 关闭服务器，断开所有连接
    /// </summary>
    private void StopServer()
    {
        Server?.Close();
        Server?.Dispose();
        Server = null;
    }

    private void RefreshIpButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshIp();
    }

    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        int port;
        if (int.TryParse(IpPortTextBox.Text, out port))
        {
            try
            {
                IsConnected = StartServer(IpListComboBox.Text, port);
            }
            catch (Exception err)
            {
                Tools.MessageBox.Show(err.Message);
            }
        }
    }

    private void StopListenButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StopServer();
            IsConnected = false;
            ShowData($"🚫 server closed");
        }
        catch (System.Exception ex) { Tools.Logger.Swallowed("udp server", ex); }
    }
}
