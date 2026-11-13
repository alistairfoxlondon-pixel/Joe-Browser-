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
    internal sealed class HistoryPage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        /// <summary>The control the main window hands the wheel to when the pointer is over it.</summary>
        internal Control ScrollTarget { get { return _host; } }
        private readonly List<Row> _rows = new List<Row>();
        private readonly JBButton _back, _clear, _copy, _open, _filter;
        private JBLabel _title, _subtitle;
        private JBPanel _empty;
        private bool _layingOut, _ready;
        private int _selected = -1;
        private bool _hopsOnly;
        private int _subtitleHeight;
        /* Rows are real window controls, one per visit. A busy profile can hold twenty thousand
           entries, and handing that to WinForms at once is a page that takes seconds to appear
           and then stutters on every scroll. The list shows the newest PageSize entries and
           offers the rest a page at a time; nothing is dropped from disk. */
        private const int PageSize = 200;
        private const int MaxShown = 1000;
        private int _shown = PageSize;
        private JBButton _more;

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
            /* Height reserved for the wrapped subtitle, recomputed in LayoutCore from the real
               text so a second line cannot be drawn over the first history row. */
            _subtitleHeight = Ui.S(20);

            _more = Button("Show older", "clock", BtnKind.Soft);
            _more.Click += delegate
            {
                _shown = Math.Min(MaxShown, _shown + PageSize);
                Rebuild();
            };
            _column.Controls.Add(_more);

            _empty = new JBPanel();
            _empty.Paint += PaintEmpty;
            _column.Controls.Add(_empty);

            _ready = true;
            LayoutCore();
        }

        private static JBButton Button(string text, string icon, BtnKind kind)
        {
            return new JBButton { Kind = kind, Icon = icon, Text = text, Radius = Theme.ButtonRadius, Height = Theme.Field };
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
        /* Control.Show() takes no arguments, so this is an overload rather than a hide. The
           old 'new' modifier made the compiler warn and, worse, hid the parameterless Show()
           from callers that legitimately wanted to reveal the control. */
        public void Show(Profile profile)
        {
            Profile = profile;
            _selected = -1;
            /* A new profile starts at the first page again, so a long list seen on one profile
               does not decide how much of the next one is shown. */
            _shown = PageSize;
            Rebuild();
        }

        public void Rebuild()
        {
            if (!_ready) return;
            foreach (var r in _rows) { _column.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();
            /* The list under the cursor is about to change, so the old index means nothing:
               keeping it selected a row the user never picked after switching the filter, and
               "Open" then opened a page from a different row than the one highlighted. */
            _selected = -1;

            /* The history is a file that grows with every page a profile visits, and reading
               it took the better part of a second on a busy profile - a second in which the
               whole application is frozen, because the read happens on the thread that draws.
               It is read on a worker now and the list is filled in when it arrives, so the
               page is there the moment it is asked for. */
            var profile = Profile;
            int token = ++_loadToken;
            _subtitle.Text = L10n.T("history.loading");
            System.Threading.Tasks.Task.Run(new Func<List<HistoryStore.Entry>>(delegate
            {
                try { return profile == null ? new List<HistoryStore.Entry>() : HistoryStore.Read(profile.Id); }
                catch { return new List<HistoryStore.Entry>(); }
            })).ContinueWith(delegate(System.Threading.Tasks.Task<List<HistoryStore.Entry>> t)
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(delegate
                    {
                        /* A newer read has already answered, or the page was left. */
                        if (token != _loadToken || IsDisposed) return;
                        ApplyEntries(t.Result ?? new List<HistoryStore.Entry>());
                    }));
                }
                catch { }
            });
        }

        /// <summary>Fills the list from the entries that were read off the disk.</summary>
        private void ApplyEntries(List<HistoryStore.Entry> all)
        {
            if (all == null) all = new List<HistoryStore.Entry>();
            int matching = 0;
            foreach (var e in all)
            {
                if (_hopsOnly && !e.Hop) continue;
                matching++;
                if (_rows.Count >= _shown) continue;
                var row = new Row(this, e, _rows.Count);
                _rows.Add(row);
                _column.Controls.Add(row);
            }
            _more.Visible = matching > _rows.Count;
            _more.Text = _rows.Count >= MaxShown
                ? "Showing the newest " + MaxShown
                : "Show older (" + (matching - _rows.Count) + ")";
            _more.Enabled = _rows.Count < MaxShown;

            /* Which profile is showing has to be visible on the page, not only in the sidebar:
               the same page serves every profile and the last opened one is not always the one
               the user meant to look at. */
            _title.Text = Profile == null
                ? L10n.T("history.title")
                : Profile.Name + "  \u00b7  " + L10n.T("history.title");
            int hops = 0;
            foreach (var e in all) if (e.Hop) hops++;
            int shown = _rows.Count;
            _subtitle.Text = all.Count == 0
                ? L10n.T("history.empty.hint")
                : all.Count + " " + L10n.T(all.Count == 1 ? "history.one" : "history.many") +
                  (hops > 0 ? "  \u00b7  " + hops + " " + L10n.T("history.hops.count") : "") +
                  (shown < matching ? "  \u00b7  newest " + shown + " shown" : "");
            _filter.Text = _hopsOnly ? L10n.T("history.all") : L10n.T("history.hops");
            _empty.Visible = _rows.Count == 0;
            if (_rows.Count == 0) _more.Visible = false;
            UpdateButtons();
            LayoutCore();
        }

        /// <summary>Counts the reads that were asked for, so a slow one cannot overwrite a newer.</summary>
        private int _loadToken;

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

        /// <summary>
        /// Moves the selection by keyboard and keeps the chosen row inside the scroller. The
        /// rows were reachable only with the mouse: they are focusable, but nothing answered a
        /// key, so a list of a few hundred pages could not be walked without the pointer.
        /// </summary>
        internal void MoveSelection(int delta)
        {
            if (_rows.Count == 0) return;
            int next = _selected < 0 ? (delta > 0 ? 0 : _rows.Count - 1) : _selected + delta;
            if (next < 0) next = 0;
            if (next >= _rows.Count) next = _rows.Count - 1;
            Select(next);
            EnsureSelectedVisible();
        }

        internal void OpenSelected()
        {
            var e = SelectedEntry;
            var h = OpenRequested;
            if (e != null && h != null) h(this, e.Url);
        }

        private void EnsureSelectedVisible()
        {
            if (_selected < 0 || _selected >= _rows.Count) return;
            var r = _rows[_selected];
            int top = r.Top, bottom = r.Bottom;
            if (top < _host.Offset)
                _host.ScrollTo(top - Ui.S(8));
            else if (bottom > _host.Offset + _host.Height)
                _host.ScrollTo(bottom - _host.Height + Ui.S(8));
        }

        private void CopySelected()
        {
            var e = SelectedEntry;
            if (e == null) return;
            try { Clipboard.SetText(e.Url); } catch { }
        }

        /// <summary>Height the wrapped text needs at the given width, in whole lines.</summary>
        private static int MeasureLines(string text, Font font, int width)
        {
            int line = Math.Max(Ui.S(16), (int)Math.Ceiling(font.GetHeight()));
            if (string.IsNullOrEmpty(text)) return line;
            try
            {
                Size size = TextRenderer.MeasureText(text, font, new Size(Math.Max(Ui.S(80), width), int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                int lines = Math.Max(1, (int)Math.Round(size.Height / (double)line));
                return line * Math.Min(3, lines) + Ui.S(2);
            }
            catch { return line; }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutCore(); }

        /// <summary>One more pass once the scroll host has fixed its width.</summary>
        public void LayoutAtWidth() { LayoutCore(); }

        private void LayoutCore()
        {
            if (_layingOut || !_ready || _host == null) return;
            _layingOut = true;
            try
            {
                _host.SetBounds(0, 0, Math.Max(1, Width), Math.Max(1, Height));
                int w = Math.Max(Ui.S(320), _host.Width - Ui.S(10));
                int pad = Ui.S(32);
                int inner = Math.Max(Ui.S(200), w - pad * 2);

                int y = pad;
                int bh = Theme.Field;
                /* Back on the left because it leaves the page; the rest gathered on the right,
                   wrapping onto a second line when the window cannot hold them all. Placing them
                   one at a time from the right edge made them overlap, and on a narrow window the
                   row ran off the page. */
                _back.SetBounds(pad, y, _back.PreferredWidth(Ui.S(96)), bh);
                int groupW = 0;
                JBButton[] group = { _filter, _copy, _open, _clear };
                foreach (var b in group) groupW += b.PreferredWidth(Ui.S(84)) + Ui.S(8);
                groupW -= Ui.S(8);

                if (_back.Right + Ui.S(8) + groupW <= pad + inner)
                {
                    /* One line: the group is right-aligned in the space that is left. */
                    int bx = pad + inner - groupW;
                    foreach (var b in group)
                    {
                        int bw = b.PreferredWidth(Ui.S(84));
                        b.SetBounds(bx, y, bw, bh);
                        bx += bw + Ui.S(8);
                    }
                    y += bh + Ui.S(20);
                }
                else
                {
                    /* Two lines: Back, then the group flow wrapped under it. */
                    y += bh + Ui.S(10);
                    y = Ui.FlowButtons(group, pad, y, inner, Ui.S(8), bh) + Ui.S(14);
                }

                _title.SetBounds(pad, y, inner, Ui.S(30));
                y += Ui.S(32);
                /* The subtitle wraps, so its box is measured rather than assumed: a two line
                   hint used to run into the first row of the list. */
                _subtitleHeight = MeasureLines(_subtitle.Text, Theme.Caption, inner);
                _subtitle.SetBounds(pad, y, inner, _subtitleHeight);
                y += _subtitleHeight + Ui.S(10);

                if (_empty.Visible)
                {
                    _empty.SetBounds(pad, y, inner, Ui.S(120));
                    y += Ui.S(130);
                }

                foreach (var r in _rows)
                {
                    /* Two px used to separate rows that are 52 px tall each with paint filling to
                       the edge, which read as one solid block. Six is the smallest gap that still
                       shows the rounded edge of a selected row. */
                    r.SetBounds(pad, y, inner, r.RowHeight);
                    y += r.RowHeight + Ui.S(6);
                }

                if (_rows.Count > 0)
                {
                    y += Ui.S(8);
                    _more.SetBounds(pad, y, _more.PreferredWidth(Ui.S(160)), Theme.Field);
                    y += Theme.Field;
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

            public int RowHeight { get { return Ui.S(56); } }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left) { Focus(); _page.Select(_index); }
            }

            protected override bool IsInputKey(Keys keyData)
            {
                return keyData == Keys.Up || keyData == Keys.Down ||
                       keyData == Keys.Enter || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down) { _page.MoveSelection(1); e.Handled = true; return; }
                if (e.KeyCode == Keys.Up) { _page.MoveSelection(-1); e.Handled = true; return; }
                if (e.KeyCode == Keys.Enter) { _page.OpenSelected(); e.Handled = true; return; }
                base.OnKeyDown(e);
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
                if (sel) { Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg); Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), Theme.AccentBorder); }
                else if (_hover) { Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay); Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), Theme.Line); }

                /* A hop is marked with a small arrow instead of a word: the list is scanned, not
                   read, and a label repeated on half the rows is noise. */
                if (Entry.Hop)
                {
                    float ic = Ui.S(13);
                    Icons.Draw(g, "arrow-right", new RectangleF(Ui.S(12), (Height - ic) / 2f, ic, ic), Theme.TextFaint);
                }

                /* The time column is measured, not assumed: a two digit hour and a month
                   abbreviation at 150% need more than the old 96 px reserve, and the title was
                   drawn underneath them. */
                float timeW = Math.Max(Ui.S(76), Ui.Measure(g, "00:00  WWW 00", Theme.Caption).Width);
                float left = Ui.S(34);
                float w = Width - left - timeW - Ui.S(24);
                /* A hop is marked by an arrow in the left margin, so the arrow's column is not
                   taken out of the text width - the title used to start under a mark that only
                   half the rows draw. */
                string title = string.IsNullOrEmpty(Entry.Title) ? Host(Entry.Url) : Entry.Title;
                if (w < Ui.S(80)) w = Math.Max(Ui.S(40), Width - left - Ui.S(8));
                /* The mark sits on every row: the history is per profile, so the name is not the
                   interesting part of the row and a column of repeats is wasted width. */
                float mk = Ui.S(14);
                BrowserMark.Draw(g, new RectangleF(left, (Height - mk) / 2f, mk, mk),
                    BrowserMark.KindFor(_page.Profile));
                left += mk + Ui.S(8);
                w -= mk + Ui.S(8);
                /* Two measured lines, centred as a pair inside the row. At 9 and 28 from the top
                   the lower line sat a pixel off the bottom edge on any row shorter than 56, and
                   the two drifted apart as soon as the fonts changed. */
                float topH = Theme.LineHeight(Theme.Body);
                float botH = Theme.LineHeight(Theme.Caption);
                float y0 = Math.Max(Ui.S(6), (Height - (topH + botH)) / 2f);
                float y1 = y0 + topH;
                Ui.Text(g, title, Theme.Body, sel ? Theme.AccentText : Theme.Text, left, y0, w);

                /* The host, then the rest of the path, so a long URL still shows where it went. */
                Ui.Text(g, Short(Entry.Url, 110), Theme.Caption, Theme.TextFaint, left, y1, w);

                Ui.Text(g, Entry.When.ToString("HH:mm"), Theme.Caption, Theme.TextFaint,
                    Width - timeW - Ui.S(14), y0, timeW, "right");
                Ui.Text(g, Entry.When.ToString("d MMM"), Theme.Caption, Theme.TextFaint,
                    Width - timeW - Ui.S(14), y1, timeW, "right");
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
