using System;
using llcom.Services.Dlt645;
using Xunit;

namespace llcom.Tests
{
    public class Dlt645CodecTests
    {
        private static byte[] Hex(string text)
        {
            Assert.True(Dlt645Codec.TryParseHex(text, out var bytes, out var error), error);
            return bytes;
        }

        [Fact]
        public void BuildReadAddressRequest_MatchesPublicSample()
        {
            var frame = Dlt645Codec.BuildReadAddressRequest(3);

            Assert.Equal(
                "FE FE FE 68 AA AA AA AA AA AA 68 13 00 DF 16",
                Dlt645Codec.ToHex(frame)
            );
        }

        [Fact]
        public void BuildReadDataRequest_EncodesDataIdentifier()
        {
            var frame = Dlt645Codec.BuildReadDataRequest(
                "000000000001",
                Hex("02 01 01 00"),
                3
            );

            Assert.Equal(
                "FE FE FE 68 01 00 00 00 00 00 68 11 04 35 34 34 33 B6 16",
                Dlt645Codec.ToHex(frame)
            );
        }

        [Fact]
        public void ParseReadAddressResponse_ReturnsActualAddress()
        {
            var frame = Dlt645Codec.Parse(
                Hex("FE FE 68 00 00 00 00 00 55 68 93 06 33 33 33 33 33 88 45 16")
            );

            Assert.Equal("550000000000", frame.Address);
            Assert.Equal(2, frame.PreambleCount);
            Assert.Equal(0x93, frame.Control);
            Assert.True(frame.IsResponse);
            Assert.False(frame.IsException);
            Assert.Equal(0x13, frame.FunctionCode);
            Assert.Equal("00 00 00 00 00 55", Dlt645Codec.ToHex(frame.Data));
            Assert.Equal(
                "33 33 33 33 33 88",
                Dlt645Codec.ToHex(frame.EncodedData)
            );
            Assert.Equal(0x45, frame.Checksum);
        }

        [Fact]
        public void TryGetReadAddress_AcceptsMatchingResponse()
        {
            var frame = Dlt645Codec.Parse(
                Hex("FE FE 68 00 00 00 00 00 55 68 93 06 33 33 33 33 33 88 45 16")
            );

            Assert.True(Dlt645Codec.TryGetReadAddress(frame, out var address, out var error), error);
            Assert.Equal("550000000000", address);
        }

        [Fact]
        public void ParseFrameWithoutPreamble_Works()
        {
            var frame = Dlt645Codec.Parse(
                Hex("68 AA AA AA AA AA AA 68 13 00 DF 16")
            );

            Assert.Equal(0, frame.PreambleCount);
            Assert.Equal("AAAAAAAAAAAA", frame.Address);
            Assert.Equal(0x13, frame.Control);
            Assert.Empty(frame.Data);
        }

        [Fact]
        public void ParseExceptionResponse_FlagsException()
        {
            var raw = Dlt645Codec.BuildFrame(
                "000000000001",
                0xD3,
                new byte[] { 0x01 },
                0
            );

            var frame = Dlt645Codec.Parse(raw);

            Assert.True(frame.IsResponse);
            Assert.True(frame.IsException);
            Assert.Equal(0x13, frame.FunctionCode);
            Assert.Equal(new byte[] { 0x01 }, frame.Data);
        }

        [Fact]
        public void ParseRejectsBadChecksum()
        {
            var raw = Hex("FE FE 68 00 00 00 00 00 55 68 93 06 33 33 33 33 33 88 45 16");
            raw[18] = 0x00;

            Assert.False(Dlt645Codec.TryParse(raw, out _, out var error));
            Assert.Contains("校验和", error);
        }

        [Fact]
        public void ParseRejectsBadEndByte()
        {
            var raw = Hex("FE FE 68 00 00 00 00 00 55 68 93 06 33 33 33 33 33 88 45 16");
            raw[raw.Length - 1] = 0x00;

            Assert.False(Dlt645Codec.TryParse(raw, out _, out var error));
            Assert.Contains("结束符", error);
        }

        [Fact]
        public void ParseRejectsTruncatedFrame()
        {
            var raw = Hex("FE FE 68 00 00 00 00 00 55 68 93 06 33 33 33 33 33 88 45");

            Assert.False(Dlt645Codec.TryParse(raw, out _, out var error));
            Assert.Contains("长度", error);
        }

        [Fact]
        public void Address_RoundTripsThroughBcd()
        {
            Assert.True(
                Dlt645Codec.TryParseAddress("000000000001", out var bytes, out var error),
                error
            );
            Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 }, bytes);
            Assert.Equal("000000000001", Dlt645Codec.FormatAddress(bytes));

            Assert.True(
                Dlt645Codec.TryParseAddress("123456789012", out var bytes2, out var error2),
                error2
            );
            Assert.Equal("123456789012", Dlt645Codec.FormatAddress(bytes2));
        }

        [Fact]
        public void Address_SupportsWildcard()
        {
            Assert.True(
                Dlt645Codec.TryParseAddress("AAAAAAAAAAAA", out var bytes, out var error),
                error
            );
            Assert.All(bytes, value => Assert.Equal(0xAA, value));
            Assert.Equal("AAAAAAAAAAAA", Dlt645Codec.FormatAddress(bytes));
        }

        [Fact]
        public void NormalizeAddress_PadsShortNumericInput()
        {
            Assert.True(
                Dlt645Codec.TryNormalizeAddress("1", out var one, out var error1),
                error1
            );
            Assert.Equal("000000000001", one);

            Assert.True(
                Dlt645Codec.TryNormalizeAddress("123", out var shortAddress, out var error2),
                error2
            );
            Assert.Equal("000000000123", shortAddress);

            Assert.True(
                Dlt645Codec.TryNormalizeAddress("AAAAAAAAAAAA", out var wildcard, out var error3),
                error3
            );
            Assert.Equal("AAAAAAAAAAAA", wildcard);

            Assert.False(Dlt645Codec.TryNormalizeAddress("ABC", out _, out _));
        }

        [Fact]
        public void Address_RejectsInvalidInput()
        {
            Assert.False(Dlt645Codec.TryParseAddress("123", out _, out _));
            Assert.False(Dlt645Codec.TryParseAddress("00000000000A", out _, out _));
        }

        [Fact]
        public void DataOffset_RoundTripsAndWraps()
        {
            var raw = new byte[] { 0x00, 0x32, 0xFF };
            var encoded = Dlt645Codec.EncodeData(raw);

            Assert.Equal(new byte[] { 0x33, 0x65, 0x32 }, encoded);
            Assert.Equal(raw, Dlt645Codec.DecodeData(encoded));
        }

        [Fact]
        public void HexParser_AcceptsCommonSeparators()
        {
            Assert.True(
                Dlt645Codec.TryParseHex("FE-FE 68:AA,AA", out var bytes, out var error),
                error
            );
            Assert.Equal(new byte[] { 0xFE, 0xFE, 0x68, 0xAA, 0xAA }, bytes);
        }

        [Fact]
        public void BuildFrame_RejectsOversizedData()
        {
            Assert.Throws<ArgumentException>(
                () => Dlt645Codec.BuildFrame("000000000001", 0x11, new byte[256])
            );
            Assert.Throws<ArgumentException>(
                () => Dlt645Codec.BuildFrame("bad-address", 0x11)
            );
        }
    }
}
