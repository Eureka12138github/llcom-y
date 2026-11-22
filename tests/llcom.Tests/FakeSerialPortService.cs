using System;
using System.IO.Ports;
using System.Threading;
using llcom.Services;

namespace llcom.Tests
{
    /// <summary>串口服务的空实现（DataShowViewModel 只用到 RTS/DTR）。</summary>
    internal sealed class FakeSerialPortService : ISerialPortService
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

        public event EventHandler UartStateChanged
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
}