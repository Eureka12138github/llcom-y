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

            // 接收条目会走 Lua 转换脚本（DataShowItem -> LuaLoader.Run），
            // 而 LuaLoader 初始化时会 require core_script.head，缺文件会让接收条目直接变空。
            // 这里复制一份最小默认脚本，保证任意测试先初始化 Lua 都能成功。
            CopyDefaultScripts(dir);
        }

        /// <summary>把仓库里的默认 Lua 脚本复制到测试 ProfilePath（目标已存在时覆盖）。</summary>
        private static void CopyDefaultScripts(string profilePath)
        {
            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
                return; // 找不到仓库目录时保持原行为，由调用方自行准备脚本

            CopyLuaDir(
                Path.Combine(repoRoot, "llcom", "DefaultFiles", "core_script"),
                Path.Combine(profilePath, "core_script")
            );
            CopyLuaDir(
                Path.Combine(repoRoot, "llcom", "DefaultFiles", "user_script_recv_convert"),
                Path.Combine(profilePath, "user_script_recv_convert")
            );
        }

        private static void CopyLuaDir(string source, string target)
        {
            if (!Directory.Exists(source))
                return;
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*.lua"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        }

        /// <summary>从测试输出目录向上找仓库根目录（以 core_script/head.lua 为标志）。</summary>
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
            {
                var marker = Path.Combine(
                    dir.FullName,
                    "llcom",
                    "DefaultFiles",
                    "core_script",
                    "head.lua"
                );
                if (File.Exists(marker))
                    return dir.FullName;
            }
            return null;
        }
    }
}
