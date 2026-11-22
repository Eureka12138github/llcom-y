using System;
using llcom.Model;
using llcom.Tools;
using llcom.ViewModels;
using Xunit;

namespace llcom.Tests
{
    /// <summary>
    /// 日志缓冲上限：条数上限与 32 MB 负载预算同时生效，超出后丢弃最早的记录。
    /// </summary>
    public class DataShowBufferTests
    {
        public DataShowBufferTests()
        {
            TestEnv.Init();
        }

        private static DataShowViewModel CreateVm() =>
            new DataShowViewModel(Global.setting, new FakeSerialPortService());

        [Fact]
        public void PackMode_TrimsToConfiguredLimit()
        {
            Global.setting.logMaxItems = 1000;
            Global.setting.logTrimPercent = 50;
            var vm = CreateVm();

            for (var i = 0; i < 1500; i++)
                vm.AppendPackItem(new DataShowItem(new byte[] { 1, 2, 3 }, DateTime.Now, true));

            //超过上限（含少量余量）后按比例裁到低水位，之后继续增长
            Assert.True(vm.Items.Count <= 1010, $"count={vm.Items.Count}");
            Assert.True(vm.Items.Count > 0, $"count={vm.Items.Count}");
        }

        [Fact]
        public void PackMode_TrimPercent_RemovesConfiguredShare()
        {
            Global.setting.logMaxItems = 1000;
            Global.setting.logTrimPercent = 50;
            var vm = CreateVm();

            // 1000 + 余量(10) + 1 条时首次触发裁剪：应删掉一半，只剩约 500 条
            for (var i = 0; i < 1011; i++)
                vm.AppendPackItem(new DataShowItem(new byte[] { 1, 2, 3 }, DateTime.Now, true));

            Assert.InRange(vm.Items.Count, 400, 600);
        }

        [Fact]
        public void PackMode_TrimPercent100_ClearsAll()
        {
            Global.setting.logMaxItems = 1000;
            Global.setting.logTrimPercent = 100;
            var vm = CreateVm();

            for (var i = 0; i < 1011; i++)
                vm.AppendPackItem(new DataShowItem(new byte[] { 1, 2, 3 }, DateTime.Now, true));

            Assert.Empty(vm.Items);
        }

        [Fact]
        public void PackMode_BulkTrim_RaisesSingleReset()
        {
            Global.setting.logMaxItems = 1000;
            Global.setting.logTrimPercent = 50;
            var vm = CreateVm();

            var resets = 0;
            vm.Items.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    resets++;
            };

            for (var i = 0; i < 1011; i++)
                vm.AppendPackItem(new DataShowItem(new byte[] { 1, 2, 3 }, DateTime.Now, true));

            // 一次裁剪只应发一次 Reset，而不是逐条 Remove
            Assert.Equal(1, resets);
        }

        [Fact]
        public void PackMode_LoweringLimit_TrimsExactly()
        {
            Global.setting.logMaxItems = 5000;
            Global.setting.logTrimPercent = 50;
            var vm = CreateVm();

            for (var i = 0; i < 3000; i++)
                vm.AppendPackItem(new DataShowItem(new byte[] { 1, 2, 3 }, DateTime.Now, true));

            // 手动调小上限时精确裁到新上限，不按比例
            Global.setting.logMaxItems = 1000;

            Assert.Equal(1000, vm.Items.Count);
        }

        [Fact]
        public void PackMode_TrimsByPayloadBudget()
        {
            Global.setting.logMaxItems = Settings.LogMaxItemsMax;
            var vm = CreateVm();
            var payload = new byte[1024 * 1024];

            for (var i = 0; i < 40; i++)
                vm.AppendPackItem(new DataShowItem(payload, DateTime.Now, true));

            var total = 0L;
            foreach (var item in vm.Items)
                total += item.PayloadLength;

            Assert.True(total <= 32L * 1024 * 1024, $"bytes={total}");
            Assert.True(vm.Items.Count < 40, $"count={vm.Items.Count}");
        }

        [Fact]
        public void StreamMode_TrimsToConfiguredChars()
        {
            Global.setting.timeout = -1;
            Global.setting.streamMaxChars = Settings.StreamMaxCharsMin;
            var vm = CreateVm();
            var chunk = new string('A', 1000);

            for (var i = 0; i < 200; i++)
                vm.AppendStreamText(chunk);

            //流式缓冲不直接暴露，通过 SaveLog 落盘检查长度
            var path = System.IO.Path.Combine(Global.ProfilePath, "stream.log");
            vm.SaveLog(path);
            var text = System.IO.File.ReadAllText(path);

            Assert.True(text.Length <= 110000, $"len={text.Length}");
            Assert.True(text.Length >= 100000, $"len={text.Length}");
        }
    }
}
