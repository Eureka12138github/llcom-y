using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace llcom.Tools;

class Win32
{
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;

    // 隐藏窗口右上角按钮
    internal static bool HideControlBox(IntPtr hWnd)
    {
        return SetWindowLong(hWnd, GWL_STYLE, GetWindowLong(hWnd, GWL_STYLE) & ~WS_SYSMENU) > 0;
    }

    /// <summary>
    /// 当前前台窗口是否属于本进程（本程序的对话框/文件选择框等）。
    /// 用于“点击程序外部自动最小化”时避免误伤自己的弹窗。
    /// </summary>
    internal static bool IsForegroundWindowInCurrentProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Process.GetCurrentProcess().Id;
    }
}
