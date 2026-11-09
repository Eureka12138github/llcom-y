using System.IO;
using System.Linq;
using llcom.Services.Dlt645;
using llcom.Tools;
using Xunit;

namespace llcom.Tests
{
    public class Dlt645IdentifierStoreTests
    {
        public Dlt645IdentifierStoreTests()
        {
            TestEnv.Init();
        }

        [Fact]
        public void LoadAll_SeedsDefaultsWhenFileMissing()
        {
            Assert.False(File.Exists(Dlt645IdentifierStore.FilePath));

            var all = Dlt645IdentifierStore.LoadAll();

            Assert.True(File.Exists(Dlt645IdentifierStore.FilePath));
            Assert.Contains(all, item => item.Hex == "02010100");
            Assert.Contains(all, item => item.Name == "A 相电压");
        }

        [Fact]
        public void SaveEntries_CanDeleteDefaultAndPersist()
        {
            var entries = Dlt645IdentifierStore.LoadEntries();
            entries.RemoveAll(item => item.Code == "02010100");
            var voltage = entries.First(item => item.Code == "02020100");
            voltage.Name = "自定义电流";

            Dlt645IdentifierStore.SaveEntries(entries);

            var all = Dlt645IdentifierStore.LoadAll();
            Assert.DoesNotContain(all, item => item.Hex == "02010100");
            Assert.Equal(
                "自定义电流",
                all.Single(item => item.Hex == "02020100").Name
            );
        }

        [Fact]
        public void ResetToDefaults_RestoresDeletedDefaults()
        {
            var entries = Dlt645IdentifierStore.LoadEntries();
            entries.RemoveAll(item => item.Code == "02010100");
            Dlt645IdentifierStore.SaveEntries(entries);
            Assert.DoesNotContain(
                Dlt645IdentifierStore.LoadAll(),
                item => item.Hex == "02010100"
            );

            Dlt645IdentifierStore.ResetToDefaults();

            Assert.Contains(
                Dlt645IdentifierStore.LoadAll(),
                item => item.Hex == "02010100"
            );
        }

        [Fact]
        public void ImportAndExport_RoundTripsAndIncludesDefaults()
        {
            var importPath = Path.Combine(Global.ProfilePath, "import-di.json");
            File.WriteAllText(
                importPath,
                @"{
  ""version"": 2,
  ""items"": [
    { ""code"": ""02 03 04 00"", ""name"": ""测试 DI"", ""category"": ""测试"", ""unit"": ""V"" }
  ]
}"
            );

            var count = Dlt645IdentifierStore.Import(importPath);

            Assert.Equal(1, count);
            var entry = Dlt645IdentifierStore
                .LoadEntries()
                .Single(item => item.Code == "02030400");
            Assert.Equal("测试 DI", entry.Name);
            Assert.Equal("V", entry.Unit);

            var exportPath = Path.Combine(Global.ProfilePath, "export-di.json");
            Dlt645IdentifierStore.Export(exportPath);

            var exported = File.ReadAllText(exportPath);
            Assert.Contains("02010100", exported);
            Assert.Contains("02030400", exported);
            Assert.Contains("测试 DI", exported);
        }

        [Fact]
        public void Import_IgnoresInvalidEntries()
        {
            var importPath = Path.Combine(Global.ProfilePath, "invalid-di.json");
            File.WriteAllText(
                importPath,
                @"{ ""version"": 2, ""items"": [ { ""code"": ""12"", ""name"": ""bad"" } ] }"
            );

            Assert.Equal(0, Dlt645IdentifierStore.Import(importPath));
            Assert.DoesNotContain(
                Dlt645IdentifierStore.LoadAll(),
                item => item.Name == "bad"
            );
        }
    }
}
