using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using llcom.LuaEnv;
using llcom.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace llcom.Pages;

/// <summary>
/// tcpTest.xaml 的交互逻辑
/// </summary>
public partial class tcpTest : Page, INotifyPropertyChanged
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

    public tcpTest()
    {
        InitializeComponent();
    }

    public WebSocket ws = new WebSocket("wss://gps.openluat.com/netlab/ws/netlab");
    public WebSocket wsV6 = new WebSocket("wss://netlab.luatos.org/ws/netlab");
    ObservableCollection<string> clients = new ObservableCollection<string>();

    /// <summary>
    /// 连接状态
    /// </summary>
    private bool _isConnected = false;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private bool _hexMode = false;
    public bool HexMode
    {
        get => _hexMode;
        set => SetProperty(ref _hexMode, value);
    }

    private string _address = "loading...";
    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    private string _addressV6 = "loading...";
    public string AddressV6
    {
        get => _addressV6;
        set => SetProperty(ref _addressV6, value);
    }

    private string _connectionType = "unknow";
    public string ConnectionType
    {
        get => _connectionType;
        set => SetProperty(ref _connectionType, value);
    }

    private static bool loaded = false;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded)
            return;
        loaded = true;
        //绑定
        MainGrid.DataContext = this;
        ClientList.ItemsSource = clients;

        //心跳（ws / wsV6 共用同一实现）
        Timer heartbeat = CreateHeartbeat(ws);
        Timer heartbeatV6 = CreateHeartbeat(wsV6);

        //允许所有tls协议版本
        var tlsAll =
            System.Security.Authentication.SslProtocols.Ssl2
            | System.Security.Authentication.SslProtocols.Ssl3
            | System.Security.Authentication.SslProtocols.Tls
            | System.Security.Authentication.SslProtocols.Tls11
            | System.Security.Authentication.SslProtocols.Tls12;
        ws.SslConfiguration.EnabledSslProtocols = tlsAll;
        wsV6.SslConfiguration.EnabledSslProtocols = tlsAll;

        //ws / wsV6 事件（共用同一套处理器，差异用 isV6 区分）
        SetupWebSocket(ws, heartbeat, false);
        SetupWebSocket(wsV6, heartbeatV6, true);

        //适配一下通用通道（Lua → 本通道）
        Tools.LuaChannelBridge.RegisterSend(
            "netlab",
            () => IsConnected,
            (_, t) =>
                Send(
                    Tools.Global.Byte2Hex(t.Get<byte[]>("data")),
                    true,
                    t.Get<string>("client")
                )
        );
    }

    private void ShowData(string title, byte[] data = null, bool send = false) =>
        Tools.NetworkLog.Show("🚀 public socket server: ", title, data, send);

    /// <summary>创建一个 30s 心跳定时器（netlab 连接保活）。</summary>
    private Timer CreateHeartbeat(WebSocket socket)
    {
        var timer = new Timer { Interval = 30000, AutoReset = true };
        timer.Elapsed += (ss, ee) =>
        {
            try
            {
                if (IsConnected)
                    socket.Send("{}");
            }
            catch (System.Exception ex)
            {
                Tools.Logger.Swallowed("tcp test heartbeat", ex);
            }
        };
        return timer;
    }

    /// <summary>为某个 WebSocket 挂接生命周期与消息处理器。</summary>
    private void SetupWebSocket(WebSocket socket, Timer heartbeat, bool isV6)
    {
        socket.OnOpen += (ss, ee) =>
        {
            IsConnected = true;
            heartbeat.Start();
            this.Dispatcher.Invoke(() => clients.Clear());
        };
        socket.OnClose += (ss, ee) =>
        {
            IsConnected = false;
            heartbeat.Stop();
            this.Dispatcher.Invoke(() => clients.Clear());
        };
        socket.OnMessage += (ss, ee) => HandleNetlabMessage(ee, isV6);
        socket.OnError += (ss, ee) => ShowData($"📢 Create failed :{ee.Message}");
    }

    /// <summary>
    /// 处理 netlab 服务端消息（原先 ws 与 wsV6 各有一份几乎逐行重复的实现，
    /// 现合为一处，仅 "port" 分支的地址格式按 isV6 区分）。
    /// </summary>
    private void HandleNetlabMessage(MessageEventArgs ee, bool isV6)
    {
        Debug.WriteLine(!ee.IsPing ? ee.Data : "ping");
        if (ee.IsPing)
            return;
        try
        {
            JObject o = (JObject)JsonConvert.DeserializeObject(ee.Data);
            switch ((string)o["action"])
            {
                case "port":
                    if (isV6)
                    {
                        Address = $"{ConnectionType}://152.70.80.204:{o["port"]}";
                        AddressV6 =
                            $"{ConnectionType}://[2603:c023:1:5fcc:c028:8ed:49a7:6e08]:{o["port"]}";
                    }
                    else
                    {
                        Address = $"{ConnectionType}://115.120.239.161:{o["port"]}";
                        AddressV6 = "not suppoer ipv6";
                    }
                    ShowData($"📢 Created a {ConnectionType} server.");
                    break;
                case "client":
                case "connected":
                    ShowData($"✔ [{o["client"]}]{o["addr"]} connected.");
                    this.Dispatcher.Invoke(
                        new Action(
                            delegate
                            {
                                clients.Add((string)o["client"]);
                                if (ClientList.Text.Length == 0)
                                    ClientList.Text = (string)o["client"];
                            }
                        )
                    );
                    //适配一下通用通道
                    LuaApis.SendChannelsReceived(
                        "netlab",
                        new { client = "connected", data = (string)o["client"] }
                    );
                    break;
                case "closed":
                    ShowData($"❌ [{o["client"]}] disconnected.");
                    this.Dispatcher.Invoke(
                        new Action(
                            delegate
                            {
                                clients.Remove((string)o["client"]);
                                if (ClientList.Text.Length == 0 && clients.Count > 0)
                                    ClientList.Text = clients[0];
                            }
                        )
                    );
                    //适配一下通用通道
                    LuaApis.SendChannelsReceived(
                        "netlab",
                        new { client = "disconnected", data = (string)o["client"] }
                    );
                    break;
                case "data":
                    string data = (string)o["data"];
                    byte[] buff = NetworkHelper.BuildSendBytes(data, (bool)o["hex"]);
                    ShowData($" → receive from [{o["client"]}]", buff);
                    //适配一下通用通道
                    LuaApis.SendChannelsReceived(
                        "netlab",
                        new { client = (string)o["client"], data = buff }
                    );
                    break;
                case "error":
                    ShowData($"❔ error:{o["msg"]}");
                    break;
                default:
                    break;
            }
        }
        catch (System.Exception ex)
        {
            //非 JSON / 未知消息：忽略即可，仅记录
            Tools.Logger.Swallowed("netlab message", ex);
        }
    }

    private bool _connecting = false;
    public bool Connecting
    {
        get => _connecting;
        set => SetProperty(ref _connecting, value);
    }

    private async void ConnectWebSocket(string ctype, string stype = null)
    {
        if (Connecting)
            return;
        Connecting = true;
        ShowData($"📢 Server is creating...");
        await Task.Run(() =>
        {
            try
            {
                if (ctype == "tcpv6")
                {
                    ConnectionType = "tcp";
                    wsV6.Connect();
                    wsV6.Send(JsonConvert.SerializeObject(new { action = "newp", type = "tcp" }));
                }
                else
                {
                    ConnectionType = ctype;
                    ws.Connect();
                    ws.Send(
                        JsonConvert.SerializeObject(new { action = "newp", type = stype ?? ctype })
                    );
                }
            }
            catch (Exception e)
            {
                Tools.Logger.Swallowed("netlab connect", e);
            }
        });
        Connecting = false;
    }

    private void CreateTcpButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectWebSocket("tcp");
    }

    private void CreateUdpButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectWebSocket("udp");
    }

    private void CreateTcpSSLButton_OnClick(object sender, RoutedEventArgs e)
    {
        ConnectWebSocket("ssl", "ssl-tcp");
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            (ws.IsAlive ? ws : wsV6).Close();
            ShowData($"📢 Server closed.");
            Address = "loading...";
            AddressV6 = "loading...";
        }
        catch (Exception ee)
        {
            Tools.MessageBox.Show(ee.Message);
        }
    }

    private void SendDataButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsConnected || ClientList.Text.Length == 0)
            return;
        Send(toSendDataTextBox.Text, HexMode, ClientList.Text);
    }

    private bool Send(string data, bool isHex, string client)
    {
        try
        {
            (ws.IsAlive ? ws : wsV6).Send(
                JsonConvert.SerializeObject(
                    new
                    {
                        action = "sendc",
                        data,
                        hex = isHex,
                        client,
                    }
                )
            );
            ShowData(
                $" ← send to [{client}]",
                NetworkHelper.BuildSendBytes(data, isHex),
                true
            );
            return true;
        }
        catch (Exception ee)
        {
            ShowData($"sent error: {ee.Message}");
            return false;
        }
    }

    private void KickClientButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsConnected || ClientList.Text.Length == 0)
            return;
        try
        {
            (ws.IsAlive ? ws : wsV6).Send(
                JsonConvert.SerializeObject(new { action = "closec", client = ClientList.Text })
            );
        }
        catch (Exception ee)
        {
            Tools.MessageBox.Show(ee.Message);
        }
    }

    private void CreateTcpIpv6Button_Click(object sender, RoutedEventArgs e)
    {
        ConnectWebSocket("tcpv6");
    }
}