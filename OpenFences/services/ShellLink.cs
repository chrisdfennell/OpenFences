using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenFences
{
    internal static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
         Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string name, IntPtr pbc,
            out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        public static void CreateShortcut(string linkPath, string targetPath,
                                          string? args = null, string? workingDir = null,
                                          string? iconPath = null, int iconIndex = 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

            var sl = (IShellLinkW)new ShellLinkCoClass();

            if (IsShellParsingName(targetPath))
            {
                // Virtual object: create link via PIDL
                if (SHParseDisplayName(targetPath, IntPtr.Zero, out var pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                {
                    sl.SetIDList(pidl);
                    ILFree(pidl);
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse shell target: " + targetPath);
                }
            }
            else
            {
                sl.SetPath(targetPath);
                if (!string.IsNullOrEmpty(workingDir)) sl.SetWorkingDirectory(workingDir);
                if (!string.IsNullOrEmpty(args)) sl.SetArguments(args);
            }

            if (!string.IsNullOrEmpty(iconPath))
                sl.SetIconLocation(iconPath!, iconIndex);

            ((IPersistFile)sl).Save(linkPath, true);
        }

        public static string? GetShortcutTarget(string lnkPath)
        {
            var sl = (IShellLinkW)new ShellLinkCoClass();
            ((IPersistFile)sl).Load(lnkPath, 0);

            var sb = new StringBuilder(260);
            sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var p = sb.ToString();

            // For PIDL-only (virtual) links, GetPath returns empty → null.
            return string.IsNullOrWhiteSpace(p) ? null : p;
        }

        private static bool IsShellParsingName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            return s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("::", StringComparison.Ordinal)      // ::{CLSID}
                || s.StartsWith(@"\\?\shell:", StringComparison.OrdinalIgnoreCase);
        }
    }
}