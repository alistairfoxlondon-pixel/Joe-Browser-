using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace JoeBrowser
{
    /// <summary>
    /// Google Material Icons rendered from the bundled font, so every glyph carries the same
    /// stroke weight and optical size. The API is unchanged from the old hand-drawn set.
    /// </summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, string> Glyph = Build();

        /// <summary>Single-stroke glyphs that stay legible on the solid face at the smallest sizes.</summary>
        private static readonly HashSet<string> Strokes = new HashSet<string>(StringComparer.Ordinal)
        {
            "plus", "min", "close", "x", "check", "play", "dots", "dots-h", "menu",
            "chevron-down", "chevron-up", "chevron-left", "chevron-right", "arrow-right",
            "sort", "filter", "sync", "refresh", "power"
        };

        private static string Ch(int cp) { return ((char)cp).ToString(); }

        private static Dictionary<string, string> Build()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            // window controls
            d["plus"] = Ch(0xE145);        // add
            d["min"] = Ch(0xE15B);         // remove
            d["max"] = Ch(0xE5D0);         // fullscreen
            d["restore"] = Ch(0xE5D1);     // fullscreen_exit
            d["close"] = Ch(0xE5CD);
            d["x"] = Ch(0xE5CD);
            d["chevron-down"] = Ch(0xE5CF);
            d["chevron-up"] = Ch(0xE5CE);
            d["chevron-right"] = Ch(0xE5CC);
            d["chevron-left"] = Ch(0xE5CB);
            d["arrow-right"] = Ch(0xE5C8);
            d["dots"] = Ch(0xE5D4);         // more_vert
            d["dots-h"] = Ch(0xE5D3);       // more_horiz
            d["menu"] = Ch(0xE5D2);
            d["sort"] = Ch(0xE164);
            d["filter"] = Ch(0xE152);       // filter_list
            d["search"] = Ch(0xE8B6);
            d["refresh"] = Ch(0xE5D5);
            d["settings"] = Ch(0xE8B8);
            d["sliders"] = Ch(0xE429);      // tune
            d["command"] = Ch(0xE8B1);      // terminal
            // content
            d["play"] = Ch(0xE037);
            d["layers"] = Ch(0xE53B);
            d["grid"] = Ch(0xE9B0);         // grid_view
            d["apps"] = Ch(0xE5C3);
            d["extension"] = Ch(0xE87B);    // extension (puzzle piece)
            d["folder"] = Ch(0xE2C7);
            d["file"] = Ch(0xE873);         // description
            d["pencil"] = Ch(0xE3C9);       // edit
            d["copy"] = Ch(0xE14D);         // content_copy
            d["clipboard"] = Ch(0xE14E);    // content_paste
            d["trash"] = Ch(0xE872);        // delete
            d["download"] = Ch(0xE2C4);     // file_download
            d["external"] = Ch(0xE89E);     // open_in_new
            d["window"] = Ch(0xE069);       // web_asset
            d["sparkle"] = Ch(0xE65F);      // auto_awesome
            d["user"] = Ch(0xE7FD);         // person
            d["account"] = Ch(0xE853);      // account_circle
            d["info"] = Ch(0xE88E);
            d["help"] = Ch(0xE887);
            d["alert"] = Ch(0xE002);        // warning
            d["error"] = Ch(0xE000);
            d["check"] = Ch(0xE5CA);
            d["check-circle"] = Ch(0xE86C);
            d["x-circle"] = Ch(0xE5C9);     // cancel
            d["dot"] = Ch(0xE837);          // radio_button_checked
            d["circle"] = Ch(0xE40C);       // panorama_fish_eye
            d["moon"] = Ch(0xE51C);         // dark_mode
            d["sun"] = Ch(0xE518);          // light_mode
            d["eye"] = Ch(0xE8F4);          // visibility
            d["eye-off"] = Ch(0xE8F5);      // visibility_off
            d["clock"] = Ch(0xE8B5);        // schedule
            d["pin"] = Ch(0xE55F);          // place
            d["bolt"] = Ch(0xE3E7);         // flash_on
            d["power"] = Ch(0xF053);        // restart_alt
            d["sync"] = Ch(0xEA18);         // sync_alt
            // privacy and identity
            d["shield"] = Ch(0xE32A);       // security
            d["shield-outline"] = Ch(0xE9E0);
            d["lock"] = Ch(0xE897);
            d["lock-open"] = Ch(0xE898);
            d["key"] = Ch(0xE0DA);          // vpn_key
            d["fingerprint"] = Ch(0xE90D);
            d["face"] = Ch(0xE87C);
            d["cookie"] = Ch(0xEAAC);
            d["policy"] = Ch(0xEA17);
            d["globe"] = Ch(0xE80B);        // public
            d["globe-off"] = Ch(0xF1CA);    // public_off
            d["language"] = Ch(0xE894);
            d["translate"] = Ch(0xE8E2);
            d["flag"] = Ch(0xE153);
            d["route"] = Ch(0xF184);        // alt_route
            d["link"] = Ch(0xE157);
            d["link-off"] = Ch(0xE16F);
            d["proxy"] = Ch(0xE328);        // router
            d["wifi"] = Ch(0xE63E);
            d["wifi-off"] = Ch(0xE648);
            d["explore"] = Ch(0xE87A);      // compass, used for timezone
            // hardware targets
            d["desktop"] = Ch(0xE30C);      // desktop_windows
            d["laptop"] = Ch(0xE31E);
            d["mac"] = Ch(0xE30B);          // desktop_mac
            d["linux"] = Ch(0xE30D);        // developer_board
            d["android"] = Ch(0xE859);
            d["iphone"] = Ch(0xE325);       // phone_iphone
            d["tablet"] = Ch(0xE32F);
            d["cpu"] = Ch(0xE30D);
            d["ram"] = Ch(0xE322);          // memory
            d["gpu"] = Ch(0xE912);          // important_devices
            d["screen"] = Ch(0xE85B);       // aspect_ratio
            d["texture"] = Ch(0xE421);      // canvas noise
            d["wave"] = Ch(0xE798);         // water_drop, audio noise
            d["speed"] = Ch(0xE9E4);
            // plan, payment and referral
            d["crown"] = Ch(0xE7EF);        // workspace_premium
            d["gift"] = Ch(0xE8F6);         // card_giftcard
            d["wallet"] = Ch(0xE850);       // account_balance_wallet
            d["receipt"] = Ch(0xEF6E);      // receipt_long
            d["timer"] = Ch(0xE425);
            d["hourglass"] = Ch(0xE88B);    // hourglass_empty
            d["verified"] = Ch(0xEF76);
            d["rocket"] = Ch(0xEBA7);       // rocket_launch
            d["qr"] = Ch(0xE00A);           // qr_code_2
            d["swap"] = Ch(0xEB70);         // currency_exchange
            d["percent"] = Ch(0xEB58);
            d["paste"] = Ch(0xE14F);        // content_paste
            return d;
        }

        public static bool Has(string name)
        {
            return name != null && Glyph.ContainsKey(name);
        }

        /// <summary>The raw glyph string for a name, for tooling that renders its own sheet.</summary>
        internal static string GlyphFor(string name)
        {
            string g;
            return name != null && Glyph.TryGetValue(name, out g) ? g : null;
        }

        /// <summary>
        /// Draws a glyph centred in box. stroke is kept for call-site compatibility: Material
        /// Icons carries its own weight, so it only nudges the size.
        /// </summary>
        public static void Draw(Graphics g, string name, RectangleF box, Color color, float stroke)
        {
            Draw(g, name, box, color);
        }

        public static void Draw(Graphics g, string name, RectangleF box, Color color)
        {
            if (g == null || box.Width <= 0 || box.Height <= 0) return;

            string glyph;
            if (name == null || !Glyph.TryGetValue(name, out glyph))
                if (!Glyph.TryGetValue("circle", out glyph)) return;

            /* Material glyphs sit inside a 24 dp box with 2 dp of padding, so an em equal to the
               box always reads two steps smaller than the control around it. Scaling by 1.3
               makes an 18 px box hold an 18 px icon with a little air around it - and the whole
               interface reads as icon-forward, which is what the product wants: icons were
               repeatedly reported as too small next to the text they sit beside.
               The size is rounded to a whole pixel and the origin snapped to the pixel grid:
               a glyph drawn at 21.6 px on a half-pixel boundary is resampled and turns soft,
               which is what "the icons look low quality" actually is. */
            float want = Math.Min(box.Width, box.Height) * 1.3f;
            int px = Math.Max(6, (int)Math.Round(want));

            /* Material ships two faces and they do not scale the same way. At 16-25 px the solid
               face collapses every multi-part glyph - layers, copy, gift, crown, terminal - into a
               filled blob, while the outlined face keeps the structure legible. Above 26 px the
               solid face is the denser, better-balanced one.
               Below 18 px even an outline thins out to a grey hairline, so the handful of
               single-stroke glyphs (a check, a plus, a triangle) keep the solid face all the way
               down. tools\audit --glyphs renders both faces at every size the UI uses. */
            bool solid = px >= 26 || (px < 18 && Strokes.Contains(name));
            Font f = solid ? Theme.IconFontSolid(px) : Theme.IconFont(px);
            /* The face is always the system UI family, so it is available even on a machine
               that has none of the bundled files. */
            if (f == null) f = Theme.Body;
            if (f == null) return;

            /* The glyph is centred on its INK, not on its em box. DrawString lays the em box at
               the requested point, and Material's 24 dp box is far from tight around the shapes:
               measuring the string first and centring what it actually reports is the difference
               between a glyph that sits in the middle of its chip and one that looks shoved left
               and slightly high. GenericTypographic is the format that reports the tight box;
               the default format pads it, which is what an earlier version measured. */
            float x = box.X;
            float y = box.Y;
            var ink = SizeF.Empty;
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            try
            {
                ink = g.MeasureString(glyph, f, int.MaxValue, format);
                if (ink.Width > 0 && ink.Height > 0)
                {
                    x = box.X + (box.Width - Math.Min(px, ink.Width)) / 2f;
                    y = box.Y + (box.Height - Math.Min(px, ink.Height)) / 2f;
                }
                else
                {
                    x = box.X + (box.Width - px) / 2f;
                    y = box.Y + (box.Height - px) / 2f;
                }
            }
            finally { format.Dispose(); }
            x = (float)Math.Floor(x);
            y = (float)Math.Floor(y);

            var oldHint = g.TextRenderingHint;
            var oldOffset = g.PixelOffsetMode;
            /* Icons are shapes: grayscale AA keeps their weight even, ClearType tints the edges.
               Grid-fit is what turns a soft grey outline into a crisp edge at 20 px - unhinted
               grayscale AA is the reason the small glyphs read as "low quality". */
            g.TextRenderingHint = px <= 30
                ? TextRenderingHint.AntiAliasGridFit
                : TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
            try
            {
                using (var b = new SolidBrush(color))
                using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    /* Measured with the same format as above, so the two agree exactly. */
                    g.DrawString(glyph, f, b, x, y, fmt);
                }
            }
            finally
            {
                g.TextRenderingHint = oldHint;
                g.PixelOffsetMode = oldOffset;
            }
        }

    }

    /// <summary>
    /// Real social brand marks, drawn as vectors so they stay crisp at caption sizes.
    /// Each keeps the platform's own colour instead of the app accent.
    /// </summary>
    internal static class BrandIcons
    {
        public static void Draw(Graphics g, string name, RectangleF box)
        {
            if (g == null || box.Width <= 1 || box.Height <= 1 || name == null) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            try
            {
                if (name == "telegram") Telegram(g, box);
                else if (name == "x") XMark(g, box);
                else if (name == "youtube") YouTube(g, box);
                else if (name == "google") Google(g, box);
            }
            finally { g.SmoothingMode = old; }
        }

        private static void Telegram(Graphics g, RectangleF box)
        {
            float d = Math.Min(box.Width, box.Height);
            float x = box.X + (box.Width - d) / 2f, y = box.Y + (box.Height - d) / 2f;
            using (var b = new SolidBrush(Color.FromArgb(0x22, 0x9E, 0xD9)))
                g.FillEllipse(b, x, y, d, d);

            // White paper plane, listed clockwise around the mark.
            PointF[] p =
            {
                new PointF(x + d * 0.19f, y + d * 0.52f),
                new PointF(x + d * 0.80f, y + d * 0.24f),
                new PointF(x + d * 0.66f, y + d * 0.78f),
                new PointF(x + d * 0.51f, y + d * 0.63f),
                new PointF(x + d * 0.44f, y + d * 0.78f),
                new PointF(x + d * 0.42f, y + d * 0.57f)
            };
            using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, p);
        }

        private static void XMark(Graphics g, RectangleF box)
        {
            float d = Math.Min(box.Width, box.Height);
            float x = box.X + (box.Width - d) / 2f, y = box.Y + (box.Height - d) / 2f;
            PointF[] p =
            {
                new PointF(x + d * 0.10f, y + d * 0.14f),
                new PointF(x + d * 0.38f, y + d * 0.14f),
                new PointF(x + d * 0.50f, y + d * 0.35f),
                new PointF(x + d * 0.63f, y + d * 0.14f),
                new PointF(x + d * 0.90f, y + d * 0.14f),
                new PointF(x + d * 0.63f, y + d * 0.49f),
                new PointF(x + d * 0.92f, y + d * 0.88f),
                new PointF(x + d * 0.63f, y + d * 0.88f),
                new PointF(x + d * 0.49f, y + d * 0.64f),
                new PointF(x + d * 0.34f, y + d * 0.88f),
                new PointF(x + d * 0.08f, y + d * 0.88f),
                new PointF(x + d * 0.38f, y + d * 0.50f)
            };
            using (var b = new SolidBrush(Theme.Text)) g.FillPolygon(b, p);
        }

        /// <summary>
        /// The four-colour Google "G": a ring split into the brand arcs plus the blue bar that
        /// runs into the centre. Drawn rather than shipped as a bitmap, because the sign-in
        /// button uses it at 16 px where a resampled PNG turns into a smudge.
        /// </summary>
        private static void Google(Graphics g, RectangleF box)
        {
            float d = Math.Min(box.Width, box.Height);
            float x = box.X + (box.Width - d) / 2f, y = box.Y + (box.Height - d) / 2f;
            float stroke = Math.Max(1.2f, d * 0.26f);
            var ring = new RectangleF(x + stroke / 2f, y + stroke / 2f, d - stroke, d - stroke);

            Color blue = Color.FromArgb(0x42, 0x85, 0xF4);
            Color red = Color.FromArgb(0xEA, 0x43, 0x35);
            Color yellow = Color.FromArgb(0xFB, 0xBC, 0x05);
            Color green = Color.FromArgb(0x34, 0xA8, 0x53);

            using (var pen = new Pen(blue, stroke))
            {
                pen.StartCap = LineCap.Flat;
                pen.EndCap = LineCap.Flat;
                g.DrawArc(pen, ring, 312f, 60f);   // right, above the bar
                pen.Color = red;
                g.DrawArc(pen, ring, 190f, 120f);  // over the top
                pen.Color = yellow;
                g.DrawArc(pen, ring, 135f, 55f);   // left
                pen.Color = green;
                g.DrawArc(pen, ring, 40f, 95f);    // bottom
            }

            // The blue bar into the centre; its right end meets the blue arc.
            float barH = stroke * 0.86f;
            float barLeft = x + d * 0.50f;
            float barRight = x + d - stroke * 0.10f;
            using (var b = new SolidBrush(blue))
                g.FillRectangle(b, barLeft, y + d * 0.5f - barH / 2f, barRight - barLeft, barH);
        }

        private static void YouTube(Graphics g, RectangleF box)
        {
            float w = Math.Min(box.Width, box.Height * 1.4f);
            float h = w / 1.4f;
            float x = box.X + (box.Width - w) / 2f, y = box.Y + (box.Height - h) / 2f;
            using (var path = Ui.Round(new RectangleF(x, y, w, h), h * 0.28f))
            using (var b = new SolidBrush(Color.FromArgb(0xFF, 0x00, 0x00)))
                g.FillPath(b, path);
            PointF[] tri =
            {
                new PointF(x + w * 0.42f, y + h * 0.30f),
                new PointF(x + w * 0.42f, y + h * 0.70f),
                new PointF(x + w * 0.68f, y + h * 0.50f)
            };
            using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, tri);
        }
    }
}
