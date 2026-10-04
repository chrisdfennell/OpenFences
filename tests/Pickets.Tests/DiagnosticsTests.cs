using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class DiagnosticsTests
    {
        [Fact]
        public void Profile_folder_user_and_pc_names_are_replaced_ignoring_case()
        {
            var log = @"Failed to read C:\Users\Jamie.Lee\Desktop\a.txt on JAMIE-PC (user jamie.lee)";
            var redacted = Diagnostics.Redact(log, @"C:\Users\jamie.lee", "jamie.lee", "Jamie-PC");
            Assert.Equal(@"Failed to read %USERPROFILE%\Desktop\a.txt on <pc> (user <user>)", redacted);
        }

        [Fact]
        public void A_short_user_name_only_matches_whole_words()
        {
            var redacted = Diagnostics.Redact("Snap and stack: al ok", null, "al", null);
            Assert.Equal("Snap and stack: <user> ok", redacted);
        }

        [Theory]
        [InlineData("Hub.Bg", ContrastRole.Window)]
        [InlineData("Hub.SurfaceHover", ContrastRole.Window)]
        [InlineData("Hub.Muted", ContrastRole.WindowText)]
        [InlineData("Hub.BorderStrong", ContrastRole.WindowText)]
        [InlineData("Hub.Accent", ContrastRole.Highlight)]
        [InlineData("Hub.OnAccent", ContrastRole.HighlightText)]
        [InlineData("Dlg.BtnBg", ContrastRole.ButtonFace)]
        [InlineData("Dlg.BtnFg", ContrastRole.ButtonText)]
        [InlineData("Menu.ItemHover", ContrastRole.Highlight)]
        [InlineData("Scroll.ThumbEdge", ContrastRole.WindowText)]
        [InlineData("Search.Selected", ContrastRole.Highlight)]
        [InlineData("Search.InputBg", ContrastRole.Window)]
        public void High_contrast_maps_each_palette_color_to_a_system_color(string key, ContrastRole expected)
        {
            Assert.Equal(expected, Theme.RoleFor(key));
        }
    }
}
