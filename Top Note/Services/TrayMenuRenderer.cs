using System.Drawing;
using System.Windows.Forms;

namespace Top_Note.Services
{
    // Colours the tray menu to match the app theme instead of the default grey WinForms look.
    public class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayColors(ThemeManager.IsDark))
        {
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is not ToolStripLabel)
            {
                e.TextColor = e.Item.Enabled ? TrayColors.Text(ThemeManager.IsDark) : Color.Gray;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var pen = new Pen(TrayColors.Text(ThemeManager.IsDark), 1.6f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
            });
        }

        private sealed class TrayColors : ProfessionalColorTable
        {
            private readonly bool dark;

            public TrayColors(bool dark)
            {
                this.dark = dark;
                UseSystemColors = false;
            }

            public static Color Text(bool dark) => dark ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);

            private Color Background => dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.White;
            private Color Hover => dark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xF0, 0xF0, 0xF0);
            private Color Border => dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xE5, 0xE5, 0xE5);

            public override Color ToolStripDropDownBackground => Background;
            public override Color ImageMarginGradientBegin => Background;
            public override Color ImageMarginGradientMiddle => Background;
            public override Color ImageMarginGradientEnd => Background;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Background;
            public override Color CheckBackground => Background;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Hover;
        }
    }
}
