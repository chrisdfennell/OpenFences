using System.Linq;
using System.Windows;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class StackTests
    {
        private const double Gap = 8;

        [Theory]
        [InlineData(300, true)]   // touching
        [InlineData(308, true)]   // snapped with the gap
        [InlineData(299, true)]   // a pixel of overlap from rounding
        [InlineData(320, false)]  // just near, not stacked
        [InlineData(250, false)]  // overlapping a lot
        public void Fences_stack_when_one_sits_right_under_the_other(double lowerTop, bool expected)
        {
            var upper = new Rect(100, 100, 300, 200); // bottom at 300
            var lower = new Rect(120, lowerTop, 300, 150);
            Assert.Equal(expected, FenceStacks.Touches(upper, lower, Gap));
        }

        [Fact]
        public void Side_by_side_fences_are_not_a_stack()
        {
            var upper = new Rect(100, 100, 300, 200);
            var beside = new Rect(408, 300, 300, 150); // starts right of upper
            Assert.False(FenceStacks.Touches(upper, beside, Gap));
        }

        [Fact]
        public void Below_follows_the_stack_down_and_column_goes_both_ways()
        {
            var top = new Rect(0, 0, 300, 100);
            var self = new Rect(0, 108, 300, 100);       // under top
            var under = new Rect(0, 216, 300, 100);      // under self
            var underUnder = new Rect(10, 316, 280, 50); // under that, touching
            var elsewhere = new Rect(600, 216, 300, 100);
            var others = new[] { top, under, underUnder, elsewhere };

            Assert.Equal(new[] { 1, 2 }, FenceStacks.Below(others, self, Gap));
            Assert.Equal(new[] { 0, 1, 2 }, FenceStacks.Column(others, self, Gap).OrderBy(i => i));
        }
    }
}
