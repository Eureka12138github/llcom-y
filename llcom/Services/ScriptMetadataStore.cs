using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using llcom.Model;
using llcom.Tools;
using Newtonsoft.Json;

namespace llcom.Services;

/// <summary>
/// 读写脚本目录下的 sidecar 元数据（scripts.index.json）。
/// 文件损坏或权限不足时退化为空元数据，不影响脚本本身的使用。
/// </summary>
public sealed class ScriptMetadataStore
{
    public const string IndexFileName = "scripts.index.json";

    private readonly string _folderPath;

    public ScriptMetadataStore(string folderPath)
    {
        _folderPath = folderPath;
    }

    /// <summary>按脚本名（不含 .lua）读取元数据；重复项以后出现的为准。</summary>
    public Dictionary<string, ScriptMetadata> Load()
    {
        var result = new Dictionary<string, ScriptMetadata>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(_folderPath, IndexFileName);
        try
        {
            if (!File.Exists(path))
                return result;

            var index = JsonConvert.DeserializeObject<ScriptIndex>(File.ReadAllText(path));
            if (index?.Scripts == null)
                return result;

            foreach (var item in index.Scripts)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.File))
                    continue;
                var name = Path.GetFileNameWithoutExtension(item.File.Trim());
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                item.File = name;
                item.Category ??= "";
                item.Description ??= "";
                result[name] = item;
            }
        }
        catch (Exception ex)
        {
            Logger.Swallowed("load script metadata", ex);
        }
        return result;
    }

    /// <summary>写入元数据；目录不存在时自动创建。</summary>
    public void Save(IEnumerable<ScriptMetadata> scripts)
    {
        var path = Path.Combine(_folderPath, IndexFileName);
        try
        {
            Directory.CreateDirectory(_folderPath);
            var index = new ScriptIndex
            {
                Version = 1,
                Scripts = scripts
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.File))
                    .OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };
            File.WriteAllText(
                path,
                JsonConvert.SerializeObject(index, Formatting.Indented) + Environment.NewLine
            );
        }
        catch (Exception ex)
        {
            Logger.Swallowed("save script metadata", ex);
        }
    }
}
