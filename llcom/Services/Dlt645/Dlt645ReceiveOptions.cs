namespace llcom.Services.Dlt645;

/// <summary>645 工具页对主接收日志的显示选项。</summary>
public static class Dlt645ReceiveOptions
{
    /// <summary>当前是否处于 645 工具页。</summary>
    public static bool IsToolActive { get; set; }

    /// <summary>是否把接收帧的数据域减 33H 后再写入主日志。</summary>
    public static bool DecodeData { get; set; }

    /// <summary>只有 645 页激活且勾选时才转换。</summary>
    public static bool Enabled => IsToolActive && DecodeData;
}
