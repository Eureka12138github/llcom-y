using System.Collections.Generic;
using System.IO;
using llcom.Services.Tooling;
using llcom.Tools;
using Xunit;

namespace llcom.Tests
{
    public class ToolIndexStoreTests
    {
        public ToolIndexStoreTests()
        {
            TestEnv.Init();
        }

        private static string IndexPath() =>
            Path.Combine(Global.ProfilePath, ToolIndexStore.FileName);

        [Fact]
        public void Load_MissingFile_ReturnsDefaults()
        {
            var index = new ToolIndexStore(IndexPath()).Load();

            Assert.Empty(index.Favorites);
            Assert.Equal("", index.LastSelected);
        }

        [Fact]
        public void SaveAndLoad_RoundTrips()
        {
            var store = new ToolIndexStore(IndexPath());
            store.Save(
                new ToolIndex
                {
                    Favorites = new List<string> { "dlt645", "tcp-test" },
                    LastSelected = "serial-monitor",
                }
            );

            var index = store.Load();

            Assert.Equal(new[] { "dlt645", "tcp-test" }, index.Favorites);
            Assert.Equal("serial-monitor", index.LastSelected);
        }

        [Fact]
        public void Load_DropsUnknownIds_AndNormalizesCase()
        {
            File.WriteAllText(
                IndexPath(),
                "{\"version\":1,\"favorites\":[\"nope\",\"DLT645\",\"dlt645\"],"
                    + "\"lastSelected\":\"Dlt645\"}"
            );

            var index = new ToolIndexStore(IndexPath()).Load();

            Assert.Equal(new[] { "dlt645" }, index.Favorites);
            Assert.Equal("dlt645", index.LastSelected);
        }

        [Fact]
        public void Save_NormalizesUnknownIds()
        {
            var store = new ToolIndexStore(IndexPath());
            store.Save(
                new ToolIndex
                {
                    Favorites = new List<string> { "nope", "DLT645" },
                    LastSelected = "NotATool",
                }
            );

            var index = store.Load();

            Assert.Equal(new[] { "dlt645" }, index.Favorites);
            Assert.Equal("", index.LastSelected);
        }

        [Fact]
        public void Load_CorruptFile_ReturnsDefaults()
        {
            File.WriteAllText(IndexPath(), "{not valid json");

            var index = new ToolIndexStore(IndexPath()).Load();

            Assert.Empty(index.Favorites);
            Assert.Equal("", index.LastSelected);
        }
    }
}
