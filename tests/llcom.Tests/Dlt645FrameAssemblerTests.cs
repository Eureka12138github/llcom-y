using llcom.Services.Dlt645;
using Xunit;

namespace llcom.Tests
{
    public class Dlt645FrameAssemblerTests
    {
        private const string ReadAddressResponse =
            "FE FE 68 00 00 00 00 00 55 68 93 06 33 33 33 33 33 88 45 16";

        private static byte[] Hex(string text)
        {
            Assert.True(Dlt645Codec.TryParseHex(text, out var bytes, out var error), error);
            return bytes;
        }

        [Fact]
        public void Append_HandlesByteByByteFragmentation()
        {
            var assembler = new Dlt645FrameAssembler();
            var data = Hex(ReadAddressResponse);

            Dlt645Frame last = null;
            for (var i = 0; i < data.Length; i++)
            {
                var frames = assembler.Append(new[] { data[i] });
                if (frames.Count > 0)
                    last = frames[0];
            }

            Assert.NotNull(last);
            Assert.Equal("550000000000", last!.Address);
            Assert.Equal(2, last.PreambleCount);
        }

        [Fact]
        public void Append_HandlesTwoFramesInOnePacket()
        {
            var assembler = new Dlt645FrameAssembler();
            var data = Hex(ReadAddressResponse + " " + ReadAddressResponse);

            var frames = assembler.Append(data);

            Assert.Equal(2, frames.Count);
            Assert.Equal("550000000000", frames[0].Address);
            Assert.Equal("550000000000", frames[1].Address);
        }

        [Fact]
        public void Append_IgnoresNoiseBeforePreamble()
        {
            var assembler = new Dlt645FrameAssembler();
            var data = Hex("00 11 22 " + ReadAddressResponse);

            var frames = assembler.Append(data);

            Assert.Single(frames);
            Assert.Equal(2, frames[0].PreambleCount);
        }

        [Fact]
        public void Append_DropsBadFrameAndFindsNextGoodFrame()
        {
            var assembler = new Dlt645FrameAssembler();
            var bad = Hex(ReadAddressResponse);
            bad[18] = 0x00;

            var good = Hex(ReadAddressResponse);
            var data = new byte[bad.Length + good.Length];
            System.Array.Copy(bad, 0, data, 0, bad.Length);
            System.Array.Copy(good, 0, data, bad.Length, good.Length);

            var frames = assembler.Append(data);

            Assert.Single(frames);
            Assert.Equal("550000000000", frames[0].Address);
        }

        [Fact]
        public void Append_WaitsForIncompleteFrame()
        {
            var assembler = new Dlt645FrameAssembler();
            var data = Hex(ReadAddressResponse);
            var head = new byte[data.Length - 2];
            System.Array.Copy(data, head, head.Length);
            var tail = new byte[2];
            System.Array.Copy(data, data.Length - 2, tail, 0, 2);

            Assert.Empty(assembler.Append(head));
            Assert.Single(assembler.Append(tail));
        }
    }
}
