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
        }

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
            Add("cookies", "cookie", "tools.cookies", "tools.cookies.desc", "btn.open");
            Add("ext", "apps", "tools.ext", "tools.ext.desc", "btn.open");
            Add("trash", "trash", "tools.trash", "tools.trash.desc", "btn.restore");
            Add("shortcuts", "command", "tools.shortcuts", "tools.shortcuts.desc", "btn.view");
            Add("clean", "sync", "tools.clean", "tools.clean.desc", "btn.clean");
        }

        private void Add(string key, string icon, string titleKey, string descKey, string btnKey)
        {
            _tiles.Add(new Tile { Key = key, Icon = icon, TitleKey = titleKey, DescKey = descKey, BtnKey = btnKey });
        }

        private void Raise(string key)
        {
            var h = ActionRequested;
            if (h != null) h(this, new ToolsActionEventArgs(key));
        }

        /// <summary>Labels changed language: pill widths depend on them, so relayout.</summary>
        public void ApplyText() { LayoutTiles(); }

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
            Ui.Fill(g, cr, Ui.S(7), Theme.AccentOnBg);
            float ic = Ui.S(18);
            Icons.Draw(g, t.Icon, new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

            string title = L10n.T(t.TitleKey);
            string desc = L10n.T(t.DescKey);
            string btn = L10n.T(t.BtnKey);

            float bw = t.Button.Width;
            float textLeft = cr.Right + Ui.S(12);
            float textRight = r.Right - Ui.S(16);
            float textW = Math.Max(Ui.S(60), textRight - textLeft);

            Ui.Text(g, title, Theme.H3, Theme.Text, textLeft, r.Y + Ui.S(17), textW);

            // Description wraps into the space above the action pill, never under it.
            float descTop = r.Y + Ui.S(40);
            float descBottom = t.Button.Top - Ui.S(8);
            PaintWrapped(g, desc, Theme.Caption, Theme.TextFaint, textLeft, descTop, textW,
                Math.Max(Ui.S(16), descBottom - descTop));

            var b = t.Button;
            Ui.Fill(g, b, Ui.S(4), t.Hover ? Theme.AccentOnBg : Theme.Panel2);
            Ui.Stroke(g, b, Ui.S(4), t.Hover ? Theme.AccentBorder : Theme.Line, 1f);
            Ui.Text(g, btn, Theme.SmallBold, t.Hover ? Theme.AccentText : Theme.TextDim,
                b.X, b.Y + (b.Height - Theme.SmallBold.GetHeight(g)) / 2f, b.Width, "center");
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
