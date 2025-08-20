using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenFences
{
    internal static class ShellIcon
    {
        // Public entry point: get a LARGE icon as WPF ImageSource for any file or .lnk, including virtual shell targets
        public static ImageSource? GetLargeIconFor(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;

                // If it's a .lnk, try to use its icon location, then PIDL, then resolved target path
                if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    // 1) Icon location embedded in the shortcut
                    if (TryGetIconFromShortcutIconLocation(path, large: true, out var img))
                        return img;

                    // 2) PIDL icon for virtual targets (::CLSID or shell:...) stored in the .lnk
                    if (TryGetIconFromShortcutPIDL(path, large: true, out img))
                        return img;

                    // 3) Fall back to resolved file target
                    var target = ShellLink.GetShortcutTarget(path);
                    if (!string.IsNullOrWhiteSpace(target))
                        return GetLargeIconForNormalPath(target);
                }

                // Non-.lnk or fallback
                return GetLargeIconForNormalPath(path);
            }
            catch
            {
                return null;
            }
        }

        // ----- normal filesystem path -----
        private static ImageSource? GetLargeIconForNormalPath(string path)
        {
            SHFILEINFO sfi = new();
            var h = SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
            if (h == IntPtr.Zero && File.Exists(path))
            {
                // Try without USEFILEATTRIBUTES if the file actually exists
                SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            }
            return FromHIcon(sfi.hIcon, destroy: true);
        }

        // ----- .lnk helpers -----

        // Use IShellLinkW.GetIconLocation if set
        private static bool TryGetIconFromShortcutIconLocation(string lnk, bool large, out ImageSource? img)
        {
            img = null;
            try
            {
                var sl = CreateShellLinkAndLoad(lnk);
                var sb = new StringBuilder(260);
                sl.GetIconLocation(sb, sb.Capacity, out int iconIndex);
                var iconPath = sb.ToString();

                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    if (ExtractIcon(iconPath, iconIndex, large, out var hIcon))
                    {
                        img = FromHIcon(hIcon, destroy: true);
                        return img != null;
                    }
                }
            }
            catch { }
            return false;
        }

        // Use PIDL stored inside the .lnk (works for ::{CLSID}, shell:…)
        private static bool TryGetIconFromShortcutPIDL(string lnk, bool large, out ImageSource? img)
        {
            img = null;
            try
            {
                var sl = CreateShellLinkAndLoad(lnk);
                sl.GetIDList(out var pidl);
                if (pidl != IntPtr.Zero)
                {
                    SHFILEINFO sfi = new();
                    uint flags = SHGFI_PIDL | SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
                    SHGetFileInfo(pidl, 0, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
                    ILFree(pidl);
                    img = FromHIcon(sfi.hIcon, destroy: true);
                    return img != null;
                }
            }
            catch { }
            return false;
        }

        // ----- utilities -----

        private static ImageSource? FromHIcon(IntPtr hIcon, bool destroy)
        {
            if (hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                if (destroy) DestroyIcon(hIcon);
            }
        }

        private static bool ExtractIcon(string iconPath, int index, bool large, out IntPtr hIcon)
        {
            hIcon = IntPtr.Zero;
            IntPtr[] largeArr = new IntPtr[1];
            IntPtr[] smallArr = new IntPtr[1];

            uint extracted = ExtractIconEx(iconPath, index, large ? largeArr : null, large ? null : smallArr, 1);
            if (extracted > 0)
            {
                hIcon = large ? largeArr[0] : smallArr[0];
                return hIcon != IntPtr.Zero;
            }
            return false;
        }

        private static IShellLinkW CreateShellLinkAndLoad(string lnkPath)
        {
            var sl = (IShellLinkW)new ShellLinkCoClass();
            ((IPersistFile)sl).Load(lnkPath, 0);
            return sl;
        }

        // ----- P/Invoke -----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_PIDL = 0x000000008;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll")]
        private static extern IntPtr SHGetFileInfo(
            IntPtr pidl, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
            IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        // Reuse COM types from ShellLink.cs
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
    }
}