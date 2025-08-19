using System;
using System.IO;

namespace OpenFences
{
    internal static class SystemShortcuts
    {
        // Well-known CLSIDs
        private const string CLSID_THIS_PC = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        private const string CLSID_CONTROL_PANEL = "{21EC2020-3AEA-1069-A2DD-08002B30309D}";
        private const string CLSID_NETWORK = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
        private const string CLSID_RECYCLE_BIN = "{645FF040-5081-101B-9F08-00AA002F954E}";

        public static int AddToFolder(string folderPath, bool includeHome = true)
        {
            int created = 0;

            created += CreateClsidShortcut(folderPath, "This PC", CLSID_THIS_PC) ? 1 : 0;
            created += CreateClsidShortcut(folderPath, "Control Panel", CLSID_CONTROL_PANEL) ? 1 : 0;
            created += CreateClsidShortcut(folderPath, "Network", CLSID_NETWORK) ? 1 : 0;
            created += CreateClsidShortcut(folderPath, "Recycle Bin", CLSID_RECYCLE_BIN) ? 1 : 0;

            if (includeHome)
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var homeName = new DirectoryInfo(home).Name;
                created += CreatePathShortcut(folderPath, homeName, home) ? 1 : 0;
            }

            return created;
        }

        private static bool CreateClsidShortcut(string destFolder, string displayName, string clsid)
        {
            try
            {
                Directory.CreateDirectory(destFolder);
                string link = Path.Combine(destFolder, Sanitize(displayName) + ".lnk");
                if (File.Exists(link)) return false;

                // Use explorer.exe with shell:::{CLSID}
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                ShellLink.CreateShortcut(link, explorer, arguments: $"shell:::{clsid}");
                return true;
            }
            catch { return false; }
        }

        private static bool CreatePathShortcut(string destFolder, string displayName, string targetPath)
        {
            try
            {
                Directory.CreateDirectory(destFolder);
                string link = Path.Combine(destFolder, Sanitize(displayName) + ".lnk");
                if (File.Exists(link)) return false;

                ShellLink.CreateShortcut(link, targetPath);
                return true;
            }
            catch { return false; }
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
