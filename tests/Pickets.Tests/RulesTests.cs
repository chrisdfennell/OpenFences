using System;
using System.Collections.Generic;
using System.IO;
using Pickets;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class RulesTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("PicketsTests").FullName;

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private string File(string name)
        {
            var p = Path.Combine(_dir, name);
            System.IO.File.WriteAllText(p, "");
            return p;
        }

        private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;

        private static readonly List<FenceRule> Rules = new()
        {
            new FenceRule { Kind = RuleKind.Extensions, Extensions = { ".png", ".jpg" }, TargetFence = "Pictures" },
            new FenceRule { Kind = RuleKind.Folder, TargetFence = "Folders" },
            new FenceRule { Kind = RuleKind.Executable, TargetFence = "Apps" },
            new FenceRule { Kind = RuleKind.Any, TargetFence = "Documents" },
        };

        [Fact]
        public void File_types_match_case_insensitively() =>
            Assert.Equal("Pictures", DesktopRules.ResolveTargetFence(File("Photo.PNG"), Rules));

        [Fact]
        public void Folders_match_the_folder_rule() =>
            Assert.Equal("Folders", DesktopRules.ResolveTargetFence(Folder("Projects"), Rules));

        [Fact]
        public void Programs_match_the_apps_rule() =>
            Assert.Equal("Apps", DesktopRules.ResolveTargetFence(File("setup.exe"), Rules));

        [Fact]
        public void Anything_else_falls_through_to_the_catch_all_rule() =>
            Assert.Equal("Documents", DesktopRules.ResolveTargetFence(File("notes.txt"), Rules));

        [Fact]
        public void First_matching_rule_wins()
        {
            var rules = new List<FenceRule>
            {
                new() { Kind = RuleKind.Any, TargetFence = "First" },
                new() { Kind = RuleKind.Extensions, Extensions = { ".png" }, TargetFence = "Second" },
            };
            Assert.Equal("First", DesktopRules.ResolveTargetFence(File("a.png"), rules));
        }

        [Fact]
        public void No_matching_rule_returns_null() =>
            Assert.Null(DesktopRules.ResolveTargetFence(File("a.txt"), new List<FenceRule>
            {
                new() { Kind = RuleKind.Extensions, Extensions = { ".png" }, TargetFence = "Pictures" }
            }));

        [Fact]
        public void Name_patterns_match_files_and_folders()
        {
            var rules = new List<FenceRule>
            {
                new() { Kind = RuleKind.NamePattern, Pattern = "invoice* screenshot*", TargetFence = "Paperwork" },
            };
            Assert.Equal("Paperwork", DesktopRules.ResolveTargetFence(File("Invoice-001.pdf"), rules));
            Assert.Equal("Paperwork", DesktopRules.ResolveTargetFence(Folder("Screenshots 2026"), rules));
            Assert.Null(DesktopRules.ResolveTargetFence(File("notes.txt"), rules));
        }

        [Fact]
        public void Older_than_uses_the_last_change()
        {
            var rules = new List<FenceRule> { new() { Kind = RuleKind.OlderThan, Amount = 30, TargetFence = "Archive" } };
            var old = File("old.txt");
            System.IO.File.SetLastWriteTime(old, DateTime.Now.AddDays(-45));
            var recent = File("recent.txt");
            System.IO.File.SetLastWriteTime(recent, DateTime.Now.AddDays(-5));

            Assert.Equal("Archive", DesktopRules.ResolveTargetFence(old, rules));
            Assert.Null(DesktopRules.ResolveTargetFence(recent, rules));
        }

        [Fact]
        public void Larger_than_counts_megabytes_and_skips_folders()
        {
            var rules = new List<FenceRule> { new() { Kind = RuleKind.LargerThan, Amount = 1, TargetFence = "Big" } };
            var big = Path.Combine(_dir, "big.bin");
            System.IO.File.WriteAllBytes(big, new byte[1024 * 1024 + 1]);
            var small = Path.Combine(_dir, "small.bin");
            System.IO.File.WriteAllBytes(small, new byte[1024 * 1024]);

            Assert.Equal("Big", DesktopRules.ResolveTargetFence(big, rules));
            Assert.Null(DesktopRules.ResolveTargetFence(small, rules));
            Assert.Null(DesktopRules.ResolveTargetFence(Folder("Huge folder"), rules));
        }

        [Theory]
        [InlineData("png, .JPG;gif", new[] { ".png", ".jpg", ".gif" })]
        [InlineData("*.pdf  *.PDF", new[] { ".pdf" })]
        [InlineData("  ", new string[0])]
        public void Typed_file_types_are_normalized(string typed, string[] expected) =>
            Assert.Equal(expected, RulesDialog.ParseExtensions(typed));
    }
}
