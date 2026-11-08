using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Browsing history. Every profile keeps its own record, and this page shows one profile at
    /// a time: the point is to recover a URL that was passed through and forgotten, so the list
    /// reads like a trail rather than a table, and the hops that never reached the address bar
    /// are marked as such.
    /// </summary>
    internal sealed class HistoryPage : Panel, IReStyle
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly List<Row> _rows = new List<Row>();
        private readonly JBButton _back, _clear, _copy, _open, _filter;
        private JBLabel _title, _subtitle;
        private JBPanel _empty;
        private bool _layingOut, _ready;
        private int _selected = -1;
        private bool _hopsOnly;

        public event EventHandler Back;
        public event EventHandler ClearRequested;
        public event EventHandler<string> OpenRequested;

        /// <summary>The profile whose history is on screen.</summary>
        public Profile Profile { get; private set; }

        public HistoryPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            _back = Button(L10n.T("history.back"), "back", BtnKind.Soft);
            _back.Click += delegate { var h = Back; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_back);

            _filter = Button(L10n.T("history.hops"), "route", BtnKind.Soft);
            _filter.Click += delegate
            {
                _hopsOnly = !_hopsOnly;
                Rebuild();
            };
            _column.Controls.Add(_filter);

            _copy = Button(L10n.T("history.copy"), "copy", BtnKind.Soft);
            _copy.Click += delegate { CopySelected(); };
            _column.Controls.Add(_copy);

            _open = Button(L10n.T("history.open"), "external", BtnKind.Soft);
            _open.Click += delegate
            {
                var e = SelectedEntry;
                var h = OpenRequested;
                if (e != null && h != null) h(this, e.Url);
            };
            _column.Controls.Add(_open);

            _clear = Button(L10n.T("history.clear"), "trash", BtnKind.Soft);
            _clear.Click += delegate { var h = ClearRequested; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_clear);

            _title = new JBLabel(LabelRole.Title, "");
            _column.Controls.Add(_title);
            _subtitle = new JBLabel(LabelRole.Faint, "");
            _subtitle.Wrap = true;
            _column.Controls.Add(_subtitle);

            _empty = new JBPanel();
            _empty.Paint += PaintEmpty;
            _column.Controls.Add(_empty);

            _ready = true;
            LayoutCore();
        }

        private static JBButton Button(string text, string icon, BtnKind kind)
        {
            return new JBButton { Kind = kind, Icon = icon, Text = text, Radius = 4, Height = Ui.S(33) };
        }

        private HistoryStore.Entry SelectedEntry
        {
            get
            {
                if (_selected < 0 || _selected >= _rows.Count) return null;
                return _rows[_selected].Entry;
            }
        }

        /// <summary>Points the page at a profile and rebuilds the list from disk.</summary>
        /* Show is a Control method, so this overload has to be new or the compiler binds to the
           inherited one and the page never receives the profile. */
        public new void Show(Profile profile)
        {
            Profile = profile;
            _selected = -1;
            Rebuild();
        }

        public void Rebuild()
        {
            if (!_ready) return;
            foreach (var r in _rows) { _column.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();

            var all = Profile == null ? new List<HistoryStore.Entry>() : HistoryStore.Read(Profile.Id);
            foreach (var e in all)
            {
                if (_hopsOnly && !e.Hop) continue;
                var row = new Row(this, e, _rows.Count);
                _rows.Add(row);
                _column.Controls.Add(row);
            }

            _title.Text = Profile == null ? L10n.T("history.title") : Profile.Name;
            int hops = 0;
            foreach (var e in all) if (e.Hop) hops++;
            _subtitle.Text = all.Count == 0
                ? L10n.T("history.empty.hint")
                : all.Count + " " + L10n.T(all.Count == 1 ? "history.one" : "history.many") +
                  (hops > 0 ? "  \u00b7  " + hops + " " + L10n.T("history.hops.count") : "");
            _filter.Text = _hopsOnly ? L10n.T("history.all") : L10n.T("history.hops");
            _empty.Visible = _rows.Count == 0;
            UpdateButtons();
            LayoutCore();
        }

        private void UpdateButtons()
        {
            bool has = SelectedEntry != null;
            _copy.Enabled = has;
            _open.Enabled = has;
            _clear.Enabled = Profile != null && _rows.Count > 0;
        }

        private void Select(int index)
        {
            if (_selected == index) return;
            int old = _selected;
            _selected = index;
            if (old >= 0 && old < _rows.Count) _rows[old].Invalidate();
            if (_selected >= 0 && _selected < _rows.Count) _rows[_selected].Invalidate();
            UpdateButtons();
        }

        private void CopySelected()
        {
            var e = SelectedEntry;
            if (e == null) return;
            try { Clipboard.SetText(e.Url); } catch { }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutCore(); }

        private void LayoutCore()
        {
            if (_layingOut || !_ready || _host == null) return;
            _layingOut = true;
            try
            {
                _host.SetBounds(0, 0, Math.Max(1, Width), Math.Max(1, Height));
                int w = Math.Max(Ui.S(320), _host.Width - Ui.S(10));
                int pad = Ui.S(28);
                int inner = Math.Max(Ui.S(200), w - pad * 2);

                int y = pad;
                /* One control row, actions right-aligned: Back on the left because it leaves the
                   page, the rest gathered so the destructive one is not adjacent to a mis-click. */
                int bh = Ui.S(33);
                _back.SetBounds(pad, y, _back.PreferredWidth(Ui.S(96)), bh);
                int right = pad + inner;
                int cw = _clear.PreferredWidth(Ui.S(90));
                _clear.SetBounds(right - cw, y, cw, bh);
                right -= cw + Ui.S(8);
                int ow = _open.PreferredWidth(Ui.S(84));
                _open.SetBounds(right - ow, y, ow, bh);
                right -= ow + Ui.S(8);
                int cpw = _copy.PreferredWidth(Ui.S(84));
                _copy.SetBounds(right - cpw, y, cpw, bh);
                right -= cpw + Ui.S(8);
                int fw = _filter.PreferredWidth(Ui.S(120));
                _filter.SetBounds(right - fw, y, fw, bh);

                y += bh + Ui.S(20);
                _title.SetBounds(pad, y, inner, Ui.S(30));
                y += Ui.S(32);
                _subtitle.SetBounds(pad, y, inner, Ui.S(20));
                y += Ui.S(30);

                if (_empty.Visible)
                {
                    _empty.SetBounds(pad, y, inner, Ui.S(120));
                    y += Ui.S(130);
                }

                foreach (var r in _rows)
                {
                    r.SetBounds(pad, y, inner, r.RowHeight);
                    y += r.RowHeight + Ui.S(2);
                }
                y += pad;

                _column.SetBounds(0, 0, w, Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }

        private void PaintEmpty(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            Color ink = Theme.TextFaint;
            float ic = Ui.S(28);
            Icons.Draw(g, "clock", new RectangleF((_empty.Width - ic) / 2f, Ui.S(14), ic, ic), ink);
            Ui.Text(g, L10n.T("history.empty"), Theme.Body, ink,
                0, Ui.S(52), _empty.Width, "center");
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            foreach (var r in _rows) r.Invalidate();
            Invalidate();
        }

        /// <summary>One visit. Painted by hand so no child label can cache an old surface.</summary>
        private sealed class Row : Control
        {
            private readonly HistoryPage _page;
            private readonly int _index;
            public HistoryStore.Entry Entry { get; private set; }
            private bool _hover;

            public Row(HistoryPage page, HistoryStore.Entry entry, int index)
            {
                _page = page;
                Entry = entry;
                _index = index;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.Selectable, true);
                BackColor = Theme.Bg;
                Cursor = Cursors.Hand;
                TabStop = false;
            }

            public int RowHeight { get { return Ui.S(52); } }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left) _page.Select(_index);
            }
            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                var h = _page.OpenRequested;
                if (h != null) h(_page, Entry.Url);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                g.Clear(Theme.Bg);
                bool sel = _page._selected == _index;
                var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                if (sel) Ui.Fill(g, r, Ui.S(6), Theme.AccentOnBg);
                else if (_hover) Ui.Fill(g, r, Ui.S(6), Theme.HoverOverlay);

                /* A hop is marked with a small arrow instead of a word: the list is scanned, not
                   read, and a label repeated on half the rows is noise. */
                if (Entry.Hop)
                {
                    float ic = Ui.S(13);
                    Icons.Draw(g, "arrow-right", new RectangleF(Ui.S(12), (Height - ic) / 2f, ic, ic), Theme.TextFaint);
                }

                float left = Ui.S(34);
                float w = Width - left - Ui.S(96);
                string title = string.IsNullOrEmpty(Entry.Title) ? Host(Entry.Url) : Entry.Title;
                /* The mark sits on every row: the history is per profile, so the name is not the
                   interesting part of the row and a column of repeats is wasted width. */
                float mk = Ui.S(14);
                BrowserMark.Draw(g, new RectangleF(left, (Height - mk) / 2f, mk, mk),
                    BrowserMark.KindFor(_page.Profile));
                left += mk + Ui.S(8);
                w -= mk + Ui.S(8);
                Ui.Text(g, title, Theme.Body, sel ? Theme.AccentText : Theme.Text, left, Ui.S(9), w);

                /* The host, then the rest of the path, so a long URL still shows where it went. */
                Ui.Text(g, Short(Entry.Url, 110), Theme.Caption, Theme.TextFaint, left, Ui.S(28), w);

                Ui.Text(g, Entry.When.ToString("HH:mm"), Theme.Caption, Theme.TextFaint,
                    Width - Ui.S(86), Ui.S(9), Ui.S(76), "right");
                Ui.Text(g, Entry.When.ToString("d MMM"), Theme.Caption, Theme.TextFaint,
                    Width - Ui.S(86), Ui.S(28), Ui.S(76), "right");
            }

            private static string Host(string url)
            {
                try { return new Uri(url).Host; } catch { return url; }
            }

            private static string Short(string s, int max)
            {
                if (string.IsNullOrEmpty(s)) return "";
                try
                {
                    var u = new Uri(s);
                    string rest = u.Host + u.PathAndQuery;
                    return rest.Length > max ? rest.Substring(0, max) + "\u2026" : rest;
                }
                catch { return s.Length > max ? s.Substring(0, max) + "\u2026" : s; }
            }
        }
    }
}
