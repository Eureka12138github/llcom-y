using System;
using System.Globalization;
using System.Text;

namespace llcom.Services;

/// <summary>
/// 计算器小工具的核心换算逻辑：位宽掩码、进制互转、IEEE-754 解析、位运算。
/// 独立出来便于单元测试，页面只负责界面。
/// </summary>
internal static class CalculatorMath
{
    /// <summary>当前位宽对应的掩码（8/16/32 位时截断高位）。</summary>
    public static ulong Mask(int width) =>
        width >= 64 ? ulong.MaxValue : (1UL << width) - 1;

    /// <summary>把无符号整数转换到指定进制（2/8/16）。</summary>
    public static string ToBaseString(ulong value, int radix)
    {
        if (radix < 2 || radix > 16)
            throw new ArgumentOutOfRangeException(nameof(radix));

        const string digits = "0123456789ABCDEF";
        var buffer = new char[64];
        var pos = buffer.Length;
        do
        {
            buffer[--pos] = digits[(int)(value % (ulong)radix)];
            value /= (ulong)radix;
        } while (value > 0);

        return new string(buffer, pos, buffer.Length - pos);
    }

    /// <summary>按指定进制解析无符号数，溢出或含非法字符时返回 false。</summary>
    public static bool TryParseUnsignedBase(string text, int radix, out ulong value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var c in text)
        {
            int digit;
            if (c >= '0' && c <= '9')
                digit = c - '0';
            else if (c >= 'A' && c <= 'F')
                digit = c - 'A' + 10;
            else if (c >= 'a' && c <= 'f')
                digit = c - 'a' + 10;
            else
                return false;

            if (digit >= radix)
                return false;

            if (value > (ulong.MaxValue - (ulong)digit) / (ulong)radix)
                return false;

            value = value * (ulong)radix + (ulong)digit;
        }

        return true;
    }

    /// <summary>解析输入框内容；十进制允许负数（按二进制补码转为无符号）。</summary>
    public static bool TryParseValue(string text, int radix, out ulong value)
    {
        value = 0;
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return false;

        if (radix == 10)
        {
            if (normalized[0] == '-')
            {
                if (
                    !long.TryParse(
                        normalized,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var signed
                    )
                )
                    return false;
                value = unchecked((ulong)signed);
                return true;
            }

            return ulong.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }

        if (radix == 16 && normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(2);

        return TryParseUnsignedBase(normalized, radix, out value);
    }

    public static string FormatHex(ulong value, int width) =>
        Group(value.ToString("X", CultureInfo.InvariantCulture).PadLeft(width / 4, '0'), 2);

    public static string FormatBin(ulong value, int width) =>
        Group(ToBaseString(value, 2).PadLeft(width, '0'), 4);

    public static string FormatOct(ulong value, int width) =>
        Group(ToBaseString(value, 8).PadLeft((width + 2) / 3, '0'), 3);

    /// <summary>从右往左每 size 位插入一个空格，便于阅读。</summary>
    public static string Group(string value, int size)
    {
        if (value.Length <= size)
            return value;

        var sb = new StringBuilder(value.Length + value.Length / size);
        var first = value.Length % size;
        if (first == 0)
            first = size;

        sb.Append(value, 0, first);
        for (var i = first; i < value.Length; i += size)
        {
            sb.Append(' ');
            sb.Append(value, i, Math.Min(size, value.Length - i));
        }

        return sb.ToString();
    }

    /// <summary>把十六进制串解析为 byteCount 个字节；不足时左补 0。</summary>
    public static bool TryParseHexBytes(string text, int byteCount, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var normalized = NormalizeHex(text);
        if (normalized.Length > byteCount * 2)
            return false;
        if (normalized.Length % 2 != 0)
            normalized = "0" + normalized;

        foreach (var c in normalized)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        var result = new byte[byteCount];
        var offset = byteCount - normalized.Length / 2;
        for (var i = 0; i < normalized.Length / 2; i++)
        {
            result[offset + i] = byte.Parse(
                normalized.Substring(i * 2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture
            );
        }

        bytes = result;
        return true;
    }

    /// <summary>把 4/8 字节十六进制串解析为 float/double；大端时先反转字节。</summary>
    public static string FormatFloat(string text, int byteCount, bool bigEndian, out bool ok)
    {
        ok = true;
        if (NormalizeHex(text).Length == 0)
            return "—";

        if (!TryParseHexBytes(text, byteCount, out var bytes))
        {
            ok = false;
            return "—";
        }

        if (bigEndian)
            Array.Reverse(bytes);

        return byteCount == 4
            ? BitConverter.ToSingle(bytes, 0).ToString("G9", CultureInfo.InvariantCulture)
            : BitConverter.ToDouble(bytes, 0).ToString("G17", CultureInfo.InvariantCulture);
    }

    /// <summary>执行位运算并按位宽截断，返回 false 表示运算符不支持。</summary>
    public static bool TryApplyBitOp(
        ulong value,
        ulong operand,
        string operation,
        int width,
        out ulong result
    )
    {
        result = 0;
        var mask = Mask(width);
        switch (operation)
        {
            case "not":
                result = ~value & mask;
                return true;
            case "shl":
            case "shr":
                operand &= mask;
                var shift = operand >= (ulong)width ? width : (int)operand;
                if (shift >= width)
                    result = 0;
                else
                    result = operation == "shl" ? value << shift : value >> shift;
                result &= mask;
                return true;
            case "and":
                result = (value & operand) & mask;
                return true;
            case "or":
                result = (value | operand) & mask;
                return true;
            case "xor":
                result = (value ^ operand) & mask;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 过滤浮点输入：只保留十六进制字符（去掉空格、逗号、0x 等），
    /// 超过 maxDigits 时保留低位，保证输入框只能容纳对应字节数。
    /// </summary>
    public static string SanitizeHex(string text, int maxDigits)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == 'x' || c == 'X' || char.IsWhiteSpace(c) || c == ',' || c == '_')
                continue;
            if (Uri.IsHexDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }

        var result = sb.ToString();
        if (result.Length > maxDigits)
            result = result.Substring(result.Length - maxDigits);
        return result;
    }

    private static string Normalize(string text) =>
        text.Replace(" ", "").Replace("\t", "").Replace("_", "");

    private static string NormalizeHex(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == ',' || c == '_')
                continue;
            sb.Append(c);
        }

        var result = sb.ToString();
        if (result.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(2);
        return result;
    }
}