using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal sealed class ExtensionActionEventArgs : EventArgs
    {
        public readonly string Key;
        public readonly int Index;
        public ExtensionActionEventArgs(string key, int index) { Key = key; Index = index; }
    }

    /// <summary>
    /// Full-width Extensions library. Extensions are added from a Chrome Web Store link or an
    /// unpacked folder, switched on or off here, and every enabled one is loaded into all
    /// Chrome / Chromium / Brave / Edge profiles on their next launch.
    /// </summary>
    internal sealed class ExtensionPage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        /// <summary>The control the main window hands the wheel to when the pointer is over it.</summary>
        internal Control ScrollTarget { get { return _host; } }
        private readonly List<ExtensionRow> _rows = new List<ExtensionRow>();
        private readonly List<RecommendCard> _cards = new List<RecommendCard>();
        /// <summary>Logos are read from disk once per path: an extension folder can hold a
        /// 128 px png and the row is repainted on every hover.</summary>
        private static readonly Dictionary<string, Image> LogoCache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        internal static Image LogoFor(ExtensionItem item)
        {
            string path = item == null ? "" : item.IconPath;
            if (string.IsNullOrEmpty(path)) return null;
            Image img;
            if (LogoCache.TryGetValue(path, out img)) return img;
            try
            {
                /* Read through a stream and copy, so the file is not left locked by the
                   browser's own read of the same folder. */
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var src = Image.FromStream(fs))
                    img = new Bitmap(src);
            }
            catch { img = null; }
            /* A missing or unreadable logo is not remembered forever: a fixed icon file (or a
               folder that was momentarily locked) then never appeared again until restart. */
            if (img != null) LogoCache[path] = img;
            return img;
        }

        /// <summary>Drops logo cache entries whose source file no longer exists.</summary>
        public static void PruneLogoCache()
        {
            foreach (string key in LogoCache.Keys.ToArray())
            {
                if (!File.Exists(key))
                {
                    Image dead;
                    if (LogoCache.TryGetValue(key, out dead))
                    {
                        LogoCache.Remove(key);
                        if (dead != null) dead.Dispose();
                    }
                }
            }
        }
        private JBButton _addUrl, _addFolder, _addFile, _openFolder, _fetch;
        private JBLabel _section;
        /* The two extensions this build ships. They arrive by download, and a download can fail,
           so they are shown with their state rather than appearing in the list only when they
           work: a feature the user was promised and cannot see is worse than one that says why. */
        private readonly List<ShippedRow> _shipped = new List<ShippedRow>();
        private JBLabel _shippedHead;
        private JBPanel _empty;
        private bool _layingOut;
        private bool _ready;

        public event EventHandler<ExtensionActionEventArgs> ActionRequested;
        /// <summary>Remove one of the shipped pair, by its position in DefaultStatuses().</summary>
        public event EventHandler<ExtensionActionEventArgs> RemoveShippedRequested;

        public List<ExtensionItem> Items { get; private set; } = new List<ExtensionItem>();

        public ExtensionPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            /* The header band above already names the page; no second title here. */
            _addUrl = MakeButton("Add by store link", "link", BtnKind.Primary, "url");
            _addFolder = MakeButton("Add unpacked folder", "folder", BtnKind.Soft, "folder");
            /* The three ways an extension arrives: a store link, a downloaded .crx or .zip, and
               a folder that is already unpacked. Chrome removed the first two in 137, which is
               why an import path has to be built rather than assumed. */
            _addFile = MakeButton("Import .crx or .zip", "download", BtnKind.Soft, "file");
            _openFolder = MakeButton("Open folder", "external", BtnKind.Soft, "openfolder");
            /* The action bar is pinned to the page rather than scrolled with the list. It sat
               inside the scrolling column, so it moved under the pointer as the list grew and
               could be scrolled half out of view - a bar of buttons that is not where it was a
               second ago. */
            Controls.Add(_addUrl);
            Controls.Add(_addFolder);
            Controls.Add(_addFile);
            Controls.Add(_openFolder);
            /* A control added later sits behind the ones already there, so the bar has to be
               lifted above the scroller it shares the page with. */
            _openFolder.BringToFront();
            _addFile.BringToFront();
            _addFolder.BringToFront();
            _addUrl.BringToFront();

            _empty = new JBPanel();
            _empty.Paint += PaintEmpty;
            _column.Controls.Add(_empty);

            /* The retry button for the shipped pair. It sits with the shipped rows, not in
               the action bar, so the action is next to the thing that failed. */
            _fetch = MakeButton("Restore", "download", BtnKind.Soft, "fetch");
            _column.Controls.Add(_fetch);

            _shippedHead = new JBLabel(LabelRole.Accent, "INCLUDED WITH JOE BROWSER");
            _column.Controls.Add(_shippedHead);
            int shippedIndex = 0;
            foreach (var row in ExtensionCatalog.DefaultStatuses())
            {
                int idx = shippedIndex++;
                var card = new ShippedRow(row[1], row[0], row[3]);
                card.RemoveRequested += delegate
                {
                    var h = RemoveShippedRequested;
                    if (h != null) h(this, new ExtensionActionEventArgs("shippedRemove", idx));
                };
                card.RestoreRequested += delegate
                {
                    var h = RemoveShippedRequested;
                    if (h != null) h(this, new ExtensionActionEventArgs("shippedRestore", idx));
                };
                _column.Controls.Add(card);
                _shipped.Add(card);
            }

            _section = new JBLabel(LabelRole.Accent, "RECOMMENDED");
            _column.Controls.Add(_section);

            foreach (var rec in ExtensionCatalog.Recommended())
            {
                int index = _cards.Count;
                var card = new RecommendCard(rec[0], rec[1], rec[3]);
                card.OpenRequested += delegate { Raise("recommend", index); };
                _column.Controls.Add(card);
                _cards.Add(card);
            }
            /* The two headings are lifted above the rows that follow them. A heading painted
               under an opaque card is a heading nobody can see, and the layout can be a
               pixel out on a scaled screen - the card wins, and the section looks unlabelled. */
            _column.Controls.SetChildIndex(_shippedHead, 0);
            _column.Controls.SetChildIndex(_section, 0);
            _ready = true;
            LayoutCore();
        }

        private JBButton MakeButton(string text, string icon, BtnKind kind, string key)
        {
            var b = new JBButton { Kind = kind, Icon = icon, Text = text, Radius = Theme.ButtonRadius, Height = Theme.Field };
            b.Click += delegate { Raise(key, -1); };
            return b;
        }

        private void Raise(string key, int index)
        {
            var h = ActionRequested;
            if (h != null) h(this, new ExtensionActionEventArgs(key, index));
        }

        /// <summary>Reuses rows so an action can refresh the list from inside its own click.</summary>
        public void SetItems(List<ExtensionItem> items)
        {
            Items = items ?? new List<ExtensionItem>();
            while (_rows.Count > Items.Count)
            {
                var last = _rows[_rows.Count - 1];
                _rows.RemoveAt(_rows.Count - 1);
                _column.Controls.Remove(last);
                last.Dispose();
            }
            while (_rows.Count < Items.Count)
            {
                var row = new ExtensionRow();
                row.ToggleRequested += delegate(object s, EventArgs e) { Raise("toggle", ((ExtensionRow)s).Index); };
                row.DeleteRequested += delegate(object s, EventArgs e) { Raise("delete", ((ExtensionRow)s).Index); };
                row.StoreRequested += delegate(object s, EventArgs e) { Raise("store", ((ExtensionRow)s).Index); };
                _column.Controls.Add(row);
                _rows.Add(row);
            }
            for (int i = 0; i < Items.Count; i++)
            {
                _rows[i].Item = Items[i];
                _rows[i].Index = i;
                _rows[i].Invalidate();
            }
            LayoutCore();
        }

        /// <summary>Re-reads the shipped pair's state, after a fetch or a removal.</summary>
        public void RefreshShipped()
        {
            var statuses = ExtensionCatalog.DefaultStatuses();
            for (int i = 0; i < _shipped.Count; i++)
            {
                bool present = i < statuses.Count && statuses[i][2] == "Installed";
                string state = i < statuses.Count ? statuses[i][2] : "";
                string detail = i < statuses.Count ? statuses[i][3] : "";
                _shipped[i].SetState(state, present, detail);
            }
            bool missing = false;
            foreach (var row in _shipped) if (!row.Present && row.State != "Removed") missing = true;
            _fetch.Visible = missing;
            PruneLogoCache();
            LayoutCore();
        }

        public void RefreshRows()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (i < Items.Count) { _rows[i].Item = Items[i]; _rows[i].Invalidate(); }
        }

        public void SetBusy(bool busy, string text)
        {
            _addUrl.Text = busy ? text : "Add by store link";
            _addUrl.Enabled = !busy;
            _addUrl.Invalidate();
        }

        private void PaintEmpty(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            float w = _empty.Width;
            float chip = Ui.S(44);
            var cr = new RectangleF(Ui.S(22), (Ui.S(92) - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
            float ic = Ui.S(22);
            Icons.Draw(g, "extension", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);
            Ui.Text(g, "No extensions added yet", Theme.H3, Theme.Text, cr.Right + Ui.S(16), Ui.S(26), w - cr.Right - Ui.S(30));
            Ui.Text(g, "Store link or manifest.json folder.",
                Theme.Small, Theme.TextDim, cr.Right + Ui.S(16), Ui.S(50), w - cr.Right - Ui.S(30), "left", 2);
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            foreach (var r in _rows) r.ReStyle();
            foreach (var c in _cards) c.Invalidate();
            _empty.Invalidate();
            Invalidate(true);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutCore(); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); LayoutCore(); }

        /// <summary>One more pass once the scroll host has fixed its width.</summary>
        public void LayoutAtWidth() { LayoutCore(); }

        private void LayoutCore()
        {
            if (_layingOut || !_ready || _host == null) return;
            _layingOut = true;
            try
            {
                int pad = Ui.S(28);

                /* Four buttons on one line when they fit and wrapped onto two when they do not,
                   rather than a row that runs off the right edge of a narrow window. The band
                   they sit in is measured first, then the scrolling list starts under it. */
                int bandW = Math.Max(Ui.S(280), Width - pad * 2);
                JBButton[] actions = { _addUrl, _addFolder, _addFile, _openFolder };
                /* The bar wraps instead of running off the edge, and the scroller starts under
                   whatever height it ended up needing. */
                int bandBottom = Ui.FlowButtons(actions, pad, Ui.S(6), bandW, Ui.S(10), Ui.S(Theme.ControlHeight));
                int gapBelow = Ui.S(10);
                _host.SetBounds(0, bandBottom + gapBelow, Math.Max(1, Width),
                    Math.Max(1, Height - bandBottom - gapBelow));
                int inner = Math.Max(Ui.S(280), _host.Width - Ui.S(10) - pad * 2);

                int y = Ui.S(10);
                _empty.Visible = Items.Count == 0;
                _empty.SetBounds(pad, y, inner, Ui.S(92));
                if (Items.Count == 0) y += Ui.S(92) + Ui.S(18);

                for (int i = 0; i < _rows.Count; i++)
                {
                    _rows[i].SetBounds(pad, y, inner, ExtensionRow.RowH);
                    y += ExtensionRow.RowH + Ui.S(10);
                }
                if (Items.Count > 0) y += Ui.S(12);

                int headH = Theme.LineHeight(Theme.Small);
                _shippedHead.SetBounds(pad, y, inner, headH);
                y += headH + Ui.S(10);
                for (int i = 0; i < _shipped.Count; i++)
                {
                    _shipped[i].SetBounds(pad, y, inner, ShippedRow.RowH);
                    y += ShippedRow.RowH + Ui.S(8);
                }
                if (_fetch.Visible)
                {
                    /* The retry button sits under the rows it belongs to, with a clear gap on
                       both sides. It used to be laid out with the heading that follows it, and
                       the two ended up on top of each other - a button that covered the
                       heading, and a heading hidden under the cards below it. */
                    _fetch.SetBounds(pad, y, Math.Min(inner, _fetch.PreferredWidth(Ui.S(190))), Ui.S(Theme.ControlHeight));
                    y += Ui.S(Theme.ControlHeight) + Ui.S(18);
                }
                y += Ui.S(6);

                _section.SetBounds(pad, y, inner, headH);
                y += headH + Ui.S(12);

                int gap = Ui.S(12);
                int cols = inner >= Ui.S(1080) ? 3 : inner >= Ui.S(660) ? 2 : 1;
                int cardW = cols > 1 ? (inner - gap * (cols - 1)) / cols : inner;
                /* Two lines of caption plus the title need more than 78 px: the second line used to
                   sit on the bottom border. */
                int cardH = Ui.S(86);
                for (int i = 0; i < _cards.Count; i++)
                {
                    int col = i % cols, row = i / cols;
                    _cards[i].SetBounds(pad + col * (cardW + gap), y + row * (cardH + gap), cardW, cardH);
                }
                int rowsCount = (_cards.Count + cols - 1) / cols;
                y += rowsCount * (cardH + gap) + Ui.S(20);

                _column.SetBounds(0, 0, Math.Max(inner + pad * 2, _host.Width - Ui.S(10)),
                    Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }
    }

    /// <summary>One extension row with an on/off switch, the source and quick actions.</summary>
    internal sealed class ExtensionRow : Control, IReStyle
    {
        public ExtensionItem Item;
        public int Index;
        public event EventHandler ToggleRequested, DeleteRequested, StoreRequested;

        private int _hover = -1;

        public ExtensionRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
            Height = RowH;
            Cursor = Cursors.Hand;
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        private RectangleF SwitchRect
        {
            get { return new RectangleF(Width - Ui.S(16) - Ui.S(44), (Height - Ui.S(24)) / 2f, Ui.S(44), Ui.S(24)); }
        }

        private RectangleF DeleteRect
        {
            get { return new RectangleF(SwitchRect.Left - Ui.S(10) - Ui.S(30), (Height - Ui.S(30)) / 2f, Ui.S(30), Ui.S(30)); }
        }

        /* Chip 40 plus a title and a caption line, with 12 above and below, and never less than
           the two icon buttons need. Two lines of text in 64 px is what left the caption a pixel
           from the bottom edge. */
        public static int RowH
        {
            get
            {
                int text = Theme.LineHeight(Theme.H3) + Ui.S(2) + Theme.LineHeight(Theme.Caption);
                return Math.Max(Ui.S(64), Math.Max(text, Ui.S(40)) + Ui.S(24));
            }
        }

        private RectangleF StoreRect
        {
            get { return new RectangleF(DeleteRect.Left - Ui.S(6) - Ui.S(30), (Height - Ui.S(30)) / 2f, Ui.S(30), Ui.S(30)); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            /* Only the switch itself toggles. The whole right edge used to, which meant a click
               meant for the trash icon could land one pixel short and switch the extension off
               instead of asking to remove it. */
            int hit = SwitchRect.Contains(e.Location) ? 0
                : DeleteRect.Contains(e.Location) ? 1
                : StoreRect.Contains(e.Location) ? 2 : -1;
            if (hit != _hover) { _hover = hit; Invalidate(); }
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (SwitchRect.Contains(e.Location)) { var h = ToggleRequested; if (h != null) h(this, EventArgs.Empty); return; }
            if (DeleteRect.Contains(e.Location)) { var h = DeleteRequested; if (h != null) h(this, EventArgs.Empty); return; }
            if (StoreRect.Contains(e.Location)) { var h = StoreRequested; if (h != null) h(this, EventArgs.Empty); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            if (Item == null) return;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            bool cues = Ui.FocusCues(this);
            /* One radius for both halves of the surface. Filling at 6 and stroking at 10 drew
               the border a couple of pixels proud of the fill at every corner, which is exactly
               the "not quite right" edge the whole redesign is about. */
            Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), cues ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), Theme.Line);

            float chip = Ui.S(34);
            var cr = new RectangleF(Ui.S(16), (Height - chip) / 2f, chip, chip);
            /* The brief asks for each extension to have its own logo, and the row drew the same
               puzzle piece for all of them. The package usually ships one; when it does not,
               the piece is still the fallback. */
            var logo = ExtensionPage.LogoFor(Item);
            if (logo != null)
            {
                var target = new RectangleF(cr.X, cr.Y, chip, chip);
                using (var path = Ui.Round(target, Ui.S(7)))
                {
                    var save = g.Save();
                    g.SetClip(path);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(logo, target);
                    g.Restore(save);
                }
            }
            else
            {
                Ui.Fill(g, cr, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
                float ic = Ui.S(18);
                Icons.Draw(g, "extension", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);
            }

            float x = cr.Right + Ui.S(14);
            /* The two lines are measured against each other, not both pinned to the centre with
               invented offsets: the block is centred, and the caption follows the title's own
               height. */
            float titleH = Theme.LineHeight(Theme.H3);
            float capH = Theme.LineHeight(Theme.Caption);
            float blockTop = (Height - (titleH + Ui.S(2) + capH)) / 2f;
            string name = string.IsNullOrWhiteSpace(Item.Name) ? "Extension" : Item.Name;
            Ui.Text(g, name, Theme.H3, Theme.Text, x, blockTop, Math.Max(Ui.S(120), StoreRect.Left - x - Ui.S(16)));
            string source = Item.Source == "url" ? "Chrome Web Store"
                : Item.Source == "builtin" ? "Built in"
                : Item.Source == "file" ? "Imported file"
                : "Unpacked folder";
            string detail = source + (string.IsNullOrWhiteSpace(Item.Version) ? "" : "  ·  v" + Item.Version);
            Ui.Text(g, detail, Theme.Caption, Theme.TextFaint, x, blockTop + titleH + Ui.S(2),
                Math.Max(Ui.S(120), StoreRect.Left - x - Ui.S(16)));

            // quick actions
            if (_hover == 2) Ui.Fill(g, StoreRect, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay);
            if (_hover == 1) Ui.Fill(g, DeleteRect, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay);
            float s = Ui.S(16);
            if (!string.IsNullOrWhiteSpace(Item.StoreUrl))
                Icons.Draw(g, "external",
                    new RectangleF(StoreRect.X + (StoreRect.Width - s) / 2f, StoreRect.Y + (StoreRect.Height - s) / 2f, s, s),
                    _hover == 2 ? Theme.Accent : Theme.TextDim);
            Icons.Draw(g, "trash",
                new RectangleF(DeleteRect.X + (DeleteRect.Width - s) / 2f, DeleteRect.Y + (DeleteRect.Height - s) / 2f, s, s),
                _hover == 1 ? Theme.Danger : Theme.TextDim);

            // switch
            var sw = SwitchRect;
            bool on = Item.Enabled;
            Ui.Fill(g, sw, Ui.S(Theme.ButtonRadius), on ? Theme.Accent : Theme.ButtonBorder);
            float knob = sw.Height - Ui.S(6);
            float kx = on ? sw.Right - knob - Ui.S(3) : sw.X + Ui.S(3);
            using (var b = new SolidBrush(Theme.OnAccent)) g.FillRectangle(b, kx, sw.Y + Ui.S(3), knob, knob);
            if (!on) Ui.Stroke(g, sw, Ui.S(Theme.ButtonRadius), Theme.Line, 1f);
            if (cues) Ui.FocusRing(g, r, Ui.S(Theme.ButtonRadius));
        }
    }

    /// <summary>
    /// One extension this build ships, with the state Chromium will be in: installed, waiting for
    /// a download, or not installed with the reason the download gave. Drawn as a quiet row
    /// rather than a card, because it is a fact about the build, not something to browse.
    /// </summary>
    internal sealed class ShippedRow : Control, IReStyle
    {
        private readonly string _name;
        private readonly string _id;
        private string _state = "", _detail = "";
        private bool _present;
        private int _hover = -1; // 0 = remove, 1 = restore

        public event EventHandler RemoveRequested;

        /// <summary>
        /// Raised when the user asks for a shipped extension they removed to be put back.
        /// A removal that could not be undone would be a removal, not a choice.
        /// </summary>
        public event EventHandler RestoreRequested;

        public string Id { get { return _id; } }

        public ShippedRow(string name, string id, string detail)
        {
            _name = name ?? "";
            _id = id ?? "";
            _detail = detail ?? "";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Ui.Opaque(this);
            Cursor = Cursors.Hand;
        }

        public string State { get { return _state; } }
        public bool Present { get { return _present; } }

        public void SetState(string state, bool present, string detail)
        {
            _state = state ?? "";
            _present = present;
            if (!string.IsNullOrEmpty(detail)) _detail = detail;
            ToolTips.Attach(this, _detail);
            Invalidate();
        }

        public static int RowH
        {
            get
            {
                return Math.Max(Ui.S(56), Theme.LineHeight(Theme.H3) + Theme.LineHeight(Theme.Caption) + Ui.S(20));
            }
        }

        private RectangleF RemoveRect
        {
            get { return new RectangleF(Width - Ui.S(16) - Ui.S(30), (Height - Ui.S(30)) / 2f, Ui.S(30), Ui.S(30)); }
        }

        /// <summary>
        /// The button that puts a removed extension back. It is a word, not a glyph: an
        /// arrow nobody recognises is not an invitation, and the row already says
        /// "Removed" beside it.
        /// </summary>
        private RectangleF RestoreRect
        {
            get
            {
                float w = Ui.S(86);
                return new RectangleF(Width - Ui.S(16) - w, (Height - Ui.S(28)) / 2f, w, Ui.S(28));
            }
        }

        /// <summary>True when the user has removed this extension and can ask for it back.</summary>
        private bool Removed
        {
            get { return string.Equals(_state, "Removed", StringComparison.OrdinalIgnoreCase); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = -1;
            if (_present && RemoveRect.Contains(e.Location)) hit = 0;
            else if (Removed && RestoreRect.Contains(e.Location)) hit = 1;
            if (hit != _hover) { _hover = hit; Invalidate(); }
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            /* The one action the shipped rows lacked: removing one. It existed only on the
               library row further down - two rows for one extension, and the remove on the
               one nobody could tell apart from the status card. */
            if (e.Button != MouseButtons.Left) return;
            if (_present && RemoveRect.Contains(e.Location))
            {
                var h = RemoveRequested;
                if (h != null) h(this, EventArgs.Empty);
            }
            else if (Removed && RestoreRect.Contains(e.Location))
            {
                /* And the other way: a removal the user cannot undo is a removal, not a
                   choice. The row offers the way back. */
                var h = RestoreRequested;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            g.Clear(Ui.SurfaceColor(this));
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(Theme.CardRadius), Theme.Panel);
            Ui.Stroke(g, r, Ui.S(Theme.CardRadius), Theme.Line);

            Color tone = _present ? Theme.Success : _state == "Removed" ? Theme.TextFaint : Theme.Warn;
            float dot = Ui.S(9);
            using (var b = new SolidBrush(tone))
                g.FillEllipse(b, Ui.S(16), (Height - dot) / 2f, dot, dot);

            /* The remove affordance sits where the state text used to end, and the state text
               moves left of it - both are measured, so neither runs into the other. A removed
               extension offers the way back instead of a bin. */
            float rightEdge = Width - Ui.S(16);
            if (_present)
            {
                if (_hover == 0) Ui.Fill(g, RemoveRect, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay);
                float s = Ui.S(15);
                Icons.Draw(g, "trash", new RectangleF(RemoveRect.X + (RemoveRect.Width - s) / 2f,
                    RemoveRect.Y + (RemoveRect.Height - s) / 2f, s, s), _hover == 0 ? Theme.Danger : Theme.TextDim);
                rightEdge = RemoveRect.Left - Ui.S(10);
            }
            else if (Removed)
            {
                var rr = RestoreRect;
                if (_hover == 1) Ui.Fill(g, rr, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay);
                Ui.Text(g, "Add back", Theme.Caption, _hover == 1 ? Theme.Accent : Theme.TextDim,
                    rr.X, rr.Y + (rr.Height - Theme.LineHeight(Theme.Caption)) / 2f, rr.Width, "center");
                rightEdge = rr.Left - Ui.S(12);
            }

            /* The state is measured, so the name is given exactly what is left and cannot run
               under it. */
            float stateW = Ui.Measure(g, _state, Theme.Caption).Width + Ui.S(4);
            float left = Ui.S(16) + dot + Ui.S(12);
            float nameW = Math.Max(Ui.S(60), rightEdge - left - stateW - Ui.S(8));
            Ui.Text(g, _name, Theme.H3, Theme.Text, left, (Height - Theme.LineHeight(Theme.H3)) / 2f, nameW);
            Ui.Text(g, _state, Theme.Caption, tone, rightEdge - stateW,
                (Height - Theme.LineHeight(Theme.Caption)) / 2f, stateW);
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }
    }

    /// <summary>Recommended extension tile. Opens a store search, never a made-up id.</summary>
    internal sealed class RecommendCard : Control, IReStyle
    {
        private readonly string _title, _desc, _icon;
        private bool _hover;
        public event EventHandler OpenRequested;

        public RecommendCard(string title, string desc, string icon)
        {
            _title = title;
            _desc = desc;
            _icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Ui.Opaque(this);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                var h = OpenRequested;
                if (h != null) h(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                var h = OpenRequested;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), _hover ? Theme.PanelHover : Theme.Panel);
            Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), _hover ? Theme.AccentBorder : Theme.Line, 1f);
            if (Ui.FocusCues(this)) Ui.FocusRing(g, r, Ui.S(Theme.ButtonRadius));

            float chip = Ui.S(34);
            var cr = new RectangleF(Ui.S(14), (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
            float ic = Ui.S(18);
            Icons.Draw(g, _icon, new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

            float tx = cr.Right + Ui.S(14);
            Ui.Text(g, _title, Theme.H3, Theme.Text, tx, Ui.S(16), Math.Max(Ui.S(80), Width - tx - Ui.S(30)));
            Ui.Text(g, _desc, Theme.Caption, Theme.TextDim, tx, Ui.S(40),
                Math.Max(Ui.S(80), Width - tx - Ui.S(30)), "left", 2);
            float a = Ui.S(14);
            Icons.Draw(g, "external", new RectangleF(Width - Ui.S(26), Ui.S(14), a, a), _hover ? Theme.Accent : Theme.TextFaint);
        }
    }
}
