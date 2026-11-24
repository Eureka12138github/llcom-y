using System;
using llcom.Model;
using Xunit;

namespace llcom.Tests
{
    /// <summary>
    /// 间隔测量的「对齐相邻峰」：只要求能稳定找到可见范围内的两个相邻峰。
    /// </summary>
    public class PlotPeaksTests
    {
        private static double[] Sine(int count, int period)
        {
            var values = new double[count];
            for (var i = 0; i < count; i++)
                values[i] = Math.Sin(2 * Math.PI * i / period);
            return values;
        }

        [Fact]
        public void FindsAdjacentPeaksNearTarget()
        {
            var values = Sine(1000, 100);

            var ok = PlotPeaks.TryFindAdjacentPeaks(
                values,
                0,
                values.Length - 1,
                300,
                out var first,
                out var second
            );

            Assert.True(ok);
            Assert.Equal(225, first);
            Assert.Equal(325, second);
        }

        [Fact]
        public void PicksFirstPairWhenTargetIsAtTheLeftEdge()
        {
            var values = Sine(1000, 100);

            var ok = PlotPeaks.TryFindAdjacentPeaks(
                values,
                0,
                values.Length - 1,
                0,
                out var first,
                out var second
            );

            Assert.True(ok);
            Assert.Equal(25, first);
            Assert.Equal(125, second);
        }

        [Fact]
        public void FlatSignalHasNoPeaks()
        {
            var values = new double[1000];

            var ok = PlotPeaks.TryFindAdjacentPeaks(
                values,
                0,
                values.Length - 1,
                500,
                out _,
                out _
            );

            Assert.False(ok);
        }

        [Fact]
        public void SinglePeakIsNotEnough()
        {
            //一个完整周期只有一个峰，无法组成“相邻峰”
            var values = Sine(100, 100);

            var ok = PlotPeaks.TryFindAdjacentPeaks(
                values,
                0,
                values.Length - 1,
                50,
                out _,
                out _
            );

            Assert.False(ok);
        }

        [Fact]
        public void InvalidRangeReturnsFalse()
        {
            var values = Sine(100, 100);

            Assert.False(
                PlotPeaks.TryFindAdjacentPeaks(values, -1, 99, 0, out _, out _)
            );
            Assert.False(
                PlotPeaks.TryFindAdjacentPeaks(values, 0, values.Length, 0, out _, out _)
            );
        }
    }
}