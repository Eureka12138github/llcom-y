using llcom.Model;

namespace llcom.ViewModels;

/// <summary>
/// 脚本列表项快照，用于搜索结果等展示场景。
/// 元数据变化后由 <see cref="LuaEditorViewModel"/> 重建列表，因此这里保持只读。
/// </summary>
public sealed class ScriptListItem
{
    public ScriptListItem(string name, ScriptMetadata metadata)
    {
        Name = name;
        Category = metadata.Category ?? "";
        Description = metadata.Description ?? "";
        Favorite = metadata.Favorite;
    }

    public string Name { get; }

    public string Category { get; }

    public string Description { get; }

    public bool Favorite { get; }
}
