using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Profile tile styled as a windows-ui card: #303030 surface, 8px radius, hairline border,
    /// a 36px identity chip, metadata chips and a quiet action row.
    /// </summary>
    internal sealed class ProfileCard : Control, IReStyle
    {
        public Profile Model;
        public bool Running;
        public bool Launching;
        public bool Picked;

        public event EventHandler OpenRequested;
        public event EventHandler EditRequested;
        public event EventHandler ProxyRequested;
        public event EventHandler DuplicateRequested;
        public event EventHandler DeleteRequested;
        public event EventHandler FocusRequested;
        public event EventHandler CloseRequested;
        public event EventHandler PickToggled;

        private bool _hover, _kebabHover, _openHover;
        private ContextMenuStrip _menu;
        private readonly ToolTip _tip;
        private string _tipText = "";

        public ProfileCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Ui.Opaque(this);
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = Theme.Body;

            _tip = new ToolTip { OwnerDraw = true, InitialDelay = 500, ReshowDelay = 200, AutoPopDelay = 9000 };
            _tip.Popup += TipPopup;
            _tip.Draw += TipDraw;
            Disposed += delegate { if (_menu != null) _menu.Dispose(); };
        }

        private void TipPopup(object sender, PopupEventArgs e)
        {
            using (var g = CreateGraphics())
            {
                var size = Ui.Measure(g, _tipText, Theme.Small);
                e.ToolTipSize = new Size((int)size.Width + Ui.S(24), Ui.S(36));
            }
        }

        private void TipDraw(object sender, DrawToolTipEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0, 0, e.Bounds.Width, e.Bounds.Height);
            Ui.Fill(g, r, Ui.S(4), Theme.Sidebar);
            Ui.Stroke(g, r, Ui.S(4), Theme.Line, 1f);
            string[] lines = (_tipText ?? "").Split('\n');
            float y = Ui.S(8);
            for (int i = 0; i < lines.Length; i++)
            {
                Ui.Text(g, lines[i], i == 0 ? Theme.SmallBold : Theme.Caption,
                    i == 0 ? Theme.Text : Theme.TextDim, Ui.S(12), y);
                y += Theme.Small.GetHeight(g) + Ui.S(1);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            RefreshTip();
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnGotFocus(EventArgs e) { RefreshTip(); Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        private void RefreshTip()
        {
            if (Model == null) return;
            string text = Model.Proxy.Enabled ? Model.Proxy.Detail : "Direct connection";
            _tipText = Model.Fp.Os + "  ·  " + Model.Fp.TimezoneShort + "\n" + text + "\n" + Model.LastOpenedText;
            _tip.SetToolTip(this, _tipText);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; _kebabHover = false; _openHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        private RectangleF KebabRect
        {
            get { return new RectangleF(Width - Ui.S(42), Ui.S(12), Ui.S(28), Ui.S(28)); }
        }

        private RectangleF PickRect
        {
            get { return new RectangleF(Width - Ui.S(78), Ui.S(14), Ui.S(22), Ui.S(22)); }
        }

        private RectangleF OpenRect
        {
            get { return new RectangleF(Width - Ui.S(16) - Ui.S(88), Height - Ui.S(46), Ui.S(88), Ui.S(30)); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool k = KebabRect.Contains(e.Location);
            bool o = OpenRect.Contains(e.Location);
            if (k != _kebabHover || o != _openHover)
            {
                _kebabHover = k;
                _openHover = o;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Right) { ShowMenu(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (PickRect.Contains(e.Location))
            {
                var h = PickToggled;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
            if (KebabRect.Contains(e.Location)) { ShowMenu(); return; }
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
            {
                var h = PickToggled;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
            RaiseOpen();
        }

        private void RaiseOpen()
        {
            if (Launching) return;
            var h = Running ? FocusRequested : OpenRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || keyData == Keys.Delete ||
                   keyData == Keys.Apps || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { RaiseOpen(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { var h = DeleteRequested; if (h != null) h(this, EventArgs.Empty); e.Handled = true; }
            else if (e.KeyCode == Keys.Apps || e.KeyCode == Keys.F10) { ShowMenu(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { var h = EditRequested; if (h != null) h(this, EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void ShowMenu()
        {
            // One menu at a time: ContextMenuStrip owns a window handle, so building a fresh one
            // on every right-click leaks a handle per invocation.
            if (_menu != null) { _menu.Dispose(); _menu = null; }
            var menu = JBMenu.Create();
            _menu = menu;
            menu.Font = Theme.Body;
            JBMenu.Add(menu, Running ? "Focus window" : Launching ? "Starting…" : "Open profile", "play",
                delegate { RaiseOpen(); }, !Launching);
            if (Running)
                JBMenu.Add(menu, "Close browser", "power", delegate { var h = CloseRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Edit profile", "pencil", delegate { var h = EditRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, Model != null && Model.Proxy.Enabled ? "Change proxy" : "Set proxy", "route",
                delegate { var h = ProxyRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, Picked ? "Remove from selection" : "Add to selection", "check",
                delegate { var h = PickToggled; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Duplicate", "copy", delegate { var h = DuplicateRequested; if (h != null) h(this, EventArgs.Empty); });
            menu.Items.Add(new ToolStripSeparator());
            JBMenu.Add(menu, "Delete profile", "trash", delegate { var h = DeleteRequested; if (h != null) h(this, EventArgs.Empty); });
            int mw = menu.GetPreferredSize(Size.Empty).Width;
            JBMenu.ShowNear(menu, this, new Point(Math.Max(Ui.S(6), Width - mw - Ui.S(10)), Ui.S(44)));
        }

        public void ReStyle()
        {
            Font = Theme.Body;
            RefreshTip();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.SurfaceColor(this));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            if (Model == null) return;

            float rad = Theme.RLarge;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

            Ui.Fill(g, r, rad, _hover ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, r, rad, _hover ? Theme.HoverStrong : Theme.Line, 1f);

            if (Picked) Ui.Stroke(g, r, rad, Theme.Accent, 2f);
            if (Picked || _hover)
            {
                var box = new RectangleF(PickRect.X + 0.5f, PickRect.Y + 0.5f, PickRect.Width - 1f, PickRect.Height - 1f);
                Ui.Fill(g, box, Ui.S(6), Picked ? Theme.Accent : Theme.Bg);
                Ui.Stroke(g, box, Ui.S(6), Picked ? Theme.Accent : Theme.HoverStrong, 1f);
                if (Picked)
                {
                    float ci = Ui.S(12);
                    Icons.Draw(g, "check",
                        new RectangleF(box.X + (box.Width - ci) / 2f, box.Y + (box.Height - ci) / 2f, ci, ci),
                        Theme.OnAccent);
                }
            }

            int pad = Ui.S(16);
            float av = Ui.S(36);
            var ar = new RectangleF(pad, pad, av, av);
            Ui.Fill(g, ar, Ui.S(8), Theme.Accent);
            var initFont = Theme.H3;
            Ui.Text(g, Model.Initials, initFont, Theme.OnAccent, ar.X,
                ar.Y + (av - initFont.GetHeight(g)) / 2f - Ui.S(1), ar.Width, "center");

            float left = pad + av + Ui.S(12);
            float rightPad = Ui.S(52);
            float nameW = Math.Max(Ui.S(40), Width - left - rightPad);
            Ui.Text(g, Model.Name, Theme.H3, Theme.Text, left, Ui.S(13), nameW);

            string meta = Model.Fp.Os + "  ·  " + Model.Fp.Language + "  ·  " + Model.Fp.TimezoneShort;
            Ui.Text(g, meta, Theme.Caption, Theme.TextFaint, left, Ui.S(32), nameW);

            // kebab
            if (_kebabHover) Ui.Fill(g, KebabRect, Ui.S(4), Theme.HoverOverlay);
            float kd = Ui.S(16);
            Icons.Draw(g, "dots", new RectangleF(KebabRect.X + (KebabRect.Width - kd) / 2f,
                KebabRect.Y + (KebabRect.Height - kd) / 2f, kd, kd), _kebabHover ? Theme.Text : Theme.TextDim);

            // metadata chips: proxy + locale, clipped rather than allowed to run off the card
            float chipY = Ui.S(60);
            float room = Width - pad * 2;
            string proxyText = Model.Proxy.Enabled ? Model.Proxy.Label : "Direct";
            float w1 = ChipWidth(g, proxyText);
            DrawChip(g, pad, chipY, proxyText, Model.Proxy.Enabled ? "route" : "globe",
                Model.Proxy.Enabled ? Theme.Accent : Theme.TextFaint);
            string langText = Model.Fp.Language;
            float w2 = ChipWidth(g, langText);
            if (w1 + w2 + Ui.S(8) <= room)
                DrawChip(g, pad + w1 + Ui.S(8), chipY, langText, "language", Theme.TextFaint);

            float rule = Height - Ui.S(62);
            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, pad, rule, Width - pad, rule);

            // status
            float rowY = Height - Ui.S(46);
            float dot = Ui.S(7);
            string status = Running ? "Running" : Launching ? "Starting…" : "Stopped";
            Color statusColor = Running ? Theme.Success : Launching ? Theme.Accent : Theme.TextFaint;
            float dotX = pad + Ui.S(3);
            float dotY = rowY + Ui.S(30) / 2f;
            if (Running || Launching)
            {
                float phase = Running ? (Environment.TickCount % 1600) / 1600f : 0f;
                if (Running)
                {
                    float halo = dot + Ui.S(7) * (1f - phase);
                    using (var hb = new SolidBrush(Color.FromArgb((int)(60 * (1f - phase)), statusColor.R, statusColor.G, statusColor.B)))
                        g.FillEllipse(hb, dotX - halo / 2f, dotY - halo / 2f, halo, halo);
                }
            }
            using (var b = new SolidBrush(statusColor))
                g.FillEllipse(b, dotX - dot / 2f, dotY - dot / 2f, dot, dot);

            Ui.Text(g, status, Theme.Caption, statusColor, pad + Ui.S(12),
                rowY + (Ui.S(30) - Theme.Caption.GetHeight(g)) / 2f, OpenRect.X - pad - Ui.S(24));

            // open / focus button — windows-ui subtle button
            var or = OpenRect;
            if (_openHover && !Launching)
                Ui.Fill(g, or, Ui.S(4), Theme.HoverOverlay);
            Ui.Stroke(g, or, Ui.S(4), Theme.ButtonBorder, 1f);

            string openLabel = Launching ? "Starting…" : Running ? "Focus" : "Open";
            Color openFg = Launching ? Theme.TextFaint : Theme.Text;
            float ic = Ui.S(13);
            float ow = Ui.Measure(g, openLabel, Theme.SmallBold).Width;
            float content = (Launching ? 0 : ic + Ui.S(6)) + ow;
            float ox = or.X + (or.Width - content) / 2f;
            if (!Launching)
            {
                Icons.Draw(g, Running ? "window" : "play",
                    new RectangleF(ox, or.Y + (or.Height - ic) / 2f, ic, ic), openFg);
                ox += ic + Ui.S(6);
            }
            Ui.Text(g, openLabel, Theme.SmallBold, openFg, ox, or.Y + (or.Height - Theme.SmallBold.GetHeight(g)) / 2f);

            if (Focused && ShowFocusCues) Ui.FocusRing(g, r, rad);
        }

        private float ChipWidth(Graphics g, string text)
        {
            return Ui.Measure(g, text, Theme.Caption).Width + Ui.S(32);
        }

        private void DrawChip(Graphics g, float x, float y, string text, string icon, Color iconColor)
        {
            float w = ChipWidth(g, text);
            float h = Ui.S(22);
            var rect = new RectangleF(x, y, w, h);
            Ui.Fill(g, rect, Ui.S(4), Theme.Panel2);
            float ic = Ui.S(12);
            Icons.Draw(g, icon, new RectangleF(x + Ui.S(8), y + (h - ic) / 2f, ic, ic), iconColor);
            Ui.Text(g, text, Theme.Caption, Theme.TextDim, x + Ui.S(24), y + (h - Theme.Caption.GetHeight(g)) / 2f, w - Ui.S(28));
        }
    }
}
