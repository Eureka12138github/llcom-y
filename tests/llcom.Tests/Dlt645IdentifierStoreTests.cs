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
        public void LoadMerged_ContainsBuiltinsAndUserOverrides()
        {
            Dlt645IdentifierStore.SaveUserEntries(
                new[]
                {
                    new Dlt645IdentifierEntry
                    {
                        Code = "02010100",
                        Name = "自定义电压",
                        Category = "测试",
                    },
                }
            );

            var merged = Dlt645IdentifierStore.LoadMerged();

            var voltage = merged.Single(item => item.Hex == "02010100");
            Assert.Equal("自定义电压", voltage.Name);
            Assert.False(voltage.IsBuiltIn);
            Assert.Contains(merged, item => item.Hex == "02020100");
        }

        [Fact]
        public void ImportAndExport_RoundTripsUserEntries()
        {
            var importPath = Path.Combine(Global.ProfilePath, "import-di.json");
            File.WriteAllText(
                importPath,
                @"{
  ""version"": 1,
  ""items"": [
    { ""code"": ""02 03 04 00"", ""name"": ""测试 DI"", ""category"": ""测试"", ""unit"": ""V"" }
  ]
}"
            );

            var count = Dlt645IdentifierStore.Import(importPath);

            Assert.Equal(1, count);
            var entry = Dlt645IdentifierStore.LoadUserEntries().Single();
            Assert.Equal("02030400", entry.Code);
            Assert.Equal("测试 DI", entry.Name);
            Assert.Equal("V", entry.Unit);

            var exportPath = Path.Combine(Global.ProfilePath, "export-di.json");
            Dlt645IdentifierStore.Export(exportPath);

            var exported = File.ReadAllText(exportPath);
            Assert.Contains("02030400", exported);
            Assert.Contains("测试 DI", exported);
        }

        [Fact]
        public void Import_IgnoresInvalidEntries()
        {
            var importPath = Path.Combine(Global.ProfilePath, "invalid-di.json");
            File.WriteAllText(
                importPath,
                @"{ ""version"": 1, ""items"": [ { ""code"": ""12"", ""name"": ""bad"" } ] }"
            );

            Assert.Equal(0, Dlt645IdentifierStore.Import(importPath));
            Assert.Empty(Dlt645IdentifierStore.LoadUserEntries());
        }
    }
}
