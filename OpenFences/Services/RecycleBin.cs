using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenFences.Services
{
    /// <summary>
    /// Sends files and folders to the Recycle Bin through the shell, so a delete from a fence
    /// can always be undone. Where a drive has no Recycle Bin (e.g. some network shares) the
    /// shell asks the user before deleting permanently, rather than doing it silently.
    /// </summary>
    internal static class RecycleBin
    {
        /// <summary>Returns true if the item is gone afterwards; false if it failed or the
        /// user cancelled.</summary>
        public static bool Send(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return true;

            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0", // double-null-terminated list
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING
            };

            int rc = SHFileOperation(ref op);
            if (rc != 0 || op.fAnyOperationsAborted) return false;
            return !File.Exists(path) && !Directory.Exists(path);
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_WANTNUKEWARNING = 0x4000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    }
}
