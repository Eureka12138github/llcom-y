using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CrashReporterDotNET;
using Microsoft.Extensions.DependencyInjection;

namespace llcom
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            //组合根负责创建主窗口：先完成配置加载与文件结构初始化（必须早于 MainViewModel 构造，
            //否则其内部的 LuaEditorViewModel 会扫描尚未创建的 user_script_run 目录而抛异常），
            //再从容器解析 MainWindow（MainViewModel 经构造注入）。
            Tools.Global.LoadSetting();
            Tools.Global.Initial();
            var window = Tools.AppHost.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
#if DEBUG
#else
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            Application.Current.DispatcherUnhandledException += DispatcherOnUnhandledException;
#endif
        }

        /// <summary>
        /// 正在处理致命异常（防止弹窗期间再次抛错导致“关一个又开一个”的递归弹窗）。
        /// </summary>
        private static bool _isHandlingFatal;

        /// <summary>
        /// UI 线程未处理异常：标记为已处理（不让进程被直接终止），交给统一处理。
        /// </summary>
        private void DispatcherOnUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs dispatcherUnhandledExceptionEventArgs
        )
        {
            //关键：标记已处理，否则异常会继续冒泡 / 在嵌套消息循环里被反复触发
            dispatcherUnhandledExceptionEventArgs.Handled = true;

            if (_isHandlingFatal)
            {
                //弹窗期间又抛异常：只记录，不再弹窗
                LogFatal(dispatcherUnhandledExceptionEventArgs.Exception);
                return;
            }
            _isHandlingFatal = true;
            try
            {
                HandleFatal(dispatcherUnhandledExceptionEventArgs.Exception);
            }
            finally
            {
                _isHandlingFatal = false;
            }
        }

        /// <summary>
        /// 非 UI 线程未处理异常：进程即将退出，无法继续，只能提示后退出。
        /// </summary>
        private static void CurrentDomainOnUnhandledException(
            object sender,
            UnhandledExceptionEventArgs unhandledExceptionEventArgs
        )
        {
            var exception = unhandledExceptionEventArgs.ExceptionObject as Exception;
            LogFatal(exception);
            if (_isHandlingFatal)
                return;
            _isHandlingFatal = true;
            try
            {
                HandleFatal(exception);
            }
            finally
            {
                _isHandlingFatal = false;
            }
        }

        /// <summary>
        /// 统一的致命错误处理：记录日志 → 弹出“只能关闭程序”的窗口 → 尽力上报 → 保证退出。
        /// </summary>
        private static void HandleFatal(Exception exception)
        {
            var logPath = LogFatal(exception);

            try
            {
                var dialog = new CrashWindow(exception, logPath);
                var owner = Application.Current?.MainWindow;
                if (owner != null && !ReferenceEquals(owner, dialog))
                    dialog.Owner = owner;
                dialog.ShowDialog();
            }
            catch
            {
                //弹窗失败也要能退出
            }

            TryUploadReport(exception);
            ExitApplication();
        }

        /// <summary>
        /// 保证程序一定能退出：先正常 Shutdown，再以 Environment.Exit 兜底，
        /// 避免出现“关不掉、只能进任务管理器强杀”的情况。
        /// </summary>
        private static void ExitApplication()
        {
            try
            {
                Tools.Global.isMainWindowsClosed = true; //允许各窗口正常关闭
            }
            catch { }
            try
            {
                Application.Current?.Shutdown();
            }
            catch { }
            try
            {
                Environment.Exit(0); //兜底：确保进程真正结束
            }
            catch { }
        }

        /// <summary>
        /// 把致命异常写入 logs/fatal.log（上报可能失败，本地留痕更可靠），返回日志文件路径。
        /// </summary>
        private static string LogFatal(Exception exception)
        {
            try
            {
                string dir;
                try
                {
                    dir = Tools.Global.GetTrueProfilePath() + "logs";
                }
                catch
                {
                    dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "llcom");
                }
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "fatal.log");
                System.IO.File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n\r\n"
                );
                return path;
            }
            catch
            {
                //记录失败也不能影响后续流程
                return "";
            }
        }

        /// <summary>
        /// 尽力上报崩溃信息（静默，不再弹出额外窗口，失败也不影响退出）。
        /// </summary>
        private static void TryUploadReport(Exception exception)
        {
            try
            {
                if (exception == null || !Tools.Global.ReportBug || Tools.Global.HasNewVersion)
                    return;
                var reportCrash = new ReportCrash("lolicon@papapoi.com")
                {
                    CaptureScreen = true,
                    Silent = true,
                };
                //后台发送，避免网络慢时卡住界面
                Task.Run(() =>
                {
                    try
                    {
                        reportCrash.Send(exception);
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
