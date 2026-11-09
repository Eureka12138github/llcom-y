using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using llcom.Tools;
using Newtonsoft.Json;

namespace llcom.Services.Dlt645;

/// <summary>用户自定义 DI 的一条记录（dlt645-di.json）。</summary>
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
    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    [JsonProperty("items")]
    public List<Dlt645IdentifierEntry> Items { get; set; } =
        new List<Dlt645IdentifierEntry>();
}

/// <summary>
/// DI 配置读写：内置目录 + 用户目录合并。
/// 用户文件只保存自定义/覆盖项，升级时不会覆盖内置项。
/// </summary>
public static class Dlt645IdentifierStore
{
    public const string FolderName = "user_dlt645";
    public const string FileName = "dlt645-di.json";

    public static string FilePath =>
        Path.Combine(Tools.Global.ProfilePath, FolderName, FileName);

    public static List<Dlt645IdentifierEntry> LoadUserEntries()
    {
        var result = new List<Dlt645IdentifierEntry>();
        try
        {
            if (!File.Exists(FilePath))
                return result;

            var file = JsonConvert.DeserializeObject<Dlt645IdentifierFile>(
                File.ReadAllText(FilePath)
            );
            if (file?.Items == null)
                return result;

            foreach (var item in file.Items)
            {
                if (TryNormalizeEntry(item, out var normalized))
                    result.Add(normalized);
            }
        }
        catch (Exception ex)
        {
            Logger.Swallowed("load dlt645 identifiers", ex);
        }
        return result;
    }

    public static void SaveUserEntries(IEnumerable<Dlt645IdentifierEntry> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var file = new Dlt645IdentifierFile
            {
                Version = 1,
                Items = entries
                    .Select(item => TryNormalizeEntry(item, out var normalized) ? normalized : null)
                    .Where(item => item != null)
                    .Select(item => item!)
                    .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
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

    /// <summary>内置目录 + 用户目录；同 code 时用户项覆盖内置项。</summary>
    public static List<Dlt645DataIdentifier> LoadMerged()
    {
        var merged = new Dictionary<string, Dlt645DataIdentifier>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var builtin in Dlt645Catalog.DataIdentifiers)
            merged[builtin.Hex] = builtin;

        foreach (var entry in LoadUserEntries())
        {
            if (!TryCreateIdentifier(entry, out var identifier))
                continue;
            merged[identifier!.Hex] = identifier;
        }

        return merged
            .Values.OrderBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>导入用户 DI；返回成功导入/更新的条数。</summary>
    public static int Import(string path)
    {
        var file = JsonConvert.DeserializeObject<Dlt645IdentifierFile>(
            File.ReadAllText(path)
        );
        if (file?.Items == null)
            throw new InvalidOperationException("DI 配置文件格式不正确");

        var existing = new Dictionary<string, Dlt645IdentifierEntry>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var item in LoadUserEntries())
            existing[item.Code] = item;
        var imported = 0;

        foreach (var item in file.Items)
        {
            if (!TryNormalizeEntry(item, out var normalized))
                continue;
            existing[normalized.Code] = normalized;
            imported++;
        }

        if (imported == 0)
            return 0;

        SaveUserEntries(existing.Values);
        return imported;
    }

    /// <summary>导出用户自定义 DI（不包含内置项）。</summary>
    public static void Export(string path)
    {
        var file = new Dlt645IdentifierFile
        {
            Version = 1,
            Items = LoadUserEntries(),
        };
        File.WriteAllText(
            path,
            JsonConvert.SerializeObject(file, Formatting.Indented) + Environment.NewLine
        );
    }

    private static bool TryNormalizeEntry(
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
