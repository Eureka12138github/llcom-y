using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using llcom.LuaEnv;
using llcom.Model;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Client.Subscribing;
using Newtonsoft.Json;
using ScottPlot.Drawing.Colormaps;

namespace llcom.Pages;

/// <summary>
/// MqttTestPage.xaml 的交互逻辑
/// </summary>
public partial class MqttTestPage : Page, INotifyPropertyChanged
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

    public MqttTestPage()
    {
        InitializeComponent();
    }

    private bool initial = false;
    private static MqttFactory factory = new MqttFactory();
    private MQTTnet.Client.IMqttClient mqttClient = factory.CreateMqttClient();

    private bool _mqttIsConnected = false;
    public bool MqttIsConnected
    {
        get => _mqttIsConnected;
        set => SetProperty(ref _mqttIsConnected, value);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (initial)
            return;
        initial = true;

        BrokerTextBox.DataContext = Tools.Global.setting;
        PortTextBox.DataContext = Tools.Global.setting;
        ClientTextBox.DataContext = Tools.Global.setting;
        TLSCheckBox.DataContext = Tools.Global.setting;
        UserTextBox.DataContext = Tools.Global.setting;
        PasswordTextBox.DataContext = Tools.Global.setting;
        KeepAliveTextBox.DataContext = Tools.Global.setting;
        CleanTextBox.DataContext = Tools.Global.setting;
        HexCheckBox.DataContext = Tools.Global.setting;
        WsCheckBox.DataContext = Tools.Global.setting;
        WsPathTextBox.DataContext = Tools.Global.setting;
        publishTopicTextBox.DataContext = Tools.Global.setting;
        subcribeTextBox.DataContext = Tools.Global.setting;
        TLSCertBox.DataContext = Tools.Global.setting;
        MQTTTLSCa.DataContext = Tools.Global.setting;
        MQTTTLSClient.DataContext = Tools.Global.setting;
        MQTTTLSPassword.DataContext = Tools.Global.setting;
        ConnectButton.DataContext = this;
        SettingStackPanel.DataContext = this;

        mqttClient.UseConnectedHandler(e =>
        {
            this.Dispatcher.Invoke(
                new Action(
                    delegate
                    {
                        MqttIsConnected = mqttClient.IsConnected;
                        subListBox.Items.Clear();
                    }
                )
            );
            ShowMqtt($"MQTT event: ✔ connected");
        });
        mqttClient.UseDisconnectedHandler(e =>
        {
            this.Dispatcher.Invoke(
                new Action(
                    delegate
                    {
                        MqttIsConnected = mqttClient.IsConnected;
                        subListBox.Items.Clear();
                        subListBox.Items.Add(TryFindResource("MQTTNotConnect") as string ?? "?!");
                    }
                )
            );
            ShowMqtt($"MQTT event: ❌ disconnected");
        });
        mqttClient.UseApplicationMessageReceivedHandler(e =>
        {
            //适配一下通用通道
            LuaApis.SendChannelsReceived(
                "mqtt",
                new
                {
                    topic = e.ApplicationMessage.Topic,
                    payload = e.ApplicationMessage.Payload,
                    qos = (int)e.ApplicationMessage.QualityOfServiceLevel,
                }
            );
            //显示数据
            ShowMqtt(
                $"MQTT → {e.ApplicationMessage.Topic}({(int)e.ApplicationMessage.QualityOfServiceLevel})",
                e.ApplicationMessage.Payload
            );
        });

        //适配一下通用通道（Lua → 本通道）
        Tools.LuaChannelBridge.RegisterSend(
            "mqtt",
            () => mqttClient.IsConnected,
            (_, t) =>
            {
                try
                {
                    return Publish(
                        t.Get<string>("topic"),
                        t.Get<byte[]>("payload"),
                        t.Get<int>("qos")
                    );
                }
                catch
                {
                    return false;
                }
            }
        );
    }

    /// <summary>统一 MQTT 数据日志（原为 6 处内联 ShowDataRaw）。</summary>
    private void ShowMqtt(string title, byte[] data = null, bool send = false) =>
        Tools.NetworkLog.Show("", title, data, send);

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (mqttClient.IsConnected)
        {
            Task.Run(async () =>
            {
                try
                {
                    await mqttClient.DisconnectAsync();
                }
                catch (System.Exception ex) { Tools.Logger.Swallowed("mqtt", ex); }
            });
        }
        else
        {
            var temp = new MqttClientOptionsBuilder()
                .WithClientId(Tools.Global.setting.mqttClientID)
                .WithCredentials(Tools.Global.setting.mqttUser, Tools.Global.setting.mqttPassword)
                .WithKeepAlivePeriod(new TimeSpan(0, 0, Tools.Global.setting.mqttKeepAlive));
            if (Tools.Global.setting.mqttTLS)
            {
                if (!Tools.Global.setting.mqttTLSCert || Tools.Global.setting.mqttWs)
                    temp.WithTls(
                        new MqttClientOptionsBuilderTlsParameters()
                        {
                            AllowUntrustedCertificates = true,
                            IgnoreCertificateChainErrors = true,
                            UseTls = true,
                            SslProtocol =
                                System.Security.Authentication.SslProtocols.Default
                                | System.Security.Authentication.SslProtocols.Tls11
                                | System.Security.Authentication.SslProtocols.Tls12
                                | System.Security.Authentication.SslProtocols.Ssl2
                                | System.Security.Authentication.SslProtocols.Ssl3,
                        }
                    );
                else
                {
                    try
                    {
                        var certCa = X509Certificate.CreateFromCertFile(
                            Tools.Global.setting.mqttTLSCertCaPath
                        );
                        var certClient = new X509Certificate2(
                            Tools.Global.setting.mqttTLSCertClientPath,
                            string.IsNullOrEmpty(Tools.Global.setting.mqttTLSCertClientPassword)
                                ? null
                                : Tools.Global.setting.mqttTLSCertClientPassword
                        );
                        temp.WithTls(
                            new MqttClientOptionsBuilderTlsParameters()
                            {
                                AllowUntrustedCertificates = false,
                                IgnoreCertificateChainErrors = true,
                                UseTls = true,
                                SslProtocol =
                                    System.Security.Authentication.SslProtocols.Default
                                    | System.Security.Authentication.SslProtocols.Tls11
                                    | System.Security.Authentication.SslProtocols.Tls12
                                    | System.Security.Authentication.SslProtocols.Ssl2
                                    | System.Security.Authentication.SslProtocols.Ssl3,
                                CertificateValidationHandler = (eventArgs) =>
                                {
                                    if (
                                        eventArgs.SslPolicyErrors
                                        != System.Net.Security.SslPolicyErrors.None
                                    )
                                    {
                                        ShowMqtt(
                                            $"MQTT event: ‼ ssl error {eventArgs.SslPolicyErrors}"
                                        );
                                    }
                                    return true;
                                },
                                Certificates = new List<X509Certificate>() { certClient, certCa },
                            }
                        );
                    }
                    catch
                    {
                        ShowMqtt($"MQTT error: ‼ ssl certificate pfx file error");
                        return;
                    }
                }
            }
            if (Tools.Global.setting.mqttWs)
                temp.WithWebSocketServer(
                    $"{Tools.Global.setting.mqttServer}:{Tools.Global.setting.mqttPort}"
                        + $"{Tools.Global.setting.mqttWsPath}"
                );
            else
                temp.WithTcpServer(Tools.Global.setting.mqttServer, Tools.Global.setting.mqttPort);
            if (Tools.Global.setting.mqttCleanSession)
                temp.WithCleanSession();
            var options = temp.Build();
            MqttIsConnected = true; //连接中，锁住数据
            Task.Run(async () =>
            {
                try
                {
                    await mqttClient.ConnectAsync(options, CancellationToken.None);
                }
                catch (System.Exception ex) { Tools.Logger.Swallowed("mqtt", ex); }
            });
        }
    }

    private void subcribeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(subcribeTextBox.Text))
        {
            Tools.MessageBox.Show("no subcribe topic!");
            return;
        }
        if (mqttClient.IsConnected)
        {
            var topic = subcribeTextBox.Text;
            var qos = int.Parse(subQOSComboBox.Text);
            Task.Run(async () =>
            {
                try
                {
                    var r = await mqttClient.SubscribeAsync(
                        new MqttTopicFilterBuilder()
                            .WithTopic(topic)
                            .WithQualityOfServiceLevel(
                                (MQTTnet.Protocol.MqttQualityOfServiceLevel)qos
                            )
                            .Build()
                    );
                    this.Dispatcher.Invoke(
                        new Action(
                            delegate
                            {
                                foreach (string i in subListBox.Items)
                                {
                                    if (i == topic)
                                        return;
                                }
                                subListBox.Items.Add(topic);
                            }
                        )
                    );
                }
                catch (System.Exception ex) { Tools.Logger.Swallowed("mqtt", ex); }
            });
        }
    }

    private void publishButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(publishTopicTextBox.Text))
        {
            Tools.MessageBox.Show("no publish topic!");
            return;
        }
        if (mqttClient.IsConnected)
        {
            var topic = publishTopicTextBox.Text;
            var payload = Tools.NetworkHelper.BuildSendBytes(
                PublishTextBox.Text,
                HexCheckBox.IsChecked ?? false
            );
            var qos = int.Parse(publishQOSComboBox.Text);
            Task.Run(() =>
            {
                Publish(topic, payload, qos);
            });
        }
    }

    private bool Publish(string topic, byte[] payload, int qos)
    {
        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .Build();
            mqttClient.PublishAsync(message, CancellationToken.None).Wait();
            ShowMqtt(
                $"MQTT ← {message.Topic}({(int)message.QualityOfServiceLevel})",
                message.Payload,
                send: true
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadCertificate_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        System.Windows.Forms.OpenFileDialog OpenFileDialog =
            new System.Windows.Forms.OpenFileDialog();
        OpenFileDialog.Filter = (string)((TextBlock)sender).Tag switch
        {
            "CA" => "crt file|*.crt",
            "Client" => "pfx file|*.pfx",
            _ => "",
        };
        if (OpenFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            try
            {
                switch ((string)((TextBlock)sender).Tag)
                {
                    case "CA":
                        Tools.Global.setting.mqttTLSCertCaPath = OpenFileDialog.FileName;
                        break;
                    case "Client":
                        Tools.Global.setting.mqttTLSCertClientPath = OpenFileDialog.FileName;
                        break;
                }
            }
            catch (System.Exception ex) { Tools.Logger.Swallowed("mqtt", ex); }
        }
    }
}
