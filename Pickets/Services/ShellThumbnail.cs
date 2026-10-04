using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pickets.Services
{
    /// <summary>
    /// Picture/video previews from Windows' thumbnail cache (IShellItemImageFactory), the same
    /// ones Explorer shows. Returns null when there's no thumbnail, so callers fall back to the
    /// file's icon. Must be called on an STA thread; the result is frozen.
    /// </summary>
    internal static class ShellThumbnail
    {
        private static readonly string[] Extensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif", ".ico", ".svg",
            ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".wmv", ".webm"
        };

        public static bool HasPreview(string path) =>
            !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) &&
            Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

        public static ImageSource? Get(string path, int sizePx) => GetImage(path, sizePx, thumbnailOnly: true);

        /// <summary>The thumbnail if there is one, otherwise the item's icon at a large size
        /// (used by Quick Look for files it can't preview).</summary>
        public static ImageSource? GetLarge(string path, int sizePx) => GetImage(path, sizePx, thumbnailOnly: false);

        private static ImageSource? GetImage(string path, int sizePx, bool thumbnailOnly)
        {
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var obj) != 0 || obj is not IShellItemImageFactory factory)
                    return null;
                try
                {
                    var size = new SIZE { cx = sizePx, cy = sizePx };
                    int flags = SIIGBF_BIGGERSIZEOK | (thumbnailOnly ? SIIGBF_THUMBNAILONLY : 0);
                    if (factory.GetImage(size, flags, out hbmp) != 0 || hbmp == IntPtr.Zero)
                        return null;
                }
                finally { Marshal.ReleaseComObject(factory); }

                return FromHBitmap(hbmp);
            }
            catch { return null; }
            finally { if (hbmp != IntPtr.Zero) DeleteObject(hbmp); }
        }

        // Copies the 32-bit DIB section (premultiplied BGRA) so transparency survives, which
        // Imaging.CreateBitmapSourceFromHBitmap doesn't preserve.
        private static BitmapSource? FromHBitmap(IntPtr hbmp)
        {
            if (GetObject(hbmp, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero) return null;
            int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
            if (ds.dsBm.bmBitsPixel != 32 || w <= 0 || h <= 0) return null;

            var pixels = new byte[stride * h];
            Marshal.Copy(ds.dsBm.bmBits, pixels, 0, pixels.Length);

            // A positive height means the rows are stored bottom-up.
            if (ds.dsBmih.biHeight > 0)
            {
                var flipped = new byte[pixels.Length];
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);
                pixels = flipped;
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_THUMBNAILONLY = 0x8;

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DIBSECTION
        {
            public BITMAP dsBm;
            public BITMAPINFOHEADER dsBmih;
            public uint dsBitfields0, dsBitfields1, dsBitfields2;
            public IntPtr dshSection;
            public uint dsOffset;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out DIBSECTION obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    }
}
