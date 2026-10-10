using llcom.Tools;
using Xunit;

namespace llcom.Tests
{
    public class EncodingHelperTests
    {
        public EncodingHelperTests()
        {
            TestEnv.Init();
        }

        [Fact]
        public void Hex2Byte_ParsesAndStripsSeparators()
        {
            Assert.Equal(new byte[] { 0x01, 0x02, 0xFF }, EncodingHelper.Hex2Byte("01 02 ff"));
            Assert.Equal(new byte[] { 0x00, 0x10 }, EncodingHelper.Hex2Byte("00 10"));
        }

        [Fact]
        public void Hex2Byte_OddLength_DropsLastNibble()
        {
            Assert.Equal(new byte[] { 0x01 }, EncodingHelper.Hex2Byte("012"));
        }

        [Fact]
        public void Hex2Byte_Empty_ReturnsEmpty()
        {
            Assert.Empty(EncodingHelper.Hex2Byte(""));
            Assert.Empty(EncodingHelper.Hex2Byte("zz"));
        }

        [Fact]
        public void Byte2Hex_UsesSeparator()
        {
            Assert.Equal("0A-0B", EncodingHelper.Byte2Hex(new byte[] { 0x0A, 0x0B }, "-"));
            Assert.Equal("0A0B", EncodingHelper.Byte2Hex(new byte[] { 0x0A, 0x0B }));
        }

        [Fact]
        public void Byte2Hex_RespectsLength()
        {
            Assert.Equal("01 02", EncodingHelper.Byte2Hex(new byte[] { 0x01, 0x02, 0x03 }, " ", 2));
        }

        [Fact]
        public void String2Hex_Utf8()
        {
            Assert.Equal("41 42", EncodingHelper.String2Hex("AB", " "));
        }

        [Fact]
        public void HexRoundTrip()
        {
            var bytes = EncodingHelper.Hex2Byte("00 20 7F FF");
            Assert.Equal("00 20 7F FF", EncodingHelper.Byte2Hex(bytes, " "));
        }

        [Fact]
        public void Byte2String_SkipsNulBytes()
        {
            Assert.Equal("AB", EncodingHelper.Byte2String(new byte[] { 0x41, 0x00, 0x42 }));
        }

        [Fact]
        public void Byte2Readable_ReplacesControlChars_WhenSymbolEnabled()
        {
            Global.setting.encoding = 65001;
            Global.setting.EnableSymbol = true;

            var s = EncodingHelper.Byte2Readable(new byte[] { 0x0D, 0x0A });

            Assert.Contains("\u240D", s); // ␍
            Assert.Contains("\u240A", s); // ␊
        }

        [Fact]
        public void Byte2Readable_Plain_WhenSymbolDisabled()
        {
            Global.setting.encoding = 65001;
            Global.setting.EnableSymbol = false;

            var s = EncodingHelper.Byte2Readable(new byte[] { 0x0D, 0x0A });

            Assert.Equal("\r\n", s);
        }
    }
}