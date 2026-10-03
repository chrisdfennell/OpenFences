// Usage: dotnet run --project tools/IconGen -- OpenFences/Assets
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Generates open-fence.ico / open-fence.png: blue rounded tile with a white picket fence.
// Geometry is on a 16-unit grid so 16/32/48/64 land on whole pixels.
var outDir = args.Length > 0 ? args[0] : ".";
int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

Bitmap Render(int size)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.Half;
    g.Clear(Color.Transparent);
    float u = size / 16f;

    // Tile
    float r = 3.5f * u;
    using (var tile = RoundRect(0, 0, size, size, r))
    using (var bg = new LinearGradientBrush(new PointF(0, 0), new PointF(0, size),
               Color.FromArgb(0x3B, 0x82, 0xF6), Color.FromArgb(0x1D, 0x4E, 0xD8)))
        g.FillPath(bg, tile);

    // Fence: 3 pickets (2 units wide, pointed tops) + 2 rails
    using var fence = new GraphicsPath();
    foreach (float x in new[] { 3f, 7f, 11f })
    {
        fence.AddPolygon(new[]
        {
            new PointF(x * u, 5 * u), new PointF((x + 1) * u, 3 * u), new PointF((x + 2) * u, 5 * u),
            new PointF((x + 2) * u, 13 * u), new PointF(x * u, 13 * u),
        });
    }
    // Rails are 1.5 units tall; snap to whole pixels at small sizes so they stay crisp
    float railH = size <= 24 ? Math.Max(1, (int)(1.5f * u)) : 1.5f * u;
    foreach (float ry in new[] { 6.5f, 10f })
    {
        float y = size <= 24 ? (float)Math.Floor(ry * u + 0.5f) : ry * u;
        fence.AddRectangle(new RectangleF(2 * u, y, 12 * u, railH));
    }
    fence.FillMode = FillMode.Winding;

    // Soft drop shadow on larger sizes
    if (size >= 32)
    {
        using var m = new Matrix();
        m.Translate(0, 0.5f * u);
        using var shadow = (GraphicsPath)fence.Clone();
        shadow.Transform(m);
        using var sb = new SolidBrush(Color.FromArgb(70, 0x0B, 0x23, 0x6B));
        g.FillPath(sb, shadow);
    }
    g.FillPath(Brushes.White, fence);
    return bmp;
}

static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
{
    var p = new GraphicsPath();
    float d = r * 2;
    p.AddArc(x, y, d, d, 180, 90);
    p.AddArc(x + w - d, y, d, d, 270, 90);
    p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
    p.AddArc(x, y + h - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
}

static byte[] Dib(Bitmap bmp)
{
    int w = bmp.Width, h = bmp.Height;
    int maskStride = ((w + 31) / 32) * 4;
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);
    bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
    bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
    for (int y = h - 1; y >= 0; y--)
        for (int x = 0; x < w; x++)
        {
            var c = bmp.GetPixel(x, y);
            bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
        }
    for (int y = h - 1; y >= 0; y--)
    {
        var row = new byte[maskStride];
        for (int x = 0; x < w; x++)
            if (bmp.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
        bw.Write(row);
    }
    return ms.ToArray();
}

static byte[] Png(Bitmap bmp)
{
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

var frames = sizes.Select(s => (s, img: Render(s))).ToList();
var blobs = frames.Select(f => f.s >= 256 ? Png(f.img) : Dib(f.img)).ToList();

using (var fs = File.Create(Path.Combine(outDir, "open-fence.ico")))
using (var bw = new BinaryWriter(fs))
{
    bw.Write((short)0); bw.Write((short)1); bw.Write((short)frames.Count);
    int offset = 6 + 16 * frames.Count;
    for (int i = 0; i < frames.Count; i++)
    {
        int s = frames[i].s;
        bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
        bw.Write(blobs[i].Length); bw.Write(offset);
        offset += blobs[i].Length;
    }
    foreach (var b in blobs) bw.Write(b);
}
frames.Last().img.Save(Path.Combine(outDir, "open-fence.png"), ImageFormat.Png);

// Preview sheet: each size at 1x on light and dark, plus 16/32 zoomed
int pw = sizes.Sum(s => s + 10) + 10;
using var sheet = new Bitmap(pw + 2 * (16 * 8 + 10) + 32 * 4 + 10, 2 * 270);
using (var g = Graphics.FromImage(sheet))
{
    g.Clear(Color.FromArgb(243, 243, 243));
    g.FillRectangle(new SolidBrush(Color.FromArgb(32, 32, 32)), 0, 270, sheet.Width, 270);
    foreach (int row in new[] { 0, 270 })
    {
        int x = 10;
        foreach (var f in frames) { g.DrawImageUnscaled(f.img, x, row + 10); x += f.s + 10; }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(frames[0].img, x, row + 10, 16 * 8, 16 * 8); x += 16 * 8 + 10;
        g.DrawImage(frames[3].img, x, row + 10, 32 * 4, 32 * 4);
    }
}
sheet.Save(Path.Combine(outDir, "preview.png"), ImageFormat.Png);
Console.WriteLine("ok");
