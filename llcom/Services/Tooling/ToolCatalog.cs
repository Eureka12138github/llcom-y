using System;
using System.Collections.Generic;

namespace llcom.Services.Tooling;

/// <summary>
/// 小工具注册表：工具下拉、页面 Frame、可见性切换都由这一份列表驱动。
/// 顺序即默认显示顺序；收藏/置顶由 <see cref="ToolIndexStore"/> 另行排序。
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> Tools { get; } = new List<ToolDefinition>
    {
        new("tcp-test", "TcpTabTitle", "TCP 测试", "Pages/tcpTest.xaml"),
        new("tcp-local", "TcpLocalTabTitle", "本地 TCP 服务器", "Pages/TcpLocalPage.xaml"),
        new("udp-local", "UdpLocalTabTitle", "本地 UDP 服务器", "Pages/UdpLocalPage.xaml"),
        new("tcp-client", "TcpClientTitle", "TCP 客户端", "Pages/SocketClientPage.xaml"),
        new("encoding", "EncodingToolsTab", "编码转换", "Pages/ConvertPage.xaml"),
        new("mqtt", null, "MQTT", "Pages/MqttTestPage.xaml"),
        new("encoding-fix", "EncodingFixHeader", "乱码修复", "Pages/EncodingFixPage.xaml"),
        new("serial-monitor", "SerialMonitorHeader", "串口监听", "Pages/SerialMonitorPage.xaml"),
        new("plot", "PlotHeader", "绘制曲线", "Pages/PlotPage.xaml"),
        new("winusb", null, "WinUSB", "Pages/WinUSBPage.xaml"),
        new("dlt645", "Dlt645Title", "DL/T 645-2007", "Pages/Dlt645Page.xaml"),
        new("calculator", "CalculatorTitle", "计算器", "Pages/CalculatorPage.xaml"),
    };

    /// <summary>按 Id 查找；找不到返回 null。</summary>
    public static ToolDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        foreach (var tool in Tools)
        {
            if (string.Equals(tool.Id, id, StringComparison.OrdinalIgnoreCase))
                return tool;
        }
        return null;
    }
}
