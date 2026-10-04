using System.Collections.Generic;
using System.Linq;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class LayoutExportTests
    {
        private static List<FenceModel> Fences() => new()
        {
            new FenceModel
            {
                Name = "Docs",
                ItemPaths = { @"C:\Users\alice\Desktop\Budget.xlsx", @"shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", @"D:\Shared\Plan.docx" },
                Tabs = { new FenceTab { Name = "Work", ItemPaths = { @"C:\Users\alice\Desktop\Budget.xlsx" } } },
                BackgroundMedia = @"C:\Users\alice\Pictures\bg.jpg",
            },
            new FenceModel { Name = "Downloads", IsPortal = true, FolderPath = @"C:\Users\alice\Downloads" },
        };

        [Fact]
        public void Profile_paths_move_to_the_new_user()
        {
            var fences = Fences();
            LayoutSnapshots.RemapPaths(fences, @"C:\Users\alice", @"C:\Users\bob\");

            Assert.Equal(@"C:\Users\bob\Desktop\Budget.xlsx", fences[0].ItemPaths[0]);
            Assert.Equal(@"C:\Users\bob\Desktop\Budget.xlsx", fences[0].Tabs[0].ItemPaths[0]);
            Assert.Equal(@"C:\Users\bob\Pictures\bg.jpg", fences[0].BackgroundMedia);
            Assert.Equal(@"C:\Users\bob\Downloads", fences[1].FolderPath);
        }

        [Fact]
        public void Other_paths_and_special_items_are_left_alone()
        {
            var fences = Fences();
            LayoutSnapshots.RemapPaths(fences, @"C:\Users\alice", @"C:\Users\bob");

            Assert.Equal(@"shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", fences[0].ItemPaths[1]);
            Assert.Equal(@"D:\Shared\Plan.docx", fences[0].ItemPaths[2]);
        }

        [Fact]
        public void A_folder_that_only_starts_with_the_same_name_is_not_moved()
        {
            var fences = new List<FenceModel> { new() { ItemPaths = { @"C:\Users\alice2\Desktop\x.txt" } } };
            LayoutSnapshots.RemapPaths(fences, @"C:\Users\alice", @"C:\Users\bob");
            Assert.Equal(@"C:\Users\alice2\Desktop\x.txt", fences[0].ItemPaths.Single());
        }

        [Fact]
        public void A_redirected_desktop_maps_onto_the_local_one()
        {
            var fences = new List<FenceModel> { new() { ItemPaths = { @"C:\Users\alice\OneDrive\Desktop\a.txt" } } };
            LayoutSnapshots.RemapPaths(fences, @"C:\Users\alice\OneDrive\Desktop", @"C:\Users\bob\Desktop");
            Assert.Equal(@"C:\Users\bob\Desktop\a.txt", fences[0].ItemPaths.Single());
        }
    }
}
