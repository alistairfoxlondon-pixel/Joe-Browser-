using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace JoeBrowser
{
    /// <summary>
    /// The Joe Browser mark: a shield carrying a white J.
    ///
    /// Everything is vector geometry, including the J - a font glyph would change shape with
    /// whichever face happened to be installed, and at 16 px a hinted letter loses its
    /// counters. The stroke weights are chosen per size so the mark survives the taskbar.
    /// </summary>
    internal static class Logo
    {
        /// <summary>Shield silhouette in a unit box. Wide shoulders, soft bottom point.</summary>
        public static GraphicsPath Shield(RectangleF box)
        {
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;
            var p = new GraphicsPath();
            p.StartFigure();
            p.AddBezier(
                x + w * 0.500f, y + h * 0.030f,
                x + w * 0.660f, y + h * 0.128f,
                x + w * 0.830f, y + h * 0.158f,
                x + w * 0.955f, y + h * 0.158f);
            p.AddLine(x + w * 0.955f, y + h * 0.158f, x + w * 0.955f, y + h * 0.520f);
            p.AddBezier(
                x + w * 0.955f, y + h * 0.520f,
                x + w * 0.955f, y + h * 0.745f,
                x + w * 0.775f, y + h * 0.892f,
                x + w * 0.500f, y + h * 0.972f);
            p.AddBezier(
                x + w * 0.500f, y + h * 0.972f,
                x + w * 0.225f, y + h * 0.892f,
                x + w * 0.045f, y + h * 0.745f,
                x + w * 0.045f, y + h * 0.520f);
            p.AddLine(x + w * 0.045f, y + h * 0.520f, x + w * 0.045f, y + h * 0.158f);
            p.AddBezier(
                x + w * 0.045f, y + h * 0.158f,
                x + w * 0.170f, y + h * 0.158f,
                x + w * 0.340f, y + h * 0.128f,
                x + w * 0.500f, y + h * 0.030f);
            p.CloseFigure();
            return p;
        }

        /// <summary>
        /// The J, as a stroked path rather than a glyph. Stroke widths are fractions of the
        /// box, so the mark keeps the same optical weight from 16 px to 512 px.
        /// </summary>
        public static void JPath(Graphics g, RectangleF box, Color color, float widthFraction)
        {
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;
            using (var p = new GraphicsPath())
            {
                p.StartFigure();
                p.AddLine(x + w * 0.606f, y + h * 0.238f, x + w * 0.606f, y + h * 0.588f);
                p.AddBezier(
                    x + w * 0.606f, y + h * 0.588f,
                    x + w * 0.606f, y + h * 0.702f,
                    x + w * 0.540f, y + h * 0.762f,
                    x + w * 0.452f, y + h * 0.762f);
                p.AddBezier(
                    x + w * 0.452f, y + h * 0.762f,
                    x + w * 0.390f, y + h * 0.762f,
                    x + w * 0.348f, y + h * 0.728f,
                    x + w * 0.328f, y + h * 0.674f);

                float width = Math.Max(1f, box.Width * widthFraction);
                using (var pen = new Pen(color, width))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, p);
                }
            }
        }

        /// <summary>The mark reduced to a tile and the letter, for 16-19 px surfaces.</summary>
        private static void DrawCompact(Graphics g, RectangleF box)
        {
            using (var tile = Ui.Round(box, box.Width * 0.22f))
            using (var brush = new LinearGradientBrush(
                new PointF(box.Left, box.Top), new PointF(box.Left, box.Bottom),
                Color.FromArgb(0x3A, 0xA6, 0xFF), Color.FromArgb(0x14, 0x5A, 0xC0)))
            {
                brush.GammaCorrection = true;
                g.FillPath(brush, tile);
            }
            var glyph = new RectangleF(box.X, box.Y, box.Width, box.Height);
            JPath(g, glyph, Color.White, 0.22f);
        }

        public static void Draw(Graphics g, RectangleF box)
        {
            if (g == null || box.Width < 3 || box.Height < 3) return;

            var oldSmoothing = g.SmoothingMode;
            var oldHint = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                bool small = box.Width < 32;

                /* At 16 px the shield's bottom point and shoulders turn to mud, and the glyph
                   has no room left to read as a "J". A plain rounded tile with the letter is
                   the same mark, just tuned for the size it is actually drawn at - which is the
                   whole difference between a crisp taskbar icon and a coloured smudge. */
                if (box.Width < 20) { DrawCompact(g, box); return; }

                using (var path = Shield(box))
                {
                    /* A vertical gradient reads as light from above, which is what makes a flat
                       shape look like a physical tile. GammaCorrection removes the banding that
                       a plain two-stop blend leaves across a 256 px icon. */
                    using (var brush = new LinearGradientBrush(
                        new PointF(box.Left, box.Top),
                        new PointF(box.Left, box.Bottom),
                        Color.FromArgb(0x3A, 0xA6, 0xFF),
                        Color.FromArgb(0x14, 0x5A, 0xC0)))
                    {
                        brush.GammaCorrection = true;
                        g.FillPath(brush, path);
                    }

                    /* Rim light, drawn INSIDE the silhouette. Stroking on the path edge blends
                       the stroke with the transparent margin, which turns a white rim into a
                       grey outline - the exact look that makes an icon seem cheap. */
                    if (!small)
                    {
                        var oldClip = g.Clip;
                        g.SetClip(path, CombineMode.Intersect);
                        using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1.2f, box.Width / 40f)))
                            g.DrawPath(pen, path);
                        g.Clip = oldClip;
                    }
                }

                /* The stroke has to be heavier relative to the box as the box shrinks, or the
                   counter closes up and the letter becomes a blob at 16 px. */
                float widthFraction = box.Width < 24 ? 0.150f : box.Width < 48 ? 0.128f : 0.112f;
                JPath(g, box, Color.White, widthFraction);
            }
            finally
            {
                g.SmoothingMode = oldSmoothing;
                g.TextRenderingHint = oldHint;
            }
        }

        /// <summary>
        /// Renders the mark to a transparent bitmap. Used for the .ico so the icon and the
        /// in-app artwork come from one drawing routine instead of two that can drift.
        /// </summary>
        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(Math.Max(1, size), Math.Max(1, size),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float pad = size * 0.045f;
                Draw(g, new RectangleF(pad, pad, size - pad * 2, size - pad * 2));
            }
            return bmp;
        }
    }
}
