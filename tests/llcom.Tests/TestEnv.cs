using System;
using System.IO;
using llcom.Model;
using llcom.Tools;

namespace llcom.Tests
{
    /// <summary>
    /// 测试环境初始化：把 ProfilePath 指向临时目录，并准备一个 Settings 实例，
    /// 避免测试污染真实配置、也避免因 Global.setting 为 null 而崩溃。
    /// </summary>
    internal static class TestEnv
    {
        public static void Init()
        {
            var dir = Path.Combine(Path.GetTempPath(), "llcom_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            AppPaths.ProfilePath = dir + "\\";
            Global.setting = new Settings();
        }
    }
}