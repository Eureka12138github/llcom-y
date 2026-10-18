using System;
using System.Windows.Threading;
using AutoUpdaterDotNET;

namespace llcom.Services;

/// <summary>
/// 自动更新：检查本项目的 GitHub Releases。
/// 更新清单由 CI 在每次 Release 中附带（autoUpdate.xml），这个地址始终指向最新版本；
/// zip 更新会解压到程序所在目录。热更新（远程执行 Lua）已停用。
/// </summary>
internal static class AppUpdateService
{
    /// <summary>始终指向“最新 Release”的更新清单。</summary>
    private const string AppCastUrl =
        "https://github.com/Eureka12138github/llcom-y/releases/latest/download/autoUpdate.xml";

    /// <summary>
    /// 检查更新。
    /// </summary>
    /// <param name="dispatcher">UI 线程调度器</param>
    /// <param name="manual">true = 用户手动点击（无更新/失败会提示）；false = 启动时检查（静默）</param>
    public static void CheckForUpdate(Dispatcher dispatcher, bool manual = false)
    {
        if (!manual && !Tools.Global.setting.autoUpdate)
            return;

        // 便携版：zip 解压到 exe 所在目录
        AutoUpdater.InstallationPath = AppDomain.CurrentDomain.BaseDirectory;

        AutoUpdater.CheckForUpdateEventHandler handler = null;
        handler = args =>
        {
            AutoUpdater.CheckForUpdateEvent -= handler;

            if (args == null)
            {
                Tools.Logger.Swallowed("update", new Exception("check update failed"));
                if (manual)
                    dispatcher.Invoke(() =>
                        Tools.MessageBox.Show("检查更新失败，请检查网络或稍后再试。")
                    );
                return;
            }

            if (args.IsUpdateAvailable)
            {
                Tools.Global.HasNewVersion = true;
                dispatcher.Invoke(() => AutoUpdater.ShowUpdateForm(args));
            }
            else if (manual)
            {
                dispatcher.Invoke(() => Tools.MessageBox.Show("已是最新版。"));
            }
        };

        AutoUpdater.CheckForUpdateEvent += handler;
        try
        {
            AutoUpdater.Start(AppCastUrl);
        }
        catch (Exception ex)
        {
            AutoUpdater.CheckForUpdateEvent -= handler;
            Tools.Logger.Swallowed("update", ex);
            if (manual)
                dispatcher.Invoke(() => Tools.MessageBox.Show("检查更新失败：" + ex.Message));
        }
    }

    /// <summary>热更已停用（不再从任何服务器下载并执行 Lua）。</summary>
    public static void RunHotfix() { }
}
