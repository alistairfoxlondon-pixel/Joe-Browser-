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
            public int DescLines = 2;
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
        /* Derived from the type scale rather than a constant. The tile holds a chip row, the
           title, two description lines and the action pill; at 130 the description had one line
           and a half above the pill, so the second line of any real description was clipped. */
        private static int TileH
        {
            get { return Math.Max(Ui.S(140), Ui.S(20) + 34 + Ui.S(10) + Math.Max(Theme.LineHeight(Theme.H3), Ui.S(20)) + Ui.S(8) + Theme.LineHeight(Theme.Caption) * 2 + Ui.S(12) + Ui.S(34) + Ui.S(16)); }
        }
        private const double Gap = 16;
        private const double Pad = 28;
        private int _scroll;
        private int _maxScroll;
        private int _hoverIndex = -1;

        public event EventHandler<ToolsActionEventArgs> ActionRequested;

        public ToolsPage()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            /* Every tile is painted here rather than being a child control, so the page itself
               has to be the tab stop - with TabStop false the whole Tools section was the one
               part of the app a keyboard-only user could not reach at all. */
            TabStop = true;
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
            Add("audit", "fingerprint", "tools.audit", "tools.audit.desc", "btn.audit");
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

        /* ---- keyboard -------------------------------------------------------------
           Arrow keys move a focus cursor over the tiles, Home/End jump to the first and last,
           Enter or Space runs the focused tile. Everything the mouse can reach stays reachable
           without it. */

        private int _focusIndex = -1;

        protected override void OnGotFocus(EventArgs e)
        {
            if (_focusIndex < 0 && _tiles.Count > 0)
            {
                _focusIndex = 0;
                for (int i = 0; i < _tiles.Count; i++) _tiles[i].Hover = i == 0;
            }
            EnsureVisible();
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up ||
                   keyData == Keys.Down || keyData == Keys.Home || keyData == Keys.End ||
                   keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int cols = Math.Max(1, ColumnCount(Math.Max(Ui.S(240), Width - Ui.S(Pad) * 2)));
            switch (e.KeyCode)
            {
                case Keys.Right: MoveFocus(_focusIndex + 1); e.Handled = true; break;
                case Keys.Left: MoveFocus(_focusIndex - 1); e.Handled = true; break;
                case Keys.Down: MoveFocus(_focusIndex + cols); e.Handled = true; break;
                case Keys.Up: MoveFocus(_focusIndex - cols); e.Handled = true; break;
                case Keys.Home: MoveFocus(0); e.Handled = true; break;
                case Keys.End: MoveFocus(_tiles.Count - 1); e.Handled = true; break;
                case Keys.Enter:
                case Keys.Space:
                    if (_focusIndex >= 0 && _focusIndex < _tiles.Count) Raise(_tiles[_focusIndex].Key);
                    e.Handled = true;
                    break;
            }
            base.OnKeyDown(e);
        }

        private void MoveFocus(int index)
        {
            if (_tiles.Count == 0) return;
            _focusIndex = Math.Max(0, Math.Min(_tiles.Count - 1, index));
            // The focus cursor and the hover highlight are the same affordance: a tile must look
            // live whether it was reached with the mouse or the keyboard.
            for (int i = 0; i < _tiles.Count; i++) _tiles[i].Hover = i == _focusIndex;
            EnsureVisible();
            Invalidate();
        }

        /// <summary>Scrolls the focused tile just far enough to be fully on screen.</summary>
        private void EnsureVisible()
        {
            if (_focusIndex < 0 || _focusIndex >= _tiles.Count || _maxScroll <= 0) return;
            var r = _tiles[_focusIndex].Rect;
            int want = _scroll;
            if (r.Top < _scroll) want = (int)Math.Floor(r.Top) - Ui.S(Pad);
            else if (r.Bottom > _scroll + Height) want = (int)Math.Ceiling(r.Bottom - Height) + Ui.S(Pad);
            want = Math.Max(0, Math.Min(_maxScroll, want));
            if (want != _scroll) { _scroll = want; Invalidate(); }
        }

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
            /* The scrollbar lane is kept clear on the right, so a wide tile never has its PRO
               pill or its action pill sitting under the thumb. */
            float colW = Math.Max(Ui.S(240), Width - pad * 2 - Ui.S(14));
            int cols = ColumnCount(colW);
            float tileW = (colW - gap * (cols - 1)) / cols;
            float tileH = Ui.S(TileH);

            for (int i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                float x = pad + (i % cols) * (tileW + gap);
                float y = pad + (i / cols) * (tileH + gap);
                t.Rect = new RectangleF(x, y, tileW, tileH);

                /* The action pill sits bottom-right and is sized to its own label, so a long
                   translation never clips. The description is then wrapped into what is left, and
                   only as many whole lines as fit are promised to the painter: two half lines of
                   text under a pill is the exact "cut off text" this pass exists to remove. */
                float bh = Ui.S(32);
                float bw = Ui.S(16) * 2 + MeasureLabel(t);
                t.Button = new RectangleF(t.Rect.Right - bw - Ui.S(14), t.Rect.Bottom - bh - Ui.S(14), bw, bh);
                float descH = (t.Rect.Bottom - Ui.S(16)) - (t.Rect.Y + Ui.S(42));
                t.DescLines = Math.Max(1, Math.Min(4, (int)(descH / Math.Max(1f, Theme.LineHeight(Theme.Caption)))));
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
            try { _measure = Ui.Ruler; } catch { _measure = null; }
            LayoutTiles();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            /* The ruler belongs to Ui and outlives this control: releasing it here disposed a
               surface every other page was measuring with. Only the reference is dropped. */
            _measure = null;
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
            if (hit >= 0)
            {
                // A click parks the keyboard cursor on the tile it activated, so Enter afterwards
                // repeats the obvious action instead of firing whatever was there before.
                _focusIndex = hit;
                Focus();
                Raise(_tiles[hit].Key);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            g.Clear(Theme.Bg);
            if (_tiles.Count == 0) return;
            if (_tiles[0].Rect.IsEmpty) LayoutTiles();

            g.TranslateTransform(0, -_scroll);
            foreach (var t in _tiles) PaintTile(g, t);
            g.ResetTransform();

            /* The bar used to be drawn inside the translated space with the scroll offset added
               back in, so it drifted a pixel per notch and could sit off either end of the page.
               It belongs to the viewport: draw it after the transform is reset. */
            if (_maxScroll > 0)
            {
                float track = Height;
                float thumb = Math.Max(Ui.S(36), track * track / (track + _maxScroll));
                float ty = (track - thumb) * (_scroll / (float)_maxScroll);
                Ui.Fill(g, new RectangleF(Width - Ui.S(9), ty, Ui.S(Theme.ButtonRadius), thumb), Ui.S(2.5f), Theme.HoverStrong);
            }

            // The keyboard cursor is drawn after the transform is reset so the ring is never
            // scaled by the scroll offset.
            if (Focused && _focusIndex >= 0 && _focusIndex < _tiles.Count && Ui.FocusCues(this))
            {
                var fr = _tiles[_focusIndex].Rect;
                var box = new RectangleF(fr.X + 0.5f, fr.Y + 0.5f - _scroll, fr.Width - 1.5f, fr.Height - 1.5f);
                if (box.Bottom > 0 && box.Top < Height) Ui.FocusRing(g, box, Ui.S(8));
            }
        }

        private void PaintTile(Graphics g, Tile t)
        {
            var r = t.Rect;
            if (r.Bottom < _scroll || r.Top > _scroll + Height) return;

            float rad = Ui.S(Theme.ButtonRadius);
            var box = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f);
            Ui.Fill(g, box, rad, t.Hover ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, box, rad, t.Hover ? Theme.HoverStrong : Theme.Line, 1f);

            float chip = Ui.S(34);
            var cr = new RectangleF(r.X + Ui.S(16), r.Y + Ui.S(16), chip, chip);
            Color tint = t.Tint.IsEmpty ? Theme.Accent : t.Tint;
            /* A 14% wash on a white card is nearly invisible and the glyph then reads as a grey
               smudge. The chip carries a visible tint and the glyph is pulled 15% toward the text
               colour so it keeps its contrast against it. */
            Ui.Fill(g, cr, Ui.S(Theme.ButtonRadius), Ui.Blend(Theme.Card, tint, Theme.Dark ? 0.30f : 0.18f));
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

            /* Description wraps into the space above the action pill, never under it, and never
               more lines than were measured for it when the tile was laid out. */
            float descTop = r.Y + Ui.S(42);
            PaintWrapped(g, desc, Theme.Caption, Theme.TextFaint, textLeft, descTop, textW,
                Math.Max(1, t.DescLines));

            var b = t.Button;
            /* In dark the secondary fill has to sit above the card, not below it: Panel2 is darker
               than a card, so the pill read as a hole punched in the tile. */
            Ui.Fill(g, b, Ui.S(Theme.ButtonRadius), t.Hover ? Theme.AccentOnBg : (Theme.Dark ? Theme.ButtonBg : Theme.Panel2));
            Ui.Stroke(g, b, Ui.S(Theme.ButtonRadius), t.Hover ? Theme.AccentBorder : Theme.Line, 1f);
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
            float x, float y, float width, int maxLines)
        {
            if (string.IsNullOrEmpty(text) || width < Ui.S(30) || maxLines < 1) return;
            float lineH = font.GetHeight(g);
            /* Wrapped by measurement and stopped at a whole number of lines. The last line that
               fits carries an ellipsis when there is more text, so a truncated description says
               so instead of stopping mid word with nothing to indicate the rest exists. */
            var words = text.Split(' ');
            var line = new System.Text.StringBuilder();
            int used = 0;
            for (int i = 0; i < words.Length; i++)
            {
                string trial = line.Length == 0 ? words[i] : line + " " + words[i];
                if (Ui.Measure(g, trial, font).Width <= width)
                {
                    if (line.Length > 0) line.Append(' ');
                    line.Append(words[i]);
                    continue;
                }
                bool last = used == maxLines - 1;
                if (last)
                {
                    Ui.Text(g, FitEllipsis(g, line.Length == 0 ? words[i] : line.ToString(), font, width),
                        font, color, x, y + used * lineH, width);
                    return;
                }
                Ui.Text(g, line.Length == 0 ? words[i] : line.ToString(), font, color, x, y + used * lineH, width);
                used++;
                line.Length = 0;
                line.Append(words[i]);
            }
            if (line.Length > 0)
                Ui.Text(g, FitEllipsis(g, line.ToString(), font, width), font, color, x, y + used * lineH, width);
        }

        /// <summary>A string shortened until it fits the width, with an ellipsis, never over it.</summary>
        private static string FitEllipsis(Graphics g, string text, Font font, float width)
        {
            if (Ui.Measure(g, text, font).Width <= width) return text;
            const string tail = "\u2026";
            int cut = text.Length;
            while (cut > 0 && Ui.Measure(g, text.Substring(0, cut).TrimEnd() + tail, font).Width > width) cut--;
            return cut <= 0 ? "" : text.Substring(0, cut).TrimEnd() + tail;
        }
    }
}
