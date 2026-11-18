using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace llcom.LuaEnv
{
    class LuaRunEnv
    {
        public static event EventHandler? LuaRunError; //报错的回调
        private static XLua.LuaEnv? lua = null;
        private static CancellationTokenSource? tokenSource = null;
        private static ConcurrentDictionary<int, CancellationTokenSource> pool =
            new ConcurrentDictionary<int, CancellationTokenSource>(); //timer回调池子
        private static ConcurrentBag<LuaPool> toRun = new ConcurrentBag<LuaPool>(); //待运行的池子
        private static XLua.LuaFunction? triggerCB = null;

        public static bool isRunning = false;

        //当前运行的脚本名（用于托盘提示文案）
        private static string currentScript = "";

        //本次运行是否已经发过“脚本出错”提示（避免回调连续报错时刷屏）
        private static bool errorNotified;

        //用事件替代轮询忙等：UI 准备好后 Set，New() 里 Wait（可被取消）
        private static readonly ManualResetEventSlim canRunEvent = new ManualResetEventSlim(false);

        /// <summary>Lua 运行环境是否已准备好（由 UI 设置）</summary>
        public static bool canRun
        {
            get => canRunEvent.IsSet;
            set
            {
                if (value)
                    canRunEvent.Set();
                else
                    canRunEvent.Reset();
            }
        }

        private static void addTigger(int id, string type = "timer", byte[]? data = null)
        {
            if (isRunning)
            {
                toRun.Add(
                    new LuaPool
                    {
                        id = id,
                        type = type,
                        data = data,
                    }
                );
                runTigger();
            }
        }

        /// <summary>
        /// 实时跑一段lua代码
        /// </summary>
        /// <param name="l"></param>
        public static void RunCommand(string l)
        {
            addTigger(-1, "cmd", Encoding.UTF8.GetBytes(l));
        }

        /// <summary>
        /// 收到通用通道消息
        /// </summary>
        public static void ChannelReceived(string channel, object data)
        {
            if (isRunning)
            {
                toRun.Add(
                    new LuaPool
                    {
                        id = -1,
                        type = channel,
                        data = data,
                    }
                );
                runTigger();
            }
        }

        private static void runTigger()
        {
            if (!canRun || lua == null || tokenSource == null)
                return;
            lock (lua)
            {
                try
                {
                    while (toRun.Count > 0)
                    {
                        if (tokenSource.IsCancellationRequested)
                            return;
                        while (toRun.Count > 0)
                        {
                            try
                            {
                                LuaPool? temp;
                                toRun.TryTake(out temp);
                                if (temp == null)
                                    continue;
                                triggerCB?.Call(temp.id, temp.type, temp.data);
                            }
                            catch (Exception le)
                            {
                                LuaApis.PrintLog("回调报错：\r\n" + le.ToString());
                                // 正常情况下 Lua 层已用 pcall 吞掉错误并通过 apiScriptError 上报，
                                // 这里兜底处理 C# 绑定层抛出的异常；同一次运行只提示一次。
                                ReportScriptError(le.ToString());
                            }
                            if (tokenSource.IsCancellationRequested)
                                return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopLua(ex.ToString());
                }
            }
        }

        /// <summary>
        /// 新建定时器
        /// </summary>
        /// <param name="id">编号</param>
        /// <param name="time">时间(ms)</param>
        public static int StartTimer(int id, int time)
        {
            CancellationTokenSource timerToken = new CancellationTokenSource();
            if (pool.ContainsKey(id))
            {
                try
                {
                    CancellationTokenSource tc;
                    pool.TryRemove(id, out tc);
                    tc.Cancel();
                }
                catch { /* 忽略：取消定时器失败 */ }
            }
            pool.TryAdd(id, timerToken);
            var timer = new System.Timers.Timer(time);
            timer.Elapsed += (sender, e) =>
            {
                if (timerToken == null || timerToken.IsCancellationRequested)
                    return;
                if (!isRunning)
                    return;
                pool.TryRemove(id, out _);
                addTigger(id);
                ((System.Timers.Timer)sender).Dispose();
            };
            timer.AutoReset = false;
            timer.Start();
            return 1;
        }

        /// <summary>
        /// 停止定时器
        /// </summary>
        /// <param name="id">编号</param>
        public static void StopTimer(int id)
        {
            if (pool.ContainsKey(id))
            {
                try
                {
                    CancellationTokenSource tc;
                    pool.TryRemove(id, out tc);
                    tc.Cancel();
                }
                catch { /* 忽略：取消定时器失败 */ }
            }
        }

        /// <summary>
        /// 脚本报错时弹托盘气泡（窗口最小化/被遮挡时也能看到）。
        /// </summary>
        private static void NotifyScriptError(string ex)
        {
            var name = string.IsNullOrEmpty(currentScript) ? "" : $"[{currentScript}] ";

            var text = (ex ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            // sys.lua 会在错误信息末尾追加 ",hex:..." 调试串，提示里不需要
            var hexIndex = text.IndexOf(",hex:", StringComparison.Ordinal);
            if (hexIndex >= 0)
                text = text.Substring(0, hexIndex);
            // 去掉 “core_script/sys.lua:296: 路径/脚本.lua:84:” 这类前缀，只保留人类可读的错误内容
            // （脚本路径在 Lua 侧是 GBK 字节，直接显示会是乱码；完整信息仍在 Lua 日志里）
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^.*\.lua:\d+:\s*",
                ""
            );
            if (text.Length > 180)
                text = text.Substring(0, 177) + "...";

            llcom.Services.NotificationService.NotifyLocalized(
                "ScriptNotifyErrorTitle",
                name + text,
                isError: true
            );
        }

        /// <summary>
        /// 供 Lua 层（head.lua 的 tiggerCB）上报回调错误：同一次运行只提示一次。
        /// </summary>
        public static void ReportScriptError(string? message)
        {
            if (errorNotified)
                return;

            errorNotified = true;
            NotifyScriptError(message ?? "");
        }

        /// <summary>
        /// 停止运行lua
        /// </summary>
        public static void StopLua(string ex)
        {
            LuaRunError?.Invoke(null, EventArgs.Empty);
            if (ex != "")
            {
                LuaApis.PrintLog("lua代码报错了：\r\n" + ex);
                ReportScriptError(ex);
            }
            else
                LuaApis.PrintLog("lua代码已停止");
            foreach (var v in pool)
            {
                v.Value.Cancel();
            }
            isRunning = false;
            tokenSource?.Cancel();
            pool.Clear();
            lua = null;
        }

        /// <summary>
        /// 新建一个新的lua虚拟机
        /// </summary>
        public static void New(string file)
        {
            canRun = false;
            isRunning = true;
            currentScript = Path.GetFileNameWithoutExtension(file);
            errorNotified = false;
            if (tokenSource != null)
                tokenSource.Dispose();
            tokenSource = new CancellationTokenSource(); //task取消指示

            //文件不存在
            if (!File.Exists(Tools.Global.ProfilePath + file))
                return;
            var token = tokenSource.Token;
            Task.Run(
                () =>
                {
                    try
                    {
                        canRunEvent.Wait(token); //等待 UI 准备好，可被取消
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    try
                    {
                        lua = new XLua.LuaEnv();
                        lock (lua)
                        {
                            lua.Global.SetInPath("runType", "script"); //一次性处理标志
                            LuaLoader.Initial(lua);
                            triggerCB = lua.Global.Get<XLua.LuaFunction>("tiggerCB");
                            lua.DoString(
                                $"require '{file.Replace("/", ".").Substring(0, file.Length - 4)}'"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        StopLua(ex.ToString());
                    }
                },
                token
            );
        }
    }

    class LuaPool
    {
        public int id { get; set; }
        public string type { get; set; } = "";
        public object? data { get; set; }
    }
}
