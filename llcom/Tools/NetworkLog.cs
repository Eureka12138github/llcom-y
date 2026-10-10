using System.Windows.Media;

namespace llcom.Tools;

/// <summary>
/// 网络工具页数据日志的统一出口。
/// 消除各页重复的 <c>ShowData</c> 包装（固定收发颜色、统一 data 空值处理）。
/// </summary>
internal static class NetworkLog
{
    /// <summary>
    /// 展示一条网络收发数据。
    /// </summary>
    /// <param name="prefix">通道前缀，如 "🔗 socket client: "</param>
    /// <param name="title">本条标题</param>
    /// <param name="data">数据（null 视为空）</param>
    /// <param name="send">true 为发送（红），false 为接收（绿）</param>
    public static void Show(string prefix, string title, byte[] data = null, bool send = false)
    {
        Logger.ShowDataRaw(
            new DataShowRaw
            {
                title = $"{prefix}{title}",
                data = data ?? new byte[0],
                color = send ? Brushes.DarkRed : Brushes.DarkGreen,
            }
        );
    }
}