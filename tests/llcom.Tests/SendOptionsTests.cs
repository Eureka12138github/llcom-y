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
            Assert.Equal(5, s.sendHistoryLimit);
            s.sendHistoryLimit = 9999;
            Assert.Equal(200, s.sendHistoryLimit);
        }

        [Fact]
        public void HexModeBytes_ParsesStrictlyAsHex()
        {
            // 合法十六进制 → 正常解析
            Assert.Equal(
                new byte[] { 0x03, 0x03 },
                MainViewModel.HexModeBytes(System.Text.Encoding.UTF8.GetBytes("0303"))
            );
            Assert.Equal(
                new byte[] { 0x0a, 0x0b },
                MainViewModel.HexModeBytes(System.Text.Encoding.UTF8.GetBytes("0A 0B"))
            );

            // Hex 模式下不做内容判断：非十六进制字符会被过滤
            Assert.Empty(MainViewModel.HexModeBytes(System.Text.Encoding.UTF8.GetBytes("test")));
            Assert.Equal(
                new byte[] { 0x03 },
                MainViewModel.HexModeBytes(System.Text.Encoding.UTF8.GetBytes("03kkkkk"))
            );
        }

        [Fact]
        public void ContinuousSettings_JsonRoundTrip_Preserved()
        {
            var s = new Settings
            {
                continuousSend = true,
                continuousDelayMs = 250,
                continuousRepeat = 3,
            };

            var back = JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(s));

            Assert.True(back.continuousSend);
            Assert.Equal(250, back.continuousDelayMs);
            Assert.Equal(3, back.continuousRepeat);
        }

        [Theory]
        [InlineData(5, 10)]
        [InlineData(10, 10)]
        [InlineData(1000, 1000)]
        [InlineData(99999999, 3600000)]
        public void NormalizeInterval_Clamps(int input, int expected)
        {
            Assert.Equal(expected, MainViewModel.NormalizeInterval(input));
        }

        [Fact]
        public void ToSendData_TimerFields_JsonRoundTrip()
        {
            var d = new ToSendData { text = "x", timerEnabled = true, timerIntervalMs = 250 };

            var back = JsonConvert.DeserializeObject<ToSendData>(JsonConvert.SerializeObject(d));

            Assert.True(back.timerEnabled);
            Assert.Equal(250, back.timerIntervalMs);
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
