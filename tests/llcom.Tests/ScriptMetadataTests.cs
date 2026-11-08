using System;
using System.IO;
using System.Linq;
using llcom.Model;
using llcom.Services;
using llcom.Tools;
using llcom.ViewModels;
using Xunit;

namespace llcom.Tests
{
    public class ScriptMetadataTests
    {
        public ScriptMetadataTests()
        {
            TestEnv.Init();
        }

        private static string ScriptDir => Global.ProfilePath + "user_script_run";

        private static void CreateScripts(params string[] names)
        {
            Directory.CreateDirectory(ScriptDir);
            foreach (var name in names)
                File.WriteAllText(Path.Combine(ScriptDir, name + ".lua"), "-- " + name);
        }

        [Fact]
        public void Store_SaveAndLoad_RoundTripsMetadata()
        {
            CreateScripts("a");
            var store = new ScriptMetadataStore(ScriptDir);
            store.Save(
                new[]
                {
                    new ScriptMetadata
                    {
                        File = "a",
                        Category = "Modbus",
                        Favorite = true,
                        Order = -1,
                        Description = "crc helper",
                    },
                }
            );

            var loaded = store.Load();

            Assert.True(loaded.ContainsKey("a"));
            Assert.Equal("Modbus", loaded["a"].Category);
            Assert.True(loaded["a"].Favorite);
            Assert.Equal(-1, loaded["a"].Order);
            Assert.Equal("crc helper", loaded["a"].Description);
        }

        [Fact]
        public void Store_Load_ReturnsEmpty_ForCorruptJson()
        {
            CreateScripts("a");
            File.WriteAllText(Path.Combine(ScriptDir, "scripts.index.json"), "{ not json");

            var loaded = new ScriptMetadataStore(ScriptDir).Load();

            Assert.Empty(loaded);
        }

        [Fact]
        public void RefreshList_CreatesSidecarAndSortsPinnedFirst()
        {
            CreateScripts("a", "b", "c");
            var current = "a";
            var vm = new LuaEditorViewModel("user_script_run/", () => current, v => current = v);

            vm.SelectedFile = "b";
            vm.SetSelectedPinned(true);
            vm.RefreshList();

            Assert.True(File.Exists(Path.Combine(ScriptDir, "scripts.index.json")));
            Assert.Equal(new[] { "b", "a", "c" }, vm.Files.ToArray());
        }

        [Fact]
        public void PinningAnotherScript_MovesItAboveExistingPins()
        {
            CreateScripts("a", "b", "c", "d", "e");
            var current = "a";
            var vm = new LuaEditorViewModel("user_script_run/", () => current, v => current = v);

            vm.SelectedFile = "c";
            vm.SetSelectedPinned(true);
            vm.SelectedFile = "e";
            vm.SetSelectedPinned(true);

            Assert.Equal(new[] { "e", "c", "a", "b", "d" }, vm.Files.ToArray());

            // 再次置顶已置顶的脚本，应重新回到最前
            vm.SelectedFile = "c";
            vm.SetSelectedPinned(true);
            Assert.Equal(new[] { "c", "e", "a", "b", "d" }, vm.Files.ToArray());

            // 取消置顶后回到普通项
            vm.SelectedFile = "e";
            vm.SetSelectedPinned(false);
            Assert.Equal(new[] { "c", "a", "b", "d", "e" }, vm.Files.ToArray());
        }

        [Fact]
        public void Filters_SearchCategoryAndFavorite()
        {
            CreateScripts("a", "b");
            var current = "a";
            var vm = new LuaEditorViewModel("user_script_run/", () => current, v => current = v);

            vm.SelectedFile = "a";
            vm.SetSelectedCategory("Modbus");
            vm.SetSelectedDescription("crc table");

            vm.SelectedFile = "b";
            vm.SetSelectedCategory("Timer");
            vm.ToggleSelectedFavorite();

            vm.SearchText = "crc";
            Assert.Equal(new[] { "a" }, vm.ScriptItems.Select(x => x.Name).ToArray());

            vm.SearchText = "";
            vm.SelectedCategoryOption = vm.CategoryOptions.Single(o => o.Value == "Timer");
            Assert.Equal(new[] { "b" }, vm.ScriptItems.Select(x => x.Name).ToArray());

            vm.SelectedCategoryOption = vm.CategoryOptions[0];
            vm.FavoritesOnly = true;
            Assert.Equal(new[] { "b" }, vm.ScriptItems.Select(x => x.Name).ToArray());
        }

        [Fact]
        public void FilteredOutSelection_KeepsCurrentScriptVisible()
        {
            CreateScripts("a", "b");
            var current = "a";
            var vm = new LuaEditorViewModel("user_script_run/", () => current, v => current = v);

            vm.SearchText = "b";

            Assert.Equal("a", vm.SelectedFile);
            Assert.Equal(new[] { "b" }, vm.ScriptItems.Select(x => x.Name).ToArray());
            Assert.Contains("a", vm.Files);
            Assert.Equal("a", vm.VisibleSelectedFile);
        }

        [Fact]
        public void ScriptItems_ExposeCategoryDescriptionAndFavorite()
        {
            CreateScripts("a");
            var current = "a";
            var vm = new LuaEditorViewModel("user_script_run/", () => current, v => current = v);

            vm.SetSelectedCategory("Modbus");
            vm.SetSelectedDescription("crc table");
            vm.ToggleSelectedFavorite();

            var item = Assert.Single(vm.ScriptItems);
            Assert.Equal("a", item.Name);
            Assert.Equal("Modbus", item.Category);
            Assert.Equal("crc table", item.Description);
            Assert.True(item.Favorite);
        }
    }
}
