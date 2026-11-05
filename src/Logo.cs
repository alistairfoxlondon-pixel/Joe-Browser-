using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace JoeBrowser
{
    /// <summary>
    /// Joe Browser mark: a shield (private, isolated browsing) carrying a white J,
    /// filled with the windows-ui accent gradient so it matches the app chrome.
    /// Drawn as a vector, so the same artwork serves the header, About page and .ico.
    /// </summary>
    internal static class Logo
    {
        public static GraphicsPath Shield(RectangleF box)
        {
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;
            var p = new GraphicsPath();
            p.StartFigure();
            p.AddBezier(
                x + w * 0.50f, y + h * 0.02f,
                x + w * 0.66f, y + h * 0.12f,
                x + w * 0.84f, y + h * 0.15f,
                x + w * 0.97f, y + h * 0.15f);
            p.AddLine(x + w * 0.97f, y + h * 0.15f, x + w * 0.97f, y + h * 0.54f);
            p.AddBezier(
                x + w * 0.97f, y + h * 0.54f,
                x + w * 0.97f, y + h * 0.76f,
                x + w * 0.78f, y + h * 0.91f,
                x + w * 0.50f, y + h * 0.98f);
            p.AddBezier(
                x + w * 0.50f, y + h * 0.98f,
                x + w * 0.22f, y + h * 0.91f,
                x + w * 0.03f, y + h * 0.76f,
                x + w * 0.03f, y + h * 0.54f);
            p.AddLine(x + w * 0.03f, y + h * 0.54f, x + w * 0.03f, y + h * 0.15f);
            p.AddBezier(
                x + w * 0.03f, y + h * 0.15f,
                x + w * 0.16f, y + h * 0.15f,
                x + w * 0.34f, y + h * 0.12f,
                x + w * 0.50f, y + h * 0.02f);
            p.CloseFigure();
            return p;
        }

        public static void Draw(Graphics g, RectangleF box)
        {
            if (g == null || box.Width < 4 || box.Height < 4) return;

            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var path = Shield(box))
                {
                    using (var brush = new LinearGradientBrush(
                        new PointF(box.Left, box.Top),
                        new PointF(box.Right, box.Bottom),
                        Color.FromArgb(0x00, 0x78, 0xD7),
                        Color.FromArgb(0x60, 0xCD, 0xFF)))
                        g.FillPath(brush, path);

                    if (box.Width >= 24)
                    {
                        using (var pen = new Pen(Color.FromArgb(52, 255, 255, 255), Math.Max(1f, box.Width / 22f)))
                            g.DrawPath(pen, path);
                    }
                }

                float size = box.Height * 0.56f;
                var font = Theme.F(size, FontStyle.Bold);
                using (var sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    var rect = new RectangleF(box.X, box.Y + box.Height * 0.02f, box.Width, box.Height * 0.92f);
                    using (var b = new SolidBrush(Color.White))
                        g.DrawString("J", font, b, rect, sf);
                }
            }
            finally { g.SmoothingMode = old; }
        }
    }
}
