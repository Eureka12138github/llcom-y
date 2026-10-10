using System;
using llcom.Model;
using llcom.Services;
using llcom.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace llcom.Tools;

/// <summary>
/// 组合根：集中注册服务与 ViewModel，避免在 View 里到处 new。
/// 现阶段 <c>Global.setting</c> / <c>Global.uart</c> 仍是既有单例，这里注册为同一实例；
/// 依赖经构造函数注入，调用点逐步不再直接读 <c>Tools.Global.*</c>（DI 渐进迁移）。
/// </summary>
internal static class AppHost
{
    private static IServiceProvider _provider;

    public static IServiceProvider Services => _provider ??= Build();

    private static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // 既有单例（工厂在解析时取值，保证拿到 LoadSetting 之后的实例）
        services.AddSingleton<Settings>(_ => Global.setting);
        services.AddSingleton<ISerialPortService>(_ => Global.uart);

        // ViewModel：依赖从构造函数注入
        services.AddSingleton<QuickSendViewModel>(sp =>
            new QuickSendViewModel(sp.GetRequiredService<Settings>())
        );

        services.AddSingleton<DataShowViewModel>(sp =>
            new DataShowViewModel(
                sp.GetRequiredService<Settings>(),
                sp.GetRequiredService<ISerialPortService>()
            )
        );

        services.AddSingleton<MainViewModel>(sp =>
            new MainViewModel(
                sp.GetRequiredService<ISerialPortService>(),
                sp.GetRequiredService<Settings>(),
                sp.GetRequiredService<QuickSendViewModel>()
            )
        );

        // View：主窗口同样从容器创建（由 App.OnStartup 解析并 Show）
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(sp.GetRequiredService<MainViewModel>())
        );

        return services.BuildServiceProvider();
    }
}