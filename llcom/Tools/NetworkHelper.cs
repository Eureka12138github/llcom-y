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

    /// <summary>
    /// 把 Socket 的远端/本地地址格式化为 <c>"远端 → 本地"</c>（IPv6 自动加方括号）。
    /// 原 <c>TcpLocalPage.GetClientName</c> 内联实现。
    /// </summary>
    public static string GetSocketName(Socket s)
    {
        var remote = (IPEndPoint)s.RemoteEndPoint;
        var local = (IPEndPoint)s.LocalEndPoint;
        return FormatHostPort(remote.Address.ToString(), remote.Port)
            + " → "
            + FormatHostPort(local.Address.ToString(), local.Port);
    }

    /// <summary>格式化 <c>"host:port"</c>，IPv6 地址自动加方括号。原散落在 Tcp/Udp 页内联实现。</summary>
    public static string FormatHostPort(string host, int port) =>
        $"{(host.Contains(":") ? "[" : "")}{host}{(host.Contains(":") ? "]" : "")}:{port}";

    /// <summary>按 HEX 模式或当前编码，把文本框内容转成待发送字节。原各页内联的三元表达式。</summary>
    public static byte[] BuildSendBytes(string text, bool hex) =>
        hex ? Global.Hex2Byte(text) : Global.GetEncoding().GetBytes(text);
}