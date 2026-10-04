using System;
using System.Collections.Generic;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class ProfileScheduleTests
    {
        // Friday 2026-10-02 and Saturday 2026-10-03.
        private static readonly DateTime Fri = new(2026, 10, 2);
        private static readonly DateTime Sat = new(2026, 10, 3);

        private static readonly DeskProfile Work = new() { Name = "Work", SwitchAt = "9:00", WeekdaysOnly = true };
        private static readonly DeskProfile Home = new() { Name = "Home", SwitchAt = "17:30" };
        private static readonly DeskProfile Manual = new() { Name = "Manual" };
        private static readonly List<DeskProfile> All = new() { Work, Home, Manual };

        [Theory]
        [InlineData("9:00", 9, 0)]
        [InlineData("09:30", 9, 30)]
        [InlineData(" 17:45 ", 17, 45)]
        public void Times_parse(string text, int h, int m)
        {
            Assert.True(ProfileSchedule.TryParseTime(text, out var t));
            Assert.Equal(new TimeSpan(h, m, 0), t);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("25:00")]
        [InlineData("9am")]
        public void Bad_times_dont_parse(string? text) => Assert.False(ProfileSchedule.TryParseTime(text, out _));

        [Fact]
        public void A_time_passing_switches()
        {
            Assert.Same(Work, ProfileSchedule.Due(All, Fri.AddHours(8.99), Fri.AddHours(9)));
            Assert.Same(Home, ProfileSchedule.Due(All, Fri.AddHours(17.4), Fri.AddHours(17.6)));
        }

        [Fact]
        public void Nothing_is_due_between_switch_times() =>
            Assert.Null(ProfileSchedule.Due(All, Fri.AddHours(10), Fri.AddHours(17)));

        [Fact]
        public void Weekdays_only_skips_the_weekend() =>
            Assert.Null(ProfileSchedule.Due(All, Sat.AddHours(8), Sat.AddHours(10)));

        [Fact]
        public void After_sleeping_through_several_the_latest_wins() =>
            Assert.Same(Home, ProfileSchedule.Due(All, Fri.AddHours(8), Fri.AddHours(20)));

        [Fact]
        public void Overnight_spans_midnight() =>
            Assert.Same(Work, ProfileSchedule.Due(All, Fri.AddDays(-1).AddHours(20), Fri.AddHours(9.5)));

        [Fact]
        public void Description_is_readable()
        {
            Assert.Equal("at 9:00 on weekdays", ProfileSchedule.Describe(Work));
            Assert.Equal("at 17:30", ProfileSchedule.Describe(Home));
            Assert.Equal("", ProfileSchedule.Describe(Manual));
        }
    }
}
