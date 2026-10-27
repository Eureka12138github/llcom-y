using System.Text;
using llcom.LuaEnv;
using Xunit;

namespace llcom.Tests
{
    public class LuaApisTests
    {
        [Fact]
        public void Sum8_ReturnsModulo256Sum()
        {
            Assert.Equal(6, LuaApis.Sum8(new byte[] { 1, 2, 3 }));
            Assert.Equal(0, LuaApis.Sum8(new byte[] { 0xFF, 0x01 }));
            Assert.Equal(0, LuaApis.Sum8(null));
        }

        [Fact]
        public void Xor8_ReturnsXor()
        {
            Assert.Equal(0x03, LuaApis.Xor8(new byte[] { 0x01, 0x02 }));
            Assert.Equal(0, LuaApis.Xor8(null));
        }

        [Fact]
        public void Crc16Modbus_MatchesStandardVector()
        {
            Assert.Equal(0x4B37, LuaApis.Crc16Modbus(Encoding.ASCII.GetBytes("123456789")));
        }

        [Fact]
        public void Base64_RoundTripsUtf8()
        {
            var encoded = LuaApis.Base64Encode("你好");
            Assert.Equal("5L2g5aW9", encoded);
            Assert.Equal("你好", LuaApis.Base64Decode(encoded));
            Assert.Equal("", LuaApis.Base64Decode(""));
        }

        [Fact]
        public void NowMs_AndVersion_ReturnValues()
        {
            Assert.True(LuaApis.NowMs() > 0);
            Assert.False(string.IsNullOrWhiteSpace(LuaApis.GetVersion()));
        }
    }
}
