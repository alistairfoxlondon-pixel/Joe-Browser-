using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace JoeBrowser
{
    /// <summary>
    /// Circular country chips shown beside locale names. Tinted disc plus ISO code, so the list
    /// reads at a glance without pulling multicolour flag artwork into a monochrome app.
    /// </summary>
    internal static class Flags
    {
        public static void Draw(Graphics g, string code, RectangleF box)
        {
            float d = Math.Min(box.Width, box.Height);
            if (g == null || d <= 4f) return;
            string text = Code(code);
            if (text.Length == 0) return;

            var r = new RectangleF(box.X + (box.Width - d) / 2f + 0.5f,
                box.Y + (box.Height - d) / 2f + 0.5f, d - 1f, d - 1f);

            var old = g.TextRenderingHint;
            using (var fill = new SolidBrush(Theme.AccentOnBg)) g.FillEllipse(fill, r);
            using (var pen = new Pen(Theme.AccentBorder, 1f)) g.DrawEllipse(pen, r);
            try
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                Font f = FitFont(g, r.Width, text);
                float h = f.GetHeight(g);
                Ui.Text(g, text, f, Theme.AccentText, r.X, r.Y + (r.Height - h) / 2f, r.Width, "center");
            }
            finally { g.TextRenderingHint = old; }
        }

        /// <summary>A standalone chip for menus and other places that need an image.</summary>
        public static Bitmap NewBitmap(string code, int px)
        {
            if (px < 4) px = 4;
            var b = new Bitmap(px, px, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                Ui.Quality(g);
                Draw(g, code, new RectangleF(0, 0, px, px));
            }
            return b;
        }

        private static Font FitFont(Graphics g, float disc, string text)
        {
            float maxWidth = disc * 0.66f;
            for (float px = disc * 0.46f; px > disc * 0.24f; px -= 0.75f)
            {
                Font f = Theme.F(Math.Round(px * 2f) / 2f, FontStyle.Bold);
                if (Ui.Measure(g, text, f).Width <= maxWidth) return f;
            }
            return Theme.H3;
        }

        private static string Code(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string s = value.Trim().ToUpperInvariant();
            return s.Length <= 2 ? s : s.Substring(0, 2);
        }
    }
}
