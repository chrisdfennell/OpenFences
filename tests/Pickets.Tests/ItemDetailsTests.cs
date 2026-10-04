using System;
using System.Globalization;
using System.Threading;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class ItemDetailsTests
    {
        private static readonly DateTime Now = new(2026, 10, 4, 15, 0, 0);

        private static T InUs<T>(Func<T> f)
        {
            var old = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            try { return f(); }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(1023, "1023 B")]
        [InlineData(1024, "1 KB")]
        [InlineData(500 * 1024, "500 KB")]
        [InlineData(3_565_158, "3.4 MB")]
        [InlineData(2L * 1024 * 1024 * 1024, "2 GB")]
        public void Sizes_are_short(long bytes, string expected) =>
            Assert.Equal(expected, InUs(() => ItemDetails.Size(bytes)));

        [Fact]
        public void Today_shows_the_time_this_year_the_day_older_the_year()
        {
            Assert.Equal("12 KB · 2:41 PM", InUs(() => ItemDetails.Format(false, 12 * 1024, Now.Date.AddHours(14).AddMinutes(41), Now)));
            Assert.Equal("12 KB · Mar 3", InUs(() => ItemDetails.Format(false, 12 * 1024, new DateTime(2026, 3, 3), Now)));
            Assert.Equal("12 KB · Mar 3, 2024", InUs(() => ItemDetails.Format(false, 12 * 1024, new DateTime(2024, 3, 3), Now)));
        }

        [Fact]
        public void Folders_show_only_the_date()
        {
            Assert.Equal("Mar 3", InUs(() => ItemDetails.Format(true, 0, new DateTime(2026, 3, 3), Now)));
        }

        [Fact]
        public void Special_items_have_no_details()
        {
            Assert.Equal("", ItemDetails.For("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
        }
    }
}
