using System;
using System.IO;

namespace OpenFences
{
    internal static class SystemShortcuts
    {
        // Adds This PC, Control Panel, Network, Recycle Bin, and the user's home folder.
        // Returns number of links created.
        public static int AddToFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            int count = 0;

            count += CreateIfMissing(folder, "This PC.lnk", @"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
            count += CreateIfMissing(folder, "Control Panel.lnk", @"shell:ControlPanelFolder");
            count += CreateIfMissing(folder, "Network.lnk", @"::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}");
            count += CreateIfMissing(folder, "Recycle Bin.lnk", @"::{645FF040-5081-101B-9F08-00AA002F954E}");

            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userLinkName = $"{Path.GetFileName(user)}.lnk";
            count += CreateIfMissing(folder, userLinkName, user);

            return count;
        }

        private static int CreateIfMissing(string destFolder, string linkName, string target)
        {
            var linkPath = Path.Combine(destFolder, linkName);
            if (File.Exists(linkPath)) return 0;

            // NOTE: parameter is args (not 'arguments')
            ShellLink.CreateShortcut(
                linkPath,
                target,
                args: null,
                workingDir: null,
                iconPath: null,
                iconIndex: 0);

            return 1;
        }
    }
}