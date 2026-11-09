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
            var b = new JBButton { Kind = kind, Icon = icon, Text = text, Radius = Theme.ButtonRadius, Height = Theme.Field };
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
            /* The empty panel is 96 tall and put its two lines at 28 and 52 from the top, which
               is arithmetic for one font size; the pair is measured and centred with the chip
               now. Centred against THIS panel's height - the old code read the page's height,
               which is hundreds of pixels, and pushed the tile and the text out of the box. */
            float w = _empty.Width;
            float boxH = _empty.Height;
            float chip = Ui.S(44);
            var cr = new RectangleF(Ui.S(22), (boxH - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
            float ic = Ui.S(22);
            Icons.Draw(g, "proxy", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);
            float h3 = Theme.LineHeight(Theme.H3);
            float sm = Theme.LineHeight(Theme.Small);
            float top = Math.Max(Ui.S(6), (boxH - (h3 + Ui.S(2) + sm)) / 2f);
            Ui.Text(g, "No proxies saved yet", Theme.H3, Theme.Text, cr.Right + Ui.S(16), top, w - cr.Right - Ui.S(30));
            Ui.Text(g, "Add one to use it in profiles.",
                Theme.Small, Theme.TextDim, cr.Right + Ui.S(16), top + h3 + Ui.S(2), w - cr.Right - Ui.S(30), "left", 1);
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
                int y = Ui.FlowButtons(actions, pad, by, inner, Ui.S(10), Ui.S(Theme.ControlHeight)) + Ui.S(22);
                _empty.Visible = Items.Count == 0;
                _empty.SetBounds(pad, y, inner, Ui.S(96));
                if (Items.Count == 0) y += Ui.S(96) + Ui.S(18);

                for (int i = 0; i < _rows.Count; i++)
                {
                    _rows[i].SetBounds(pad, y, inner, Ui.S(72));
                    y += Ui.S(72) + Ui.S(8);
                }
                if (Items.Count > 0) y += Ui.S(10);

                _promo.SetBounds(pad, y, inner, PromoCard.CardH);
                y += PromoCard.CardH + Ui.S(24);

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
            /* The address is the value people ellipsize by accident: a long host in a narrow
               column is cut with no way to read the whole thing. Hovering the row now tells
               you exactly what is saved - and the right-click menu copies any part of it. */
            string key = hit >= 0 && hit < ActionTips.Length ? ActionTips[hit] : RowTip();
            if (key == _tipKey) return;
            _tipKey = key;
            try { Tips.SetToolTip(this, key); } catch { }
        }

        /// <summary>The row tooltip: everything the row knows, including credentials.</summary>
        private string RowTip()
        {
            if (Item == null || Item.Config == null || !Item.Config.Enabled) return "Click to apply this proxy live";
            var c = Item.Config;
            string addr = (c.Type == "socks5" ? "socks5://" : c.Type == "https" ? "https://" : "http://")
                + (string.IsNullOrEmpty(c.User) ? "" : c.User + (string.IsNullOrEmpty(c.Password) ? "" : ":" + c.Password) + "@")
                + c.Host + ":" + c.Port;
            string tip = addr + "\nClick to apply this proxy live · right-click to copy";
            if (!string.IsNullOrEmpty(Item.LastIp)) tip += "\nLast check: " + Item.LastIp + (Item.LastLatencyMs > 0 ? " · " + Item.LastLatencyMs + " ms" : "");
            return tip;
        }

        /// <summary>The full proxy link, ready to paste into any tool that takes one.</summary>
        private string FullAddress()
        {
            if (Item == null || Item.Config == null) return "";
            var c = Item.Config;
            if (!c.Enabled) return "";
            return (c.Type == "socks5" ? "socks5://" : c.Type == "https" ? "https://" : "http://")
                + (string.IsNullOrEmpty(c.User) ? "" : c.User + (string.IsNullOrEmpty(c.Password) ? "" : ":" + c.Password) + "@")
                + c.Host + ":" + c.Port;
        }

        private static void CopyTo(string text, string what)
        {
            try { Clipboard.SetText(text ?? ""); } catch { }
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
            /* Right click is the inspect gesture on a list row: copy the address, the host, the
               credentials - the things a proxy manager exists to carry - plus the actions the
               icons already offer. Nothing here touched the right button before, which is why
               "copy the proxy IP" was reported as impossible. */
            if (e.Button == MouseButtons.Right)
            {
                ShowRowMenu(e.Location);
                return;
            }
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

        private void ShowRowMenu(Point at)
        {
            if (Item == null || Item.Config == null || !Item.Config.Enabled) return;
            var c = Item.Config;
            var menu = JBMenu.Create();
            string full = FullAddress();
            string hostPort = c.Host + ":" + c.Port;
            JBMenu.Add(menu, "Copy full address", "copy", delegate { CopyTo(full, "address"); });
            JBMenu.Add(menu, "Copy IP / host and port", "globe", delegate { CopyTo(hostPort, "host"); });
            if (!string.IsNullOrEmpty(c.User))
                JBMenu.Add(menu, "Copy username", "user", delegate { CopyTo(c.User ?? "", "user"); });
            if (!string.IsNullOrEmpty(c.Password))
                JBMenu.Add(menu, "Copy password", "key", delegate { CopyTo(c.Password ?? "", "password"); });
            if (!string.IsNullOrEmpty(Item.LastIp))
                JBMenu.Add(menu, "Copy last checked IP", "check-circle", delegate { CopyTo(Item.LastIp ?? "", "ip"); });
            menu.Items.Add(new ToolStripSeparator { Font = Theme.Body });
            JBMenu.Add(menu, "Check now", "bolt", delegate { var h = TestRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Apply live", "sync", delegate { var h = LiveRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Edit", "pencil", delegate { var h = EditRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Delete", "trash", delegate { var h = DeleteRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.ShowNear(menu, this, at);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            if (Item == null) return;
            /* A row is a surface with a hairline, and it lifts under the pointer: on a list of
               twenty proxies the hovered row is how a person knows which one a click will hit. */
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), _hover >= 0 || _rowHover ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), _hover >= 0 || _rowHover ? Theme.HoverStrong : Theme.Line);

            float pad = Ui.S(16);
            float chip = Ui.S(36);
            var cr = new RectangleF(pad, (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
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
            /* The name and its address are a measured pair, centred together. Fixed offsets from
               the middle (15 up, 3 down) only line up for the font they were written at. */
            float nameH = Theme.LineHeight(Theme.H3);
            float addrH = Theme.LineHeight(Theme.Caption);
            float nameTop = Math.Max(Ui.S(4), (Height - (nameH + addrH)) / 2f);
            Ui.Text(g, name, Theme.H3, Theme.Text, x, nameTop, cols[0] - Ui.S(8));
            Ui.Text(g, Item.Connection, Theme.Caption, Theme.TextFaint, x, nameTop + nameH, cols[0] - Ui.S(8));
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
                if (_hover == i) { Ui.Fill(g, a[i], Ui.S(Theme.ButtonRadius), Theme.HoverOverlay); Ui.Stroke(g, a[i], Ui.S(Theme.ButtonRadius), Theme.Line); }
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
            Height = CardH;
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        /// <summary>
        /// A 46 px chip and three measured lines, with the same inset above and below. Derived
        /// from the type scale so the card grows with the text instead of clipping it.
        /// </summary>
        public static int CardH
        {
            get
            {
                int text = Theme.LineHeight(Theme.H2) + Theme.LineHeight(Theme.Small) +
                           Theme.LineHeight(Theme.Caption) + Ui.S(8);
                return Math.Max(Ui.S(112), Math.Max(text, Ui.S(46)) + Ui.S(24));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), Theme.Panel);
            Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), Theme.Line);

            float chip = Ui.S(46);
            var cr = new RectangleF(Ui.S(20), (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
            float ic = Ui.S(24);
            Icons.Draw(g, "globe", new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

            float tx = cr.Right + Ui.S(18);
            /* The text and the badge share one row, and the text used to reserve a fixed 180 px
               for the badge. On a narrow window the badge is what ran out of room, so the badge is
               measured and the text takes what is left. */
            float bw = Math.Max(Ui.S(96), Ui.Measure(g, "Coming soon", Theme.SmallBold).Width + Ui.S(28));
            float bh = Ui.S(32);
            float textW = Math.Max(Ui.S(120), Width - tx - bw - Ui.S(36));
            /* Three lines at 20, 50 and 74 from the top were one line of arithmetic for one font
               size. They are measured and centred as a block now, so the card holds at any scale. */
            float h2 = Theme.LineHeight(Theme.H2);
            float sm = Theme.LineHeight(Theme.Small);
            float cap = Theme.LineHeight(Theme.Caption);
            float blockTop = Math.Max(Ui.S(10), (Height - (h2 + sm + cap + Ui.S(8))) / 2f);
            Ui.Text(g, "Static residential proxy", Theme.H2, Theme.Text, tx, blockTop, textW);
            Ui.Text(g, "1 proxy  ·  $3.50 / month  ·  clean, dedicated home IP", Theme.Small, Theme.TextDim,
                tx, blockTop + h2 + Ui.S(2), textW);
            Ui.Text(g, "Buy direct inside Joe Browser. No third-party checkout, no tracking.",
                Theme.Caption, Theme.TextFaint, tx, blockTop + h2 + sm + Ui.S(4), textW);

            var badge = new RectangleF(Width - Ui.S(20) - bw, (Height - bh) / 2f, bw, bh);
            Ui.Fill(g, badge, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
            Ui.Stroke(g, badge, Ui.S(Theme.ButtonRadius), Theme.AccentBorder);
            Ui.Text(g, "Coming soon", Theme.SmallBold, Theme.AccentText, badge.X,
                badge.Y + (bh - Theme.SmallBold.GetHeight(g)) / 2f, bw, "center");
        }
    }
}
