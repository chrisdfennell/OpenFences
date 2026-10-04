using System.Drawing;
using System.Windows.Forms;

namespace Pickets.Services
{
    /// <summary>
    /// Light or dark look for the tray icon's menu (a WinForms menu, so the WPF palettes don't
    /// reach it). Colors match Themes/Palette.*.xaml's menu colors.
    /// </summary>
    internal static class TrayMenuTheme
    {
        public static void Apply(ContextMenuStrip menu, bool light)
        {
            menu.Renderer = new Renderer(light ? LightColors : DarkColors);
            menu.BackColor = (light ? LightColors : DarkColors).Back;
        }

        private sealed record Colors(Color Back, Color Text, Color Disabled, Color Hover, Color Border, Color Separator);

        private static readonly Colors DarkColors = new(
            Back: Color.FromArgb(0x15, 0x18, 0x21), Text: Color.White, Disabled: Color.FromArgb(0x7A, 0x82, 0x94),
            Hover: Color.FromArgb(0x23, 0x2A, 0x39), Border: Color.FromArgb(0x24, 0x29, 0x38), Separator: Color.FromArgb(0x30, 0x38, 0x4A));

        private static readonly Colors LightColors = new(
            Back: Color.FromArgb(0xFB, 0xFB, 0xFD), Text: Color.FromArgb(0x1A, 0x1D, 0x24), Disabled: Color.FromArgb(0x9A, 0xA1, 0xAE),
            Hover: Color.FromArgb(0xEA, 0xEE, 0xF5), Border: Color.FromArgb(0xD5, 0xDA, 0xE2), Separator: Color.FromArgb(0xE0, 0xE3, 0xEA));

        private sealed class Table : ProfessionalColorTable
        {
            private readonly Colors _c;
            public Table(Colors c) { _c = c; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground => _c.Back;
            public override Color ImageMarginGradientBegin => _c.Back;
            public override Color ImageMarginGradientMiddle => _c.Back;
            public override Color ImageMarginGradientEnd => _c.Back;
            public override Color MenuBorder => _c.Border;
            public override Color MenuItemBorder => _c.Hover;
            public override Color MenuItemSelected => _c.Hover;
            public override Color MenuItemSelectedGradientBegin => _c.Hover;
            public override Color MenuItemSelectedGradientEnd => _c.Hover;
            public override Color MenuItemPressedGradientBegin => _c.Hover;
            public override Color MenuItemPressedGradientEnd => _c.Hover;
            public override Color SeparatorDark => _c.Separator;
            public override Color SeparatorLight => _c.Separator;
            public override Color CheckBackground => _c.Hover;
            public override Color CheckSelectedBackground => _c.Hover;
            public override Color CheckPressedBackground => _c.Hover;
        }

        // Text and arrow colors are set while drawing, so items added later (the Profiles
        // submenu is rebuilt each time it opens) get them too.
        private sealed class Renderer : ToolStripProfessionalRenderer
        {
            private readonly Colors _c;
            public Renderer(Colors c) : base(new Table(c)) { _c = c; RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? _c.Text : _c.Disabled;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = _c.Text;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                // The default check glyph is drawn black; draw our own in the text color.
                var r = e.ImageRectangle;
                using var pen = new Pen(_c.Text, 1.6f);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                    new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f),
                });
            }
        }
    }
}
