using System;
using System.IO;
using llcom.Model;
using llcom.Tools;
using llcom.ViewModels;
using Xunit;

namespace llcom.Tests
{
    /// <summary>
    /// 导出日志与主日志一致：时间 + 方向 + 当前 Hex/字符串表示（四种收发开关组合）。
    /// </summary>
    public class DataShowSaveLogTests
    {
        public DataShowSaveLogTests()
        {
            TestEnv.Init(); // 已准备好 core_script 与接收转换脚本 default.lua
            Global.setting.recvScript = "default";
            Global.setting.EnableSymbol = false;
        }

        private static DataShowViewModel CreateVm() =>
            new DataShowViewModel(Global.setting, new FakeSerialPortService());

        private static string Export(DataShowViewModel vm)
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "llcom-export-" + Guid.NewGuid().ToString("N") + ".txt"
            );
            vm.SaveLog(path);
            return path;
        }

        [Theory]
        [InlineData(true, true, "41 42", "43 44")]
        [InlineData(true, false, "41 42", "CD")]
        [InlineData(false, false, "AB", "CD")]
        [InlineData(false, true, "AB", "43 44")]
        public void SaveLog_FollowsSendRecvToggles(
            bool sendHex,
            bool recvHex,
            string expectedSend,
            string expectedRecv
        )
        {
            Global.setting.hexSend = sendHex;
            Global.setting.recvShowHex = recvHex;
            var vm = CreateVm();

            vm.AppendPackItem(new DataShowItem(new byte[] { 0x41, 0x42 }, DateTime.Now, true));
            vm.AppendPackItem(new DataShowItem(new byte[] { 0x43, 0x44 }, DateTime.Now, false));

            var lines = File.ReadAllLines(Export(vm));

            Assert.Equal(2, lines.Length);
            Assert.Contains(" → ", lines[0]);
            Assert.EndsWith(expectedSend, lines[0]);
            Assert.Contains(" ← ", lines[1]);
            Assert.EndsWith(expectedRecv, lines[1]);
        }

        [Fact]
        public void SaveLog_ToggleChangeAffectsExistingItems()
        {
            // 界面切换 Hex/字符串会重绘所有旧行，导出时也应使用当前设置
            Global.setting.hexSend = true;
            var vm = CreateVm();
            vm.AppendPackItem(new DataShowItem(new byte[] { 0x41, 0x42 }, DateTime.Now, true));

            Global.setting.hexSend = false;
            var lines = File.ReadAllLines(Export(vm));

            Assert.Single(lines);
            Assert.EndsWith("AB", lines[0]);
        }

        [Fact]
        public void SaveLog_GenericItem_KeepsTitleAndText()
        {
            Global.setting.recvShowHex = true;
            var vm = CreateVm();
            vm.AppendPackItem(
                new DataShowItem("MQTT", new byte[] { 0x41, 0x42 }, DateTime.Now, null)
            );

            var lines = File.ReadAllLines(Export(vm));

            Assert.Equal(2, lines.Length);
            Assert.Contains("MQTT", lines[0]);
            // 通用通道固定用 Hex 显示（不受发送/接收开关影响）
            Assert.Equal("41 42", lines[1]);
        }

        [Fact]
        public void SaveLog_StreamMode_WritesBufferAsIs()
        {
            Global.setting.timeout = -1;
            Global.setting.hexSend = true;
            var vm = CreateVm();

            vm.AppendStreamText("AB");
            vm.AppendStreamText("CD");

            Assert.True(vm.HasLogContent);
            Assert.Equal("ABCD", File.ReadAllText(Export(vm)));
        }

        [Fact]
        public void SaveLog_UsesVisibleMode_WhenTimeoutChangedButNoDataArrived()
        {
            // 改「串口超时」后界面要等新数据才切换；导出应跟当前界面，而不是新设置
            Global.setting.timeout = 50;
            Global.setting.hexSend = true;
            var vm = CreateVm();
            vm.AppendPackItem(new DataShowItem(new byte[] { 0x41, 0x42 }, DateTime.Now, true));

            Global.setting.timeout = -1;

            Assert.True(vm.HasLogContent);
            var lines = File.ReadAllLines(Export(vm));
            Assert.Single(lines);
            Assert.EndsWith("41 42", lines[0]);
        }

        [Fact]
        public void HasLogContent_ReflectsVisibleItems()
        {
            var vm = CreateVm();
            Assert.False(vm.HasLogContent);

            vm.AppendPackItem(new DataShowItem(new byte[] { 0x41 }, DateTime.Now, true));

            Assert.True(vm.HasLogContent);
        }

        [Fact]
        public void ExportLogPath_UsesTimestampAndAvoidsOverwrite()
        {
            var now = new DateTime(2026, 11, 22, 10, 20, 30);
            var first = Logger.GetExportLogPath(now);
            Assert.Equal("log20261122_102030.txt", Path.GetFileName(first));

            File.WriteAllText(first, "x");
            var second = Logger.GetExportLogPath(now);

            Assert.Equal("log20261122_102030_2.txt", Path.GetFileName(second));
            Assert.NotEqual(first, second);
        }
    }
}