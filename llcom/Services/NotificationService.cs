using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace llcom.Services;

/// <summary>一条脚本提示（用于托盘「最近提示」和状态栏留痕）。</summary>
public sealed class Notice
{
    public Notice(DateTime time, string title, string message, bool isError)
    {
        Time = time;
        Title = title ?? "";
        Message = message ?? "";
        IsError = isError;
    }

    /// <summary>事件发生时间（如脚本调用 apiNotify 的时刻），不是气泡弹出时间。</summary>
    public DateTime Time { get; }

    public string Title { get; }

    public string Message { get; }

    public bool IsError { get; }
}

/// <summary>
/// 系统托盘提示：
/// - 弹气泡（NotifyIcon.ShowBalloonTip，样式由系统决定）；
/// - 气泡消失后保留「未读角标」（完成=绿色、报错=红色），并在窗口非激活时闪烁 30 秒；
/// - 鼠标悬浮/点击托盘图标停止闪烁，角标保留到用户回到主窗口或点开提示；
/// - 托盘菜单和主窗口状态栏保留最近几条提示，避免错过。
/// </summary>
public static class NotificationService
{
    private const int MaxRecentNotices = 5;
    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FlashInterval = TimeSpan.FromMilliseconds(500);

    private static WinForms.NotifyIcon? _icon;
    private static Window? _owner;
    private static bool _started;

    private static Icon? _baseIcon;
    private static Icon? _okIcon;
    private static Icon? _errorIcon;

    private static DispatcherTimer? _flashTimer;
    private static bool _flashShowBadge;
    private static DateTime _flashDeadline;

    private static bool _hasUnread;
    private static bool _unreadIsError;

    private static readonly List<Notice> _recent = new List<Notice>();
    private static WinForms.ToolStripMenuItem? _recentMenu;

    /// <summary>收到新提示（UI 线程）。</summary>
    public static event Action<Notice>? NoticeAdded;

    /// <summary>未读状态变化（UI 线程）。</summary>
    public static event Action? UnreadChanged;

    /// <summary>最近提示（最新的在前）。</summary>
    public static IReadOnlyList<Notice> Recent => _recent;

    public static bool HasUnread => _hasUnread;

    /// <summary>
    /// 主窗口加载完成后调用：按当前设置决定是否常驻托盘图标。
    /// </summary>
    public static void Initialize(Window owner)
    {
        _owner = owner;
        _started = true;
        ApplyEnabled(Tools.Global.setting.scriptNotifyEnabled);
    }

    /// <summary>
    /// 跟随「脚本结束提示」设置创建或释放托盘图标。
    /// </summary>
    public static void ApplyEnabled(bool enabled)
    {
        if (!_started)
            return;

        if (!enabled)
        {
            MarkAllRead(silent: true);
            DisposeIcon();
            return;
        }

        EnsureIcon();
    }

    /// <summary>跟随「托盘图标闪烁」设置：关闭时立即停止闪烁。</summary>
    public static void ApplyFlashSetting()
    {
        if (!Tools.Global.setting.scriptNotifyFlash)
            StopFlashing();
    }

    /// <summary>
    /// 弹托盘气泡提示并设置未读角标；关闭提示开关时直接忽略。
    /// 可从任意线程调用（Lua 脚本在后台线程执行）。
    /// </summary>
    /// <param name="title">标题，留空显示 LLCOM-Y</param>
    /// <param name="message">内容，过长会自动截断</param>
    /// <param name="isError">true = 红色（脚本报错），false = 绿色（正常完成）</param>
    /// <param name="eventTime">
    /// 事件发生时间（如脚本调用 apiNotify 的那一刻）。留空则在当前调用线程立即取当前时间。
    /// 提示记录的时间是「事件时间」，不是气泡真正弹出的时间（后者受 UI 队列影响会晚一点）。
    /// </param>
    public static void Notify(
        string? title,
        string? message,
        bool isError = false,
        DateTime? eventTime = null
    )
    {
        if (!_started || !Tools.Global.setting.scriptNotifyEnabled)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        if (!dispatcher.CheckAccess())
        {
            //事件时间在调用线程（脚本线程）立即取，而不是等 UI 线程执行时才取：
            //这样记录的是“脚本真正调用 apiNotify 的时刻”，而不是排队后的显示时刻。
            var stamp = eventTime ?? DateTime.Now;

            //用 Background 优先级：与主日志/Lua 日志的渲染任务同一个队列、先来先处理，
            //保证「界面上的日志先追平、完成提示再弹出」。用默认的 Normal 会插队到
            //积压的日志渲染任务前面，看起来就是日志还停在旧进度、提示已经弹出。
            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => Notify(title, message, isError, stamp))
            );
            return;
        }

        //走到这里一定已有事件时间（跨线程时已在调用线程取好）
        var noticeTime = eventTime ?? DateTime.Now;

        EnsureIcon();
        if (_icon == null)
            return;

        var displayTitle = string.IsNullOrWhiteSpace(title) ? "LLCOM-Y" : title!.Trim();
        var notice = new Notice(noticeTime, displayTitle, message ?? "", isError);

        _recent.Insert(0, notice);
        while (_recent.Count > MaxRecentNotices)
            _recent.RemoveAt(_recent.Count - 1);

        // 系统气泡（样式不可自定义，但可靠性最好）
        var tipText = notice.Message;
        if (tipText.Length > 240)
            tipText = tipText.Substring(0, 237) + "...";
        _icon.BalloonTipTitle = displayTitle;
        _icon.BalloonTipText = tipText;
        _icon.BalloonTipIcon = isError
            ? WinForms.ToolTipIcon.Error
            : WinForms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(6000);

        NoticeAdded?.Invoke(notice);

        // 窗口在前台时用户能直接看到，不再设置未读/闪烁
        var ownerActive =
            _owner != null
            && _owner.IsActive
            && _owner.WindowState != WindowState.Minimized;
        if (!ownerActive)
            SetUnread(isError);
        else
            UpdateTooltip();
    }

    /// <summary>
    /// 用本地化资源作为标题弹出气泡（资源查找在 UI 线程完成）。
    /// </summary>
    /// <param name="eventTime">事件发生时间；留空则在当前调用线程立即取当前时间。</param>
    public static void NotifyLocalized(
        string titleResourceKey,
        string? message,
        bool isError = false,
        DateTime? eventTime = null
    )
    {
        if (!_started || !Tools.Global.setting.scriptNotifyEnabled)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        if (!dispatcher.CheckAccess())
        {
            //同上：事件时间在调用线程先取，避免排队后的显示时刻被当成发生时间
            var stamp = eventTime ?? DateTime.Now;

            //与日志渲染任务同队列，避免提示抢在积压日志之前显示
            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => NotifyLocalized(titleResourceKey, message, isError, stamp))
            );
            return;
        }

        Notify(TryFindResource(titleResourceKey), message, isError, eventTime);
    }

    /// <summary>
    /// 清除未读角标（回到主窗口、点击托盘图标或气泡时调用）。
    /// </summary>
    public static void MarkAllRead(bool silent = false)
    {
        StopFlashing();
        if (!_hasUnread)
        {
            UpdateIcon();
            return;
        }

        _hasUnread = false;
        UpdateIcon();
        UpdateTooltip();
        if (!silent)
            UnreadChanged?.Invoke();
    }

    /// <summary>
    /// 主窗口关闭时释放托盘图标，避免残留“幽灵图标”。
    /// </summary>
    public static void Shutdown()
    {
        StopFlashing();
        DisposeIcon();
        _started = false;
        _owner = null;
        _recent.Clear();
        _hasUnread = false;

        _baseIcon?.Dispose();
        _okIcon?.Dispose();
        _errorIcon?.Dispose();
        _baseIcon = null;
        _okIcon = null;
        _errorIcon = null;
    }

    private static void SetUnread(bool isError)
    {
        _hasUnread = true;
        _unreadIsError = isError;
        UpdateIcon();
        UpdateTooltip();
        UnreadChanged?.Invoke();

        if (Tools.Global.setting.scriptNotifyFlash)
            StartFlashing();
        else
            StopFlashing();
    }

    private static void StartFlashing()
    {
        if (_icon == null)
            return;

        _flashDeadline = DateTime.Now + FlashDuration;
        _flashShowBadge = true;

        if (_flashTimer == null)
        {
            _flashTimer = new DispatcherTimer { Interval = FlashInterval };
            _flashTimer.Tick += (_, _) => OnFlashTick();
        }
        _flashTimer.Start();
    }

    private static void OnFlashTick()
    {
        if (DateTime.Now >= _flashDeadline)
        {
            StopFlashing();
            return;
        }

        _flashShowBadge = !_flashShowBadge;
        SetIcon(_flashShowBadge ? GetBadgeIcon() : _baseIcon);
    }

    /// <summary>停止闪烁；未读角标保留（如果有未读）。</summary>
    private static void StopFlashing()
    {
        if (_flashTimer == null || !_flashTimer.IsEnabled)
            return;

        _flashTimer.Stop();
        UpdateIcon();
    }

    private static void EnsureIcon()
    {
        if (_icon != null)
            return;

        try
        {
            _baseIcon ??= LoadAppIcon();
            _okIcon ??= CreateBadgedIcon(_baseIcon, Color.FromArgb(34, 197, 94)); // 绿色：完成
            _errorIcon ??= CreateBadgedIcon(_baseIcon, Color.FromArgb(239, 68, 68)); // 红色：报错

            var menu = new WinForms.ContextMenuStrip { ShowItemToolTips = true };

            _recentMenu = new WinForms.ToolStripMenuItem(
                TryFindResource("TrayRecentNotices") ?? "Recent notifications"
            );
            _recentMenu.DropDownOpening += (_, _) => RebuildRecentMenu();
            menu.Items.Add(_recentMenu);

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(
                TryFindResource("TrayShowWindow") ?? "Show main window",
                null,
                (_, _) => OpenFromTray()
            );
            menu.Items.Add(
                TryFindResource("TrayExit") ?? "Exit",
                null,
                (_, _) => Application.Current?.Shutdown()
            );

            var icon = new WinForms.NotifyIcon
            {
                Icon = _baseIcon,
                Text = "LLCOM-Y",
                Visible = true,
                ContextMenuStrip = menu,
            };
            icon.MouseMove += (_, _) => StopFlashing(); // 悬浮停止闪烁，保留角标
            icon.MouseClick += (_, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left)
                    OpenFromTray();
            };
            icon.BalloonTipClicked += (_, _) => OpenFromTray();
            _icon = icon;
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("tray icon", ex);
        }
    }

    private static void RebuildRecentMenu()
    {
        if (_recentMenu == null)
            return;

        var oldItems = new List<WinForms.ToolStripItem>();
        foreach (WinForms.ToolStripItem item in _recentMenu.DropDownItems)
        {
            oldItems.Add(item);
        }
        _recentMenu.DropDownItems.Clear();
        foreach (var item in oldItems)
            item.Dispose();

        if (_recent.Count == 0)
        {
            _recentMenu.DropDownItems.Add(
                new WinForms.ToolStripMenuItem(
                    TryFindResource("TrayNoNotices") ?? "(none)"
                )
                {
                    Enabled = false,
                }
            );
            return;
        }

        foreach (var notice in _recent)
        {
            var text = $"{notice.Time:HH:mm:ss}  {notice.Title}";
            var item = new WinForms.ToolStripMenuItem(text);
            item.ToolTipText = string.IsNullOrWhiteSpace(notice.Message)
                ? notice.Title
                : notice.Message;
            if (notice.IsError)
                item.ForeColor = Color.FromArgb(239, 68, 68);
            item.Click += (_, _) => OpenFromTray();
            _recentMenu.DropDownItems.Add(item);
        }
    }

    private static void OpenFromTray()
    {
        ShowMainWindow();
        MarkAllRead();
    }

    private static void ShowMainWindow()
    {
        var owner = _owner;
        if (owner == null)
            return;

        owner.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (owner.WindowState == WindowState.Minimized)
                    owner.WindowState = WindowState.Normal;
                owner.Show();
                owner.Activate();
            })
        );
    }

    private static void UpdateIcon() =>
        SetIcon(_hasUnread ? GetBadgeIcon() : _baseIcon);

    private static void SetIcon(Icon? icon)
    {
        if (_icon == null || icon == null)
            return;

        try
        {
            _icon.Icon = icon;
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("tray icon update", ex);
        }
    }

    private static Icon? GetBadgeIcon() => _unreadIsError ? _errorIcon : _okIcon;

    private static void UpdateTooltip()
    {
        if (_icon == null)
            return;

        try
        {
            var text = "LLCOM-Y";
            if (_hasUnread && _recent.Count > 0)
            {
                var last = _recent[0];
                text = $"LLCOM-Y · {last.Time:HH:mm} {last.Title}";
                if (text.Length > 63)
                    text = text.Substring(0, 63);
            }
            _icon.Text = text;
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("tray tooltip", ex);
        }
    }

    private static string? TryFindResource(string key) =>
        Application.Current?.TryFindResource(key) as string;

    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null)
                    return icon;
            }
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("tray icon image", ex);
        }

        return SystemIcons.Application;
    }

    /// <summary>在原始图标右下角画一个带白边的彩色圆点，作为未读角标。</summary>
    private static Icon CreateBadgedIcon(Icon baseIcon, Color color)
    {
        using (var bitmap = baseIcon.ToBitmap())
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var size = Math.Max(6, bitmap.Width / 2);
            var rect = new Rectangle(
                bitmap.Width - size - 1,
                bitmap.Height - size - 1,
                size,
                size
            );

            using (var border = new SolidBrush(Color.White))
                graphics.FillEllipse(border, Rectangle.Inflate(rect, 1, 1));
            using (var brush = new SolidBrush(color))
                graphics.FillEllipse(brush, rect);

            var handle = bitmap.GetHicon();
            try
            {
                using (var temp = Icon.FromHandle(handle))
                    return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
    }

    private static void DisposeIcon()
    {
        StopFlashing();

        if (_icon == null)
            return;

        try
        {
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("tray icon dispose", ex);
        }
        finally
        {
            _icon = null;
            _recentMenu = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}