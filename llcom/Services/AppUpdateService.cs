using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using RestSharp;

namespace llcom.Services;

/// <summary>
/// 应用升级 / 热更（原散落在 <c>MainWindow.Window_Loaded</c> 的两段后台 Task）。
/// 都属“启动时联网做一次”的自包含逻辑，下沉到服务层，View 只负责调用。
/// </summary>
internal static class AppUpdateService
{
    /// <summary>
    /// 自动更新 / 热更总开关。
    /// 个人维护版目前**关闭**：暂未提供自己的更新源，且不应再向原作者服务器
    /// 拉取 hotfix.lua（第三方远程代码）。发布自己的 Release 后，把这里改为
    /// true，并把下面两处 URL 换成自己的更新源。
    /// </summary>
    private static readonly bool Enabled = false;

    /// <summary>检查新版本（AutoUpdater.NET）；有更新且开启自动升级则弹升级窗口。</summary>
    public static void CheckForUpdate(Dispatcher dispatcher)
    {
        if (!Enabled)
            return;

        Task.Run(() =>
        {
            bool runed = false;
            AutoUpdaterDotNET.AutoUpdater.CheckForUpdateEvent += args =>
            {
                if (runed)
                    return;
                runed = true;
                if (args.IsUpdateAvailable)
                {
                    Tools.Global.HasNewVersion = true; // 有新版本
                    if (Tools.Global.setting.autoUpdate) // 开了自动升级功能再开
                    {
                        dispatcher.Invoke(
                            () => AutoUpdaterDotNET.AutoUpdater.ShowUpdateForm(args)
                        );
                    }
                }
            };
            Random r = new Random(); // 加上随机参数，确保获取的是最新数据
            try
            {
                AutoUpdaterDotNET.AutoUpdater.Start(
                    "https://llcom.papapoi.com/autoUpdate.xml?" + r
                );
            }
            catch
            {
                runed = true;
            }
        });
    }

    /// <summary>拉取并执行热更脚本（防止恶性 bug 时远程修复）。个人维护版已关闭。</summary>
    public static void RunHotfix()
    {
        if (!Enabled)
            return;

        Task.Run(() =>
        {
            try
            {
                Random r = new Random(); // 加上随机参数，确保获取的是最新数据
                var client = new RestClient("https://llcom.papapoi.com/hotfix.lua?" + r.Next());
                var request = new RestRequest();
                var response = client.Get(request);
                var lua = new LuaEnv.LuaEnv();
                lua.DoString(response.Content);
            }
            catch (Exception ex)
            {
                Tools.Logger.Swallowed("hotfix", ex);
            }
        });
    }
}