using Microsoft.Win32;

namespace llcom.Tools;

/// <summary>
/// 读取本机 .NET Framework 4.x 就地更新版本号（如 "4.8.09221"），
/// 用于启动时的环境检查。替代原 Walterlv.Environment.Source 源包（已不再维护，
/// 且与现代 SDK-style WPF 临时工程不兼容）。
/// </summary>
internal static class NdpInfo
{
    /// <summary>
    /// 获取 .NET Framework 4.x 的最新版本号；未安装则返回 null。
    /// </summary>
    public static string GetCurrentVersionName()
    {
        // 优先读取权威的 v4\Full\Version；32/64 位视图都尝试
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var v = TryRead(view, @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")
                ?? TryRead(view, @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4");
            if (!string.IsNullOrEmpty(v))
                return v;
        }
        return null;
    }

    private static string TryRead(RegistryView view, string subKey)
    {
        try
        {
            using (
                var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
            )
            using (var key = baseKey.OpenSubKey(subKey))
            {
                return key?.GetValue("Version") as string;
            }
        }
        catch
        {
            return null;
        }
    }
}