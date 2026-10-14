using System.Collections.Generic;
using llcom.Model;
using llcom.Tools;
using llcom.ViewModels;
using Newtonsoft.Json;
using Xunit;

namespace llcom.Tests
{
    /// <summary>
    /// 发送区新增项：追加换行的字节映射、发送记录相关设置的取值范围与持久化。
    /// </summary>
    public class SendOptionsTests
    {
        public SendOptionsTests()
        {
            TestEnv.Init();
        }

        [Fact]
        public void AppendNewlineBytes_MapsModes()
        {
            Assert.Empty(MainViewModel.AppendNewlineBytes(0));
            Assert.Equal(new byte[] { 0x0a }, MainViewModel.AppendNewlineBytes(1));
            Assert.Equal(new byte[] { 0x0d }, MainViewModel.AppendNewlineBytes(2));
            Assert.Equal(new byte[] { 0x0a, 0x0d }, MainViewModel.AppendNewlineBytes(3));
            Assert.Equal(new byte[] { 0x0d, 0x0a }, MainViewModel.AppendNewlineBytes(4));
            Assert.Empty(MainViewModel.AppendNewlineBytes(99)); //越界按“无追加”
        }

        [Fact]
        public void AppendNewline_And_HistoryLimit_AreClamped()
        {
            var s = new Settings();

            s.appendNewline = 9;
            Assert.Equal(4, s.appendNewline);
            s.appendNewline = -3;
            Assert.Equal(0, s.appendNewline);

            s.sendHistoryLimit = 0;
            Assert.Equal(1, s.sendHistoryLimit);
            s.sendHistoryLimit = 9999;
            Assert.Equal(200, s.sendHistoryLimit);
        }

        [Fact]
        public void HexModeBytes_ParsesHexAndFallsBackOnlyWhenNothingParsed()
        {
            // 合法十六进制 → 正常解析
            var hexInput = System.Text.Encoding.UTF8.GetBytes("0303");
            Assert.Equal(new byte[] { 0x03, 0x03 }, MainViewModel.HexModeBytes(hexInput));

            // 完全解析不出（不是十六进制）→ 原样按字符串发送，避免什么都不发
            var textInput = System.Text.Encoding.UTF8.GetBytes("test");
            Assert.Equal(textInput, MainViewModel.HexModeBytes(textInput));
        }

        [Fact]
        public void SendHistory_JsonRoundTrip_Preserved()
        {
            var s = new Settings();
            s.sendHistory = new List<string> { "abc", "def" };
            s.appendNewline = 4;

            var json = JsonConvert.SerializeObject(s);
            var back = JsonConvert.DeserializeObject<Settings>(json);

            Assert.Equal(2, back.sendHistory.Count);
            Assert.Equal("abc", back.sendHistory[0]);
            Assert.Equal(4, back.appendNewline);
        }
    }
}
