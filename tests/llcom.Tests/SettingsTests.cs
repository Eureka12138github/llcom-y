using System.Collections.Generic;
using System.IO;
using llcom.Model;
using llcom.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace llcom.Tests
{
    public class SettingsTests
    {
        public SettingsTests()
        {
            TestEnv.Init();
        }

        /// <summary>
        /// 回归：quickSend 是 quickSendList 的视图属性并带 [JsonIgnore]。
        /// 若移除该特性，反序列化时 Newtonsoft（ObjectCreationHandling.Auto）
        /// 会向同一 List 追加两次，导致快捷发送数据翻倍。
        /// </summary>
        [Fact]
        public void QuickSend_JsonRoundTrip_DoesNotDuplicate()
        {
            var s = new Settings();
            s.quickSendSelect = 0;
            s.quickSend = new List<ToSendData>
            {
                new ToSendData { text = "a" },
                new ToSendData { text = "b" },
            };

            var json = JsonConvert.SerializeObject(s);

            // 序列化结果里应只保留 quickSendList 一份数据（quickSend 被 [JsonIgnore] 排除）
            var page0 = JObject.Parse(json)["quickSendList"]?[0] as JArray;
            Assert.NotNull(page0);
            Assert.Equal(2, page0.Count);

            var back = JsonConvert.DeserializeObject<Settings>(json);
            back.quickSendSelect = 0;

            Assert.Equal(2, back.quickSend.Count);
        }

        /// <summary>
        /// Flush 应立即把配置写盘（不等待 600ms 防抖）。
        /// </summary>
        [Fact]
        public void Flush_WritesSettingsFile()
        {
            var s = new Settings();
            s.baudRate = 57600;

            s.Flush();

            var path = Global.ProfilePath + "settings.json";
            Assert.True(File.Exists(path));
            Assert.Contains("57600", File.ReadAllText(path));
        }

        /// <summary>日志缓冲上限：默认 5 万条 / 200 万字符，并夹取到合法范围。</summary>
        [Fact]
        public void LogBufferLimits_AreClamped()
        {
            var s = new Settings();
            Assert.Equal(50000, s.logMaxItems);
            Assert.Equal(2000000, s.streamMaxChars);

            s.logMaxItems = 10;
            Assert.Equal(Settings.LogMaxItemsMin, s.logMaxItems);
            s.logMaxItems = 9999999;
            Assert.Equal(Settings.LogMaxItemsMax, s.logMaxItems);

            s.streamMaxChars = 1;
            Assert.Equal(Settings.StreamMaxCharsMin, s.streamMaxChars);
            s.streamMaxChars = 999999999;
            Assert.Equal(Settings.StreamMaxCharsMax, s.streamMaxChars);
        }
    }
}