using llcom.Model;
using llcom.ViewModels;
using Xunit;

namespace llcom.Tests
{
    public class QuickSendViewModelTests
    {
        public QuickSendViewModelTests()
        {
            TestEnv.Init();
        }

        private static QuickSendViewModel NewVm(out Settings setting)
        {
            setting = new Settings();
            return new QuickSendViewModel(setting);
        }

        [Fact]
        public void Constructor_LoadsDefaults()
        {
            var vm = NewVm(out _);

            Assert.Equal(10, vm.Pages.Count);
            Assert.Equal(6, vm.Items.Count); // 空页会填充默认示例
            Assert.Equal(0, vm.CurrentPageIndex);
        }

        [Fact]
        public void CheckIds_RenumbersSequentially()
        {
            var vm = NewVm(out _);
            vm.Items.Clear();
            vm.Items.Add(new ToSendData { id = 99, text = "a" });
            vm.Items.Add(new ToSendData { id = 7, text = "b" });

            vm.CheckIds();

            Assert.Equal(1, vm.Items[0].id);
            Assert.Equal(2, vm.Items[1].id);
        }

        [Fact]
        public void AddCommand_AppendsItem_AndPersists()
        {
            var vm = NewVm(out var setting);
            var before = vm.Items.Count;

            vm.AddCommand.Execute(null);

            Assert.Equal(before + 1, vm.Items.Count);
            Assert.Equal(before + 1, setting.quickSend.Count);
        }

        [Fact]
        public void RemoveLastCommand_RemovesLastItem_AndPersists()
        {
            var vm = NewVm(out var setting);
            var before = vm.Items.Count;

            vm.RemoveLastCommand.Execute(null);

            Assert.Equal(before - 1, vm.Items.Count);
            Assert.Equal(before - 1, setting.quickSend.Count);
        }

        [Fact]
        public void SwitchPage_LoadsTargetPage()
        {
            var vm = NewVm(out var setting);

            vm.SwitchPageCommand.Execute(3);

            Assert.Equal(3, vm.CurrentPageIndex);
            Assert.Equal(3, setting.quickSendSelect);
        }
    }
}