using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using llcom.Tools;
using Newtonsoft.Json;

namespace llcom.Services.Dlt645;

/// <summary>用户 DI 的一条记录（dlt645-di.json）。</summary>
public sealed class Dlt645IdentifierEntry
{
    [JsonProperty("code")]
    public string Code { get; set; } = "";

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("category")]
    public string Category { get; set; } = "";

    [JsonProperty("unit")]
    public string Unit { get; set; } = "";

    [JsonProperty("decimals")]
    public int? Decimals { get; set; }

    [JsonProperty("note")]
    public string Note { get; set; } = "";
}

/// <summary>dlt645-di.json 根对象。</summary>
public sealed class Dlt645IdentifierFile
{
    /// <summary>v1 为旧版（只有用户项）；v2 为完整 DI 配置。</summary>
    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    [JsonProperty("items")]
    public List<Dlt645IdentifierEntry> Items { get; set; } =
        new List<Dlt645IdentifierEntry>();
}

/// <summary>
/// DI 配置读写。
/// 首次运行/旧版文件会写入包含默认项的 v2 文件；之后文件即唯一数据源，
/// 用户可编辑/删除默认项，也可恢复默认。
/// </summary>
public static class Dlt645IdentifierStore
{
    public const string FolderName = "user_dlt645";
    public const string FileName = "dlt645-di.json";
    private const int CurrentVersion = 2;

    public static string FilePath =>
        Path.Combine(Tools.Global.ProfilePath, FolderName, FileName);

    /// <summary>内置默认 DI，用于首次写入和“恢复默认”。</summary>
    public static List<Dlt645IdentifierEntry> DefaultEntries()
    {
        return Dlt645Catalog
            .DataIdentifiers.Select(identifier => new Dlt645IdentifierEntry
            {
                Code = identifier.Hex,
                Name = identifier.Name,
                Category = identifier.Category,
                Unit = identifier.Unit,
                Decimals = identifier.Decimals,
                Note = identifier.Note,
            })
            .ToList();
    }

    /// <summary>读取当前全部 DI；不存在时写入默认项，旧版文件自动迁移。</summary>
    public static List<Dlt645IdentifierEntry> LoadEntries()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var defaults = DefaultEntries();
                SaveEntries(defaults);
                return defaults;
            }

            var file = JsonConvert.DeserializeObject<Dlt645IdentifierFile>(
                File.ReadAllText(FilePath)
            );
            if (file?.Items == null)
                return DefaultEntries();

            var entries = NormalizeEntries(file.Items);
            if (file.Version < CurrentVersion)
            {
                // v1 文件只有用户项；合并默认项后写成 v2，之后以文件为准。
                entries = MergeWithDefaults(entries);
                SaveEntries(entries);
            }
            return entries;
        }
        catch (Exception ex)
        {
            Logger.Swallowed("load dlt645 identifiers", ex);
            return DefaultEntries();
        }
    }

    /// <summary>当前全部 DI 转成界面模型。</summary>
    public static List<Dlt645DataIdentifier> LoadAll()
    {
        var result = new List<Dlt645DataIdentifier>();
        foreach (var entry in LoadEntries())
        {
            if (!TryCreateIdentifier(entry, out var identifier))
                continue;
            result.Add(identifier!);
        }
        return result;
    }

    public static void SaveEntries(IEnumerable<Dlt645IdentifierEntry> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var file = new Dlt645IdentifierFile
            {
                Version = CurrentVersion,
                Items = NormalizeEntries(entries),
            };
            File.WriteAllText(
                FilePath,
                JsonConvert.SerializeObject(file, Formatting.Indented) + Environment.NewLine
            );
        }
        catch (Exception ex)
        {
            Logger.Swallowed("save dlt645 identifiers", ex);
        }
    }

    /// <summary>恢复默认 DI（覆盖当前配置）。</summary>
    public static void ResetToDefaults()
    {
        SaveEntries(DefaultEntries());
    }

    /// <summary>删除指定 DI。</summary>
    public static bool Delete(string code)
    {
        var entries = LoadEntries();
        var removed = entries.RemoveAll(item =>
            string.Equals(item.Code, NormalizeCode(code), StringComparison.OrdinalIgnoreCase)
        );
        if (removed == 0)
            return false;
        SaveEntries(entries);
        return true;
    }

    /// <summary>导入并合并 DI；返回成功导入/更新的条数。</summary>
    public static int Import(string path)
    {
        var imported = LoadEntriesFromFile(path);
        if (imported.Count == 0)
            return 0;

        var entries = LoadEntries();
        foreach (var item in imported)
        {
            var index = entries.FindIndex(existing =>
                string.Equals(existing.Code, item.Code, StringComparison.OrdinalIgnoreCase)
            );
            if (index >= 0)
                entries[index] = item;
            else
                entries.Add(item);
        }

        SaveEntries(entries);
        return imported.Count;
    }

    /// <summary>从文件读取并校验 DI，不写入当前配置。</summary>
    public static List<Dlt645IdentifierEntry> LoadEntriesFromFile(string path)
    {
        var file = JsonConvert.DeserializeObject<Dlt645IdentifierFile>(
            File.ReadAllText(path)
        );
        if (file?.Items == null)
            throw new InvalidOperationException("DI 配置文件格式不正确");
        return NormalizeEntries(file.Items);
    }

    /// <summary>导出当前全部 DI（包含默认项）。</summary>
    public static void Export(string path)
    {
        var file = new Dlt645IdentifierFile
        {
            Version = CurrentVersion,
            Items = LoadEntries(),
        };
        File.WriteAllText(
            path,
            JsonConvert.SerializeObject(file, Formatting.Indented) + Environment.NewLine
        );
    }

    /// <summary>导出指定 DI 列表（供管理窗口使用）。</summary>
    public static void Export(string path, IEnumerable<Dlt645IdentifierEntry> entries)
    {
        var file = new Dlt645IdentifierFile
        {
            Version = CurrentVersion,
            Items = NormalizeEntries(entries),
        };
        File.WriteAllText(
            path,
            JsonConvert.SerializeObject(file, Formatting.Indented) + Environment.NewLine
        );
    }

    /// <summary>规范化并校验一组 DI；无效项会被丢弃。</summary>
    public static List<Dlt645IdentifierEntry> NormalizeEntries(
        IEnumerable<Dlt645IdentifierEntry> entries
    )
    {
        var result = new List<Dlt645IdentifierEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!TryNormalizeEntry(entry, out var normalized))
                continue;
            if (!seen.Add(normalized.Code))
                continue;
            result.Add(normalized);
        }
        return result;
    }

    public static bool TryNormalizeEntry(
        Dlt645IdentifierEntry? entry,
        out Dlt645IdentifierEntry normalized
    )
    {
        normalized = new Dlt645IdentifierEntry();
        if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            return false;
        if (
            !Dlt645Codec.TryParseHex(entry.Code, out var bytes, out _)
            || bytes.Length != 4
        )
            return false;

        normalized = new Dlt645IdentifierEntry
        {
            Code = Dlt645Codec.ToHex(bytes, ""),
            Name = entry.Name.Trim(),
            Category = (entry.Category ?? "").Trim(),
            Unit = (entry.Unit ?? "").Trim(),
            Decimals = entry.Decimals,
            Note = (entry.Note ?? "").Trim(),
        };
        return true;
    }

    private static string NormalizeCode(string code) =>
        Dlt645Codec.TryParseHex(code, out var bytes, out _) && bytes.Length == 4
            ? Dlt645Codec.ToHex(bytes, "")
            : code;

    private static List<Dlt645IdentifierEntry> MergeWithDefaults(
        List<Dlt645IdentifierEntry> entries
    )
    {
        var merged = DefaultEntries();
        foreach (var entry in entries)
        {
            var index = merged.FindIndex(existing =>
                string.Equals(existing.Code, entry.Code, StringComparison.OrdinalIgnoreCase)
            );
            if (index >= 0)
                merged[index] = entry;
            else
                merged.Add(entry);
        }
        return merged;
    }

    private static bool TryCreateIdentifier(
        Dlt645IdentifierEntry entry,
        out Dlt645DataIdentifier? identifier
    )
    {
        identifier = null;
        if (!TryNormalizeEntry(entry, out var normalized))
            return false;
        if (!Dlt645Codec.TryParseHex(normalized.Code, out var bytes, out _))
            return false;

        identifier = new Dlt645DataIdentifier(
            "",
            normalized.Name,
            bytes,
            normalized.Category,
            normalized.Unit,
            normalized.Decimals,
            normalized.Note,
            isBuiltIn: false
        );
        return true;
    }
}
