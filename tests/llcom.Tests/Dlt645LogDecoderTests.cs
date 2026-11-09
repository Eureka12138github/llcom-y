using System;
using llcom.Services.Dlt645;
using Xunit;

namespace llcom.Tests
{
    public class Dlt645LogDecoderTests
    {
        private const string Response =
            "FE FE 68 00 00 00 00 00 55 68 93 06 33 33 33 33 33 88 45 16";

        private static byte[] Hex(string text)
        {
            Assert.True(Dlt645Codec.TryParseHex(text, out var bytes, out var error), error);
            return bytes;
        }

        [Fact]
        public void Append_DecodesDataFieldAndRecomputesChecksum()
        {
            var decoder = new Dlt645LogDecoder();

            var entries = decoder.Append(Hex(Response));

            var entry = Assert.Single(entries);
            Assert.True(entry.Decoded);

            var raw = entry.Data;
            var data = new byte[6];
            Array.Copy(raw, 12, data, 0, data.Length);
            Assert.Equal("00 00 00 00 00 55", Dlt645Codec.ToHex(data));

            var checksum = 0;
            for (var i = 2; i < 18; i++)
                checksum += raw[i];
            Assert.Equal((byte)checksum, raw[18]);
            Assert.Equal(0x16, raw[19]);
        }

        [Fact]
        public void Append_HandlesTwoFrames()
        {
            var decoder = new Dlt645LogDecoder();

            var entries = decoder.Append(Hex(Response + " " + Response));

            Assert.Equal(2, entries.Count);
            Assert.All(entries, entry => Assert.True(entry.Decoded));
        }

        [Fact]
        public void Append_OutputsNoiseRawAndFrameDecoded()
        {
            var decoder = new Dlt645LogDecoder();

            var entries = decoder.Append(Hex("00 11 " + Response));

            Assert.Equal(2, entries.Count);
            Assert.False(entries[0].Decoded);
            Assert.True(entries[1].Decoded);
        }

        [Fact]
        public void Append_WaitsForIncompleteFrame_ThenFlushOutputsRaw()
        {
            var decoder = new Dlt645LogDecoder();
            var data = Hex(Response);
            var head = new byte[data.Length - 2];
            Array.Copy(data, head, head.Length);

            Assert.Empty(decoder.Append(head));
            Assert.True(decoder.HasPending);

            var entries = decoder.Flush();

            Assert.Single(entries);
            Assert.False(entries[0].Decoded);
            Assert.False(decoder.HasPending);
        }

        [Fact]
        public void Append_Non645DataStaysRaw()
        {
            var decoder = new Dlt645LogDecoder();

            var entries = decoder.Append(new byte[] { 0x01, 0x02, 0x03 });

            var entry = Assert.Single(entries);
            Assert.False(entry.Decoded);
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, entry.Data);
        }
    }
}
