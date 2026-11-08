using System.Collections.Generic;
using Newtonsoft.Json;

namespace llcom.Model;

/// <summary>
/// 单个脚本的 sidecar 元数据（保存在脚本目录的 scripts.index.json）。
/// 文件本身仍平铺在脚本目录，元数据只记录分类、收藏、排序和描述。
/// </summary>
public sealed class ScriptMetadata
{
    [JsonProperty("file")]
    public string File { get; set; } = "";

    [JsonProperty("category")]
    public string Category { get; set; } = "";

    [JsonProperty("favorite")]
    public bool Favorite { get; set; }

    /// <summary>数值越小越靠前；0 为默认，-1 表示置顶。</summary>
    [JsonProperty("order")]
    public int Order { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; } = "";
}

/// <summary>scripts.index.json 的根对象。</summary>
public sealed class ScriptIndex
{
    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    [JsonProperty("scripts")]
    public List<ScriptMetadata> Scripts { get; set; } = new List<ScriptMetadata>();
}
