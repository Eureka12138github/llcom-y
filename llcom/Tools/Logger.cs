using System;
using System.IO;
using System.Text;
using System.Windows.Media;
using Serilog;

namespace llcom.Tools
{
    class Logger
    {
        //显示日志数据的回调函数
        public static event EventHandler<DataShow>? DataShowTask;

        //清空显示的回调函数
        public static event EventHandler? DataClearEvent;

        //清空日志显示
        public static void ClearData()
        {
            DataClearEvent?.Invoke(null, EventArgs.Empty);
        }

        //显示日志数据
        public static void ShowData(byte[] data, bool send)
        {
            //不刷新日志
            if (Tools.Global.setting.DisableLog)
                return;
            DataShowTask?.Invoke(null, new DataShowPara { data = data, send = send });
        }

        //显示日志数据
        public static void ShowDataRaw(DataShowRaw s)
        {
            //不刷新日志
            if (Tools.Global.setting.DisableLog)
                return;
            DataShowTask?.Invoke(null, s);
        }

        private static Serilog.Core.Logger? luaLogFile = null;

        /// <summary>
        /// 一次导出日志的目标路径：`logs/logyyyyMMdd_HHmmss.txt`。
        /// 主日志只在用户点击导出/保存时写盘，平时不创建文件；
        /// 同一秒重复导出时自动追加 `_2`、`_3`，避免覆盖已有快照。
        /// </summary>
        public static string GetExportLogPath(DateTime now)
        {
            var dir = Path.Combine(Tools.Global.ProfilePath, "logs");
            Directory.CreateDirectory(dir);

            var baseName = "log" + now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(dir, baseName + ".txt");
            for (var i = 2; File.Exists(path); i++)
                path = Path.Combine(dir, baseName + "_" + i + ".txt");
            return path;
        }

        /// <summary>
        /// 记录一处被吞掉的异常（仅调试输出）。把"静默 catch"变为可追踪：
        /// 不改变容错行为，但能在调试器/调试输出中看到。
        /// 主日志改为"导出时才写入"，这里不再落盘。
        /// </summary>
        /// <param name="context">发生位置/操作说明</param>
        /// <param name="ex">被吞掉的异常</param>
        public static void Swallowed(string context, Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[swallowed] {context}: {ex?.GetType().Name} {ex?.Message}"
                );
            }
            catch
            {
                // 记录日志本身失败时，绝不能再抛出
            }
        }

        /// <summary>
        /// 初始化lua日志文件
        /// </summary>
        public static void InitLuaLog()
        {
            luaLogFile = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    Tools.Global.ProfilePath + "user_script_run/logs/log.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    encoding: Encoding.UTF8,
                    rollOnFileSizeLimit: true
                )
                .CreateLogger();
        }

        public static void CloseLuaLog()
        {
            if (luaLogFile == null)
                return;
            luaLogFile.Dispose();
            luaLogFile = null;
        }

        /// <summary>
        /// 写入一条lua日志
        /// </summary>
        /// <param name="l"></param>
        public static void AddLuaLog(string l)
        {
            if (luaLogFile == null)
                InitLuaLog();
            luaLogFile?.Information(l);
        }
    }

    //整个父类统一下
    class DataShow
    {
        public DateTime time { get; set; } = DateTime.Now;
        public byte[] data = Array.Empty<byte>();
    }

    /// <summary>
    /// 显示到日志显示页面的类
    /// </summary>
    class DataShowPara : DataShow
    {
        public bool send;
    }

    /// <summary>
    /// 更通用的日志数据
    /// </summary>
    class DataShowRaw : DataShow
    {
        public string title = "";
        public SolidColorBrush color = Brushes.Transparent;
    }
}
