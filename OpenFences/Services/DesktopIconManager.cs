using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing; // Point
using System.Runtime.InteropServices;

namespace OpenFences.Services
{
    /// <summary>
    /// Cross-process control of the *real* desktop icons. The desktop icons live in
    /// explorer's "SysListView32" (located by <see cref="DesktopHelper"/>). We read their
    /// names/positions and move them by talking to that list view across the process
    /// boundary.
    ///
    /// Moving (LVM_SETITEMPOSITION) packs the coordinates into lParam, so it works
    /// cross-process with a plain SendMessage. Reading text/position (LVM_GETITEMTEXT /
    /// LVM_GETITEMPOSITION) marshals pointers into the *caller's* address space, so we must
    /// allocate the receiving buffers inside explorer.exe (VirtualAllocEx) and copy the
    /// results back with ReadProcessMemory.
    ///
    /// LIMITS (by design):
    ///  - LVM_SETITEMPOSITION packs x/y as 16-bit values, so coordinates must be 0..32767.
    ///    That's fine for the primary monitor and normal virtual desktops; we clamp.
    ///  - This pokes an undocumented-ish shell control and is Win10/11/explorer specific.
    /// </summary>
    internal static class DesktopIconManager
    {
        // ----- ListView messages -----
        private const int LVM_FIRST = 0x1000;
        private const int LVM_GETITEMCOUNT = LVM_FIRST + 4;    // 0x1004
        private const int LVM_SETITEMPOSITION = LVM_FIRST + 15;   // 0x100F (wParam=index, lParam=MAKELPARAM(x,y))
        private const int LVM_GETITEMPOSITION = LVM_FIRST + 16;   // 0x1010 (lParam = POINT* in target)
        private const int LVM_REDRAWITEMS = LVM_FIRST + 21;   // 0x1015
        private const int LVM_GETITEMTEXTW = LVM_FIRST + 115;  // 0x1073 (lParam = LVITEM* in target)

        private const uint LVIF_TEXT = 0x0001;

        // ----- Desktop list view window style (for auto-arrange) -----
        private const int GWL_STYLE = -16;
        private const int LVS_AUTOARRANGE = 0x0100;

        // DefView "Auto arrange icons" verb. Sending it toggles the state.
        private const int WM_COMMAND = 0x0111;
        private const int FVIDM_AUTOARRANGE = 0x7051;

        private const uint SMTO_ABORTIFHUNG = 0x0002;

        // OpenProcess access rights for VirtualAllocEx + R/W process memory.
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const int MAX_TEXT = 260;

        /// <summary>A real desktop icon: its list-view index, display text, and current position
        /// (in list-view client device pixels).</summary>
        internal readonly record struct DesktopIcon(int Index, string Name, Point Pos);

        /// <summary>
        /// Ensure the desktop is in a state where our explicit icon positions stick:
        /// turn OFF "Auto arrange icons" (leave "Align to grid" untouched). No-op if it's
        /// already off. Safe to call repeatedly.
        /// </summary>
        public static void EnsureClassicLayout()
        {
            try
            {
                IntPtr lv = DesktopHelper.GetIconListView();
                IntPtr defView = DesktopHelper.GetDefView();
                if (lv == IntPtr.Zero || defView == IntPtr.Zero) return;

                long style = GetWindowLongPtr(lv, GWL_STYLE).ToInt64();
                if ((style & LVS_AUTOARRANGE) != 0)
                {
                    // The verb toggles; only send it when auto-arrange is currently ON.
                    SendMessageTimeout(defView, WM_COMMAND, new IntPtr(FVIDM_AUTOARRANGE),
                                       IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _);
                }
            }
            catch { /* best effort */ }
        }

        /// <summary>Enumerate every real desktop icon with its name and current position.</summary>
        public static IReadOnlyList<DesktopIcon> Enumerate()
        {
            var result = new List<DesktopIcon>();
            IntPtr lv = DesktopHelper.GetIconListView();
            if (lv == IntPtr.Zero) return result;

            int count = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0) return result;

            GetWindowThreadProcessId(lv, out uint pid);
            IntPtr hProc = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProc == IntPtr.Zero) return result;

            IntPtr remoteText = IntPtr.Zero, remoteItem = IntPtr.Zero, remotePoint = IntPtr.Zero;
            try
            {
                int textBytes = MAX_TEXT * 2; // unicode
                int itemBytes = Marshal.SizeOf<LVITEM>();

                remoteText = VirtualAllocEx(hProc, IntPtr.Zero, (uint)textBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                remoteItem = VirtualAllocEx(hProc, IntPtr.Zero, (uint)itemBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                remotePoint = VirtualAllocEx(hProc, IntPtr.Zero, 8u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (remoteText == IntPtr.Zero || remoteItem == IntPtr.Zero || remotePoint == IntPtr.Zero)
                    return result;

                for (int i = 0; i < count; i++)
                {
                    string name = ReadItemText(hProc, lv, i, remoteItem, remoteText, textBytes);
                    Point pos = ReadItemPosition(hProc, lv, i, remotePoint);
                    result.Add(new DesktopIcon(i, name, pos));
                }
            }
            catch { /* return what we have */ }
            finally
            {
                if (remoteText != IntPtr.Zero) VirtualFreeEx(hProc, remoteText, 0, MEM_RELEASE);
                if (remoteItem != IntPtr.Zero) VirtualFreeEx(hProc, remoteItem, 0, MEM_RELEASE);
                if (remotePoint != IntPtr.Zero) VirtualFreeEx(hProc, remotePoint, 0, MEM_RELEASE);
                CloseHandle(hProc);
            }

            return result;
        }

        /// <summary>Find the list-view index of the icon whose display name matches (case-insensitive).
        /// <paramref name="skipIndices"/> lets callers resolve duplicate names to the first unclaimed one.</summary>
        public static int? FindIndexByName(string name, ISet<int>? skipIndices = null)
        {
            foreach (var icon in Enumerate())
            {
                if (skipIndices != null && skipIndices.Contains(icon.Index)) continue;
                if (string.Equals(icon.Name, name, StringComparison.OrdinalIgnoreCase))
                    return icon.Index;
            }
            return null;
        }

        /// <summary>Move a desktop icon to a position given in list-view client device pixels.</summary>
        public static void SetPosition(int index, int clientX, int clientY)
        {
            IntPtr lv = DesktopHelper.GetIconListView();
            if (lv == IntPtr.Zero) return;

            int x = Math.Clamp(clientX, 0, 32767);
            int y = Math.Clamp(clientY, 0, 32767);
            IntPtr lParam = new IntPtr((y << 16) | (x & 0xFFFF));
            SendMessage(lv, LVM_SETITEMPOSITION, new IntPtr(index), lParam);
        }

        /// <summary>Repaint the desktop list view after moving items so the new layout shows immediately.</summary>
        public static void Refresh()
        {
            IntPtr lv = DesktopHelper.GetIconListView();
            if (lv == IntPtr.Zero) return;
            int count = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (count > 0) SendMessage(lv, LVM_REDRAWITEMS, IntPtr.Zero, new IntPtr(count - 1));
            InvalidateRect(lv, IntPtr.Zero, true);
            UpdateWindow(lv);
        }

        /// <summary>Convert a screen point (device pixels) into desktop list-view client coordinates.</summary>
        public static Point ScreenToListViewClient(Point screenDevicePx)
        {
            IntPtr lv = DesktopHelper.GetIconListView();
            if (lv == IntPtr.Zero) return screenDevicePx;
            var p = new POINT { X = screenDevicePx.X, Y = screenDevicePx.Y };
            ScreenToClient(lv, ref p);
            return new Point(p.X, p.Y);
        }

        // ---------- internals ----------

        private static string ReadItemText(IntPtr hProc, IntPtr lv, int index,
                                           IntPtr remoteItem, IntPtr remoteText, int textBytes)
        {
            var item = new LVITEM
            {
                mask = LVIF_TEXT,
                iItem = index,
                iSubItem = 0,
                pszText = remoteText,
                cchTextMax = MAX_TEXT
            };

            byte[] itemBytes = StructToBytes(item);
            if (!WriteProcessMemory(hProc, remoteItem, itemBytes, (uint)itemBytes.Length, out _))
                return string.Empty;

            // Returns the number of chars copied into the (remote) text buffer.
            IntPtr len = SendMessage(lv, LVM_GETITEMTEXTW, new IntPtr(index), remoteItem);
            int chars = Math.Clamp((int)len, 0, MAX_TEXT);
            if (chars == 0) return string.Empty;

            byte[] buf = new byte[textBytes];
            if (!ReadProcessMemory(hProc, remoteText, buf, (uint)textBytes, out _))
                return string.Empty;

            return System.Text.Encoding.Unicode.GetString(buf, 0, chars * 2);
        }

        private static Point ReadItemPosition(IntPtr hProc, IntPtr lv, int index, IntPtr remotePoint)
        {
            SendMessage(lv, LVM_GETITEMPOSITION, new IntPtr(index), remotePoint);
            byte[] buf = new byte[8];
            if (!ReadProcessMemory(hProc, remotePoint, buf, 8u, out _))
                return Point.Empty;
            int x = BitConverter.ToInt32(buf, 0);
            int y = BitConverter.ToInt32(buf, 4);
            return new Point(x, y);
        }

        private static byte[] StructToBytes<T>(T value) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, false);
                Marshal.Copy(ptr, bytes, 0, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return bytes;
        }

        // ---------- P/Invoke ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        // 64-bit-accurate LVITEMW layout (explorer is 64-bit on 64-bit Windows; this app
        // ships win-x64/arm64 so IntPtr widths match the target process).
        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam,
                                                        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
                                                    uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
                                                      uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
                                                     uint nSize, out IntPtr lpNumberOfBytesRead);
    }
}
