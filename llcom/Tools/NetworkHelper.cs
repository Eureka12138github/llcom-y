using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows.Controls;

namespace llcom.Tools;

/// <summary>
/// 网络工具页公共助手。消除各页重复的实现（原 TcpLocalPage / UdpLocalPage 各有一份）。
/// </summary>
internal static class NetworkHelper
{
    /// <summary>
    /// 填充"本机 IP 下拉框"：先加固定项 <c>0.0.0.0</c> / <c>::</c>，
    /// 再加入本机所有 IPv4/IPv6 地址（去重），最后选中第一项。
    /// </summary>
    /// <param name="comboBox">目标下拉框</param>
    /// <param name="context">日志上下文（用于吞异常时记录来源）</param>
    public static void FillLocalIpComboBox(ComboBox comboBox, string context)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add("0.0.0.0");
        comboBox.Items.Add("::");
        var temp = new List<string>();
        try
        {
            string name = Dns.GetHostName();
            IPAddress[] ipadrlist = Dns.GetHostAddresses(name);
            foreach (IPAddress ipa in ipadrlist)
            {
                if (
                    ipa.AddressFamily == AddressFamily.InterNetwork
                    || ipa.AddressFamily == AddressFamily.InterNetworkV6
                )
                    temp.Add(ipa.ToString());
            }
        }
        catch (System.Exception ex)
        {
            Logger.Swallowed(context, ex);
        }
        //去重
        temp.Distinct().ToList().ForEach(ip => comboBox.Items.Add(ip));
        comboBox.SelectedIndex = 0;
    }
}