using System;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using llcom.Model;
using llcom.Tools;

namespace llcom.ViewModels;

/// <summary>
/// 本地 UDP 服务器工具页 ViewModel（“VM 化样板”）。
/// 状态（<see cref="IsConnected"/>）与监听逻辑全部在此，Page 只保留 UI 桥接，
/// 因而该页**不再需要手写 INotifyPropertyChanged**（对应 docs/02 第四节所述框架怪癖）。
/// </summary>
public partial class UdpLocalViewModel : ObservableObject
{
    /// <summary>全局设置（供 XAML 绑定 <c>udpServerPort</c> 等）</summary>
    public Settings Setting { get; }

    /// <summary>是否处于监听状态（绑定按钮/输入框的可用性与可见性）</summary>
    [ObservableProperty]
    private bool _isConnected;

    private UdpClient _server;

    public UdpLocalViewModel(Settings setting)
    {
        Setting = setting;
    }

    /// <summary>
    /// 开始监听。<paramref name="ip"/> 非法时抛异常（由调用方提示）；
    /// 返回后 <see cref="IsConnected"/> 即代表是否成功处于监听状态。
    /// </summary>
    public void Listen(string ip, int port)
    {
        if (_server != null)
        {
            IsConnected = false;
            return;
        }

        IPAddress localAddr = IPAddress.Parse(ip);
        IPEndPoint endpoint = new IPEndPoint(localAddr, port);
        _server = new UdpClient(endpoint);

        ShowData($"🗑 {NetworkHelper.FormatHostPort(ip, port)}");

        AsyncCallback callback = null;
        callback = ar =>
        {
            try
            {
                UdpClient u = ((UdpState)ar.AsyncState).u;
                IPEndPoint e = ((UdpState)ar.AsyncState).e;

                byte[] receiveBytes = u.EndReceive(ar, ref e);
                ShowData(NetworkHelper.FormatHostPort(e.Address.ToString(), e.Port), receiveBytes);
                _server.BeginReceive(callback, ar.AsyncState);
            }
            catch (Exception ex)
            {
                Logger.Swallowed("udp server", ex);
            }
        };

        UdpState s = new UdpState { e = endpoint, u = _server };
        try
        {
            _server.BeginReceive(callback, s);
        }
        catch (Exception ex)
        {
            ShowData($"❗ Server create error {ex.Message}");
            IsConnected = false;
            return;
        }

        IsConnected = true;
    }

    /// <summary>关闭监听。</summary>
    public void Stop()
    {
        _server?.Close();
        _server?.Dispose();
        _server = null;
        IsConnected = false;
        ShowData($"🚫 server closed");
    }

    private void ShowData(string title, byte[] data = null, bool send = false) =>
        NetworkLog.Show("🗑 local udp server: ", title, data, send);
}