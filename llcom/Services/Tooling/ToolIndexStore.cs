using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using llcom.Tools;
using Newtonsoft.Json;

namespace llcom.Services.Tooling;

/// <summary>工具列表的持久化状态：置顶项 + 上次选中的工具 Id。</summary>
public sealed class ToolIndex
{
    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    [JsonProperty("favorites")]
    public List<string> Favorites { get; set; } = new();

    [JsonProperty("lastSelected")]
    public string LastSelected { get; set; } = "";
}

/// <summary>
/// 读写配置目录下的 tools.index.json。
/// 文件损坏/权限不足时回退为空状态，不影响工具正常使用。
/// </summary>
public sealed class ToolIndexStore
{
    public const string FileName = "tools.index.json";

    private readonly string _filePath;

    public ToolIndexStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>默认位置：配置目录根下的 tools.index.json。</summary>
    public static ToolIndexStore CreateDefault() =>
        new(Path.Combine(Global.ProfilePath, FileName));

    public ToolIndex Load()
    {
        var result = new ToolIndex();
        try
        {
            if (!File.Exists(_filePath))
                return result;

            var index = JsonConvert.DeserializeObject<ToolIndex>(File.ReadAllText(_filePath));
            if (index == null)
                return result;

            result.Favorites = NormalizeFavorites(index.Favorites);
            result.LastSelected = NormalizeToolId(index.LastSelected);
        }
        catch (Exception ex)
        {
            Logger.Swallowed("load tool index", ex);
        }
        return result;
    }

    public void Save(ToolIndex index)
    {
        try
        {
            var normalized = new ToolIndex
            {
                Version = 1,
                Favorites = NormalizeFavorites(index.Favorites),
                LastSelected = NormalizeToolId(index.LastSelected),
            };

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                _filePath,
                JsonConvert.SerializeObject(normalized, Formatting.Indented) + Environment.NewLine
            );
        }
        catch (Exception ex)
        {
            Logger.Swallowed("save tool index", ex);
        }
    }

    /// <summary>只保留注册表里存在的 Id，去重并统一为注册表中的大小写。</summary>
    private static List<string> NormalizeFavorites(IEnumerable<string>? favorites)
    {
        var result = new List<string>();
        if (favorites == null)
            return result;

        foreach (var id in favorites)
        {
            var tool = ToolCatalog.Find(id);
            if (tool == null)
                continue;
            if (
                !result.Any(existing =>
                    string.Equals(existing, tool.Id, StringComparison.OrdinalIgnoreCase)
                )
            )
                result.Add(tool.Id);
        }
        return result;
    }

    private static string NormalizeToolId(string? id) => ToolCatalog.Find(id)?.Id ?? "";
}
