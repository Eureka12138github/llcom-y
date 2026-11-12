using System;
using llcom.Services;
using Xunit;

namespace llcom.Tests
{
    public class CalculatorMathTests
    {
        [Theory]
        [InlineData(8, 0xFFUL)]
        [InlineData(16, 0xFFFFUL)]
        [InlineData(32, 0xFFFFFFFFUL)]
        [InlineData(64, ulong.MaxValue)]
        public void Mask_MatchesBitWidth(int width, ulong expected)
        {
            Assert.Equal(expected, CalculatorMath.Mask(width));
        }

        [Fact]
        public void FormatHex_PadsToWidthAndGroupsByByte()
        {
            Assert.Equal("00 00 00 FF", CalculatorMath.FormatHex(0xFF, 32));
            Assert.Equal("FF", CalculatorMath.FormatHex(0xFF, 8));
        }

        [Fact]
        public void FormatBin_GroupsByNibble()
        {
            Assert.Equal("0000 0000 1111 1111", CalculatorMath.FormatBin(0xFF, 16));
        }

        [Fact]
        public void FormatOct_GroupsByThreeDigits()
        {
            Assert.Equal("000 377", CalculatorMath.FormatOct(0xFF, 16));
        }

        [Theory]
        [InlineData(10UL, 2, "1010")]
        [InlineData(8UL, 8, "10")]
        [InlineData(255UL, 16, "FF")]
        [InlineData(0UL, 2, "0")]
        [InlineData(ulong.MaxValue, 16, "FFFFFFFFFFFFFFFF")]
        public void ToBaseString_ConvertsUnsignedValues(ulong value, int radix, string expected)
        {
            Assert.Equal(expected, CalculatorMath.ToBaseString(value, radix));
        }

        [Theory]
        [InlineData("FF", 16, 255UL)]
        [InlineData("0x10", 16, 16UL)]
        [InlineData("777", 8, 511UL)]
        [InlineData("1010", 2, 10UL)]
        [InlineData("1000", 10, 1000UL)]
        [InlineData("1_000", 10, 1000UL)]
        public void TryParseValue_ParsesSupportedBases(string text, int radix, ulong expected)
        {
            Assert.True(CalculatorMath.TryParseValue(text, radix, out var value));
            Assert.Equal(expected, value);
        }

        [Fact]
        public void TryParseValue_NegativeDecimalBecomesTwosComplement()
        {
            Assert.True(CalculatorMath.TryParseValue("-1", 10, out var value));
            Assert.Equal(ulong.MaxValue, value);
        }

        [Theory]
        [InlineData("XYZ", 16)]
        [InlineData("2", 2)]
        [InlineData("8", 8)]
        [InlineData("", 10)]
        [InlineData("10000000000000000", 16)]
        public void TryParseValue_RejectsInvalidOrOverflowingValues(string text, int radix)
        {
            Assert.False(CalculatorMath.TryParseValue(text, radix, out _));
        }

        [Theory]
        [InlineData("3f 80 00 00", 8, "3F800000")]
        [InlineData("0x3F800000", 8, "3F800000")]
        [InlineData("xyz", 8, "")]
        [InlineData("123456789", 8, "23456789")]
        [InlineData("abc", 16, "ABC")]
        public void SanitizeHex_KeepsOnlyHexDigitsAndLimitsLength(
            string text,
            int maxDigits,
            string expected
        )
        {
            Assert.Equal(expected, CalculatorMath.SanitizeHex(text, maxDigits));
        }

        [Fact]
        public void FormatFloat_BigEndianFloat()
        {
            Assert.Equal("1", CalculatorMath.FormatFloat("3F800000", 4, true, out var ok));
            Assert.True(ok);
        }

        [Fact]
        public void FormatFloat_LittleEndianFloat()
        {
            Assert.Equal("1", CalculatorMath.FormatFloat("0000803F", 4, false, out var ok));
            Assert.True(ok);
        }

        [Fact]
        public void FormatFloat_BigEndianDouble()
        {
            var text = CalculatorMath.FormatFloat("400921FB54442D18", 8, true, out var ok);
            Assert.True(ok);
            Assert.StartsWith("3.14159265358979", text);
        }

        [Fact]
        public void FormatFloat_RejectsInvalidHex()
        {
            Assert.Equal("—", CalculatorMath.FormatFloat("XYZ", 4, true, out var ok));
            Assert.False(ok);
        }

        [Fact]
        public void FormatFloat_EmptyInputIsNeutral()
        {
            Assert.Equal("—", CalculatorMath.FormatFloat("  ", 4, true, out var ok));
            Assert.True(ok);
        }

        [Theory]
        [InlineData(0xF0UL, 0x0FUL, "and", 32, 0UL)]
        [InlineData(0xF0UL, 0x0FUL, "or", 32, 0xFFUL)]
        [InlineData(0xFFUL, 0x0FUL, "xor", 32, 0xF0UL)]
        [InlineData(0x10UL, 0UL, "not", 8, 0xEFUL)]
        [InlineData(1UL, 4UL, "shl", 8, 0x10UL)]
        [InlineData(0x10UL, 4UL, "shr", 8, 1UL)]
        [InlineData(1UL, 8UL, "shl", 8, 0UL)]
        [InlineData(1UL, 9UL, "shl", 8, 0UL)]
        [InlineData(0x1FFUL, 0UL, "not", 8, 0UL)]
        public void TryApplyBitOp_MasksToWidth(
            ulong value,
            ulong operand,
            string operation,
            int width,
            ulong expected
        )
        {
            Assert.True(
                CalculatorMath.TryApplyBitOp(value, operand, operation, width, out var result)
            );
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TryApplyBitOp_UnknownOperationFails()
        {
            Assert.False(CalculatorMath.TryApplyBitOp(1, 1, "bogus", 32, out _));
        }
    }
}