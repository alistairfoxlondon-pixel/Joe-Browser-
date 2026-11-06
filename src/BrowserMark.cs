using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace JoeBrowser
{
    /// <summary>
    /// Vector browser marks used on profile rows. These are drawn, never loaded from disk,
    /// so they stay sharp at any DPI and need no third-party artwork or licences.
    /// The circular silhouette follows the windows-ui / Fluent "app avatar" shape.
    /// </summary>
    internal static class BrowserMark
    {
        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        /// <summary>
        /// Engine the app resolves "auto" profiles to. Set once at startup from BrowserLocator so a
        /// profile created with automatic detection carries the real logo instead of a placeholder.
        /// </summary>
        public static string AutoKind = "";

        public static string KindFor(Profile p)
        {
            string kind = p != null ? (p.BrowserKind ?? "") : "";
            if (kind.Equals("custom", StringComparison.OrdinalIgnoreCase)) return "custom";
            if (kind.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "chrome";
            if (kind.Equals("chromium", StringComparison.OrdinalIgnoreCase)) return "chromium";
            if (kind.Equals("edge", StringComparison.OrdinalIgnoreCase)) return "edge";
            if (kind.Equals("brave", StringComparison.OrdinalIgnoreCase)) return "brave";
            if (kind.Equals("firefox", StringComparison.OrdinalIgnoreCase)) return "firefox";

            string brand = p != null && p.Fp != null ? (p.Fp.Brand ?? "") : "";
            if (brand.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) >= 0) return "firefox";
            if (brand.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0) return "edge";

            // Only the explicit browser path is a strong enough hint for auto profiles.
            string path = p != null ? (p.BrowserPath ?? "") : "";
            if (path.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) >= 0) return "firefox";
            if (path.IndexOf("brave", StringComparison.OrdinalIgnoreCase) >= 0) return "brave";
            if (path.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0) return "edge";
            if (path.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return path.IndexOf("chromium", StringComparison.OrdinalIgnoreCase) >= 0 ? "chromium" : "chrome";
            }
            return string.IsNullOrEmpty(AutoKind) ? "auto" : AutoKind;
        }

        public static string LabelFor(string kind)
        {
            switch (Norm(kind))
            {
                case "chrome": return "Google Chrome";
                case "chromium": return "Chromium";
                case "edge": return "Microsoft Edge";
                case "brave": return "Brave";
                case "firefox": return "Mozilla Firefox";
                case "custom": return "Custom browser";
                default: return "Auto detect";
            }
        }

        public static Color TintFor(string kind)
        {
            switch (Norm(kind))
            {
                case "chrome": return Color.FromArgb(30, 66, 133, 244);
                case "chromium": return Color.FromArgb(30, 90, 145, 205);
                case "edge": return Color.FromArgb(30, 15, 142, 199);
                case "brave": return Color.FromArgb(30, 251, 84, 43);
                case "firefox": return Color.FromArgb(30, 255, 149, 40);
                default: return Theme.AccentOnBg;
            }
        }

        private static string Norm(string kind)
        {
            return string.IsNullOrEmpty(kind) ? "auto" : kind.ToLowerInvariant();
        }

        public static void Draw(Graphics g, RectangleF box, string kind)
        {
            if (g == null || box.Width < 4 || box.Height < 4) return;
            string k = Norm(kind);
            float d = Math.Min(box.Width, box.Height);
            var r = new RectangleF(box.X + (box.Width - d) / 2f, box.Y + (box.Height - d) / 2f, d, d);

            GraphicsState state = g.Save();
            try
            {
                Ui.Quality(g);
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(r);
                    g.SetClip(clip, CombineMode.Intersect);
                }

                switch (k)
                {
                    case "chrome": Chrome(g, r); break;
                    case "chromium": Chromium(g, r); break;
                    case "edge": Edge(g, r); break;
                    case "brave": Brave(g, r); break;
                    case "firefox": Firefox(g, r); break;
                    case "custom": Neutral(g, r, true); break;
                    default: Neutral(g, r, false); break;
                }
            }
            finally { g.Restore(state); }

            // A hairline ring keeps the mark crisp on both light and dark surfaces.
            using (var pen = new Pen(Color.FromArgb(Theme.Dark ? 40 : 24, Theme.Dark ? 255 : 0, Theme.Dark ? 255 : 0, Theme.Dark ? 255 : 0), Math.Max(1f, d / 32f)))
                g.DrawEllipse(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
        }

        private static void Chrome(Graphics g, RectangleF r)
        {
            float x = r.X, y = r.Y, d = r.Width;
            using (var b = new SolidBrush(C(0xF6, 0xF7, 0xF8))) g.FillEllipse(b, r);

            // Three 120 degree sectors form the coloured ring.
            var pie = new RectangleF(x + d * 0.02f, y + d * 0.02f, d * 0.96f, d * 0.96f);
            g.FillPie(new SolidBrush(C(0xEA, 0x43, 0x35)), Rectangle.Round(pie), -90f, 120f);
            g.FillPie(new SolidBrush(C(0x34, 0xA8, 0x53)), Rectangle.Round(pie), 30f, 120f);
            g.FillPie(new SolidBrush(C(0xFB, 0xBC, 0x05)), Rectangle.Round(pie), 150f, 120f);

            float cx = x + d / 2f, cy = y + d / 2f;
            float white = d * 0.68f, blue = d * 0.56f;
            using (var b = new SolidBrush(C(0xF6, 0xF7, 0xF8)))
                g.FillEllipse(b, cx - white / 2f, cy - white / 2f, white, white);
            using (var b = new SolidBrush(C(0x42, 0x85, 0xF4)))
                g.FillEllipse(b, cx - blue / 2f, cy - blue / 2f, blue, blue);
            using (var b = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
                g.FillEllipse(b, x + d * 0.10f, y + d * 0.07f, d * 0.80f, d * 0.44f);
        }

        private static void Chromium(Graphics g, RectangleF r)
        {
            float x = r.X, y = r.Y, d = r.Width;
            using (var bg = new LinearGradientBrush(new PointF(x, y), new PointF(x + d, y + d),
                C(0xD7, 0xE6, 0xF3), C(0x7C, 0xA8, 0xD6)))
                g.FillEllipse(bg, r);

            float cx = x + d / 2f, cy = y + d / 2f;
            // Six rounded petals around a lighter core, the Chromium silhouette.
            for (int i = 0; i < 6; i++)
            {
                float a = (float)(i * Math.PI / 3.0);
                float px = cx + (float)Math.Cos(a) * d * 0.27f;
                float py = cy + (float)Math.Sin(a) * d * 0.27f;
                float pw = d * 0.30f;
                using (var b = new SolidBrush(i % 2 == 0 ? C(0x4A, 0x82, 0xBD) : C(0x8F, 0xB6, 0xDD)))
                    g.FillEllipse(b, px - pw / 2f, py - pw / 2f, pw, pw);
            }
            float core = d * 0.44f;
            using (var b = new SolidBrush(C(0xE9, 0xF2, 0xFA)))
                g.FillEllipse(b, cx - core / 2f, cy - core / 2f, core, core);
            using (var b = new SolidBrush(C(0x3C, 0x76, 0xB4)))
                g.FillEllipse(b, cx - core * 0.31f, cy - core * 0.31f, core * 0.62f, core * 0.62f);
        }

        private static void Edge(Graphics g, RectangleF r)
        {
            float x = r.X, y = r.Y, d = r.Width;
            using (var bg = new LinearGradientBrush(new PointF(x, y + d), new PointF(x + d, y),
                C(0x0B, 0x7F, 0xC7), C(0x37, 0xC5, 0xF2)))
                g.FillEllipse(bg, r);

            using (var wave = new GraphicsPath())
            {
                wave.StartFigure();
                wave.AddBezier(x + d * 0.10f, y + d * 0.66f,
                    x + d * 0.24f, y + d * 0.29f,
                    x + d * 0.63f, y + d * 0.24f,
                    x + d * 0.90f, y + d * 0.42f);
                wave.AddBezier(x + d * 0.90f, y + d * 0.42f,
                    x + d * 0.64f, y + d * 0.34f,
                    x + d * 0.38f, y + d * 0.55f,
                    x + d * 0.47f, y + d * 0.76f);
                wave.AddBezier(x + d * 0.47f, y + d * 0.76f,
                    x + d * 0.29f, y + d * 0.72f,
                    x + d * 0.16f, y + d * 0.70f,
                    x + d * 0.10f, y + d * 0.66f);
                wave.CloseFigure();
                using (var b = new SolidBrush(Color.FromArgb(238, 255, 255, 255))) g.FillPath(b, wave);
            }
            using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                g.FillEllipse(b, x + d * 0.66f, y + d * 0.22f, d * 0.20f, d * 0.20f);
        }

        private static void Brave(Graphics g, RectangleF r)
        {
            float x = r.X, y = r.Y, d = r.Width;
            using (var bg = new SolidBrush(C(0xFB, 0xF2, 0xEC))) g.FillEllipse(bg, r);

            using (var shield = new GraphicsPath())
            {
                shield.StartFigure();
                shield.AddLine(x + d * 0.50f, y + d * 0.10f, x + d * 0.86f, y + d * 0.22f);
                shield.AddLine(x + d * 0.86f, y + d * 0.22f, x + d * 0.79f, y + d * 0.66f);
                shield.AddLine(x + d * 0.79f, y + d * 0.66f, x + d * 0.50f, y + d * 0.92f);
                shield.AddLine(x + d * 0.50f, y + d * 0.92f, x + d * 0.21f, y + d * 0.66f);
                shield.AddLine(x + d * 0.21f, y + d * 0.66f, x + d * 0.14f, y + d * 0.22f);
                shield.CloseFigure();
                using (var bg = new LinearGradientBrush(new PointF(x, y), new PointF(x + d, y + d),
                    C(0xFB, 0x54, 0x2B), C(0xF7, 0x9A, 0x2B)))
                    g.FillPath(bg, shield);
            }

            using (var pen = new Pen(Color.White, Math.Max(1.4f, d * 0.065f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new[]
                {
                    new PointF(x + d * 0.34f, y + d * 0.47f),
                    new PointF(x + d * 0.50f, y + d * 0.37f),
                    new PointF(x + d * 0.66f, y + d * 0.47f),
                    new PointF(x + d * 0.50f, y + d * 0.57f),
                    new PointF(x + d * 0.34f, y + d * 0.47f)
                });
                g.DrawLine(pen, x + d * 0.50f, y + d * 0.57f, x + d * 0.50f, y + d * 0.70f);
            }
        }

        private static void Firefox(Graphics g, RectangleF r)
        {
            float x = r.X, y = r.Y, d = r.Width;
            using (var bg = new LinearGradientBrush(new PointF(x + d, y), new PointF(x, y + d),
                C(0x59, 0x2C, 0x8C), C(0xFF, 0x71, 0x2A)))
                g.FillEllipse(bg, r);

            // Warm swirl tail: two overlapping arcs that read as the fox flame.
            using (var tail = new GraphicsPath())
            {
                tail.StartFigure();
                tail.AddBezier(x + d * 0.18f, y + d * 0.70f,
                    x + d * 0.10f, y + d * 0.24f,
                    x + d * 0.58f, y + d * 0.02f,
                    x + d * 0.88f, y + d * 0.30f);
                tail.AddBezier(x + d * 0.88f, y + d * 0.30f,
                    x + d * 0.74f, y + d * 0.20f,
                    x + d * 0.54f, y + d * 0.28f,
                    x + d * 0.62f, y + d * 0.48f);
                tail.AddBezier(x + d * 0.62f, y + d * 0.48f,
                    x + d * 0.42f, y + d * 0.46f,
                    x + d * 0.30f, y + d * 0.66f,
                    x + d * 0.18f, y + d * 0.70f);
                tail.CloseFigure();
                using (var b = new SolidBrush(C(0xFF, 0xBD, 0x2E))) g.FillPath(b, tail);
            }
            float core = d * 0.34f;
            using (var b = new SolidBrush(C(0xFF, 0xE1, 0x74)))
                g.FillEllipse(b, x + d * 0.52f, y + d * 0.40f, core, core);
        }

        private static void Neutral(Graphics g, RectangleF r, bool custom)
        {
            using (var bg = new LinearGradientBrush(new PointF(r.X, r.Y), new PointF(r.Right, r.Bottom),
                Theme.AccentHi, Theme.Accent))
                g.FillEllipse(bg, r);

            float ic = r.Width * 0.46f;
            var ir = new RectangleF(r.X + (r.Width - ic) / 2f, r.Y + (r.Height - ic) / 2f, ic, ic);
            if (custom) { Icons.Draw(g, "window", ir, Color.White); return; }

            using (var pen = new Pen(Color.FromArgb(225, 255, 255, 255), Math.Max(1f, r.Width / 34f)))
            {
                g.DrawEllipse(pen, ir);
                g.DrawArc(pen, new RectangleF(ir.X, ir.Y + ir.Height * 0.02f, ir.Width, ir.Height * 0.96f), -68f, 136f);
                g.DrawArc(pen, new RectangleF(ir.X + ir.Width * 0.18f, ir.Y, ir.Width * 0.64f, ir.Height), 0f, 360f);
                g.DrawLine(pen, ir.X, ir.Y + ir.Height / 2f, ir.Right, ir.Y + ir.Height / 2f);
            }
        }
    }
}
