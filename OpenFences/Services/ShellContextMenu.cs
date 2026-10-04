using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenFences.Services
{
    /// <summary>
    /// Shows Windows' own right-click menu (Open with, Send to, Properties, menu entries other
    /// apps add…) for files, with a few OpenFences items added at the top. The owner window
    /// must forward menu messages to <see cref="HandleMenuMessage"/> while it's open so
    /// submenus such as "Open with" and "Send to" fill in.
    /// </summary>
    internal static class ShellContextMenu
    {
        public sealed record Extra(string Label, Action OnClick);

        private const uint FirstShellId = 1, LastShellId = 0x7FFF, FirstExtraId = 0x8000;
        private static IContextMenu2? _active2;
        private static IContextMenu3? _active3;

        /// <summary>Returns false if the menu couldn't be built (caller shows its own menu).
        /// <paramref name="onRename"/> runs when the user picks Rename, since there's no
        /// Explorer view here to do an in-place rename.</summary>
        public static bool Show(IntPtr owner, IReadOnlyList<string> paths, IReadOnlyList<Extra> extras, Action onRename)
        {
            if (paths.Count == 0) return false;

            // GetUIObjectOf needs items from a single folder; use the ones next to the first.
            var parentDir = Path.GetDirectoryName(paths[0]);
            var items = paths.Where(p => string.Equals(Path.GetDirectoryName(p), parentDir, StringComparison.OrdinalIgnoreCase)).ToList();

            var pidls = new List<IntPtr>();
            IntPtr parentPidl = IntPtr.Zero, menuPtr = IntPtr.Zero, hmenu = IntPtr.Zero;
            IShellFolder? parent = null;
            IContextMenu? cm = null;
            try
            {
                foreach (var p in items)
                {
                    if (SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return false;
                    pidls.Add(pidl);
                }

                var iidFolder = typeof(IShellFolder).GUID;
                if (SHBindToParent(pidls[0], ref iidFolder, out var folderObj, out _) != 0) return false;
                parent = (IShellFolder)folderObj;

                var children = pidls.Select(ILFindLastID).ToArray();
                var iidMenu = typeof(IContextMenu).GUID;
                if (parent.GetUIObjectOf(owner, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out menuPtr) != 0) return false;
                cm = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);

                hmenu = CreatePopupMenu();
                uint pos = 0;
                for (int i = 0; i < extras.Count; i++)
                    InsertMenu(hmenu, pos++, MF_BYPOSITION | MF_STRING, (UIntPtr)(FirstExtraId + i), extras[i].Label);
                if (extras.Count > 0) InsertMenu(hmenu, pos++, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);

                uint flags = CMF_NORMAL | CMF_CANRENAME;
                if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) flags |= CMF_EXTENDEDVERBS;
                cm.QueryContextMenu(hmenu, pos, FirstShellId, LastShellId, flags);

                _active2 = cm as IContextMenu2;
                _active3 = cm as IContextMenu3;

                GetCursorPos(out var pt);
                SetForegroundWindow(owner);
                uint cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, owner, IntPtr.Zero);
                _active2 = null;
                _active3 = null;
                if (cmd == 0) return true; // dismissed

                if (cmd >= FirstExtraId)
                {
                    int i = (int)(cmd - FirstExtraId);
                    if (i < extras.Count) extras[i].OnClick();
                    return true;
                }

                uint offset = cmd - FirstShellId;
                if (string.Equals(Verb(cm, offset), "rename", StringComparison.OrdinalIgnoreCase))
                {
                    onRename();
                    return true;
                }

                var info = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = owner,
                    lpVerb = (IntPtr)offset,
                    lpVerbW = (IntPtr)offset,
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = pt
                };
                cm.InvokeCommand(ref info);
                return true;
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("Shell context menu", ex);
                return false;
            }
            finally
            {
                _active2 = null;
                _active3 = null;
                if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
                if (cm != null) Marshal.ReleaseComObject(cm);
                if (menuPtr != IntPtr.Zero) Marshal.Release(menuPtr);
                if (parent != null) Marshal.ReleaseComObject(parent);
                foreach (var p in pidls) Marshal.FreeCoTaskMem(p);
            }
        }

        /// <summary>Forward owner-window messages while the menu is open (fills "Send to",
        /// "Open with" and other owner-drawn submenus). Returns true if handled.</summary>
        public static bool HandleMenuMessage(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (msg != WM_INITMENUPOPUP && msg != WM_DRAWITEM && msg != WM_MEASUREITEM && msg != WM_MENUCHAR) return false;
            try
            {
                if (_active3 != null) return _active3.HandleMenuMsg2((uint)msg, wParam, lParam, out result) == 0;
                if (_active2 != null) return _active2.HandleMenuMsg((uint)msg, wParam, lParam) == 0;
            }
            catch { /* ignore */ }
            return false;
        }

        private static string? Verb(IContextMenu cm, uint offset)
        {
            try
            {
                var sb = new StringBuilder(256);
                return cm.GetCommandString((UIntPtr)offset, GCS_VERBW, IntPtr.Zero, sb, (uint)sb.Capacity) == 0 ? sb.ToString() : null;
            }
            catch { return null; }
        }

        // ---------- Interop ----------
        private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_BYPOSITION = 0x400;
        private const uint CMF_NORMAL = 0x0, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
        private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
        private const uint GCS_VERBW = 0x4;
        private const int CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000, SW_SHOWNORMAL = 1;
        private const int WM_INITMENUPOPUP = 0x0117, WM_DRAWITEM = 0x002B, WM_MEASUREITEM = 0x002C, WM_MENUCHAR = 0x0120;
        private const int VK_SHIFT = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CMINVOKECOMMANDINFOEX
        {
            public int cbSize;
            public int fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
            public int nShow;
            public int dwHotKey;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
            public IntPtr lpVerbW;
            public string? lpParametersW;
            public string? lpDirectoryW;
            public string? lpTitleW;
            public POINT ptInvoke;
        }

        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attrs);
            [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);
            [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
            [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
            [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr pName);
            [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
        }

        [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
        }

        [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu2
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
            [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu3
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
            [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
            [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

        [DllImport("shell32.dll")]
        private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv, out IntPtr ppidlLast);

        [DllImport("shell32.dll")]
        private static extern IntPtr ILFindLastID(IntPtr pidl);

        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool InsertMenu(IntPtr hMenu, uint position, uint flags, UIntPtr id, string? text);
        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern short GetKeyState(int vKey);
    }
}
