using System;
using System.Collections.Generic;
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;

    // 隐藏窗口右上角按钮
    internal static bool HideControlBox(IntPtr hWnd)
    {
        return SetWindowLong(hWnd, GWL_STYLE, GetWindowLong(hWnd, GWL_STYLE) & ~WS_SYSMENU) > 0;
    }

    /// <summary>让原生标题栏使用深色（Windows 10 1809+；失败时忽略）。</summary>
    internal static void SetDarkTitleBar(IntPtr hWnd, bool dark)
    {
        if (hWnd == IntPtr.Zero)
            return;

        int value = dark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1+), 19 = 旧版
        DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int));
        DwmSetWindowAttribute(hWnd, 19, ref value, sizeof(int));
    }
}
