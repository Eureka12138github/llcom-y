using System.Collections.Specialized;
using System.Linq;
using llcom.Tools;
using Xunit;

namespace llcom.Tests
{
    /// <summary>批量裁剪集合：保留尾部、只发一次 Reset。</summary>
    public class BulkObservableCollectionTests
    {
        [Fact]
        public void TrimStart_KeepsTailInOrder()
        {
            var list = new BulkObservableCollection<int>();
            for (var i = 0; i < 10; i++)
                list.Add(i);

            list.TrimStart(4);

            Assert.Equal(new[] { 4, 5, 6, 7, 8, 9 }, list.ToArray());
            Assert.Equal(6, list.Count);
        }

        [Fact]
        public void TrimStart_RaisesSingleReset()
        {
            var list = new BulkObservableCollection<int>();
            for (var i = 0; i < 10; i++)
                list.Add(i);

            var resets = 0;
            list.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                    resets++;
            };

            list.TrimStart(3);
            list.TrimStart(0); // 空操作不应触发通知
            list.TrimStart(999); // 超过条数时只删到空

            Assert.Equal(2, resets);
            Assert.Empty(list);
        }
    }
}