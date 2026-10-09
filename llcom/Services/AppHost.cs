using System;
using llcom.Model;
using llcom.Services;
using llcom.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace llcom.Tools;

/// <summary>
/// 组合根（DI 第一步）：集中注册服务与 ViewModel，避免在 View 里到处 new。
/// 现阶段 <c>Global.setting</c> / <c>Global.uart</c> 仍是既有单例，这里注册为同一实例；
/// 后续可逐步把调用点从 <c>Tools.Global.*</c> 迁移为构造函数注入。
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

        // ViewModel（用工厂以便调用 internal 构造函数）
        services.AddSingleton<MainViewModel>(sp =>
            new MainViewModel(
                sp.GetRequiredService<ISerialPortService>(),
                sp.GetRequiredService<Settings>()
            )
        );

        return services.BuildServiceProvider();
    }
}