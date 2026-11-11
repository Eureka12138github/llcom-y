namespace llcom.Services.Tooling;

/// <summary>
/// 一个小工具的定义：稳定 Id、标题资源键（可选）、兜底标题、页面 URI。
/// 新增工具只需要在 <see cref="ToolCatalog"/> 里加一条，不再分别维护下拉项、Frame 和数组。
/// </summary>
public sealed class ToolDefinition
{
    public ToolDefinition(string id, string? titleKey, string fallbackTitle, string pageUri)
    {
        Id = id;
        TitleKey = titleKey;
        FallbackTitle = fallbackTitle;
        PageUri = pageUri;
    }

    /// <summary>稳定 Id，用于置顶/收藏/记住上次选中；不要随意改名。</summary>
    public string Id { get; }

    /// <summary>i18n 资源键；为空表示使用 <see cref="FallbackTitle"/>（如 MQTT、WinUSB）。</summary>
    public string? TitleKey { get; }

    public string FallbackTitle { get; }

    /// <summary>相对应用根目录的页面 URI，例如 Pages/tcpTest.xaml。</summary>
    public string PageUri { get; }
}
