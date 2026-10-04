using System;
using System.Collections.Generic;
using System.Linq;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class AppCloserTests
    {
        private static readonly Dictionary<string, string?> Links = new(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Users\me\Desktop\Code.lnk"] = @"C:\Apps\VSCode\Code.exe",
            [@"C:\Users\me\Desktop\Code (2).lnk"] = @"C:\Apps\VSCode\Code.exe",
            [@"C:\Users\me\Desktop\Notes.lnk"] = @"C:\Docs\notes.txt",
            [@"C:\Users\me\Desktop\Explorer.lnk"] = @"C:\Windows\explorer.exe",
            [@"C:\Users\me\Desktop\Store app.lnk"] = null,
        };

        private static string? Target(string lnk) => Links.TryGetValue(lnk, out var t) ? t : null;

        private static IntPtr H(int n) => new(n);

        [Theory]
        [InlineData(@"C:\Users\me\Desktop\Code.lnk", @"C:\Apps\VSCode\Code.exe")]
        [InlineData(@"C:\Tools\putty.EXE", @"C:\Tools\putty.EXE")]
        [InlineData(@"C:\Users\me\Desktop\Notes.lnk", null)]          // shortcut to a document
        [InlineData(@"C:\Users\me\Desktop\Store app.lnk", null)]      // PIDL-only shortcut
        [InlineData(@"C:\Users\me\Desktop\Explorer.lnk", null)]       // Explorer draws the desktop
        [InlineData(@"C:\Users\me\Desktop\report.docx", null)]
        [InlineData(@"C:\Users\me\Desktop\Projects", null)]
        [InlineData("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", null)]
        public void Only_app_tiles_have_a_program(string tile, string? expected)
        {
            Assert.Equal(expected, AppCloser.ExeForTile(tile, Target));
        }

        [Fact]
        public void Running_apps_are_matched_by_program_once_each_in_tile_order()
        {
            var tiles = new[]
            {
                @"C:\Tools\putty.exe",
                @"C:\Users\me\Desktop\Code.lnk",
                @"C:\Users\me\Desktop\Code (2).lnk",
                @"C:\Tools\idle.exe",
                @"C:\Users\me\Desktop\Notes.lnk",
            };
            var windows = new[]
            {
                (H(1), @"c:\apps\vscode\CODE.EXE"),
                (H(2), @"C:\Apps\VSCode\Code.exe"),
                (H(3), @"C:\Tools\putty.exe"),
                (H(4), @"C:\Windows\System32\notepad.exe"),
            };

            var apps = AppCloser.Match(tiles, Target, windows);

            Assert.Equal(new[] { "putty.exe", "Code" }, apps.Select(a => a.Name));
            Assert.Equal(new[] { H(3) }, apps[0].Windows);
            Assert.Equal(new[] { H(1), H(2) }, apps[1].Windows);
        }

        [Fact]
        public void Pickets_never_closes_itself()
        {
            var apps = AppCloser.Match(new[] { @"C:\Apps\Pickets\Pickets.exe" }, Target,
                                       new[] { (H(1), @"C:\Apps\Pickets\Pickets.exe") },
                                       ownExe: @"C:\Apps\Pickets\Pickets.exe");
            Assert.Empty(apps);
        }
    }
}
