using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal sealed class ToolsActionEventArgs : EventArgs
    {
        public readonly string Key;
        public ToolsActionEventArgs(string key) { Key = key; }
    }

    /// <summary>
    /// "Tools &amp; data" page. Every tile is painted by this one control and hit-tested by hand:
    /// there are no child labels or buttons whose opaque background could be cached against the
    /// wrong surface, which is what used to leave white patches when the page was resized while
    /// hidden. Text comes from L10n at paint time, so a language change needs no relayout.
    /// </summary>
    internal sealed class ToolsPage : Control, IReStyle
    {
        private sealed class Tile
        {
            public string Key;
            public string Icon;
            public string TitleKey;
            public string DescKey;
            public string BtnKey;
            public RectangleF Rect;
            public RectangleF Button;
            public bool Hover;
            public bool Locked;
            public Color Tint;
        }

        /* One accent per tool. A grid where every icon is the same blue reads as decoration;
           a small, stable palette makes the tiles scannable at a glance. */
        private static readonly Color[] Palette =
        {
            Color.FromArgb(0x4C, 0x8D, 0xFF), // blue
            Color.FromArgb(0x14, 0xB8, 0xA6), // teal
            Color.FromArgb(0x8B, 0x5C, 0xF6), // violet
            Color.FromArgb(0xF5, 0x9E, 0x0B), // amber
            Color.FromArgb(0xEC, 0x48, 0x99), // rose
            Color.FromArgb(0x22, 0xC5, 0x5E), // green
            Color.FromArgb(0x06, 0xB6, 0xD4), // cyan
            Color.FromArgb(0x63, 0x66, 0xF1), // indigo
            Color.FromArgb(0x64, 0x74, 0x8B)  // slate
        };

        private readonly List<Tile> _tiles = new List<Tile>();
        private const double TileH = 130;
        private const double Gap = 16;
        private const double Pad = 28;
        private int _scroll;
        private int _maxScroll;
        private int _hoverIndex = -1;

        public event EventHandler<ToolsActionEventArgs> ActionRequested;

        public ToolsPage()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
            TabStop = false;
            Cursor = Cursors.Arrow;

            Add("batch", "layers", "tools.batch", "tools.batch.desc", "btn.create.many");
            Add("import", "download", "tools.import", "tools.import.desc", "btn.import");
            Add("export", "external", "tools.export", "tools.export.desc", "btn.export");
            Add("proxy", "route", "tools.proxy", "tools.proxy.desc", "btn.testall");
            Add("net", "wifi", "tools.net", "tools.net.desc", "btn.check");
            Add("cookies", "cookie", "tools.cookies", "tools.cookies.desc", "btn.open");
            Add("ext", "apps", "tools.ext", "tools.ext.desc", "btn.open");
            Add("trash", "trash", "tools.trash", "tools.trash.desc", "btn.restore");
            Add("shortcuts", "command", "tools.shortcuts", "tools.shortcuts.desc", "btn.view");
            Add("clean", "sync", "tools.clean", "tools.clean.desc", "btn.clean");
            Add("report", "error", "tools.report", "tools.report.desc", "btn.report");
        }

        private void Add(string key, string icon, string titleKey, string descKey, string btnKey)
        {
            _tiles.Add(new Tile
            {
                Key = key,
                Icon = icon,
                TitleKey = titleKey,
                DescKey = descKey,
                BtnKey = btnKey,
                Tint = Palette[_tiles.Count % Palette.Length]
            });
        }

        private void Raise(string key)
        {
            var h = ActionRequested;
            if (h != null) h(this, new ToolsActionEventArgs(key));
        }

        /// <summary>Labels changed language: pill widths depend on them, so relayout.</summary>
        public void ApplyText() { LayoutTiles(); }

        /// <summary>
        /// Marks a tool as part of Pro. The tile keeps working - it opens the upgrade page -
        /// but the pill says so up front instead of letting the user find out by clicking.
        /// </summary>
        public void SetLocked(string key, bool locked)
        {
            foreach (var t in _tiles)
            {
                if (!string.Equals(t.Key, key, StringComparison.Ordinal)) continue;
                if (t.Locked == locked) return;
                t.Locked = locked;
                Invalidate();
                return;
            }
        }

        public void ReStyle() { BackColor = Theme.Bg; Invalidate(); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutTiles();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); }

        private static int ColumnCount(float colW)
        {
            if (colW >= Ui.S(1420)) return 4;
            if (colW >= Ui.S(1060)) return 3;
            if (colW >= Ui.S(680)) return 2;
            return 1;
        }

        private void LayoutTiles()
        {
            if (_tiles.Count == 0 || Width <= 0) return;
            float pad = Ui.S(Pad);
            float gap = Ui.S(Gap);
            float colW = Math.Max(Ui.S(240), Width - pad * 2);
            int cols = ColumnCount(colW);
            float tileW = (colW - gap * (cols - 1)) / cols;
            float tileH = Ui.S(TileH);

            for (int i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                float x = pad + (i % cols) * (tileW + gap);
                float y = pad + (i / cols) * (tileH + gap);
                t.Rect = new RectangleF(x, y, tileW, tileH);

                // The action pill sits bottom-right inside the tile and is sized to its own label,
                // so a long translation never clips.
                float bh = Ui.S(30);
                float bw = Ui.S(16) * 2 + MeasureLabel(t);
                t.Button = new RectangleF(t.Rect.Right - bw - Ui.S(14), t.Rect.Bottom - bh - Ui.S(14), bw, bh);
            }

            int rows = (int)Math.Ceiling(_tiles.Count / (double)cols);
            float contentH = pad * 2 + rows * tileH + (rows - 1) * gap;
            _maxScroll = Math.Max(0, (int)Math.Ceiling(contentH - Height));
            if (_scroll > _maxScroll) _scroll = _maxScroll;
            Invalidate();
        }

        private float MeasureLabel(Tile t)
        {
            // PreferredWidth of a real button is the honest metric, and it is cached per language
            // change rather than per paint.
            if (_measure == null) return Ui.S(64);
            return Ui.Measure(_measure, L10n.T(t.BtnKey), Theme.SmallBold).Width;
        }

        private Graphics _measure;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { _measure = CreateGraphics(); } catch { _measure = null; }
            LayoutTiles();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_measure != null) { _measure.Dispose(); _measure = null; }
            base.OnHandleDestroyed(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_maxScroll <= 0) return;
            int next = _scroll - Math.Sign(e.Delta) * Ui.S(48);
            next = Math.Max(0, Math.Min(_maxScroll, next));
            if (next != _scroll) { _scroll = next; Invalidate(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = HitTest(e.Location);
            if (hit != _hoverIndex)
            {
                _hoverIndex = hit;
                foreach (var t in _tiles) t.Hover = false;
                if (hit >= 0) _tiles[hit].Hover = true;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Arrow;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                foreach (var t in _tiles) t.Hover = false;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _tiles.Count; i++)
                if (_tiles[i].Rect.Contains(p.X, p.Y - _scroll)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int hit = HitTest(e.Location);
            if (hit >= 0) Raise(_tiles[hit].Key);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            g.Clear(Theme.Bg);
            if (_tiles.Count == 0) return;
            if (_tiles[0].Rect.IsEmpty) LayoutTiles();

            g.TranslateTransform(0, -_scroll);

            // Scrollbar, only when the grid is taller than the page.
            if (_maxScroll > 0)
            {
                float track = Height;
                float thumb = Math.Max(Ui.S(36), track * track / (track + _maxScroll));
                float ty = (track - thumb) * (_scroll / (float)_maxScroll);
                Ui.Fill(g, new RectangleF(Width - Ui.S(8), ty + _scroll, Ui.S(4), thumb),
                    Ui.S(2), Theme.HoverStrong);
            }

            foreach (var t in _tiles) PaintTile(g, t);
            g.ResetTransform();
        }

        private void PaintTile(Graphics g, Tile t)
        {
            var r = t.Rect;
            if (r.Bottom < _scroll || r.Top > _scroll + Height) return;

            float rad = Ui.S(8);
            var box = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f);
            Ui.Fill(g, box, rad, t.Hover ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, box, rad, t.Hover ? Theme.HoverStrong : Theme.Line, 1f);

            float chip = Ui.S(34);
            var cr = new RectangleF(r.X + Ui.S(16), r.Y + Ui.S(16), chip, chip);
            Color tint = t.Tint.IsEmpty ? Theme.Accent : t.Tint;
            /* A 14% wash on a white card is nearly invisible and the glyph then reads as a grey
               smudge. The chip carries a visible tint and the glyph is pulled 15% toward the text
               colour so it keeps its contrast against it. */
            Ui.Fill(g, cr, Ui.S(7), Ui.Blend(Theme.Card, tint, Theme.Dark ? 0.30f : 0.18f));
            Color ink = Theme.Dark ? tint : Ui.Blend(tint, Theme.Text, 0.15f);
            float ic = Ui.S(20);
            Icons.Draw(g, t.Icon, new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), ink);

            string title = L10n.T(t.TitleKey);
            string desc = L10n.T(t.DescKey);
            string btn = L10n.T(t.BtnKey);

            float bw = t.Button.Width;
            float textLeft = cr.Right + Ui.S(12);
            float textRight = r.Right - Ui.S(16);
            if (t.Locked) textRight -= ProBadgeWidth(g);
            float textW = Math.Max(Ui.S(60), textRight - textLeft);

            Ui.Text(g, title, Theme.H3, Theme.Text, textLeft, r.Y + Ui.S(17), textW);

            // Description wraps into the space above the action pill, never under it.
            float descTop = r.Y + Ui.S(40);
            float descBottom = t.Button.Top - Ui.S(8);
            PaintWrapped(g, desc, Theme.Caption, Theme.TextFaint, textLeft, descTop, textW,
                Math.Max(Ui.S(16), descBottom - descTop));

            var b = t.Button;
            /* In dark the secondary fill has to sit above the card, not below it: Panel2 is darker
               than a card, so the pill read as a hole punched in the tile. */
            Ui.Fill(g, b, Ui.S(4), t.Hover ? Theme.AccentOnBg : (Theme.Dark ? Theme.ButtonBg : Theme.Panel2));
            Ui.Stroke(g, b, Ui.S(4), t.Hover ? Theme.AccentBorder : Theme.Line, 1f);
            Ui.Text(g, btn, Theme.SmallBold, t.Hover ? Theme.AccentText : Theme.TextDim,
                b.X, b.Y + (b.Height - Theme.SmallBold.GetHeight(g)) / 2f, b.Width, "center");

            if (t.Locked) PaintProBadge(g, r);
        }

        /// <summary>The PRO pill in the corner of a tile that the free plan cannot use.</summary>
        private static void PaintProBadge(Graphics g, RectangleF r)
        {
            float h = Ui.S(20);
            float li = Ui.S(11);
            const string label = "PRO";
            /* Sized from the label, not from a constant: "PRO" in a translated build, or at a
               larger text scale, used to be cut to "P…" inside a pill that never grew. */
            float tw = Ui.Measure(g, label, Theme.Micro).Width;
            float w = Ui.S(9) + li + Ui.S(5) + tw + Ui.S(10);
            var pill = new RectangleF(r.Right - w - Ui.S(14), r.Y + Ui.S(14), w, h);
            Ui.Fill(g, pill, h / 2f, Theme.AccentOnBg);
            Ui.Stroke(g, pill, h / 2f, Theme.AccentBorder, 1f);

            Icons.Draw(g, "lock", new RectangleF(pill.X + Ui.S(9), pill.Y + (h - li) / 2f, li, li),
                Theme.AccentText);
            Ui.Text(g, label, Theme.Micro, Theme.AccentText,
                pill.X + Ui.S(9) + li + Ui.S(5), pill.Y + (h - Theme.Micro.GetHeight(g)) / 2f, tw + Ui.S(4));
        }

        /// <summary>How much width the title must leave free for the badge.</summary>
        private static float ProBadgeWidth(Graphics g)
        {
            return Ui.S(9) + Ui.S(11) + Ui.S(5) + Ui.Measure(g, "PRO", Theme.Micro).Width + Ui.S(10) + Ui.S(10);
        }

        /// <summary>Greedy word wrap clipped to the tile, so a long translation can never overflow.</summary>
        private static void PaintWrapped(Graphics g, string text, Font font, Color color,
            float x, float y, float width, float maxHeight)
        {
            if (string.IsNullOrEmpty(text) || width < Ui.S(30)) return;
            float lineH = font.GetHeight(g);
            var sb = new System.Text.StringBuilder();
            float cursorY = y;
            foreach (string word in text.Split(' '))
            {
                string trial = sb.Length == 0 ? word : sb + " " + word;
                if (Ui.Measure(g, trial, font).Width <= width) { sb.Append(sb.Length == 0 ? word : " " + word); continue; }
                if (sb.Length == 0) { sb.Append(word); }
                Ui.Text(g, sb.ToString(), font, color, x, cursorY, width);
                cursorY += lineH;
                sb.Remove(0, sb.Length);
                sb.Append(word);
                if (cursorY + lineH > y + maxHeight) { sb.Remove(0, sb.Length); break; }
            }
            if (sb.Length > 0 && cursorY + lineH <= y + maxHeight)
                Ui.Text(g, sb.ToString(), font, color, x, cursorY, width);
        }
    }
}
