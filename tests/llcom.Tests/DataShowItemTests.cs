using System;
using llcom.Model;
using Xunit;

namespace llcom.Tests
{
    /// <summary>
    /// DataShowItem 显示策略：数据统一为十六进制单行（不再有“原始数据 / Hex:”额外行）。
    /// </summary>
    public class DataShowItemTests
    {
        public DataShowItemTests()
        {
            TestEnv.Init();
        }

        [Fact]
        public void Sent_UsesHexSingleLine()
        {
            var item = new DataShowItem(
                new byte[] { 0x00, 0x03, 0xE8, 0x00, 0x06 },
                DateTime.Now,
                true
            );

            Assert.Equal("00 03 E8 00 06", item.DataText);
            Assert.Equal(" ← ", item.ArrowText);
            Assert.True(string.IsNullOrEmpty(item.RawTitle));
            Assert.True(string.IsNullOrEmpty(item.RawText));
        }

        [Fact]
        public void GenericChannel_UsesHexInRawText()
        {
            var item = new DataShowItem("MQTT", new byte[] { 0x0A, 0x0B }, DateTime.Now, null);

            Assert.Equal("\n0A 0B", item.RawText);
            Assert.Equal("MQTT", item.RawTitle);
        }
    }
}
