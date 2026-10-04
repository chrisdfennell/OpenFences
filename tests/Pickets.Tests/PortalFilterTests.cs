using System;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class PortalFilterTests
    {
        private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Patterns_accept_extensions_wildcards_and_plain_words()
        {
            Assert.Equal(new[] { "*.pdf", "*.docx", "screenshot*", "*invoice*" },
                         PortalFilter.ParsePatterns(" *.pdf; .docx, screenshot*  invoice ;;"));
            Assert.Empty(PortalFilter.ParsePatterns("  "));
            Assert.Empty(PortalFilter.ParsePatterns(null));
        }

        [Fact]
        public void Duplicate_patterns_are_dropped_ignoring_case()
        {
            Assert.Equal(new[] { "*.pdf" }, PortalFilter.ParsePatterns(".pdf *.PDF"));
        }

        [Theory]
        [InlineData("Report.PDF", true)]
        [InlineData("notes.docx", true)]
        [InlineData("Screenshot 2026-10-04.png", true)]
        [InlineData("Invoice-001.xlsx", true)]
        [InlineData("photo.jpg", false)]
        public void Files_match_any_pattern_ignoring_case(string name, bool expected)
        {
            var patterns = PortalFilter.ParsePatterns(".pdf .docx screenshot* invoice");
            Assert.Equal(expected, PortalFilter.Matches(name, isFolder: false, Now, patterns, null, Now));
        }

        [Fact]
        public void Name_patterns_hide_folders()
        {
            var patterns = PortalFilter.ParsePatterns(".pdf");
            Assert.False(PortalFilter.Matches("Scans.pdf", isFolder: true, Now, patterns, null, Now));
        }

        [Fact]
        public void Age_limit_applies_to_files_and_folders()
        {
            var none = PortalFilter.ParsePatterns(null);
            Assert.True(PortalFilter.Matches("new.txt", false, Now.AddDays(-6), none, 7, Now));
            Assert.False(PortalFilter.Matches("old.txt", false, Now.AddDays(-8), none, 7, Now));
            Assert.True(PortalFilter.Matches("New folder", true, Now.AddHours(-2), none, 1, Now));
            Assert.False(PortalFilter.Matches("Old folder", true, Now.AddDays(-2), none, 1, Now));
        }

        [Fact]
        public void No_filter_shows_everything()
        {
            var none = PortalFilter.ParsePatterns(null);
            Assert.False(PortalFilter.IsActive(null, null));
            Assert.True(PortalFilter.Matches("anything", true, DateTime.MinValue, none, null, Now));
        }

        [Fact]
        public void Description_lists_patterns_and_age()
        {
            Assert.Equal("*.pdf, *.docx · Last 7 days", PortalFilter.Describe(".pdf .docx", 7));
            Assert.Equal("Last 24 hours", PortalFilter.Describe(null, 1));
            Assert.Equal("", PortalFilter.Describe("", null));
        }
    }
}
