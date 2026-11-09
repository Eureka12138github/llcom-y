using System;
using System.Collections.Generic;

namespace llcom.Services.Dlt645;

/// <summary>645 操作定义（向导模式使用）。</summary>
public sealed class Dlt645Operation
{
    public Dlt645Operation(
        string nameKey,
        string fallbackName,
        byte control,
        bool requiresDataIdentifier = false,
        bool isWrite = false,
        bool needsConfirmation = false
    )
    {
        NameKey = nameKey;
        Name = fallbackName;
        Control = control;
        RequiresDataIdentifier = requiresDataIdentifier;
        IsWrite = isWrite;
        NeedsConfirmation = needsConfirmation;
    }

    /// <summary>本地化资源键。</summary>
    public string NameKey { get; }

    /// <summary>资源缺失时的回退名称。</summary>
    public string Name { get; }

    public byte Control { get; }

    public bool RequiresDataIdentifier { get; }

    public bool IsWrite { get; }

    public bool NeedsConfirmation { get; }
}

/// <summary>常用数据标识（DI）。</summary>
public sealed class Dlt645DataIdentifier
{
    public Dlt645DataIdentifier(
        string nameKey,
        string fallbackName,
        byte[] bytes,
        string category = "",
        string unit = "",
        int? decimals = null,
        string note = "",
        bool isBuiltIn = true
    )
    {
        if (bytes == null || bytes.Length != 4)
            throw new ArgumentException("DI 必须是 4 字节", nameof(bytes));
        NameKey = nameKey;
        Name = fallbackName;
        Bytes = (byte[])bytes.Clone();
        Category = category;
        Unit = unit;
        Decimals = decimals;
        Note = note;
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>本地化资源键；用户自定义项为空。</summary>
    public string NameKey { get; }

    /// <summary>资源缺失时的回退名称；用户自定义项直接使用该名称。</summary>
    public string Name { get; }

    /// <summary>按帧内传输顺序保存的 4 字节 DI。</summary>
    public byte[] Bytes { get; }

    public string Category { get; }

    public string Unit { get; }

    public int? Decimals { get; }

    public string Note { get; }

    public bool IsBuiltIn { get; }

    public string Hex => Dlt645Codec.ToHex(Bytes, "");
}

/// <summary>
/// 645 操作码与常用 DI 目录。
/// 首版内置代码；单位/小数位等解码信息待真实电表校准后再补。
/// </summary>
public static class Dlt645Catalog
{
    public static IReadOnlyList<Dlt645Operation> Operations { get; } =
        new[]
        {
            new Dlt645Operation(
                "Dlt645OpReadData",
                "读数据",
                Dlt645Codec.ControlReadData,
                requiresDataIdentifier: true
            ),
            new Dlt645Operation(
                "Dlt645OpReadAddress",
                "读通信地址",
                Dlt645Codec.ControlReadAddress
            ),
            new Dlt645Operation(
                "Dlt645OpWriteData",
                "写数据",
                Dlt645Codec.ControlWriteData,
                requiresDataIdentifier: true,
                isWrite: true,
                needsConfirmation: true
            ),
        };

    /// <summary>
    /// 公开资料中的常用 DI。
    /// 字节顺序按帧内传输顺序（低字节在前），实际数据长度/小数位需按表手册校准。
    /// </summary>
    public static IReadOnlyList<Dlt645DataIdentifier> DataIdentifiers { get; } =
        new[]
        {
            new Dlt645DataIdentifier(
                "Dlt645DiVoltageA",
                "A 相电压",
                new byte[] { 0x02, 0x01, 0x01, 0x00 },
                "电压"
            ),
            new Dlt645DataIdentifier(
                "Dlt645DiCurrentA",
                "A 相电流",
                new byte[] { 0x02, 0x02, 0x01, 0x00 },
                "电流"
            ),
            new Dlt645DataIdentifier(
                "Dlt645DiActivePower",
                "瞬时有功功率",
                new byte[] { 0x02, 0x03, 0x00, 0x00 },
                "功率"
            ),
            new Dlt645DataIdentifier(
                "Dlt645DiPowerFactor",
                "功率因数",
                new byte[] { 0x02, 0x06, 0x00, 0x00 },
                "功率"
            ),
            new Dlt645DataIdentifier(
                "Dlt645DiEnergyForward",
                "正向有功总电能",
                new byte[] { 0x00, 0x01, 0x00, 0x00 },
                "电能"
            ),
            new Dlt645DataIdentifier(
                "Dlt645DiEnergyCombined",
                "组合有功总电能",
                new byte[] { 0x00, 0x00, 0x00, 0x00 },
                "电能"
            ),
        };
}
