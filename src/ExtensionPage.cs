using System;
using System.Collections.Generic;
using System.Drawing;
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
    internal sealed class ExtensionPage : Panel, IReStyle
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly List<ExtensionRow> _rows = new List<ExtensionRow>();
        private readonly List<RecommendCard> _cards = new List<RecommendCard>();
        private JBButton _addUrl, _addFolder, _openFolder;
        private JBLabel _title, _sub, _section;
        private JBPanel _empty;
        private bool _layingOut;
        private bool _ready;

        public event EventHandler<ExtensionActionEventArgs> ActionRequested;

        public List<ExtensionItem> Items { get; private set; } = new List<ExtensionItem>();

        public ExtensionPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            _title = new JBLabel(LabelRole.Strong, "Extensions");
            _sub = new JBLabel(LabelRole.Faint,
                "Add once, switch on or off here. Enabled extensions load into every Chromium profile you open.");
            _sub.Wrap = true;
            _column.Controls.Add(_title);
            _column.Controls.Add(_sub);

            _addUrl = MakeButton("Add by store link", "link", BtnKind.Primary, "url");
            _addFolder = MakeButton("Add unpacked folder", "folder", BtnKind.Soft, "folder");
            _openFolder = MakeButton("Open folder", "external", BtnKind.Soft, "openfolder");
            _column.Controls.Add(_addUrl);
            _column.Controls.Add(_addFolder);
            _column.Controls.Add(_openFolder);

            _empty = new JBPanel();
            _empty.Paint += PaintEmpty;
            _column.Controls.Add(_empty);

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
            _ready = true;
            LayoutCore();
        }

        private JBButton MakeButton(string text, string icon, BtnKind kind, string key)
        {
            var b = new JBButton { Kind = kind, Icon = icon, Text = text, Radius = 4, Height = Ui.S(33) };
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
            Ui.Fill(g, cr, Ui.S(8), Theme.AccentOnBg);
            float ic = Ui.S(22);
            Icons.Draw(g, "apps", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);
            Ui.Text(g, "No extensions added yet", Theme.H3, Theme.Text, cr.Right + Ui.S(16), Ui.S(26), w - cr.Right - Ui.S(30));
            Ui.Text(g, "Paste a Chrome Web Store link, or choose a folder that contains manifest.json.",
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

        private void LayoutCore()
        {
            if (_layingOut || !_ready || _host == null) return;
            _layingOut = true;
            try
            {
                _host.SetBounds(0, 0, Math.Max(1, Width), Math.Max(1, Height));
                int pad = Ui.S(28);
                int inner = Math.Max(Ui.S(280), _host.Width - Ui.S(10) - pad * 2);

                _title.SetBounds(pad, Ui.S(6), inner, Ui.S(22));
                _sub.SetBounds(pad, Ui.S(30), inner, Ui.S(20));

                int b1 = _addUrl.PreferredWidth(Ui.S(150));
                int b2 = _addFolder.PreferredWidth(Ui.S(160));
                int b3 = _openFolder.PreferredWidth(Ui.S(130));
                int by = Ui.S(60);
                _addUrl.SetBounds(pad, by, b1, Ui.S(33));
                _addFolder.SetBounds(pad + b1 + Ui.S(10), by, b2, Ui.S(33));
                _openFolder.SetBounds(pad + b1 + b2 + Ui.S(20), by, b3, Ui.S(33));

                int y = by + Ui.S(33) + Ui.S(22);
                _empty.Visible = Items.Count == 0;
                _empty.SetBounds(pad, y, inner, Ui.S(92));
                if (Items.Count == 0) y += Ui.S(92) + Ui.S(18);

                for (int i = 0; i < _rows.Count; i++)
                {
                    _rows[i].SetBounds(pad, y, inner, Ui.S(64));
                    y += Ui.S(64) + Ui.S(8);
                }
                if (Items.Count > 0) y += Ui.S(12);

                _section.SetBounds(pad, y, inner, Ui.S(18));
                y += Ui.S(18) + Ui.S(12);

                int gap = Ui.S(12);
                int cols = inner >= Ui.S(1080) ? 3 : inner >= Ui.S(660) ? 2 : 1;
                int cardW = cols > 1 ? (inner - gap * (cols - 1)) / cols : inner;
                int cardH = Ui.S(78);
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
            Height = Ui.S(64);
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

        private RectangleF StoreRect
        {
            get { return new RectangleF(DeleteRect.Left - Ui.S(6) - Ui.S(30), (Height - Ui.S(30)) / 2f, Ui.S(30), Ui.S(30)); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = e.X >= SwitchRect.Left ? 0 : DeleteRect.Contains(e.Location) ? 1 : StoreRect.Contains(e.Location) ? 2 : -1;
            if (hit != _hover) { _hover = hit; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.X >= SwitchRect.Left) { var h = ToggleRequested; if (h != null) h(this, EventArgs.Empty); return; }
            if (DeleteRect.Contains(e.Location)) { var h = DeleteRequested; if (h != null) h(this, EventArgs.Empty); return; }
            if (StoreRect.Contains(e.Location)) { var h = StoreRequested; if (h != null) h(this, EventArgs.Empty); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            if (Item == null) return;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(6), Theme.Card);
            Ui.Stroke(g, r, Ui.S(6), Theme.Line, 1f);

            float chip = Ui.S(34);
            var cr = new RectangleF(Ui.S(16), (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(7), Theme.AccentOnBg);
            float ic = Ui.S(18);
            Icons.Draw(g, "apps", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

            float x = cr.Right + Ui.S(14);
            float mid = Height / 2f;
            string name = string.IsNullOrWhiteSpace(Item.Name) ? "Extension" : Item.Name;
            Ui.Text(g, name, Theme.H3, Theme.Text, x, mid - Ui.S(15), Math.Max(Ui.S(120), StoreRect.Left - x - Ui.S(16)));
            string source = Item.Source == "url" ? "Chrome Web Store" : Item.Source == "builtin" ? "Built in" : "Unpacked folder";
            string detail = source + (string.IsNullOrWhiteSpace(Item.Version) ? "" : "  ·  v" + Item.Version);
            Ui.Text(g, detail, Theme.Caption, Theme.TextFaint, x, mid + Ui.S(3), Math.Max(Ui.S(120), StoreRect.Left - x - Ui.S(16)));

            // quick actions
            if (_hover == 2) Ui.Fill(g, StoreRect, Ui.S(4), Theme.HoverOverlay);
            if (_hover == 1) Ui.Fill(g, DeleteRect, Ui.S(4), Theme.HoverOverlay);
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
            Ui.Fill(g, sw, sw.Height / 2f, on ? Theme.Accent : Theme.ButtonBorder);
            float knob = sw.Height - Ui.S(6);
            float kx = on ? sw.Right - knob - Ui.S(3) : sw.X + Ui.S(3);
            using (var b = new SolidBrush(Theme.OnAccent)) g.FillEllipse(b, kx, sw.Y + Ui.S(3), knob, knob);
            if (!on) Ui.Stroke(g, sw, sw.Height / 2f, Theme.Line, 1f);
        }
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
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
            Cursor = Cursors.Hand;
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { var h = OpenRequested; if (h != null) h(this, EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(8), _hover ? Theme.PanelHover : Theme.Panel);
            Ui.Stroke(g, r, Ui.S(8), _hover ? Theme.AccentBorder : Theme.Line, 1f);

            float chip = Ui.S(34);
            var cr = new RectangleF(Ui.S(14), (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(7), Theme.AccentOnBg);
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
