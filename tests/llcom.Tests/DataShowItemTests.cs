using System;
using llcom.Model;
using llcom.Tools;
using Xunit;

namespace llcom.Tests
{
    /// <summary>
    /// DataShowItem 显示策略：数据单行显示；发送端看 hexSend（发送Hex），接收端看 recvShowHex。
    /// </summary>
    public class DataShowItemTests
    {
        public DataShowItemTests()
        {
            TestEnv.Init();
        }

        [Fact]
        public void Sent_WithHexSend_UsesHexSingleLine()
        {
            var old = Global.setting.hexSend;
            try
            {
                Global.setting.hexSend = true;

                var item = new DataShowItem(
                    new byte[] { 0x00, 0x03, 0xE8, 0x00, 0x06 },
                    DateTime.Now,
                    true
                );

                Assert.Equal("00 03 E8 00 06", item.DataText);
                Assert.Equal(" → ", item.ArrowText);
                Assert.True(string.IsNullOrEmpty(item.RawTitle));
                Assert.True(string.IsNullOrEmpty(item.RawText));
            }
            finally
            {
                Global.setting.hexSend = old;
            }
        }

        [Fact]
        public void Sent_WithoutHexSend_UsesTextSingleLine()
        {
            var old = Global.setting.hexSend;
            try
            {
                Global.setting.hexSend = false;

                var item = new DataShowItem(new byte[] { 0x41, 0x42 }, DateTime.Now, true);

                Assert.Contains("AB", item.DataText);
                Assert.Equal(" → ", item.ArrowText);
            }
            finally
            {
                Global.setting.hexSend = old;
            }
        }

        [Fact]
        public void GenericChannel_UsesHexInRawText()
        {
            var item = new DataShowItem("MQTT", new byte[] { 0x0A, 0x0B }, DateTime.Now, null);

            Assert.Equal("\n0A 0B", item.RawText);
            Assert.Equal("MQTT", item.RawTitle);
        }

        [Fact]
        public void Cache_FollowsSettings_AndRefreshNotifiesBinding()
        {
            var old = Global.setting.hexSend;
            try
            {
                Global.setting.hexSend = true;
                var item = new DataShowItem(new byte[] { 0x41, 0x42 }, DateTime.Now, true);
                Assert.Equal("41 42", item.DataText);

                //缓存键包含显示模式：改设置后无需 Refresh 也能按新格式读取
                Global.setting.hexSend = false;
                Assert.Contains("AB", item.DataText);

                //Refresh 只负责通知绑定（虚拟化下只重新读取可见条目）
                var changed = new System.Collections.Generic.List<string>();
                item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
                item.Refresh();
                Assert.Contains(nameof(DataShowItem.DataText), changed);
                Assert.Contains(nameof(DataShowItem.RawText), changed);
            }
            finally
            {
                Global.setting.hexSend = old;
            }
        }
    }
}
