using System.IO;
using llcom.Tools;
using llcom.ViewModels;
using Xunit;

namespace llcom.Tests
{
    public class LuaEditorViewModelTests
    {
        public LuaEditorViewModelTests()
        {
            TestEnv.Init();
        }

        /// <summary>
        /// 回归：全新安装时 user_script_run 目录尚不存在，
        /// 构造 LuaEditorViewModel 不应抛 DirectoryNotFoundException。
        /// </summary>
        [Fact]
        public void Constructor_DoesNotThrow_WhenFolderMissing()
        {
            var vm = new LuaEditorViewModel("user_script_run/", () => "s", _ => { });

            Assert.Empty(vm.Files);
        }

        [Fact]
        public void RefreshList_ListsOnlyLuaFiles()
        {
            var dir = Global.ProfilePath + "user_script_run";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "a.lua"), "-- a");
            File.WriteAllText(Path.Combine(dir, "b.lua"), "-- b");
            File.WriteAllText(Path.Combine(dir, "c.txt"), "not lua");

            var vm = new LuaEditorViewModel("user_script_run/", () => "a", _ => { });

            Assert.Contains("a", vm.Files);
            Assert.Contains("b", vm.Files);
            Assert.DoesNotContain("c", vm.Files);
        }

        [Fact]
        public void CreateNew_CreatesFileAndLoads()
        {
            var dir = Global.ProfilePath + "user_script_run";
            Directory.CreateDirectory(dir);
            var current = "default";
            var vm = new LuaEditorViewModel("user_script_run/", () => current, v => current = v);

            vm.CreateNew("newone");

            Assert.True(File.Exists(Path.Combine(dir, "newone.lua")));
            Assert.Contains("newone", vm.Files);
            Assert.Equal("newone", current);
        }
    }
}