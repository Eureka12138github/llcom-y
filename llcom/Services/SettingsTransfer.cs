using System;
using System.IO;
using System.Text;
using llcom.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace llcom.Services;

/// <summary>
/// settings.json 的导出 / 导入。
/// 导入是整文件替换（不做逐项合并）：先备份当前配置，再写入导入文件；
/// 内存中的旧配置要到重启后才被替换，所以导入后设置 <see cref="Settings.ImportPendingRestart"/>，
/// 防止退出时把旧配置写回磁盘。
/// </summary>
internal static class SettingsTransfer
{
    /// <summary>导出文件的默认文件名。</summary>
    public const string DefaultFileName = "llcom-settings.json";

    /// <summary>导入前对当前配置做的备份文件名（位于配置目录）。</summary>
    public const string BackupFileName = "settings.json.import-bak";

    /// <summary>配置文件名。</summary>
    private const string SettingsFileName = "settings.json";

    /// <summary>导出当前内存中的配置（缩进格式，便于查看和手工编辑）。</summary>
    public static void Export(string path)
    {
        var json = JsonConvert.SerializeObject(
            Tools.Global.setting,
            Formatting.Indented
        );
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>导入配置：校验通过后备份当前配置并整体替换，重启后生效。</summary>
    /// <exception cref="InvalidDataException">文件内容不是有效的 LLCOM 配置。</exception>
    public static void Import(string path)
    {
        var text = File.ReadAllText(path);
        if (!TryValidate(text, out var error))
            throw new InvalidDataException(error);

        var directory = Tools.Global.ProfilePath;
        Directory.CreateDirectory(directory);

        var target = directory + SettingsFileName;
        if (File.Exists(target))
            File.Copy(target, directory + BackupFileName, true);

        File.WriteAllText(target, text, new UTF8Encoding(false));
        Settings.ImportPendingRestart = true;
    }

    /// <summary>
    /// 校验配置文本：必须是 JSON 对象，且带有本项目配置的基本字段，
    /// 避免把其它 JSON 当成配置导入。
    /// </summary>
    public static bool TryValidate(string? text, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "文件为空";
            return false;
        }

        JObject obj;
        try
        {
            obj = JObject.Parse(text!);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        if (
            obj["baudRate"]?.Type != JTokenType.Integer
            || obj["dataBits"]?.Type != JTokenType.Integer
            || obj["stopBit"]?.Type != JTokenType.Integer
        )
        {
            error = "不是 LLCOM 配置文件（缺少 baudRate / dataBits / stopBit）";
            return false;
        }

        return true;
    }
}
