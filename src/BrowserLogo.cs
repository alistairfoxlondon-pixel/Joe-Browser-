using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace JoeBrowser
{
    /// <summary>
    /// The real vendor logos, embedded in the executable as PNG resources.
    ///
    /// Why embedded files rather than the vector art in <see cref="BrowserMark"/>: a user
    /// recognises Chrome, Edge, Brave and Firefox by their actual mark, and a hand-drawn
    /// approximation reads as a knock-off in a tool whose whole job is looking legitimate.
    /// The bitmaps are 256-330 px masters, so they stay sharp from a 16 px list row to a
    /// 44 px card and at 200 % DPI.
    ///
    /// Logos are trademarks of their owners and are used here only to identify the browser
    /// they belong to. Every engine still falls back to the drawn mark if a resource is
    /// missing, so a stripped build degrades instead of crashing.
    /// </summary>
    internal static class BrowserLogo
    {
        private const string Prefix = "JoeBrowser.logos.";

        private static readonly Dictionary<string, Bitmap> Cache =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Missing =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Engine keys that ship a real logo.</summary>
        public static bool Has(string kind)
        {
            return Load(kind) != null;
        }

        /// <summary>
        /// Draws the logo fitted into <paramref name="box"/>, keeping its aspect ratio and
        /// centred. Returns false when there is no artwork for this engine, so the caller can
        /// fall back to the vector mark.
        /// </summary>
        public static bool TryDraw(Graphics g, RectangleF box, string kind)
        {
            Bitmap bmp = Load(kind);
            if (bmp == null || g == null || box.Width < 3 || box.Height < 3) return false;

            float scale = Math.Min(box.Width / bmp.Width, box.Height / bmp.Height);
            float w = bmp.Width * scale, h = bmp.Height * scale;
            var dst = new RectangleF(box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);

            var oldMode = g.InterpolationMode;
            var oldOffset = g.PixelOffsetMode;
            var oldSmoothing = g.SmoothingMode;
            var oldCompositing = g.CompositingQuality;
            try
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(bmp, dst);
            }
            finally
            {
                g.InterpolationMode = oldMode;
                g.PixelOffsetMode = oldOffset;
                g.SmoothingMode = oldSmoothing;
                g.CompositingQuality = oldCompositing;
            }
            return true;
        }

        private static Bitmap Load(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            string key = kind.Trim().ToLowerInvariant();
            Bitmap hit;
            if (Cache.TryGetValue(key, out hit)) return hit;
            if (Missing.Contains(key)) return null;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(Prefix + key + ".png"))
                {
                    if (s == null) { Missing.Add(key); return null; }
                    /* Copy through a MemoryStream: a Bitmap built straight from a manifest
                       stream keeps the stream alive for the lifetime of the image. */
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        ms.Position = 0;
                        var bmp = new Bitmap(ms);
                        Cache[key] = bmp;
                        return bmp;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Browser logo resource could not be loaded: " + key, ex);
                Missing.Add(key);
                return null;
            }
        }
    }
}
