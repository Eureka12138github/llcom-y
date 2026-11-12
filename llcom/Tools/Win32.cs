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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;

    /// <summary>WM_HOTKEY 消息号。</summary>
    internal const int WmHotKey = 0x0312;

    /// <summary>注册 Alt+M：窗口最小化后仍能收到按键，用于恢复窗口。</summary>
    internal static bool RegisterAltM(IntPtr hWnd, int id) =>
        RegisterHotKey(hWnd, id, 0x0001 /* MOD_ALT */, 0x4D /* VK_M */);

    internal static void UnregisterMinimizeHotKey(IntPtr hWnd, int id) =>
        UnregisterHotKey(hWnd, id);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_FRAME = 0x0400;
    private const uint WM_NCACTIVATE = 0x0086;

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

        // Win10 部分版本不会立即重绘“活动窗口”的标题栏，要等窗口失焦/聚焦才更新。
        // 这里主动刷新非客户区，并模拟一次失活/激活，让标题栏立刻跟随主题。
        SetWindowPos(
            hWnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
        );
        RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_FRAME | RDW_UPDATENOW);
        SendMessage(hWnd, WM_NCACTIVATE, IntPtr.Zero, IntPtr.Zero);
        SendMessage(hWnd, WM_NCACTIVATE, (IntPtr)1, IntPtr.Zero);
    }
}
