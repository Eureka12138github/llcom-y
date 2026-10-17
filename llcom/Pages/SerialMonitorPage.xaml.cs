using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace llcom.Pages;

/// <summary>
/// SerialMonitorPage.xaml 的交互逻辑
/// </summary>
public partial class SerialMonitorPage : Page
{
    public delegate int CallbackDelegate(IntPtr param);

    [DllImport("serial_monitor.dll")]
    static extern bool UnMonitorComm();

    [DllImport("serial_monitor.dll")]
    static extern bool MonitorComm(uint Pid, uint ComIndex, CallbackDelegate lpCallFunc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>当前监听的 COM 端口号（回调过滤用；monitor DLL 会转发目标进程的所有串口）</summary>
    private volatile int _selectedCom;

    /// <summary>
    /// 事件类型，对应CommState
    /// </summary>
    enum State
    {
        Disconnect = 2,
        Receive,
        Send,
    }

    private readonly CallbackDelegate myDelegate;

    public SerialMonitorPage()
    {
        InitializeComponent();
        myDelegate = new CallbackDelegate(OnMonitorData);
    }

    /// <summary>监听回调（由 monitor DLL 的工作线程调用）：把一包数据写入日志。</summary>
    private int OnMonitorData(IntPtr e)
    {
        Udata d = Marshal.PtrToStructure<Udata>(e);
        //只显示当前选择的那一个串口（DLL 会转发目标进程的所有串口）
        if (d.ComPort != _selectedCom)
            return 1;
        byte[] b = new byte[d.DataSize];
        for (int i = 0; i < d.DataSize; i++)
            b[i] = d.Data[i];
        var c = Brushes.Black;
        string show = "unknow";
        switch (d.CommState)
        {
            case (byte)State.Send:
                show = "→";
                c = Brushes.DarkRed;
                break;
            case (byte)State.Receive:
                show = "←";
                c = Brushes.DarkGreen;
                break;
            case (byte)State.Disconnect:
                show = "❌";
                break;
            default:
                break;
        }
        Tools.Logger.ShowDataRaw(
            new Tools.DataShowRaw
            {
                title = $"monitor COM{d.ComPort} {show}",
                data = b,
                color = c,
            }
        );
        return 1;
    }

    bool first = true;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (first)
        {
            Refresh();
            first = false;
            try
            {
                UnMonitorComm();
            }
            catch (Exception ex)
            {
                MonitorButton.IsEnabled = false;
                Tools.Logger.Swallowed("serial monitor", ex);
                Tools.MessageBox.Show(
                    string.Format(
                        TryFindResource("SerialMonitorLoadFailed") as string
                            ?? "串口监听插件加载失败：{0}",
                        ex.Message
                    )
                );
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Udata
    {
        public byte ComPort;
        public byte CommState;
        public int FileHandle;
        public int DataSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8192)]
        public byte[] Data;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private bool connected = false;

    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!connected)
        {
            if (PidComboBox.SelectedItem == null || SerialPortComboBox.SelectedItem == null)
            {
                Tools.MessageBox.Show(
                    TryFindResource("SerialMonitorSelectHint") as string
                        ?? "请先选择要监听的进程和串口。"
                );
                return;
            }

            var start = PidComboBox.Text.IndexOf("[");
            var pid = uint.Parse(
                PidComboBox.Text.Substring(start + 1, PidComboBox.Text.Length - start - 2)
            );
            var com = uint.Parse(SerialPortComboBox.Text.Substring(3));

            //注入要求目标与宿主同位数：不一致时直接提示，避免“显示监听中但没有数据”
            if (!CheckTargetBitness((int)pid))
                return;

            //先记下选择的端口：回调可能早于 MonitorComm 返回就已到达
            _selectedCom = (int)com;
            try
            {
                connected = MonitorComm(pid, com, myDelegate);
            }
            catch (Exception ex)
            {
                _selectedCom = 0;
                MonitorButton.IsEnabled = false;
                Tools.Logger.Swallowed("serial monitor", ex);
                Tools.MessageBox.Show("加载失败：" + ex.Message);
                return;
            }

            if (!connected)
            {
                _selectedCom = 0;
                Tools.MessageBox.Show(
                    TryFindResource("SerialMonitorStartFailed") as string
                        ?? "监听启动失败。"
                );
                return;
            }

            //x64 版（Rust 实现）注入失败时 DLL 仍会返回成功，这里用模块列表做一次校验
            if (Environment.Is64BitProcess && !IsHookLoaded((int)pid))
            {
                UnMonitorComm();
                connected = false;
                _selectedCom = 0;
                Tools.MessageBox.Show(
                    TryFindResource("SerialMonitorInjectFailed") as string
                        ?? "监听启动失败：未能把监听模块注入目标进程。"
                );
                return;
            }
        }
        else
        {
            UnMonitorComm();
            connected = false;
            _selectedCom = 0;
        }
        if (connected)
        {
            RefreshButton.IsEnabled = false;
            PidComboBox.IsEnabled = false;
            SerialPortComboBox.IsEnabled = false;
            MonitorButton.Content = TryFindResource("SerialMonitorStop") as string ?? "?!";
        }
        else
        {
            RefreshButton.IsEnabled = true;
            PidComboBox.IsEnabled = true;
            SerialPortComboBox.IsEnabled = true;
            MonitorButton.Content = TryFindResource("SerialMonitorStart") as string ?? "?!";
        }
    }

    /// <summary>
    /// 目标位数是否与当前进程一致；不一致时提示并返回 false。
    /// （monitor DLL 的 hook 必须与目标进程同位数，否则注入静默失败）
    /// </summary>
    private bool CheckTargetBitness(int pid)
    {
        var targetBits = GetTargetBitness(pid);
        if (targetBits == 0)
            return true; //读不到就交给后续注入校验

        var selfBits = Environment.Is64BitProcess ? 64 : 32;
        if (targetBits == selfBits)
            return true;

        Tools.MessageBox.Show(
            string.Format(
                TryFindResource("SerialMonitorBitnessMismatch") as string
                    ?? "目标进程是 {0} 位程序，当前 LLCOM 是 {1} 位版本，无法监听。",
                targetBits,
                selfBits
            )
        );
        return false;
    }

    /// <summary>取目标进程位数：32 / 64；读不到返回 0。</summary>
    private static int GetTargetBitness(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            return 0;
        try
        {
            if (!IsWow64Process(handle, out var wow64))
                return 0;
            if (wow64)
                return 32;
            return Environment.Is64BitOperatingSystem ? 64 : 32;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>目标进程里是否真的加载了 hook DLL（读不到模块列表时不误判）。</summary>
    private static bool IsHookLoaded(int pid)
    {
        try
        {
            using (var process = Process.GetProcessById(pid))
            {
                foreach (ProcessModule module in process.Modules)
                {
                    if (
                        string.Equals(
                            module.ModuleName,
                            "serial_monitor_hook.dll",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        return true;
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void Refresh()
    {
        string lastP = PidComboBox.Text;
        PidComboBox.Items.Clear();
        var sl = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                sl.Add($"{p.ProcessName}[{p.Id}]");
            }
            catch (System.Exception ex) { Tools.Logger.Swallowed("serial monitor", ex); }
        }
        sl.Sort();
        foreach (var i in sl)
            PidComboBox.Items.Add(i);
        if (PidComboBox.Items.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(lastP) && sl.Contains(lastP))
                PidComboBox.Text = lastP;
            else
                PidComboBox.SelectedIndex = 0;
        }

        lastP = SerialPortComboBox.Text;
        SerialPortComboBox.Items.Clear();
        foreach (var p in SerialPort.GetPortNames())
        {
            if (p.IndexOf("COM") == 0)
                SerialPortComboBox.Items.Add(p);
        }
        if (SerialPortComboBox.Items.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(lastP) && SerialPortComboBox.Items.Contains(lastP))
                SerialPortComboBox.Text = lastP;
            else
                SerialPortComboBox.SelectedIndex = 0;
        }
    }
}
