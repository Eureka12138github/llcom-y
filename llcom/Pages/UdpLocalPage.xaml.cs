using System;
using System.Windows;
using System.Windows.Controls;
using llcom.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace llcom.Pages;

/// <summary>
/// UdpLocalPage.xaml 的交互逻辑。
/// “VM 化样板”：状态与监听逻辑在 <see cref="UdpLocalViewModel"/>，本页只做 UI 桥接，
/// 因此不再手写 INotifyPropertyChanged（对照改造前的 TcpLocalPage 等）。
/// </summary>
public partial class UdpLocalPage : Page
{
    private UdpLocalViewModel _vm;
    private bool loaded = false;

    public UdpLocalPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded)
            return;
        loaded = true;

        // 从组合根解析 VM（Page 由 XAML 加载器创建，无法构造注入）
        _vm = Tools.AppHost.Services.GetRequiredService<UdpLocalViewModel>();
        MainGrid.DataContext = _vm;
        IpPortTextBox.DataContext = _vm.Setting;

        RefreshIp();
    }

    /// <summary>刷新本机 IP 列表（UI 相关，留在 View）。</summary>
    private void RefreshIp() =>
        Tools.NetworkHelper.FillLocalIpComboBox(IpListComboBox, "udp server");

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
                _vm.Listen(IpListComboBox.Text, port);
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
            _vm.Stop();
        }
        catch (System.Exception ex)
        {
            Tools.Logger.Swallowed("udp server", ex);
        }
    }
}