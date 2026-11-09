using System.IO;
using llcom.Model;
using llcom.Services;
using llcom.Tools;
using Xunit;

namespace llcom.Tests
{
    public class SettingsTransferTests
    {
        public SettingsTransferTests()
        {
            TestEnv.Init();
        }

        [Fact]
        public void Export_WritesConfigWithKnownFields()
        {
            var path = Path.Combine(Tools.Global.ProfilePath, "export.json");

            SettingsTransfer.Export(path);

            var text = File.ReadAllText(path);
            Assert.Contains("\"baudRate\"", text);
            Assert.Contains("\"dataBits\"", text);
            Assert.Contains("\"stopBit\"", text);
            Assert.True(SettingsTransfer.TryValidate(text, out _));
        }

        [Fact]
        public void TryValidate_RejectsNonSettingsJson()
        {
            Assert.False(SettingsTransfer.TryValidate("{\"a\":1}", out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void TryValidate_RejectsEmptyOrBrokenJson()
        {
            Assert.False(SettingsTransfer.TryValidate("", out _));
            Assert.False(SettingsTransfer.TryValidate("   ", out _));
            Assert.False(SettingsTransfer.TryValidate("{not json", out _));
        }

        [Fact]
        public void TryValidate_RejectsWrongFieldTypes()
        {
            Assert.False(
                SettingsTransfer.TryValidate(
                    "{\"baudRate\":\"115200\",\"dataBits\":8,\"stopBit\":1}",
                    out _
                )
            );
            Assert.False(
                SettingsTransfer.TryValidate("{\"baudRate\":115200,\"dataBits\":8}", out _)
            );
        }

        [Fact]
        public void Import_BacksUpAndReplacesSettingsFile()
        {
            var directory = Tools.Global.ProfilePath;
            var target = directory + "settings.json";
            File.WriteAllText(target, "{\"old\":true}");

            var importPath = Path.Combine(directory, "import.json");
            SettingsTransfer.Export(importPath);
            var importedText = File.ReadAllText(importPath);

            try
            {
                SettingsTransfer.Import(importPath);

                Assert.Equal(importedText, File.ReadAllText(target));
                Assert.True(File.Exists(directory + SettingsTransfer.BackupFileName));
                Assert.Equal("{\"old\":true}", File.ReadAllText(directory + SettingsTransfer.BackupFileName));
                Assert.True(Settings.ImportPendingRestart);
            }
            finally
            {
                Settings.ImportPendingRestart = false;
            }
        }

        [Fact]
        public void Flush_DoesNotOverwriteFileWhileImportPendingRestart()
        {
            var target = Tools.Global.ProfilePath + "settings.json";
            File.WriteAllText(target, "{\"imported\":true}");

            Settings.ImportPendingRestart = true;
            try
            {
                Tools.Global.setting.Flush();
                Assert.Equal("{\"imported\":true}", File.ReadAllText(target));
            }
            finally
            {
                Settings.ImportPendingRestart = false;
            }

            // 恢复正常后仍可正常写盘
            Tools.Global.setting.Flush();
            Assert.Contains("baudRate", File.ReadAllText(target));
        }
    }
}
