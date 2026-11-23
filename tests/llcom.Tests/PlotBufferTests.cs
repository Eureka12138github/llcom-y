using llcom.Model;
using Xunit;

namespace llcom.Tests
{
    /// <summary>
    /// 曲线缓冲：按容量保留最近的点、绝对样本序号、满容量批量丢弃最旧点。
    /// </summary>
    public class PlotBufferTests
    {
        [Fact]
        public void Add_AppendsPointsAndIncreasesAbsoluteX()
        {
            var buffer = new PlotBuffer(10);

            buffer.Add(0, 1.5);
            buffer.Add(0, 2.5);
            buffer.Add(1, 3.5);

            Assert.Equal(2, buffer.Counts[0]);
            Assert.Equal(1, buffer.Counts[1]);
            Assert.Equal(1.5, buffer.Values[0][0]);
            Assert.Equal(2.5, buffer.Values[0][1]);
            Assert.Equal(3.5, buffer.Values[1][0]);
            //每条线独立编号：line0 最新是 1，line1 最新是 0
            Assert.Equal(1, buffer.LatestX);
            Assert.Equal(0, buffer.OldestX(0));
            Assert.Equal(1, buffer.NewestX(0));
        }

        [Fact]
        public void Add_WhenFull_DropsOldestChunkAndKeepsOrder()
        {
            var buffer = new PlotBuffer(10);
            for (var i = 0; i < 12; i++)
                buffer.Add(0, i);

            // 容量 10：第 11 个点前丢掉 1 个（10/20 -> 至少 1），第 12 个点前再丢 1 个
            Assert.Equal(10, buffer.Counts[0]);
            Assert.Equal(2, buffer.BaseX[0]);
            Assert.Equal(2, buffer.OldestX(0));
            Assert.Equal(11, buffer.NewestX(0));
            Assert.Equal(2, buffer.Values[0][0]);
            Assert.Equal(11, buffer.Values[0][9]);
            Assert.Equal(11, buffer.LatestX);
        }

        [Fact]
        public void TryGetVisibleRange_AccountsForBaseX()
        {
            var buffer = new PlotBuffer(20);
            for (var i = 0; i < 5; i++)
                buffer.Add(0, i);

            Assert.True(buffer.TryGetVisibleRange(0, 1.5, 3.5, out var start, out var end));
            Assert.Equal(2, start);
            Assert.Equal(3, end);

            Assert.False(buffer.TryGetVisibleRange(0, 100, 200, out _, out _));
            Assert.False(buffer.TryGetVisibleRange(1, 0, 10, out _, out _));
        }

        [Fact]
        public void Add_IgnoresInvalidValuesAndLineNumbers()
        {
            var buffer = new PlotBuffer(10);

            buffer.Add(-1, 1);
            buffer.Add(10, 1);
            buffer.Add(0, double.NaN);
            buffer.Add(0, double.PositiveInfinity);

            Assert.Equal(0, buffer.Counts[0]);
            Assert.Equal(-1, buffer.LatestX);
        }

        [Fact]
        public void Clear_ResetsDataAndIndices()
        {
            var buffer = new PlotBuffer(10);
            buffer.Add(0, 1);
            buffer.Add(1, 2);

            buffer.Clear();

            Assert.Equal(0, buffer.Counts[0]);
            Assert.Equal(0, buffer.Counts[1]);
            Assert.Equal(0, buffer.BaseX[0]);
            Assert.Equal(-1, buffer.LatestX);
            Assert.Equal(0, buffer.Values[0][0]);
        }

        [Fact]
        public void Capacity_IsAtLeastTwo()
        {
            var buffer = new PlotBuffer(1);
            Assert.Equal(2, buffer.Capacity);
        }
    }
}