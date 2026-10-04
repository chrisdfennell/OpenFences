using System;
using System.Collections.Generic;
using System.IO;
using OpenFences;
using OpenFences.Services;
using Xunit;

namespace OpenFences.Tests
{
    public class RulesTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("OpenFencesTests").FullName;

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

        [Theory]
        [InlineData("png, .JPG;gif", new[] { ".png", ".jpg", ".gif" })]
        [InlineData("*.pdf  *.PDF", new[] { ".pdf" })]
        [InlineData("  ", new string[0])]
        public void Typed_file_types_are_normalized(string typed, string[] expected) =>
            Assert.Equal(expected, RulesDialog.ParseExtensions(typed));
    }
}
