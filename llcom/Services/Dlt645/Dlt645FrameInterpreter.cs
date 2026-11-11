using System;
using System.Collections.Generic;

namespace llcom.Services.Dlt645;

/// <summary>解析结果中的一条字段：名称用资源键 + 中文兜底，值已是可显示文本。</summary>
public sealed class Dlt645InterpretedField
{
    public Dlt645InterpretedField(string nameKey, string nameFallback, string value)
    {
        NameKey = nameKey;
        NameFallback = nameFallback;
        Value = value;
    }

    public string NameKey { get; }
    public string NameFallback { get; }
    public string Value { get; }
}

/// <summary>解析结果中的一条本地化文本（错误位、告警等）。</summary>
public sealed class Dlt645InterpretedText
{
    public Dlt645InterpretedText(string key, string fallback)
    {
        Key = key;
        Fallback = fallback;
    }

    public string Key { get; }
    public string Fallback { get; }
}

/// <summary>一帧的解析结果（帧级字段之外的功能级信息）。</summary>
public sealed class Dlt645FrameInterpretation
{
    /// <summary>功能码是否在支持列表中。</summary>
    public bool IsKnownFunction { get; internal set; }

    public string FunctionNameKey { get; internal set; } = "";
    public string FunctionNameFallback { get; internal set; } = "";

    /// <summary>异常应答的错误信息字（ERR）；非异常帧为 null。</summary>
    public byte? ErrorWord { get; internal set; }

    public List<Dlt645InterpretedField> Fields { get; } = new();
    public List<Dlt645InterpretedText> ErrorBits { get; } = new();
    public List<Dlt645InterpretedText> Warnings { get; } = new();
}

/// <summary>
/// DL/T 645-2007 常用帧语义解析（80% 范围）。
/// 负责：功能码名称、异常 ERR 位、常用功能的数据域字段拆分。
/// 不处理：读后续数据/后续帧、0x1C/0x1D/0x03、安全认证、状态字/特征字、数据块展开。
/// </summary>
public static class Dlt645FrameInterpreter
{
    private const byte FunctionBroadcastTime = 0x08;
    private const byte FunctionReadData = 0x11;
    private const byte FunctionReadAddress = 0x13;
    private const byte FunctionWriteData = 0x14;
    private const byte FunctionWriteAddress = 0x15;
    private const byte FunctionFreeze = 0x16;
    private const byte FunctionChangeBaud = 0x17;
    private const byte FunctionChangePassword = 0x18;
    private const byte FunctionMaxDemandReset = 0x19;
    private const byte FunctionMeterReset = 0x1A;
    private const byte FunctionEventReset = 0x1B;

    private static readonly Dictionary<byte, (string Key, string Fallback)> FunctionNames =
        new()
        {
            [FunctionBroadcastTime] = ("Dlt645OpBroadcastTime", "广播校时"),
            [FunctionReadData] = ("Dlt645OpReadData", "读数据"),
            [FunctionReadAddress] = ("Dlt645OpReadAddress", "读通信地址"),
            [FunctionWriteData] = ("Dlt645OpWriteData", "写数据"),
            [FunctionWriteAddress] = ("Dlt645OpWriteAddress", "写通信地址"),
            [FunctionFreeze] = ("Dlt645OpFreeze", "冻结命令"),
            [FunctionChangeBaud] = ("Dlt645OpChangeBaud", "更改通信速率"),
            [FunctionChangePassword] = ("Dlt645OpChangePassword", "修改密码"),
            [FunctionMaxDemandReset] = ("Dlt645OpMaxDemandReset", "最大需量清零"),
            [FunctionMeterReset] = ("Dlt645OpMeterReset", "电表清零"),
            [FunctionEventReset] = ("Dlt645OpEventReset", "事件清零"),
        };

    private static readonly (byte Mask, string Key, string Fallback)[] ErrorBitDefinitions =
    {
        (0x01, "Dlt645ErrOther", "其他错误"),
        (0x02, "Dlt645ErrNoData", "无请求数据"),
        (0x04, "Dlt645ErrPassword", "密码错 / 未授权"),
        (0x08, "Dlt645ErrBaud", "通信速率不能更改"),
        (0x10, "Dlt645ErrYearZones", "年时区数超"),
        (0x20, "Dlt645ErrDayPeriods", "日时段数超"),
        (0x40, "Dlt645ErrRateCount", "费率数超"),
    };

    /// <summary>按功能码取名称资源键；未收录的功能码返回 false。</summary>
    public static bool TryGetFunctionName(
        byte functionCode,
        out string nameKey,
        out string fallbackName
    )
    {
        if (FunctionNames.TryGetValue(functionCode, out var name))
        {
            nameKey = name.Key;
            fallbackName = name.Fallback;
            return true;
        }

        nameKey = "";
        fallbackName = "";
        return false;
    }

    /// <summary>解析一帧的功能级信息。</summary>
    public static Dlt645FrameInterpretation Interpret(Dlt645Frame frame)
    {
        var result = new Dlt645FrameInterpretation();

        if (TryGetFunctionName(frame.FunctionCode, out var key, out var fallback))
        {
            result.IsKnownFunction = true;
            result.FunctionNameKey = key;
            result.FunctionNameFallback = fallback;
        }

        // 数据域长度上限提示（不阻断解析）
        if (frame.FunctionCode == FunctionReadData && frame.Data.Length > 200)
            AddWarning(result, "Dlt645WarnReadLength", "读数据长度超过 200 字节");
        if (frame.FunctionCode == FunctionWriteData && frame.Data.Length > 50)
            AddWarning(result, "Dlt645WarnWriteLength", "写数据长度超过 50 字节");

        if (frame.IsException)
        {
            InterpretException(frame, result);
            return result;
        }

        switch (frame.FunctionCode)
        {
            case FunctionReadData:
                InterpretReadData(frame.Data, result);
                break;

            case FunctionWriteData:
                InterpretWriteData(frame.Data, result);
                break;

            case FunctionWriteAddress:
                InterpretWriteAddress(frame, result);
                break;

            case FunctionBroadcastTime:
                InterpretBroadcastTime(frame.Data, result);
                break;

            case FunctionFreeze:
                InterpretFreezeTime(frame.Data, result);
                break;

            case FunctionChangeBaud:
                InterpretChangeBaud(frame.Data, result);
                break;

            case FunctionChangePassword:
                InterpretChangePassword(frame, result);
                break;

            case FunctionMaxDemandReset:
            case FunctionMeterReset:
                InterpretResetCommand(frame.Data, result);
                break;

            case FunctionEventReset:
                InterpretEventReset(frame.Data, result);
                break;
        }

        return result;
    }

    private static void InterpretException(
        Dlt645Frame frame,
        Dlt645FrameInterpretation result
    )
    {
        if (frame.Data.Length != 1)
        {
            AddWarning(
                result,
                "Dlt645WarnExceptionLength",
                "异常应答的数据域应为 1 字节 ERR"
            );
            if (frame.Data.Length == 0)
                return;
        }

        var errorWord = frame.Data[0];
        result.ErrorWord = errorWord;
        foreach (var (mask, key, fallback) in ErrorBitDefinitions)
        {
            if ((errorWord & mask) != 0)
                result.ErrorBits.Add(new Dlt645InterpretedText(key, fallback));
        }
    }

    private static void InterpretReadData(byte[] data, Dlt645FrameInterpretation result)
    {
        if (data.Length < 4)
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddField(
            result,
            "Dlt645DataIdentifier",
            "数据标识",
            FormatDi(0, data)
        );
        if (data.Length > 4)
            AddField(result, "Dlt645Data", "数据", HexSlice(data, 4, data.Length - 4));
    }

    private static void InterpretWriteData(byte[] data, Dlt645FrameInterpretation result)
    {
        if (data.Length < 12)
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddField(result, "Dlt645DataIdentifier", "数据标识", FormatDi(0, data));
        AddField(
            result,
            "Dlt645PasswordPermission",
            "密码权限",
            $"0x{data[4]:X2}"
        );
        AddField(result, "Dlt645Password", "密码", HexSlice(data, 5, 3));
        AddField(result, "Dlt645OperatorCode", "操作者代码", HexSlice(data, 8, 4));
        if (data.Length > 12)
            AddField(result, "Dlt645Data", "数据", HexSlice(data, 12, data.Length - 12));
    }

    private static void InterpretWriteAddress(
        Dlt645Frame frame,
        Dlt645FrameInterpretation result
    )
    {
        if (frame.IsResponse)
        {
            AddField(result, "Dlt645NewAddress", "新通信地址", frame.Address);
            return;
        }

        if (frame.Data.Length == 6)
            AddField(
                result,
                "Dlt645NewAddress",
                "新通信地址",
                Dlt645Codec.FormatAddress(frame.Data)
            );
        else
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
    }

    private static void InterpretBroadcastTime(
        byte[] data,
        Dlt645FrameInterpretation result
    )
    {
        if (data.Length != 6 || !TryFormatBroadcastTime(data, out var text))
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddField(result, "Dlt645Time", "校时时间", text);
    }

    private static void InterpretFreezeTime(byte[] data, Dlt645FrameInterpretation result)
    {
        if (data.Length != 4 || !TryFormatFreezeTime(data, out var text))
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddField(result, "Dlt645FreezeTime", "冻结时间", text);
    }

    private static void InterpretChangeBaud(
        byte[] data,
        Dlt645FrameInterpretation result
    )
    {
        if (data.Length < 1)
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddField(result, "Dlt645RateChar", "速率特征字", $"0x{data[0]:X2}");
    }

    private static void InterpretChangePassword(
        Dlt645Frame frame,
        Dlt645FrameInterpretation result
    )
    {
        var data = frame.Data;
        if (frame.IsResponse)
        {
            if (data.Length < 4)
            {
                AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
                return;
            }

            AddField(result, "Dlt645NewPassword", "新密码", HexSlice(data, 0, 4));
            return;
        }

        if (data.Length < 12)
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddField(result, "Dlt645DataIdentifier", "数据标识", FormatDi(0, data));
        AddField(result, "Dlt645OldPassword", "原密码", HexSlice(data, 4, 4));
        AddField(result, "Dlt645NewPassword", "新密码", HexSlice(data, 8, 4));
    }

    private static void InterpretResetCommand(
        byte[] data,
        Dlt645FrameInterpretation result
    )
    {
        if (data.Length < 8)
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddPasswordAndOperator(data, result);
    }

    private static void InterpretEventReset(
        byte[] data,
        Dlt645FrameInterpretation result
    )
    {
        if (data.Length < 12)
        {
            AddWarning(result, "Dlt645WarnFieldTooShort", "数据域长度不足，无法拆分字段");
            return;
        }

        AddPasswordAndOperator(data, result);
        AddField(result, "Dlt645EventDi", "事件数据标识", HexSlice(data, 8, 4));
    }

    private static void AddPasswordAndOperator(
        byte[] data,
        Dlt645FrameInterpretation result
    )
    {
        AddField(result, "Dlt645PasswordPermission", "密码权限", $"0x{data[0]:X2}");
        AddField(result, "Dlt645Password", "密码", HexSlice(data, 1, 3));
        AddField(result, "Dlt645OperatorCode", "操作者代码", HexSlice(data, 4, 4));
    }

    private static void AddField(
        Dlt645FrameInterpretation result,
        string key,
        string fallback,
        string value
    ) => result.Fields.Add(new Dlt645InterpretedField(key, fallback, value));

    private static void AddWarning(
        Dlt645FrameInterpretation result,
        string key,
        string fallback
    ) => result.Warnings.Add(new Dlt645InterpretedText(key, fallback));

    private static string FormatDi(int start, byte[] data) =>
        Dlt645Codec.ToHex(
            Dlt645Codec.ToLittleEndian(HexSliceBytes(data, start, 4)),
            ""
        );

    private static string HexSlice(byte[] data, int start, int length)
    {
        if (start < 0 || length <= 0 || start + length > data.Length)
            return "";
        var buffer = new byte[length];
        Array.Copy(data, start, buffer, 0, length);
        return Dlt645Codec.ToHex(buffer);
    }

    private static byte[] HexSliceBytes(byte[] data, int start, int length)
    {
        var buffer = new byte[length];
        Array.Copy(data, start, buffer, 0, length);
        return buffer;
    }

    /// <summary>广播校时：数据域 ss mm hh DD MM YY（低字节在前）。</summary>
    private static bool TryFormatBroadcastTime(byte[] data, out string text)
    {
        text = "";
        if (
            !TryBcd(data[0], out var ss)
            || !TryBcd(data[1], out var mm)
            || !TryBcd(data[2], out var hh)
            || !TryBcd(data[3], out var day)
            || !TryBcd(data[4], out var month)
            || !TryBcd(data[5], out var year)
        )
            return false;

        text = $"20{year:D2}-{month:D2}-{day:D2} {hh:D2}:{mm:D2}:{ss:D2}";
        return true;
    }

    /// <summary>冻结命令：数据域 mm hh DD MM（低字节在前）。</summary>
    private static bool TryFormatFreezeTime(byte[] data, out string text)
    {
        text = "";
        if (
            !TryBcd(data[0], out var minute)
            || !TryBcd(data[1], out var hour)
            || !TryBcd(data[2], out var day)
            || !TryBcd(data[3], out var month)
        )
            return false;

        text = $"{month:D2}-{day:D2} {hour:D2}:{minute:D2}";
        return true;
    }

    private static bool TryBcd(byte value, out int number)
    {
        var high = value >> 4;
        var low = value & 0x0F;
        if (high > 9 || low > 9)
        {
            number = 0;
            return false;
        }

        number = high * 10 + low;
        return true;
    }
}
