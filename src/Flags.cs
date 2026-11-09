using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace JoeBrowser
{
    /// <summary>
    /// Circular country flags. The artwork is drawn as vector shapes and clipped to a disc, so
    /// the pickers read at a glance on any DPI without shipping a bitmap per country.
    /// </summary>
    internal static class Flags
    {
        public static void Draw(Graphics g, string code, RectangleF box)
        {
            float d = Math.Min(box.Width, box.Height);
            if (g == null || d <= 4f) return;
            string cc = Code(code);

            var r = new RectangleF(box.X + (box.Width - d) / 2f + 0.5f,
                box.Y + (box.Height - d) / 2f + 0.5f, d - 1f, d - 1f);

            var old = g.SmoothingMode;
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(r);
                    g.SetClip(clip, CombineMode.Replace);
                    DrawArt(g, cc, r);
                }
                g.ResetClip();
                using (var pen = new Pen(Theme.AccentBorder, Math.Max(1f, d / 22f)))
                    g.DrawEllipse(pen, r);
            }
            finally
            {
                g.Restore(state);
                g.SmoothingMode = old;
            }
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

        private static string Code(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string s = value.Trim().ToUpperInvariant();
            // Locale tags ("en-GB") resolve to their region; bare regions pass through.
            int dash = s.LastIndexOf('-');
            if (dash > 0 && dash + 2 <= s.Length) s = s.Substring(dash + 1);
            return s.Length <= 2 ? s : s.Substring(0, 2);
        }

        private static void DrawArt(Graphics g, string c, RectangleF r)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            switch (c)
            {
                case "US": DrawUs(g, r); return;
                case "GB": DrawUnionJack(g, r, true); return;
                case "AU": DrawAu(g, r); return;
                case "NZ": DrawAu(g, r); return;
                case "CA": DrawCa(g, r); return;
                case "DE": Bands(g, r, C.Black, C.Red, C.Gold); return;
                case "FR": Vert(g, r, C.Blue, C.White, C.Red); return;
                case "ES": DrawEs(g, r); return;
                case "IT": Vert(g, r, C.Green, C.White, C.Red); return;
                case "NL": Bands(g, r, C.Red, C.White, C.Blue); return;
                case "BE": Vert(g, r, C.Black, C.Yellow, C.Red); return;
                case "CH": DrawCh(g, r); return;
                case "AT": Bands(g, r, C.Red, C.White, C.Red); return;
                case "SE": DrawNordic(g, r, C.Blue, C.Yellow, 0.34f, 0f); return;
                case "NO": DrawNordic(g, r, C.Red, C.White, 0.42f, 0.22f, C.Blue); return;
                case "DK": DrawNordic(g, r, C.Red, C.White, 0.30f, 0f); return;
                case "FI": DrawNordic(g, r, C.White, C.Blue, 0.34f, 0f); return;
                case "IS": DrawNordic(g, r, C.Blue, C.White, 0.34f, 0.18f, C.Red); return;
                case "PL": Bands(g, r, C.White, C.Red); return;
                case "PT": DrawPt(g, r); return;
                case "IE": Vert(g, r, C.Green, C.White, C.Orange); return;
                case "RU": Bands(g, r, C.White, C.Blue, C.Red); return;
                case "UA": Bands(g, r, C.Blue, C.Yellow); return;
                case "TR": DrawTr(g, r); return;
                case "BR": DrawBr(g, r); return;
                case "MX": DrawMx(g, r); return;
                case "AR": DrawAr(g, r); return;
                case "CO": BandsW(g, r, new[] { 0.5f, 0.25f, 0.25f }, C.Yellow, C.Blue, C.Red); return;
                case "IN": DrawIn(g, r); return;
                case "PK": DrawPk(g, r); return;
                case "AE": DrawAe(g, r); return;
                case "SA": DrawSa(g, r); return;
                case "IL": DrawIl(g, r); return;
                case "SG": DrawSg(g, r); return;
                case "MY": DrawMy(g, r); return;
                case "TH": BandsW(g, r, new[] { 0.17f, 0.16f, 0.34f, 0.16f, 0.17f }, C.Red, C.White, C.Blue, C.White, C.Red); return;
                case "VN": DrawStarFlag(g, r, C.Red, C.Yellow); return;
                case "CN": DrawCn(g, r); return;
                case "ID": Bands(g, r, C.Red, C.White); return;
                case "PH": DrawPh(g, r); return;
                case "HK": DrawHk(g, r); return;
                case "JP": DrawJp(g, r); return;
                case "KR": DrawKr(g, r); return;
                case "ZA": DrawZa(g, r); return;
                case "NG": Vert(g, r, C.Green, C.White, C.Green); return;
                case "EG": DrawEg(g, r); return;
                default: DrawGeneric(g, r); return;
            }
        }

        // ---- primitives -------------------------------------------------------

        private static void Fill(Graphics g, RectangleF r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        private static void Bands(Graphics g, RectangleF r, params Color[] colors)
        {
            float bh = r.Height / colors.Length;
            for (int i = 0; i < colors.Length; i++)
                Fill(g, new RectangleF(r.X, r.Y + i * bh, r.Width, bh + 0.7f), colors[i]);
        }

        private static void BandsW(Graphics g, RectangleF r, float[] weights, params Color[] colors)
        {
            float yy = r.Y;
            for (int i = 0; i < colors.Length; i++)
            {
                float bh = r.Height * weights[i];
                Fill(g, new RectangleF(r.X, yy, r.Width, bh + 0.7f), colors[i]);
                yy += bh;
            }
        }

        private static void Vert(Graphics g, RectangleF r, params Color[] colors)
        {
            float bw = r.Width / colors.Length;
            for (int i = 0; i < colors.Length; i++)
                Fill(g, new RectangleF(r.X + i * bw, r.Y, bw + 0.7f, r.Height), colors[i]);
        }

        private static void Cross(Graphics g, RectangleF r, Color c, float thick)
        {
            float tw = r.Width * thick;
            Fill(g, new RectangleF(r.X + (r.Width - tw) / 2f, r.Y, tw, r.Height), c);
            float th = r.Height * thick;
            Fill(g, new RectangleF(r.X, r.Y + (r.Height - th) / 2f, r.Width, th), c);
        }

        private static void Star(Graphics g, PointF center, float radius, Color c, int points = 5)
        {
            var pts = new PointF[points * 2];
            for (int i = 0; i < pts.Length; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / points;
                float rr = (i % 2 == 0) ? radius : radius * 0.42f;
                pts[i] = new PointF(center.X + (float)Math.Cos(a) * rr, center.Y + (float)Math.Sin(a) * rr);
            }
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        private static void Triangle(Graphics g, RectangleF r, Color c)
        {
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, new[] { new PointF(r.Left, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Top + r.Height / 2f) });
        }

        // ---- flag artwork -----------------------------------------------------

        private static void DrawUs(Graphics g, RectangleF r)
        {
            Fill(g, r, C.White);
            float stripe = r.Height / 13f;
            for (int i = 0; i < 13; i += 2)
                Fill(g, new RectangleF(r.X, r.Y + i * stripe, r.Width, stripe + 0.5f), C.Red);
            float cw = r.Width * 0.42f, ch = stripe * 7f;
            Fill(g, new RectangleF(r.X, r.Y, cw, ch), C.Navy);
            using (var b = new SolidBrush(C.White))
            {
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 6; col++)
                    {
                        float sx = r.X + cw * (0.09f + col * 0.16f);
                        float sy = r.Y + ch * (0.10f + row * 0.19f);
                        g.FillEllipse(b, sx, sy, Math.Max(0.8f, r.Width * 0.025f), Math.Max(0.8f, r.Width * 0.025f));
                    }
            }
        }

        private static void DrawUnionJack(Graphics g, RectangleF area, bool standalone)
        {
            RectangleF r = area;
            Fill(g, r, C.UnionBlue);
            float t = Math.Max(1.1f, r.Height * 0.16f);
            using (var pen = new Pen(C.White, t)) { g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom); g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom); }
            using (var pen = new Pen(C.Red, t * 0.58f)) { g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom); g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom); }
            Cross(g, r, C.White, 0.34f);
            Cross(g, r, C.Red, 0.19f);
            if (standalone) return;
        }

        private static void DrawAu(Graphics g, RectangleF r)
        {
            Fill(g, r, C.UnionBlue);
            DrawUnionJack(g, new RectangleF(r.X, r.Y, r.Width * 0.5f, r.Height * 0.5f), false);
            Star(g, new PointF(r.X + r.Width * 0.30f, r.Y + r.Height * 0.74f), r.Width * 0.20f, C.White, 7);
            Star(g, new PointF(r.X + r.Width * 0.76f, r.Y + r.Height * 0.70f), r.Width * 0.075f, C.White);
            Star(g, new PointF(r.X + r.Width * 0.86f, r.Y + r.Height * 0.52f), r.Width * 0.075f, C.White);
            Star(g, new PointF(r.X + r.Width * 0.70f, r.Y + r.Height * 0.36f), r.Width * 0.065f, C.White);
            Star(g, new PointF(r.X + r.Width * 0.84f, r.Y + r.Height * 0.26f), r.Width * 0.065f, C.White);
            Star(g, new PointF(r.X + r.Width * 0.58f, r.Y + r.Height * 0.20f), r.Width * 0.055f, C.White);
        }

        private static void DrawCa(Graphics g, RectangleF r)
        {
            Fill(g, r, C.White);
            Fill(g, new RectangleF(r.X, r.Y, r.Width * 0.28f, r.Height), C.Red);
            Fill(g, new RectangleF(r.Right - r.Width * 0.28f, r.Y, r.Width * 0.28f, r.Height), C.Red);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f, s = r.Width * 0.13f;
            using (var b = new SolidBrush(C.Red))
                g.FillPolygon(b, new[]
                {
                    new PointF(cx, cy - s), new PointF(cx + s * 0.30f, cy - s * 0.38f),
                    new PointF(cx + s * 0.95f, cy - s * 0.58f), new PointF(cx + s * 0.58f, cy + s * 0.06f),
                    new PointF(cx + s * 1.05f, cy + s * 0.30f), new PointF(cx + s * 0.22f, cy + s * 0.36f),
                    new PointF(cx, cy + s * 1.05f), new PointF(cx - s * 0.22f, cy + s * 0.36f),
                    new PointF(cx - s * 1.05f, cy + s * 0.30f), new PointF(cx - s * 0.58f, cy + s * 0.06f),
                    new PointF(cx - s * 0.95f, cy - s * 0.58f), new PointF(cx - s * 0.30f, cy - s * 0.38f)
                });
        }

        private static void DrawEs(Graphics g, RectangleF r)
        {
            BandsW(g, r, new[] { 0.25f, 0.5f, 0.25f }, C.Red, C.Gold, C.Red);
            float cx = r.X + r.Width * 0.30f, cy = r.Y + r.Height * 0.5f, s = r.Width * 0.10f;
            using (var b = new SolidBrush(C.Red)) g.FillEllipse(b, cx - s * 0.45f, cy - s * 0.85f, s * 0.9f, s * 1.7f);
            using (var b = new SolidBrush(C.Gold)) g.FillEllipse(b, cx - s * 0.25f, cy - s * 0.25f, s * 0.5f, s * 0.5f);
        }

        private static void DrawCh(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Red);
            float tw = r.Width * 0.18f, th = r.Height * 0.52f;
            Fill(g, new RectangleF(r.X + (r.Width - tw) / 2f, r.Y + (r.Height - th) / 2f, tw, th), C.White);
            float tw2 = r.Width * 0.52f, th2 = r.Height * 0.18f;
            Fill(g, new RectangleF(r.X + (r.Width - tw2) / 2f, r.Y + (r.Height - th2) / 2f, tw2, th2), C.White);
        }

        private static void DrawNordic(Graphics g, RectangleF r, Color bg, Color cross, float thick, float inner, Color? innerColor = null)
        {
            Fill(g, r, bg);
            float tw = r.Width * thick;
            float cx = r.X + r.Width * 0.42f;
            Fill(g, new RectangleF(cx - tw / 2f, r.Y, tw, r.Height), cross);
            float th = r.Height * thick;
            Fill(g, new RectangleF(r.X, r.Y + (r.Height - th) / 2f, r.Width, th), cross);
            if (inner > 0 && innerColor.HasValue)
            {
                float iw = r.Width * inner;
                Fill(g, new RectangleF(cx - iw / 2f, r.Y, iw, r.Height), innerColor.Value);
                float ih = r.Height * inner;
                Fill(g, new RectangleF(r.X, r.Y + (r.Height - ih) / 2f, r.Width, ih), innerColor.Value);
            }
        }

        private static void DrawPt(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Red);
            Fill(g, new RectangleF(r.X, r.Y, r.Width * 0.40f, r.Height), C.Green);
            float cr = r.Width * 0.14f;
            using (var b = new SolidBrush(C.Yellow))
                g.FillEllipse(b, r.X + r.Width * 0.40f - cr, r.Y + r.Height * 0.5f - cr, cr * 2, cr * 2);
            using (var b = new SolidBrush(C.Red))
                g.FillEllipse(b, r.X + r.Width * 0.40f - cr * 0.55f, r.Y + r.Height * 0.5f - cr * 0.55f, cr * 1.1f, cr * 1.1f);
        }

        private static void DrawTr(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Red);
            float cx = r.X + r.Width * 0.42f, cy = r.Y + r.Height * 0.5f, rr = r.Width * 0.24f;
            using (var b = new SolidBrush(C.White)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
            using (var b = new SolidBrush(C.Red)) g.FillEllipse(b, cx - rr * 0.78f, cy - rr * 0.86f, rr * 1.96f, rr * 1.96f);
            Star(g, new PointF(r.X + r.Width * 0.66f, cy), r.Width * 0.13f, C.White);
        }

        private static void DrawBr(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Green);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f;
            using (var b = new SolidBrush(C.Yellow))
                g.FillPolygon(b, new[] { new PointF(cx, r.Y + r.Height * 0.14f), new PointF(r.Right - r.Width * 0.14f, cy), new PointF(cx, r.Bottom - r.Height * 0.14f), new PointF(r.X + r.Width * 0.14f, cy) });
            using (var b = new SolidBrush(C.Navy)) g.FillEllipse(b, cx - r.Width * 0.18f, cy - r.Width * 0.18f, r.Width * 0.36f, r.Width * 0.36f);
            using (var p = new Pen(C.White, Math.Max(1f, r.Width * 0.035f)))
                g.DrawArc(p, cx - r.Width * 0.18f, cy - r.Width * 0.18f, r.Width * 0.36f, r.Width * 0.36f, 160, 100);
        }

        private static void DrawMx(Graphics g, RectangleF r)
        {
            Vert(g, r, C.Green, C.White, C.Red);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f, s = r.Width * 0.09f;
            using (var b = new SolidBrush(C.Olive)) g.FillEllipse(b, cx - s * 0.75f, cy - s * 0.45f, s * 1.5f, s * 0.9f);
            using (var b = new SolidBrush(C.Navy)) g.FillEllipse(b, cx - s * 0.30f, cy - s * 0.85f, s * 0.6f, s * 0.6f);
        }

        private static void DrawAr(Graphics g, RectangleF r)
        {
            Bands(g, r, C.LightBlue, C.White, C.LightBlue);
            using (var b = new SolidBrush(C.Sun))
                g.FillEllipse(b, r.X + r.Width * 0.5f - r.Width * 0.09f, r.Y + r.Height * 0.5f - r.Width * 0.09f, r.Width * 0.18f, r.Width * 0.18f);
        }

        private static void DrawIn(Graphics g, RectangleF r)
        {
            Bands(g, r, C.Saffron, C.White, C.Green);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f, rr = r.Width * 0.14f;
            using (var p = new Pen(C.Navy, Math.Max(1f, r.Width * 0.025f))) g.DrawEllipse(p, cx - rr, cy - rr, rr * 2, rr * 2);
            using (var p = new Pen(C.Navy, Math.Max(0.6f, r.Width * 0.012f)))
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, cx, cy, cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
                }
        }

        private static void DrawPk(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Green);
            Fill(g, new RectangleF(r.X, r.Y, r.Width * 0.25f, r.Height), C.White);
            float cx = r.X + r.Width * 0.58f, cy = r.Y + r.Height * 0.5f, rr = r.Width * 0.18f;
            using (var b = new SolidBrush(C.White)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
            using (var b = new SolidBrush(C.Green)) g.FillEllipse(b, cx - rr * 0.68f, cy - rr * 0.90f, rr * 1.9f, rr * 1.9f);
            Star(g, new PointF(cx + rr * 0.72f, cy - rr * 0.62f), rr * 0.42f, C.White);
        }

        private static void DrawAe(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Green);
            Fill(g, new RectangleF(r.X, r.Y + r.Height / 3f, r.Width * 0.75f, r.Height / 3f + 0.5f), C.White);
            Fill(g, new RectangleF(r.X, r.Y + r.Height * 2f / 3f, r.Width * 0.75f, r.Height / 3f + 0.5f), C.Black);
            Fill(g, new RectangleF(r.X, r.Y, r.Width * 0.25f, r.Height), C.Red);
        }

        private static void DrawSa(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Green);
            float y = r.Y + r.Height * 0.58f;
            using (var p = new Pen(C.White, Math.Max(1f, r.Height * 0.06f)))
            {
                g.DrawLine(p, r.X + r.Width * 0.16f, y, r.X + r.Width * 0.84f, y);
                g.DrawLine(p, r.X + r.Width * 0.22f, y - r.Height * 0.14f, r.X + r.Width * 0.74f, y - r.Height * 0.14f);
            }
        }

        private static void DrawIl(Graphics g, RectangleF r)
        {
            Fill(g, r, C.White);
            Fill(g, new RectangleF(r.X, r.Y, r.Width, r.Height * 0.14f), C.Blue);
            Fill(g, new RectangleF(r.X, r.Bottom - r.Height * 0.14f, r.Width, r.Height * 0.14f), C.Blue);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f, s = r.Width * 0.20f;
            using (var p = new Pen(C.Blue, Math.Max(0.9f, r.Width * 0.025f)))
            {
                g.DrawPolygon(p, new[] { new PointF(cx, cy - s), new PointF(cx + s * 0.92f, cy + s * 0.55f), new PointF(cx - s * 0.92f, cy + s * 0.55f) });
                g.DrawPolygon(p, new[] { new PointF(cx, cy + s), new PointF(cx + s * 0.92f, cy - s * 0.55f), new PointF(cx - s * 0.92f, cy - s * 0.55f) });
            }
        }

        private static void DrawSg(Graphics g, RectangleF r)
        {
            Fill(g, r, C.White);
            Fill(g, new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f), C.Red);
            float cx = r.X + r.Width * 0.28f, cy = r.Y + r.Height * 0.27f, rr = r.Width * 0.16f;
            using (var b = new SolidBrush(C.White)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
            using (var b = new SolidBrush(C.Red)) g.FillEllipse(b, cx - rr * 0.70f, cy - rr * 0.92f, rr * 1.9f, rr * 1.9f);
            Star(g, new PointF(cx + rr * 0.20f, cy), rr * 0.55f, C.White);
            Star(g, new PointF(cx + rr * 1.05f, cy - rr * 0.55f), rr * 0.34f, C.White);
            Star(g, new PointF(cx + rr * 1.35f, cy - rr * 0.10f), rr * 0.34f, C.White);
            Star(g, new PointF(cx + rr * 1.22f, cy + rr * 0.45f), rr * 0.34f, C.White);
            Star(g, new PointF(cx + rr * 0.72f, cy + rr * 0.38f), rr * 0.34f, C.White);
        }

        private static void DrawMy(Graphics g, RectangleF r)
        {
            Fill(g, r, C.White);
            float stripe = r.Height / 14f;
            for (int i = 0; i < 14; i += 2)
                Fill(g, new RectangleF(r.X, r.Y + i * stripe, r.Width, stripe + 0.4f), C.Red);
            float cw = r.Width * 0.5f, ch = r.Height * 0.5f;
            Fill(g, new RectangleF(r.X, r.Y, cw, ch), C.Navy);
            float cx = r.X + cw * 0.42f, cy = r.Y + ch * 0.55f, rr = cw * 0.24f;
            using (var b = new SolidBrush(C.Yellow)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
            using (var b = new SolidBrush(C.Navy)) g.FillEllipse(b, cx - rr * 0.66f, cy - rr * 0.92f, rr * 1.9f, rr * 1.9f);
            Star(g, new PointF(cx + rr * 0.90f, cy), rr * 0.55f, C.Yellow);
        }

        private static void DrawStarFlag(Graphics g, RectangleF r, Color bg, Color star)
        {
            Fill(g, r, bg);
            Star(g, new PointF(r.X + r.Width * 0.5f, r.Y + r.Height * 0.5f), r.Width * 0.24f, star);
        }

        private static void DrawCn(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Red);
            Star(g, new PointF(r.X + r.Width * 0.24f, r.Y + r.Height * 0.30f), r.Width * 0.16f, C.Yellow);
            Star(g, new PointF(r.X + r.Width * 0.46f, r.Y + r.Height * 0.14f), r.Width * 0.055f, C.Yellow);
            Star(g, new PointF(r.X + r.Width * 0.54f, r.Y + r.Height * 0.26f), r.Width * 0.055f, C.Yellow);
            Star(g, new PointF(r.X + r.Width * 0.54f, r.Y + r.Height * 0.40f), r.Width * 0.055f, C.Yellow);
            Star(g, new PointF(r.X + r.Width * 0.46f, r.Y + r.Height * 0.52f), r.Width * 0.055f, C.Yellow);
        }

        private static void DrawPh(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Blue);
            Fill(g, new RectangleF(r.X, r.Y + r.Height * 0.5f, r.Width, r.Height * 0.5f + 0.5f), C.Red);
            Triangle(g, new RectangleF(r.X, r.Y, r.Width * 0.5f, r.Height), C.White);
            float cx = r.X + r.Width * 0.16f, cy = r.Y + r.Height * 0.5f;
            using (var b = new SolidBrush(C.Sun)) g.FillEllipse(b, cx - r.Width * 0.055f, cy - r.Width * 0.055f, r.Width * 0.11f, r.Width * 0.11f);
            Star(g, new PointF(cx + r.Width * 0.16f, cy - r.Height * 0.20f), r.Width * 0.04f, C.Sun);
            Star(g, new PointF(cx + r.Width * 0.22f, cy - r.Height * 0.04f), r.Width * 0.04f, C.Sun);
            Star(g, new PointF(cx + r.Width * 0.16f, cy + r.Height * 0.16f), r.Width * 0.04f, C.Sun);
        }

        private static void DrawHk(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Red);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f;
            using (var b = new SolidBrush(C.White))
                for (int i = 0; i < 5; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI * 2 / 5;
                    float px = cx + (float)Math.Cos(a) * r.Width * 0.16f;
                    float py = cy + (float)Math.Sin(a) * r.Width * 0.16f;
                    g.FillEllipse(b, px - r.Width * 0.09f, py - r.Width * 0.09f, r.Width * 0.18f, r.Width * 0.18f);
                }
        }

        private static void DrawJp(Graphics g, RectangleF r)
        {
            Fill(g, r, C.White);
            using (var b = new SolidBrush(C.Red))
                g.FillEllipse(b, r.X + r.Width * 0.27f, r.Y + r.Height * 0.27f, r.Width * 0.46f, r.Height * 0.46f);
        }

        private static void DrawKr(Graphics g, RectangleF r)
        {
            Fill(g, r, C.White);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f, rr = r.Width * 0.22f;
            using (var b = new SolidBrush(C.Red)) g.FillPie(b, cx - rr, cy - rr, rr * 2, rr * 2, 180, 180);
            using (var b = new SolidBrush(C.Blue)) g.FillPie(b, cx - rr, cy - rr, rr * 2, rr * 2, 0, 180);
            using (var b = new SolidBrush(C.Red)) g.FillEllipse(b, cx - rr * 0.52f, cy - rr * 0.52f, rr * 1.04f, rr * 1.04f);
            using (var b = new SolidBrush(C.Blue)) g.FillPie(b, cx - rr * 0.52f, cy - rr * 0.52f, rr * 1.04f, rr * 1.04f, 0, 180);
            using (var p = new Pen(C.Black, Math.Max(0.8f, r.Width * 0.035f)))
            {
                for (int k = 0; k < 4; k++)
                {
                    double a = Math.PI / 4 + k * Math.PI / 2;
                    float px = cx + (float)Math.Cos(a) * rr * 1.55f;
                    float py = cy + (float)Math.Sin(a) * rr * 1.55f;
                    g.DrawLine(p, px - r.Width * 0.06f, py, px + r.Width * 0.06f, py);
                }
            }
        }

        private static void DrawZa(Graphics g, RectangleF r)
        {
            Fill(g, r, C.Red);
            Fill(g, new RectangleF(r.X, r.Y + r.Height * 0.5f, r.Width, r.Height * 0.5f + 0.5f), C.Blue);
            using (var b = new SolidBrush(C.Green))
                g.FillPolygon(b, new[] { new PointF(r.X, r.Y + r.Height * 0.30f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.5f), new PointF(r.X, r.Y + r.Height * 0.70f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.5f) });
            using (var b = new SolidBrush(C.Black))
                g.FillPolygon(b, new[] { new PointF(r.X, r.Y), new PointF(r.X + r.Width * 0.30f, r.Y + r.Height * 0.5f), new PointF(r.X, r.Bottom) });
            using (var b = new SolidBrush(C.Gold))
                g.FillPolygon(b, new[] { new PointF(r.X, r.Y + r.Height * 0.36f), new PointF(r.X + r.Width * 0.33f, r.Y + r.Height * 0.5f), new PointF(r.X, r.Y + r.Height * 0.64f) });
        }

        private static void DrawEg(Graphics g, RectangleF r)
        {
            Bands(g, r, C.Red, C.White, C.Black);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f;
            using (var b = new SolidBrush(C.Gold))
                g.FillEllipse(b, cx - r.Width * 0.05f, cy - r.Width * 0.05f, r.Width * 0.10f, r.Width * 0.10f);
        }

        private static void DrawGeneric(Graphics g, RectangleF r)
        {
            Fill(g, r, Theme.AccentOnBg);
            using (var b = new SolidBrush(Theme.AccentText))
            {
                float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f, rr = r.Width * 0.24f;
                g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
                using (var p = new Pen(Theme.AccentOnBg, Math.Max(1f, r.Width * 0.06f)))
                {
                    g.DrawEllipse(p, cx - rr, cy - rr, rr * 2, rr * 2);
                    g.DrawLine(p, cx - rr, cy, cx + rr, cy);
                    g.DrawEllipse(p, cx - rr * 0.55f, cy - rr, rr * 1.1f, rr * 2);
                }
            }
        }

        /// <summary>Flag palette. Kept separate from the UI theme: flags are real-world colours.</summary>
        private static class C
        {
            public static readonly Color Red = Color.FromArgb(206, 17, 38);
            public static readonly Color White = Color.FromArgb(255, 255, 255);
            public static readonly Color Blue = Color.FromArgb(0, 82, 165);
            public static readonly Color UnionBlue = Color.FromArgb(1, 33, 105);
            public static readonly Color Navy = Color.FromArgb(35, 47, 110);
            public static readonly Color Black = Color.FromArgb(24, 24, 24);
            public static readonly Color Green = Color.FromArgb(0, 122, 61);
            public static readonly Color Gold = Color.FromArgb(255, 196, 0);
            public static readonly Color Yellow = Color.FromArgb(255, 205, 0);
            public static readonly Color Orange = Color.FromArgb(255, 136, 62);
            public static readonly Color LightBlue = Color.FromArgb(116, 172, 223);
            public static readonly Color Saffron = Color.FromArgb(255, 153, 51);
            public static readonly Color Sun = Color.FromArgb(247, 181, 60);
            public static readonly Color Olive = Color.FromArgb(100, 120, 60);
        }
    }
}
