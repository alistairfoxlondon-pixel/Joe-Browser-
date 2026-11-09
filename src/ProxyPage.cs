using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal sealed class ProxyActionEventArgs : EventArgs
    {
        public readonly string Key;
        public readonly int Index;
        public ProxyActionEventArgs(string key, int index) { Key = key; Index = index; }
    }

    /// <summary>
    /// Full-width Proxy Library. A proxy is stored once, checked here, then picked when a
    /// profile is created. A running profile can also be re-pointed at a different proxy
    /// without closing its browser tab.
    /// </summary>
    internal sealed class ProxyPage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly List<ProxyRow> _rows = new List<ProxyRow>();
        private JBButton _addBtn, _importBtn, _checkBtn;
        private PromoCard _promo;
        private JBPanel _empty;
        private bool _layingOut;
        private bool _ready;

        public event EventHandler<ProxyActionEventArgs> ActionRequested;

        public List<ProxyEntry> Items { get; private set; } = new List<ProxyEntry>();

        public ProxyPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            /* The header band above already names the page and explains it; repeating a title
               and a subtitle inside the scroll area was pure duplication. */

            _addBtn = MakeButton("Add proxy", "plus", BtnKind.Primary, "add");
            _importBtn = MakeButton("Import list", "download", BtnKind.Soft, "import");
            _checkBtn = MakeButton("Check all", "bolt", BtnKind.Soft, "checkall");
            _column.Controls.Add(_addBtn);
            _column.Controls.Add(_importBtn);
            _column.Controls.Add(_checkBtn);

            _promo = new PromoCard();
            _column.Controls.Add(_promo);

            _empty = new JBPanel();
            _empty.Paint += PaintEmpty;
            _column.Controls.Add(_empty);
            _ready = true;
            LayoutCore();
        }

        private JBButton MakeButton(string text, string icon, BtnKind kind, string key)
        {
            var b = new JBButton { Kind = kind, Icon = icon, Text = text, Radius = 6, Height = Theme.Field };
            b.Click += delegate { Raise(key, -1); };
            return b;
        }

        private void Raise(string key, int index)
        {
            var h = ActionRequested;
            if (h != null) h(this, new ProxyActionEventArgs(key, index));
        }

        /// <summary>
        /// Reuses existing rows so a click handler can refresh the list without disposing the
        /// control that is currently dispatching that click.
        /// </summary>
        public void SetItems(List<ProxyEntry> items)
        {
            Items = items ?? new List<ProxyEntry>();
            while (_rows.Count > Items.Count)
            {
                var last = _rows[_rows.Count - 1];
                _rows.RemoveAt(_rows.Count - 1);
                _column.Controls.Remove(last);
                last.Dispose();
            }
            while (_rows.Count < Items.Count)
            {
                var row = new ProxyRow();
                row.TestRequested += delegate(object s, EventArgs e) { Raise("test", ((ProxyRow)s).Index); };
                row.UseRequested += delegate(object s, EventArgs e) { Raise("use", ((ProxyRow)s).Index); };
                row.EditRequested += delegate(object s, EventArgs e) { Raise("edit", ((ProxyRow)s).Index); };
                row.DeleteRequested += delegate(object s, EventArgs e) { Raise("delete", ((ProxyRow)s).Index); };
                row.LiveRequested += delegate(object s, EventArgs e) { Raise("live", ((ProxyRow)s).Index); };
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
            _checkBtn.Text = busy ? text : "Check all";
            _checkBtn.Enabled = !busy;
            _checkBtn.Invalidate();
        }

        private void PaintEmpty(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            float w = _empty.Width;
            float chip = Ui.S(44);
            var cr = new RectangleF(Ui.S(22), (Ui.S(96) - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(8), Theme.AccentOnBg);
            float ic = Ui.S(22);
            Icons.Draw(g, "proxy", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);
            Ui.Text(g, "No proxies saved yet", Theme.H3, Theme.Text, cr.Right + Ui.S(16), Ui.S(28), w - cr.Right - Ui.S(30));
            Ui.Text(g, "Add one to use it in profiles.",
                Theme.Small, Theme.TextDim, cr.Right + Ui.S(16), Ui.S(52), w - cr.Right - Ui.S(30), "left", 2);
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            foreach (var r in _rows) r.ReStyle();
            _promo.Invalidate();
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
                _host.SetBounds(0, 0, Math.Max(1, Width), Math.Max(1, Height));
                int w = Math.Max(Ui.S(320), _host.Width - Ui.S(10));
                int pad = Ui.S(28);
                int inner = Math.Max(Ui.S(200), w - pad * 2);

                /* Laid out by the shared flow so the three buttons wrap on a narrow window
                   instead of the third one running past the right edge, which is what the
                   hand-placed positions above did. */
                int by = Ui.S(6);
                JBButton[] actions = { _addBtn, _importBtn, _checkBtn };
                int y = Ui.FlowButtons(actions, pad, by, inner, Ui.S(10), Ui.S(33)) + Ui.S(22);
                _empty.Visible = Items.Count == 0;
                _empty.SetBounds(pad, y, inner, Ui.S(96));
                if (Items.Count == 0) y += Ui.S(96) + Ui.S(18);

                for (int i = 0; i < _rows.Count; i++)
                {
                    _rows[i].SetBounds(pad, y, inner, Ui.S(72));
                    y += Ui.S(72) + Ui.S(8);
                }
                if (Items.Count > 0) y += Ui.S(10);

                _promo.SetBounds(pad, y, inner, Ui.S(112));
                y += Ui.S(112) + Ui.S(24);

                _column.SetBounds(0, 0, Math.Max(inner + pad * 2, w), Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }
    }

    /// <summary>One proxy row: name, connection, detected location, status and inline actions.</summary>
    internal sealed class ProxyRow : Control, IReStyle
    {
        public ProxyEntry Item;
        public int Index;
        public event EventHandler TestRequested, UseRequested, EditRequested, DeleteRequested, LiveRequested;

        private int _hover = -1;
        private bool _rowHover;
        private static readonly string[] ActionKeys = { "bolt", "plus", "pencil", "trash" };
        private static readonly string[] ActionTips = { "Check this proxy", "Create a profile with it", "Edit", "Delete" };

        /* One shared tooltip for every row: the four icons carry no text, and the row body has a
           second action (apply live) that nothing else advertises. */
        private static readonly ToolTip Tips = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 500,
            ReshowDelay = 150,
            AutoPopDelay = 6000
        };

        private string _tipKey;

        /// <summary>Shows the tip for the action under the pointer, or for the row itself.</summary>
        private void SyncTip(int hit)
        {
            string key = hit >= 0 && hit < ActionTips.Length ? ActionTips[hit] : "Click to apply this proxy live";
            if (key == _tipKey) return;
            _tipKey = key;
            try { Tips.SetToolTip(this, key); } catch { }
        }

        public ProxyRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
            Height = Ui.S(72);
            Cursor = Cursors.Hand;
        }

        public void ReStyle() { Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        private RectangleF[] Actions()
        {
            float s = Ui.S(30);
            float right = Width - Ui.S(16);
            var list = new RectangleF[4];
            for (int i = 3; i >= 0; i--)
            {
                list[i] = new RectangleF(right - s, (Height - s) / 2f, s, s);
                right -= s + Ui.S(6);
            }
            return list;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var a = Actions();
            int hit = -1;
            for (int i = 0; i < a.Length; i++) if (a[i].Contains(e.Location)) { hit = i; break; }
            SyncTip(hit);
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            bool row = hit < 0;
            if (hit != _hover || row != _rowHover) { _hover = hit; _rowHover = row; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; _rowHover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var a = Actions();
            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Contains(e.Location)) continue;
                EventHandler h = i == 0 ? TestRequested : i == 1 ? UseRequested : i == 2 ? EditRequested : DeleteRequested;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
            // Clicking the row body offers the live re-point action.
            var live = LiveRequested;
            if (live != null) live(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            if (Item == null) return;
            /* A row is a surface with a hairline, and it lifts under the pointer: on a list of
               twenty proxies the hovered row is how a person knows which one a click will hit. */
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(10), _hover >= 0 || _rowHover ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, r, Ui.S(10), _hover >= 0 || _rowHover ? Theme.HoverStrong : Theme.Line);

            float pad = Ui.S(16);
            float chip = Ui.S(36);
            var cr = new RectangleF(pad, (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(7), Theme.AccentOnBg);
            float ic = Ui.S(18);
            Icons.Draw(g, "proxy", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

            float x = cr.Right + Ui.S(14);
            float tableRight = Actions()[0].Left - Ui.S(18);
            /* On a narrow window the four columns used to be forced to a 240 px minimum and so
               ran under the action icons on the right. The hint column is the first thing to go,
               then the remaining three share what is really there. */
            float total = Math.Max(0f, tableRight - x);
            bool showHint = total >= Ui.S(260);
            float[] cols = showHint
                ? new[] { total * 0.26f, total * 0.27f, total * 0.25f, total * 0.22f }
                : new[] { total * 0.34f, total * 0.34f, total * 0.32f, 0f };
            float mid = Height / 2f;

            string name = string.IsNullOrWhiteSpace(Item.Name) ? Item.Connection : Item.Name;
            Ui.Text(g, name, Theme.H3, Theme.Text, x, mid - Ui.S(15), cols[0] - Ui.S(8));
            Ui.Text(g, Item.Connection, Theme.Caption, Theme.TextFaint, x, mid + Ui.S(3), cols[0] - Ui.S(8));
            x += cols[0];

            Ui.Text(g, Item.Location, Theme.Body, Theme.TextDim, x, mid - Theme.Body.GetHeight(g) / 2f, cols[1] - Ui.S(10));
            x += cols[1];

            string state = Item.LastStatus == 1 ? "Working" : Item.LastStatus == -1 ? "Offline" : "Not checked";
            Color stateColor = Item.LastStatus == 1 ? Theme.Success : Item.LastStatus == -1 ? Theme.Danger : Theme.TextFaint;
            float dot = Ui.S(8);
            using (var b = new SolidBrush(stateColor)) g.FillEllipse(b, x, mid - dot / 2f, dot, dot);
            string latency = Item.LastStatus == 1 && Item.LastLatencyMs > 0 ? "  " + Item.LastLatencyMs + " ms" : "";
            Ui.Text(g, state + latency, Theme.Body, stateColor, x + dot + Ui.S(9), mid - Theme.Body.GetHeight(g) / 2f, cols[2] - dot - Ui.S(16));
            x += cols[2];

            if (showHint)
                Ui.Text(g, "Click row to apply live", Theme.Caption, Theme.TextFaint, x,
                    mid - Theme.Caption.GetHeight(g) / 2f, cols[3] - Ui.S(8));

            var a = Actions();
            for (int i = 0; i < a.Length; i++)
            {
                if (_hover == i) { Ui.Fill(g, a[i], Ui.S(7), Theme.HoverOverlay); Ui.Stroke(g, a[i], Ui.S(7), Theme.Line); }
                float s = Ui.S(15);
                Icons.Draw(g, ActionKeys[i],
                    new RectangleF(a[i].X + (a[i].Width - s) / 2f, a[i].Y + (a[i].Height - s) / 2f, s, s),
                    _hover == i ? Theme.Accent : Theme.TextDim);
            }
        }
    }

    /// <summary>Buy-proxy placeholder. Honest "Coming soon" badge, no fake checkout.</summary>
    internal sealed class PromoCard : Control, IReStyle
    {
        public PromoCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
            Height = Ui.S(112);
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(8), Theme.Panel);
            Ui.Stroke(g, r, Ui.S(10), Theme.Line);

            float chip = Ui.S(46);
            var cr = new RectangleF(Ui.S(20), (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(9), Theme.AccentOnBg);
            float ic = Ui.S(24);
            Icons.Draw(g, "globe", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

            float tx = cr.Right + Ui.S(18);
            /* The text and the badge share one row, and the text used to reserve a fixed 180 px
               for the badge. On a narrow window the badge is what ran out of room, so the badge is
               measured and the text takes what is left. */
            float bw = Math.Max(Ui.S(96), Ui.Measure(g, "Coming soon", Theme.SmallBold).Width + Ui.S(28));
            float bh = Ui.S(32);
            float textW = Math.Max(Ui.S(120), Width - tx - bw - Ui.S(36));
            Ui.Text(g, "Static residential proxy", Theme.H2, Theme.Text, tx, Ui.S(20), textW);
            Ui.Text(g, "1 proxy  ·  $3.50 / month  ·  clean, dedicated home IP", Theme.Small, Theme.TextDim,
                tx, Ui.S(50), textW);
            Ui.Text(g, "Buy direct inside Joe Browser. No third-party checkout, no tracking.",
                Theme.Caption, Theme.TextFaint, tx, Ui.S(74), textW);

            var badge = new RectangleF(Width - Ui.S(20) - bw, (Height - bh) / 2f, bw, bh);
            Ui.Fill(g, badge, Ui.S(6), Theme.AccentOnBg);
            Ui.Stroke(g, badge, Ui.S(6), Theme.AccentBorder, 1f);
            Ui.Text(g, "Coming soon", Theme.SmallBold, Theme.AccentText, badge.X,
                badge.Y + (bh - Theme.SmallBold.GetHeight(g)) / 2f, bw, "center");
        }
    }
}
