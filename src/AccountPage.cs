using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Account page: who is signed in, what plan they are on, the accounts that exist on this
    /// PC, and the settings that belong to an account rather than to a profile.
    ///
    /// Before this page the only account surface was one row in Settings that showed the email
    /// and nothing else, so there was no way to see which accounts existed, when each last
    /// signed in, whether a PIN or 2FA was set, or to switch, sign out or delete one.
    /// </summary>
    internal sealed class AccountPage : Panel, IReStyle
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly List<AccountRow> _rows = new List<AccountRow>();
        private JBLabel _title, _plan, _planNote, _accountsHeading;
        private JBPanel _planCard, _listCard;
        private JBButton _signOut, _switch, _pin, _twoFactor, _refresh;
        private bool _layingOut, _ready;
        private List<Account> _accounts = new List<Account>();

        public event EventHandler SignOutRequested;
        public event EventHandler SwitchRequested;
        public event EventHandler PinRequested;
        public event EventHandler TwoFactorRequested;
        public event EventHandler ManagePlanRequested;
        public event EventHandler<string> DeleteAccountRequested;

        public AccountPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            _title = new JBLabel(LabelRole.Title, "");
            _column.Controls.Add(_title);
            _plan = new JBLabel(LabelRole.Body, "");
            _column.Controls.Add(_plan);
            _planNote = new JBLabel(LabelRole.Faint, "");
            _planNote.Wrap = true;
            _column.Controls.Add(_planNote);

            /* Reserved for the plan panel. It used to be added with no bounds at all, so it sat
               at 0,0 sized 0x0 - invisible, but still a control in the column that the layout
               below had to work around. Collapsed by default and only given space when it has
               something to show. */
            _planCard = new JBPanel { Visible = false, Hoverable = false };
            _column.Controls.Add(_planCard);

            _manage = MakeButton(L10n.T("acct.manageplan"), "crown", BtnKind.Primary);
            _manage.Click += delegate { var h = ManagePlanRequested; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_manage);

            _signOut = MakeButton(L10n.T("acct.signout"), "close", BtnKind.Soft);
            _signOut.Click += delegate { var h = SignOutRequested; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_signOut);

            _switch = MakeButton(L10n.T("acct.switch"), "user", BtnKind.Soft);
            _switch.Click += delegate { var h = SwitchRequested; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_switch);

            _pin = MakeButton(L10n.T("acct.pin"), "lock", BtnKind.Soft);
            _pin.Click += delegate { var h = PinRequested; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_pin);

            _twoFactor = MakeButton(L10n.T("acct.2fa"), "shield", BtnKind.Soft);
            _twoFactor.Click += delegate { var h = TwoFactorRequested; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_twoFactor);

            _accountsHeading = new JBLabel(LabelRole.Strong, L10n.T("acct.others"));
            _column.Controls.Add(_accountsHeading);

            _refresh = MakeButton(L10n.T("acct.refresh"), "refresh", BtnKind.Soft);
            _refresh.Click += delegate { Reload(); };
            _column.Controls.Add(_refresh);

            _ready = true;
            LayoutCore();
        }

        private JBButton _manage;

        private static JBButton MakeButton(string text, string icon, BtnKind kind)
        {
            return new JBButton { Kind = kind, Icon = icon, Text = text, Radius = 4, Height = Ui.S(33) };
        }

        /// <summary>Re-reads the account store and the plan, then rebuilds.</summary>
        public void Reload()
        {
            if (!_ready) return;
            _accounts = Accounts.LoadAll();
            var me = Session.Current;
            _title.Text = me == null ? L10n.T("acct.signedout") : me.Email;
            if (me != null && !string.IsNullOrEmpty(me.DisplayName))
                _title.Text = me.DisplayName + "   " + me.Email;
            /* A long address in a narrow window is trimmed with an ellipsis, and the last thing a
               user needs clipped on an account page is which account it is. The tooltip and a
               selectable title both give the whole value back. */
            ToolTips.Attach(_title, _title.Text);
            _title.Selectable = me != null;

            _plan.Text = Plan.Summary();
            var until = Plan.Until;
            if (Plan.IsLifetime)
                _planNote.Text = L10n.T("acct.plan.unlimited");
            else if (Plan.IsPro && until != null)
                _planNote.Text = L10n.T("acct.plan.renews") + " " + until.Value.ToLocalTime().ToString("d MMMM yyyy");
            else
                _planNote.Text = L10n.T("acct.plan.free") + " " + Entitlements.FreeProfiles + " " +
                                 L10n.T("acct.plan.profiles");

            _manage.Visible = !Plan.IsPro;
            _pin.Text = (me != null && !string.IsNullOrEmpty(me.PinHash)) ? L10n.T("acct.pin.on") : L10n.T("acct.pin");
            _twoFactor.Text = (me != null && !string.IsNullOrEmpty(me.TotpSecret)) ? L10n.T("acct.2fa.on") : L10n.T("acct.2fa");

            foreach (var r in _rows) { _column.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();
            foreach (var a in _accounts)
            {
                if (me != null && string.Equals(a.Email, me.Email, StringComparison.OrdinalIgnoreCase)) continue;
                var row = new AccountRow(this, a);
                _rows.Add(row);
                _column.Controls.Add(row);
            }
            _accountsHeading.Visible = _rows.Count > 0;
            LayoutCore();
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

                _title.SetBounds(pad, y, inner, Ui.S(30));
                y += Ui.S(34);
                _plan.SetBounds(pad, y, inner, Ui.S(22));
                y += Ui.S(26);
                /* The plan note wraps, and on a narrow window two lines of it sat on top of the
                   first button row. Its height is measured from the text that is in it. */
                int noteH = MeasureLines(_planNote.Text, Theme.Caption, inner);
                _planNote.SetBounds(pad, y, inner, noteH);
                y += noteH + Ui.S(10);

                int bh = Ui.S(33);
                JBButton[] actions = { _manage, _signOut, _switch, _pin, _twoFactor };
                /* FlowButtons wraps and never overlaps; the old loop compared bx + bw against
                   the wrap edge without adding the gap it was about to advance by, so the last
                   button on a line could cross the edge and start the next line under it. */
                y = Ui.FlowButtons(actions, pad, y, inner, Ui.S(8), bh) + Ui.S(24);

                int rw = _refresh.PreferredWidth(Ui.S(100));
                /* The heading keeps its own line when the refresh button would leave nothing
                   readable of it; otherwise the two share a row. */
                int headW = inner - rw - Ui.S(8);
                if (headW < Ui.S(120))
                {
                    _accountsHeading.SetBounds(pad, y, inner, Ui.S(22));
                    y += Ui.S(26);
                    _refresh.SetBounds(pad + inner - rw, y, rw, bh);
                    y += bh + Ui.S(4);
                }
                else
                {
                    _accountsHeading.SetBounds(pad, y, headW, Ui.S(22));
                    _refresh.SetBounds(pad + inner - rw, y - Ui.S(4), rw, bh);
                    y += Ui.S(30);
                }
                y += Ui.S(30);

                foreach (var r in _rows)
                {
                    r.SetBounds(pad, y, inner, r.RowHeight);
                    y += r.RowHeight + Ui.S(6);
                }
                y += pad;

                _column.SetBounds(0, 0, w, Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            foreach (var r in _rows) r.Invalidate();
            Invalidate();
        }

        /// <summary>One other account on this PC, as a card with its own remove action.</summary>
        private sealed class AccountRow : Control
        {
            private readonly AccountPage _page;
            private readonly Account _account;
            private readonly JBButton _remove;
            private bool _hover;

            public AccountRow(AccountPage page, Account account)
            {
                _page = page;
                _account = account;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                _remove = new JBButton { Kind = BtnKind.Ghost, Text = L10n.T("acct.remove"), Height = Ui.S(28) };
                _remove.Click += delegate
                {
                    var h = _page.DeleteAccountRequested;
                    if (h != null) h(_page, _account.Email);
                };
                Controls.Add(_remove);
                ToolTips.Attach(this, (string.IsNullOrEmpty(account.Email) ? "" : account.Email));
            }

            public int RowHeight { get { return Ui.S(62); } }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                LayoutRemove();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                LayoutRemove();
            }

            /* Keeps the button inside the row whatever the row's width and scale are, and
               repaints the text that shares the row with it. */
            private void LayoutRemove()
            {
                int bw = _remove.PreferredWidth(Ui.S(96));
                if (bw > Math.Max(Ui.S(40), Width - Ui.S(28))) bw = Math.Max(Ui.S(40), Width - Ui.S(28));
                _remove.SetBounds(Math.Max(Ui.S(6), Width - bw - Ui.S(12)),
                    (RowHeight - Ui.S(28)) / 2, bw, Ui.S(28));
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                g.Clear(Theme.Bg);
                var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                Ui.Fill(g, box, Ui.S(8), _hover ? Theme.PanelHover : Theme.Card);
                Ui.Stroke(g, box, Ui.S(8), _hover ? Theme.HoverStrong : Theme.Line, 1f);

                float left = Ui.S(14);
                /* The remove button is measured, not assumed: a fixed 120 px reserve is narrower
                   than the button on a large font, and the address then ran underneath it. */
                int removeW = _remove.PreferredWidth(Ui.S(96)) + Ui.S(12);
                float w = Math.Max(Ui.S(60), Width - left - removeW);
                Ui.Text(g, string.IsNullOrEmpty(_account.DisplayName) ? _account.Email : _account.DisplayName,
                    Theme.Body, Theme.Text, left, Ui.S(9), w);
                Ui.Text(g, _account.Email, Theme.Caption, Theme.TextFaint, left, Ui.S(30), w);
            }
        }
    }
}
