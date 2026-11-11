using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace llcom.Services.Dlt645;

/// <summary>解析后的 DL/T 645-2007 帧。</summary>
public sealed class Dlt645Frame
{
    internal Dlt645Frame(
        byte[] raw,
        int preambleCount,
        byte[] addressBytes,
        byte control,
        byte[] encodedData,
        byte[] data,
        byte checksum
    )
    {
        Raw = raw;
        PreambleCount = preambleCount;
        AddressBytes = addressBytes;
        Control = control;
        EncodedData = encodedData;
        Data = data;
        Checksum = checksum;
    }

    /// <summary>完整原始帧（含前导 FE）。</summary>
    public byte[] Raw { get; }

    /// <summary>前导 FE 的个数。</summary>
    public int PreambleCount { get; }

    /// <summary>地址域 6 字节，低字节在前。</summary>
    public byte[] AddressBytes { get; }

    /// <summary>地址显示值（12 位，已反转成正常顺序）。</summary>
    public string Address => Dlt645Codec.FormatAddress(AddressBytes);

    /// <summary>控制码 C。</summary>
    public byte Control { get; }

    /// <summary>数据域（已减 0x33，供业务使用）。</summary>
    public byte[] Data { get; }

    /// <summary>数据域原始字节（含 +0x33 偏移）。</summary>
    public byte[] EncodedData { get; }

    /// <summary>帧内校验和。</summary>
    public byte Checksum { get; }

    /// <summary>从站应答帧（控制码最高位为 1）。</summary>
    public bool IsResponse => (Control & 0x80) != 0;

    /// <summary>异常应答（控制码 bit6 为 1）。</summary>
    public bool IsException => (Control & 0x40) != 0;

    /// <summary>功能码（控制码低 5 位）。</summary>
    public byte FunctionCode => (byte)(Control & 0x1F);
}

/// <summary>DL/T 645 帧解析/组帧错误。</summary>
public sealed class Dlt645ParseException : Exception
{
    public Dlt645ParseException(string message)
        : base(message) { }
}

/// <summary>
/// DL/T 645-2007 纯协议实现：
/// BCD 地址、FE 前导、68…68 帧、长度、校验和、数据域 ±0x33。
/// 不依赖 WPF/串口，便于单元测试。
/// </summary>
public static class Dlt645Codec
{
    public const byte StartByte = 0x68;
    public const byte EndByte = 0x16;
    public const byte PreambleByte = 0xFE;
    public const byte DataOffset = 0x33;
    public const int AddressLength = 6;

    public const byte ControlReadData = 0x11;
    public const byte ControlReadAddress = 0x13;
    public const byte ControlWriteData = 0x14;
    public const byte ControlBroadcastTime = 0x08;
    public const byte ControlWriteAddress = 0x15;
    public const byte ControlFreeze = 0x16;
    public const byte ControlChangeBaud = 0x17;
    public const byte ControlChangePassword = 0x18;
    public const byte ControlMaxDemandReset = 0x19;
    public const byte ControlMeterReset = 0x1A;
    public const byte ControlEventReset = 0x1B;

    public const string WildcardAddress = "AAAAAAAAAAAA";
    public const string BroadcastAddress = "999999999999";

    /// <summary>组帧：[FE...] 68 A0..A5 68 C L DATA CS 16。</summary>
    /// <param name="address">12 位表地址；支持 AA 通配（如 AAAAAAAAAAAA）。</param>
    /// <param name="control">控制码。</param>
    /// <param name="data">业务数据域（未加 0x33）。</param>
    /// <param name="preambleFe">前导 FE 个数，0–16。</param>
    /// <param name="encodeData">是否对数据域逐字节 +0x33。</param>
    public static byte[] BuildFrame(
        string address,
        byte control,
        byte[]? data = null,
        int preambleFe = 3,
        bool encodeData = true
    )
    {
        if (!TryParseAddress(address, out var addressBytes, out var addressError))
            throw new ArgumentException(addressError, nameof(address));
        if (preambleFe < 0 || preambleFe > 16)
            throw new ArgumentOutOfRangeException(nameof(preambleFe), "前导 FE 个数应为 0–16");

        var payload = data ?? Array.Empty<byte>();
        if (payload.Length > 255)
            throw new ArgumentException("数据域长度不能超过 255 字节", nameof(data));

        var encoded = encodeData ? EncodeData(payload) : (byte[])payload.Clone();
        var frame = new byte[preambleFe + 12 + encoded.Length];
        var index = 0;
        for (var i = 0; i < preambleFe; i++)
            frame[index++] = PreambleByte;

        frame[index++] = StartByte;
        Array.Copy(addressBytes, 0, frame, index, AddressLength);
        index += AddressLength;
        frame[index++] = StartByte;
        frame[index++] = control;
        frame[index++] = (byte)encoded.Length;
        Array.Copy(encoded, 0, frame, index, encoded.Length);
        index += encoded.Length;

        var checksum = 0;
        for (var i = preambleFe; i < index; i++)
            checksum += frame[i];
        frame[index++] = unchecked((byte)checksum);
        frame[index] = EndByte;
        return frame;
    }

    /// <summary>读通信地址请求（通配地址 + 0x13 + 空数据域）。</summary>
    public static byte[] BuildReadAddressRequest(int preambleFe = 3) =>
        BuildFrame(WildcardAddress, ControlReadAddress, Array.Empty<byte>(), preambleFe);

    /// <summary>读数据请求（地址 + 0x11 + 4 字节 DI）；DI 按显示顺序传入，组帧时转低位先行。</summary>
    public static byte[] BuildReadDataRequest(
        string address,
        byte[] dataIdentifier,
        int preambleFe = 3
    )
    {
        if (dataIdentifier == null || dataIdentifier.Length != 4)
            throw new ArgumentException("DI 必须是 4 字节", nameof(dataIdentifier));
        return BuildFrame(
            address,
            ControlReadData,
            ToLittleEndian(dataIdentifier),
            preambleFe
        );
    }

    /// <summary>解析单帧；失败时给出具体原因。</summary>
    /// <param name="frame">完整帧字节（含 FE 前导）。</param>
    /// <param name="result">解析结果。</param>
    /// <param name="error">失败原因。</param>
    /// <param name="decodeData">
    /// true = 数据域按标准 +33H 编码处理，<see cref="Dlt645Frame.Data"/> 返回减 33H 后的值；
    /// false = 输入数据已经是减 33H 后的值（例如页面勾选「主日志减 33H」后复制出来的帧），不再重复换算。
    /// </param>
    public static bool TryParse(
        byte[]? frame,
        out Dlt645Frame? result,
        out string error,
        bool decodeData = true
    )
    {
        result = null;
        error = "";

        if (frame == null || frame.Length == 0)
        {
            error = "帧内容为空";
            return false;
        }

        var start = 0;
        while (start < frame.Length && frame[start] == PreambleByte)
            start++;

        if (start >= frame.Length)
        {
            error = "缺少起始符 68";
            return false;
        }
        if (frame[start] != StartByte)
        {
            error = "起始符不是 68";
            return false;
        }

        var secondStart = start + 7;
        if (secondStart >= frame.Length || frame[secondStart] != StartByte)
        {
            error = "地址域后的第二个 68 缺失";
            return false;
        }

        var controlIndex = secondStart + 1;
        var lengthIndex = secondStart + 2;
        if (lengthIndex >= frame.Length)
        {
            error = "帧长度不足，缺少长度域";
            return false;
        }

        var dataLength = frame[lengthIndex];
        var dataStart = lengthIndex + 1;
        var checksumIndex = dataStart + dataLength;
        var endIndex = checksumIndex + 1;

        if (endIndex >= frame.Length)
        {
            error = "帧长度与数据域长度不匹配";
            return false;
        }
        if (frame[endIndex] != EndByte)
        {
            error = "缺少结束符 16";
            return false;
        }
        if (endIndex != frame.Length - 1)
        {
            error = "帧尾存在多余数据";
            return false;
        }

        var checksum = 0;
        for (var i = start; i < checksumIndex; i++)
            checksum += frame[i];
        if (unchecked((byte)checksum) != frame[checksumIndex])
        {
            error =
                $"校验和错误：计算值 {unchecked((byte)checksum):X2}，帧内值 {frame[checksumIndex]:X2}";
            return false;
        }

        var addressBytes = new byte[AddressLength];
        Array.Copy(frame, start + 1, addressBytes, 0, AddressLength);

        var encodedData = new byte[dataLength];
        Array.Copy(frame, dataStart, encodedData, 0, dataLength);

        result = new Dlt645Frame(
            (byte[])frame.Clone(),
            start,
            addressBytes,
            frame[controlIndex],
            encodedData,
            decodeData ? DecodeData(encodedData) : (byte[])encodedData.Clone(),
            frame[checksumIndex]
        );
        return true;
    }

    /// <summary>解析单帧；失败时抛出 <see cref="Dlt645ParseException"/>。</summary>
    public static Dlt645Frame Parse(byte[] frame)
    {
        if (!TryParse(frame, out var result, out var error))
            throw new Dlt645ParseException(error);
        return result!;
    }

    /// <summary>从读通信地址应答中取出 12 位表地址。</summary>
    public static bool TryGetReadAddress(
        Dlt645Frame frame,
        out string address,
        out string error
    )
    {
        address = "";
        error = "";

        if (!frame.IsResponse)
        {
            error = "不是从站应答帧";
            return false;
        }
        if (frame.IsException)
        {
            error = "从站返回异常应答";
            return false;
        }
        if (frame.FunctionCode != ControlReadAddress)
        {
            error = $"控制码不是读通信地址应答（0x{ControlReadAddress:X2}）";
            return false;
        }
        if (frame.Data.Length != AddressLength)
        {
            error = $"读地址应答的数据域应为 6 字节，实际 {frame.Data.Length} 字节";
            return false;
        }

        address = FormatAddress(frame.Data);
        if (
            !string.Equals(frame.Address, WildcardAddress, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(frame.Address, address, StringComparison.OrdinalIgnoreCase)
        )
        {
            error = $"应答地址 {frame.Address} 与数据域地址 {address} 不一致";
            return false;
        }
        return true;
    }

    /// <summary>解析 12 位表地址为 6 字节 BCD（低字节在前）；支持 AA 通配。</summary>
    public static bool TryParseAddress(
        string? text,
        out byte[] addressBytes,
        out string error
    )
    {
        addressBytes = new byte[AddressLength];
        error = "";

        var value = (text ?? "").Trim();
        if (value.Length != 12)
        {
            error = "表地址必须是 12 位数字（或 AA 通配）";
            return false;
        }

        for (var i = 0; i < AddressLength; i++)
        {
            var pair = value.Substring(12 - 2 * (i + 1), 2);
            if (string.Equals(pair, "AA", StringComparison.OrdinalIgnoreCase))
            {
                addressBytes[i] = 0xAA;
                continue;
            }
            if (!byte.TryParse(pair, out var decimalValue) || decimalValue > 99)
            {
                error = $"表地址包含非法字符：{pair}";
                return false;
            }
            addressBytes[i] = (byte)(((decimalValue / 10) << 4) | (decimalValue % 10));
        }
        return true;
    }

    /// <summary>
    /// 规范化用户输入的表地址：纯数字不足 12 位时高位补 0；
    /// 含 AA 通配时只做大写处理。校验失败返回 false，原输入不变。
    /// </summary>
    public static bool TryNormalizeAddress(
        string? text,
        out string normalized,
        out string error
    )
    {
        normalized = "";
        error = "";

        var value = (text ?? "").Trim().Replace(" ", "");
        if (value.Length == 0)
        {
            error = "表地址不能为空";
            return false;
        }

        if (value.Length < 12 && value.All(c => c >= '0' && c <= '9'))
            value = value.PadLeft(12, '0');
        else if (value.Any(c => c == 'A' || c == 'a'))
            value = value.ToUpperInvariant();

        if (!TryParseAddress(value, out _, out error))
            return false;

        normalized = value;
        return true;
    }

    /// <summary>6 字节 BCD 地址转 12 位显示值（字节反转）。</summary>
    public static string FormatAddress(byte[] addressBytes)    {
        if (addressBytes == null || addressBytes.Length != AddressLength)
            throw new ArgumentException("地址必须是 6 字节", nameof(addressBytes));

        var builder = new StringBuilder(12);
        for (var i = AddressLength - 1; i >= 0; i--)
            builder.Append(addressBytes[i].ToString("X2"));
        return builder.ToString();
    }

    /// <summary>数据域逐字节 +0x33（模 256）。</summary>
    public static byte[] EncodeData(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = unchecked((byte)(data[i] + DataOffset));
        return result;
    }

    /// <summary>
    /// 645 数据域中的多字节量按低位先行传输。
    /// 输入/显示按高位在先（如 DI 02010100），组帧前用本方法反转成线上顺序（00 01 01 02）。
    /// </summary>
    public static byte[] ToLittleEndian(byte[] value)
    {
        var result = (byte[])value.Clone();
        Array.Reverse(result);
        return result;
    }

    /// <summary>
    /// 组装写数据（0x14）的数据域：DI + [密码 + 操作者代码] + 附加数据。
    /// 入参均按界面显示顺序（高位在先），返回线上顺序（各段低位先行）。
    /// </summary>
    /// <param name="dataIdentifier">4 字节 DI（显示顺序）</param>
    /// <param name="password">4 字节密码；为 null 表示不添加</param>
    /// <param name="operatorCode">4 字节操作者代码；与密码同时提供或同时为 null</param>
    /// <param name="extraData">附加数据（显示顺序，整段反转）；为空表示不添加</param>
    public static byte[] BuildWriteDataDomain(
        byte[] dataIdentifier,
        byte[]? password = null,
        byte[]? operatorCode = null,
        byte[]? extraData = null
    )
    {
        if (dataIdentifier == null || dataIdentifier.Length != 4)
            throw new ArgumentException("DI 必须是 4 字节", nameof(dataIdentifier));
        if ((password == null) != (operatorCode == null))
            throw new ArgumentException("密码与操作者代码必须同时提供", nameof(password));
        if (password != null && password.Length != 4)
            throw new ArgumentException("密码必须是 4 字节", nameof(password));
        if (operatorCode != null && operatorCode.Length != 4)
            throw new ArgumentException("操作者代码必须是 4 字节", nameof(operatorCode));

        var result = new List<byte>(dataIdentifier.Length + 8 + (extraData?.Length ?? 0));
        result.AddRange(ToLittleEndian(dataIdentifier));
        if (password != null && operatorCode != null)
        {
            result.AddRange(ToLittleEndian(password));
            result.AddRange(ToLittleEndian(operatorCode));
        }
        if (extraData != null && extraData.Length > 0)
            result.AddRange(ToLittleEndian(extraData));
        return result.ToArray();
    }

    /// <summary>数据域逐字节 -0x33（模 256）。</summary>
    public static byte[] DecodeData(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = unchecked((byte)(data[i] - DataOffset));
        return result;
    }

    /// <summary>解析带空格/连字符/冒号的十六进制文本。</summary>
    public static bool TryParseHex(string? text, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "十六进制内容为空";
            return false;
        }

        var value = text!;
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c == '-' || c == ':' || c == ',')
                continue;
            if (!Uri.IsHexDigit(c))
            {
                error = $"非法十六进制字符：{c}";
                return false;
            }
            builder.Append(c);
        }

        if (builder.Length == 0 || builder.Length % 2 != 0)
        {
            error = "十六进制字符数必须为偶数";
            return false;
        }

        bytes = new byte[builder.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(builder.ToString(i * 2, 2), 16);
        return true;
    }

    /// <summary>字节数组转大写十六进制文本。</summary>
    public static string ToHex(IEnumerable<byte> data, string separator = " ")
    {
        var builder = new StringBuilder();
        foreach (var value in data)
        {
            if (builder.Length > 0)
                builder.Append(separator);
            builder.Append(value.ToString("X2"));
        }
        return builder.ToString();
    }
}
