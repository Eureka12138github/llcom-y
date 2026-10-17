using System;
using System.IO.Ports;
using System.Threading;
using llcom.Model;
using llcom.Services;
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

        /// <summary>串口服务的空实现（DataShowViewModel 只用到 RTS/DTR）。</summary>
        private sealed class FakeSerial : ISerialPortService
        {
            public SerialPort serial => null;

            public event EventHandler UartDataRecived
            {
                add { }
                remove { }
            }

            public event EventHandler UartDataSent
            {
                add { }
                remove { }
            }

            public event EventHandler UartDataRawSent
            {
                add { }
                remove { }
            }

            public bool Rts { get; set; }
            public bool Dtr { get; set; }

            public string GetName() => "COM1";

            public void SetName(string s) { }

            public bool IsOpen() => false;

            public void Open() { }

            public void Close() { }

            public void SendData(byte[] data, byte[] dataRaw = null) { }

            public EventWaitHandle WaitUartReceive { get; } =
                new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        private static DataShowViewModel CreateVm() =>
            new DataShowViewModel(Global.setting, new FakeSerial());

        [Fact]
        public void PackMode_TrimsToConfiguredLimit()
        {
            Global.setting.logMaxItems = 1000;
            var vm = CreateVm();

            for (var i = 0; i < 1500; i++)
                vm.AppendPackItem(new DataShowItem(new byte[] { 1, 2, 3 }, DateTime.Now, true));

            //裁剪留 100 条余量：最终应落在 1000~1100
            Assert.True(vm.Items.Count >= 1000, $"count={vm.Items.Count}");
            Assert.True(vm.Items.Count <= 1100, $"count={vm.Items.Count}");
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
