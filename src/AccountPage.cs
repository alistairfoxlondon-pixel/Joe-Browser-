using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Account page: who is signed in, what the plan covers, and whether the account is
    /// protected - the three things a person opens an account page to see, in that order.
    ///
    /// The previous page was five buttons and a plan sentence. Every button looked the same,
    /// nothing on the page said which of them mattered, and the one fact that decides whether an
    /// account can be recovered safely - is there a second factor and is there a PIN - was
    /// encoded in the button label instead of shown. That is what "the design is bad" meant.
    ///
    /// The page is now an identity header, a plan card with the one number that matters, a
    /// security card whose two rows each state their own status, and a short account list. It is
    /// built from measured cards rather than a clipped column of labels, and it says nothing
    /// twice: every line either identifies the account, states the plan, states the protection or
    /// offers the action that changes it.
    /// </summary>
    internal sealed class AccountPage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;

        private readonly IdentityCard _identity;
        private readonly InfoCard _plan;
        private readonly InfoCard _security;
        private readonly JBLabel _accountsHeading;
        private readonly JBPanel _accountsBox;
        private readonly List<AccountRow> _rows = new List<AccountRow>();
        private readonly JBLabel _emptyNote;

        private List<Account> _accounts = new List<Account>();
        private bool _layingOut, _ready;

        public event EventHandler SignOutRequested;
        public event EventHandler SwitchRequested;
        public event EventHandler PinRequested;
        public event EventHandler TwoFactorRequested;
        public event EventHandler ManagePlanRequested;
        public event EventHandler<string> DeleteAccountRequested;

        internal static JBButton MakeButton(string text, string icon, BtnKind kind)
        {
            return new JBButton { Kind = kind, Icon = icon, Text = text, Height = Theme.Field };
        }

        public AccountPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            _identity = new IdentityCard();
            _identity.SignOut.Click += delegate { var h = SignOutRequested; if (h != null) h(this, EventArgs.Empty); };
            _identity.Switch.Click += delegate { var h = SwitchRequested; if (h != null) h(this, EventArgs.Empty); };
            _column.Controls.Add(_identity);

            _plan = new InfoCard("PLAN", "crown");
            _column.Controls.Add(_plan);
            _plan.Primary.Click += delegate { var h = ManagePlanRequested; if (h != null) h(this, EventArgs.Empty); };

            _security = new InfoCard("SECURITY", "shield");
            _column.Controls.Add(_security);
            _security.Primary.Click += delegate { var h = PinRequested; if (h != null) h(this, EventArgs.Empty); };
            _security.Secondary.Click += delegate { var h = TwoFactorRequested; if (h != null) h(this, EventArgs.Empty); };

            _accountsHeading = new JBLabel(LabelRole.Strong, L10n.T("acct.others"));
            _column.Controls.Add(_accountsHeading);

            _accountsBox = new JBPanel { Hoverable = false };
            _column.Controls.Add(_accountsBox);

            _emptyNote = new JBLabel(LabelRole.Faint, L10n.T("acct.only"));
            _emptyNote.Wrap = true;
            _emptyNote.TextAlign = ContentAlignment.TopLeft;
            _column.Controls.Add(_emptyNote);

            _ready = true;
            LayoutCore();
        }

        /// <summary>Re-reads the account store and the plan, then rebuilds every card.</summary>
        public void Reload()
        {
            if (!_ready) return;
            _accounts = Accounts.LoadAll();
            var me = Session.Current;

            _identity.Bind(me);

            bool pro = Plan.IsPro;
            _plan.Title = L10n.T("acct.planhead");
            _plan.Primary.Text = pro ? L10n.T("acct.manageplan") : L10n.T("acct.upgrade");
            _plan.Primary.Icon = pro ? "settings" : "crown";
            _plan.Primary.Kind = pro ? BtnKind.Soft : BtnKind.Primary;
            _plan.SetRows(new[]
            {
                new InfoCard.Row(L10n.T("acct.plannow"), Plan.Summary(), true),
                new InfoCard.Row(L10n.T("acct.planprofiles"), Plan.IsPro
                    ? L10n.T("acct.planunlimited")
                    : UsedProfiles() + " / " + Entitlements.FreeProfiles, false),
                new InfoCard.Row(L10n.T("acct.planrenew"), PlanNote(), false)
            });

            bool pin = me != null && !string.IsNullOrEmpty(me.PinHash);
            bool two = me != null && !string.IsNullOrEmpty(me.TotpSecret);
            _security.Primary.Text = pin ? L10n.T("acct.pin.on") : L10n.T("acct.pin");
            _security.Primary.Icon = "lock";
            _security.Secondary.Text = two ? L10n.T("acct.2fa.on") : L10n.T("acct.2fa");
            _security.Secondary.Icon = "shield";
            _security.SetRows(new[]
            {
                new InfoCard.Row(L10n.T("acct.pinhead"),
                    pin ? L10n.T("acct.pin.on.state") : L10n.T("acct.pin.off.state"), false, pin),
                new InfoCard.Row(L10n.T("acct.2fahead"),
                    two ? L10n.T("acct.2fa.on.state") : L10n.T("acct.2fa.off.state"), false, two)
            });

            _identity.Switch.Visible = me != null;
            _identity.SignOut.Visible = me != null;

            foreach (var r in _rows) { _accountsBox.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();
            foreach (var a in _accounts)
            {
                if (me != null && string.Equals(a.Email, me.Email, StringComparison.OrdinalIgnoreCase)) continue;
                var row = new AccountRow(this, a);
                _rows.Add(row);
                _accountsBox.Controls.Add(row);
            }
            bool any = _rows.Count > 0;
            _accountsHeading.Visible = any;
            _accountsBox.Visible = any;
            _emptyNote.Visible = !any;
            LayoutCore();
        }

        /// <summary>
        /// The one number a plan page has to answer: how many profiles this account holds right
        /// now. Read from the store rather than counted from the cards on the other page, so it
        /// is right even when no profile card has been built yet.
        /// </summary>
        private static int UsedProfiles()
        {
            try { return ProfileStore.CountMine(); } catch { return 0; }
        }

        private static string PlanNote()
        {
            var until = Plan.Until;
            if (Plan.IsLifetime) return L10n.T("acct.plan.unlimited");
            if (Plan.IsPro && until != null) return L10n.T("acct.plan.renews") + " " + until.Value.ToLocalTime().ToString("d MMMM yyyy");
            return L10n.T("acct.free.note");
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            _identity.Invalidate();
            _plan.Invalidate();
            _security.Invalidate();
            foreach (var r in _rows) r.Invalidate();
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutCore(); }

        public void LayoutAtWidth() { LayoutCore(); }

        private void LayoutCore()
        {
            if (_layingOut || !_ready || _host == null) return;
            _layingOut = true;
            try
            {
                _host.SetBounds(0, 0, Math.Max(1, Width), Math.Max(1, Height));
                int w = Math.Max(Ui.S(320), _host.Width - Ui.S(10));
                int pad = Ui.S(30);
                int inner = Math.Max(Ui.S(240), w - pad * 2);
                int gap = Ui.S(14);
                int y = pad;

                _identity.SetBounds(pad, y, inner, _identity.NeededHeight(inner));
                y += _identity.Height + gap;

                /* Two cards side by side when there is room for two readable columns, stacked
                   otherwise. The break point is the width at which a "label   value" row stops
                   being one line, not a device class. */
                bool two = inner >= Ui.S(760);
                if (two)
                {
                    int colW = (inner - gap) / 2;
                    int h = Math.Max(_plan.NeededHeight(colW), _security.NeededHeight(colW));
                    _plan.SetBounds(pad, y, colW, h);
                    _security.SetBounds(pad + colW + gap, y, colW, h);
                    y += h + gap;
                }
                else
                {
                    int h = _plan.NeededHeight(inner);
                    _plan.SetBounds(pad, y, inner, h);
                    y += h + gap;
                    h = _security.NeededHeight(inner);
                    _security.SetBounds(pad, y, inner, h);
                    y += h + gap;
                }

                if (_accountsHeading.Visible)
                {
                    int headH = Theme.LineHeight(Theme.Small);
                    _accountsHeading.SetBounds(pad, y, inner, headH);
                    y += headH + Ui.S(8);
                }
                if (_accountsBox.Visible)
                {
                    int yy = 0;
                    foreach (var r in _rows)
                    {
                        r.SetBounds(0, yy, inner, r.RowHeight);
                        yy += r.RowHeight + Ui.S(8);
                    }
                    _accountsBox.SetBounds(pad, y, inner, Math.Max(0, yy - Ui.S(8)));
                    y += _accountsBox.Height + gap;
                }
                if (_emptyNote.Visible)
                {
                    int h = Measure(_emptyNote.Text, Theme.Caption, inner);
                    _emptyNote.SetBounds(pad, y, inner, h);
                    y += h + gap;
                }

                y += Ui.S(6);
                _column.SetBounds(0, 0, w, Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }

        /// <summary>Height wrapped text needs at this width, measured, never guessed.</summary>
        internal static int Measure(string text, Font font, int width)
        {
            int line = Math.Max(Ui.S(16), (int)Math.Ceiling(font.GetHeight()));
            if (string.IsNullOrEmpty(text)) return line;
            try
            {
                Size size = TextRenderer.MeasureText(text, font, new Size(Math.Max(Ui.S(60), width), int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return Math.Max(line, size.Height + Ui.S(2));
            }
            catch { return line; }
        }

        /// <summary>
        /// Who is signed in, at the top, with the only two actions that apply to a whole identity.
        /// A monogram rather than a photo: the app may be offline, and a broken image on an
        /// account page is worse than none.
        /// </summary>
        private sealed class IdentityCard : Control
        {
            public readonly JBButton SignOut = MakeButton(L10n.T("acct.signout"), "close", BtnKind.Ghost);
            public readonly JBButton Switch = MakeButton(L10n.T("acct.switch"), "user", BtnKind.Soft);

            private string _title = "", _line = "";
            private bool _signedIn;

            public IdentityCard()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                Controls.Add(SignOut);
                Controls.Add(Switch);
            }

            public void Bind(Account me)
            {
                _signedIn = me != null;
                _title = me == null ? L10n.T("acct.signedout") : (string.IsNullOrEmpty(me.DisplayName) ? me.Email : me.DisplayName);
                if (me != null && !string.IsNullOrEmpty(me.DisplayName))
                    _line = me.Email + (me.Created == null ? "" : "  ·  " + L10n.T("acct.since") + " " + me.Created);
                else if (me != null) _line = "";
                else _line = L10n.T("acct.signedout.note");
                ToolTips.Attach(this, me == null ? _line : me.Email);
                Invalidate();
            }

            public int NeededHeight(int width)
            {
                int top = Ui.S(20);
                int title = Theme.LineHeight(Theme.H1);
                int line = Theme.LineHeight(Theme.Small);
                int buttons = Theme.Field + Ui.S(20);
                bool stack = width < Ui.S(560);
                int text = top + title + Ui.S(6) + line + Ui.S(18);
                return stack ? text + buttons + Ui.S(14) : Math.Max(text, top + Theme.Field + Ui.S(20));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                g.Clear(Theme.Bg);

                var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                Ui.Fill(g, box, Ui.S(Theme.CardRadius), Theme.Card);
                Ui.Stroke(g, box, Ui.S(Theme.CardRadius), Theme.Line);

                /* The monogram: one letter of the identity, in the accent, on its own tinted
                   square. It is the only decorative element on the page, which is what makes the
                   rest of it read as information. */
                float m = Ui.S(56);
                float mx = Ui.S(20), my = Ui.S(20);
                var tile = new RectangleF(mx, my, m, m);
                Ui.Fill(g, tile, Ui.S(Theme.CardRadius), Theme.AccentOnBg);
                Ui.Stroke(g, tile, Ui.S(Theme.CardRadius), Theme.AccentBorder);
                string initial = Initial(_title);
                var f = Theme.F(26, FontStyle.Bold);
                float tw = Ui.Measure(g, initial, f).Width;
                Ui.Text(g, initial, f, Theme.AccentText, mx + (m - tw) / 2f, my + (m - Theme.LineHeight(f)) / 2f, tw + Ui.S(2));

                float left = mx + m + Ui.S(18);
                int btnW = Math.Min(Switch.PreferredWidth(Ui.S(150)) + SignOut.PreferredWidth(Ui.S(110)) + Ui.S(8),
                    Math.Max(Ui.S(120), Width - left - Ui.S(20)));
                bool stack = Width < Ui.S(560);
                float textW = stack ? Width - left - Ui.S(20) : Math.Max(Ui.S(80), Width - left - btnW - Ui.S(28));

                Ui.Text(g, _title, Theme.H1, Theme.Text, left, my, textW, "left");
                if (!string.IsNullOrEmpty(_line))
                    Ui.Text(g, _line, Theme.Small, Theme.TextFaint, left, my + Theme.LineHeight(Theme.H1) + Ui.S(6), textW, "left");

                if (stack) SetBoundsCoreForButtons(left, Height - Theme.Field - Ui.S(14), Math.Max(Ui.S(120), Width - left - Ui.S(20)), btnW);
                else SetBoundsCoreForButtons(Width - btnW - Ui.S(18), my + (m - Theme.Field) / 2f, btnW, btnW);
            }

            private void SetBoundsCoreForButtons(float x, float y, float available, float wanted)
            {
                int sw = Switch.PreferredWidth(Ui.S(130));
                int gw = SignOut.PreferredWidth(Ui.S(100));
                if (sw + gw + Ui.S(8) > available) { sw = (int)available; gw = 0; }
                if (gw > 0) SignOut.SetBounds((int)x, (int)y, gw, Theme.Field);
                SignOut.Visible = gw > 0 && _signedIn;
                Switch.SetBounds((int)(gw > 0 ? x + gw + Ui.S(8) : x), (int)y,
                    Math.Min(sw, (int)Math.Max(Ui.S(80), available - gw)), Theme.Field);
            }
        }

        internal static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
            return "?";
        }

        /// <summary>
        /// A section card: an eyebrow, a title, a short list of label/value rows with an at-a-glance
        /// state, and the actions that change what the rows say. Painted rather than composed from
        /// a dozen labels, so the rows can be measured as a block and never overlap.
        /// </summary>
        private sealed class InfoCard : Control
        {
            internal struct Row
            {
                public readonly string Label;
                public readonly string Value;
                /// <summary>Emphasised: the row a person came to this card to read.</summary>
                public readonly bool Strong;
                /// <summary>Tri-state: false neutral, true a good state, null not a state row.</summary>
                public readonly bool? Good;

                public Row(string label, string value, bool strong) { Label = label; Value = value; Strong = strong; Good = null; }
                public Row(string label, string value, bool strong, bool good) { Label = label; Value = value; Strong = strong; Good = good; }
            }

            public readonly JBButton Primary, Secondary;
            private readonly string _eyebrow, _glyph;
            private Row[] _rows = new Row[0];

            public InfoCard(string eyebrow, string glyph)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                _eyebrow = eyebrow;
                _glyph = glyph;
                Primary = MakeButton("", "crown", BtnKind.Primary);
                Secondary = MakeButton("", "shield", BtnKind.Soft);
                Controls.Add(Primary);
                Controls.Add(Secondary);
            }

            public string Title { get; set; }

            public void SetRows(Row[] rows) { _rows = rows ?? new Row[0]; Invalidate(); }

            public int NeededHeight(int width)
            {
                int y = Ui.S(16) + Theme.LineHeight(Theme.Small) + Ui.S(8);   // eyebrow
                y += Theme.LineHeight(Theme.H2) + Ui.S(12);                  // title
                foreach (var r in _rows) y += RowHeight(r, width) + Ui.S(8);
                /* The action row is always one field high; whether one or two buttons are shown
                   does not change the card's height, which is what keeps two cards side by side
                   on the same baseline. */
                y += Theme.Field + Ui.S(16) + Ui.S(2);
                return y;
            }

            private static int RowHeight(Row r, int width)
            {
                int label = Theme.LineHeight(Theme.Caption);
                int value = Theme.LineHeight(r.Strong ? Theme.H3 : Theme.Body);
                /* One line when the label and the value fit on one line, two when they do not.
                   The threshold is measured from the text, so a translation that makes the label
                   longer makes the row taller instead of covering the value. */
                int labelW = (int)(width * 0.42f);
                int valueW = Math.Max(Ui.S(60), width - labelW - Ui.S(32));
                int valueH = Measure(r.Value, r.Strong ? Theme.H3 : Theme.Body, valueW);
                return Math.Max(label, valueH) + Ui.S(6);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                g.Clear(Theme.Bg);

                var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                Ui.Fill(g, box, Ui.S(Theme.CardRadius), Theme.Card);
                Ui.Stroke(g, box, Ui.S(Theme.CardRadius), Theme.Line);

                float pad = Ui.S(16);
                float w = Width - pad * 2;
                float y = pad;

                /* Eyebrow with its glyph, so the card is identifiable without reading it. */
                float gi = Ui.S(16);
                if (!string.IsNullOrEmpty(_glyph)) Icons.Draw(g, _glyph, new RectangleF(pad, y + (Theme.LineHeight(Theme.Small) - gi) / 2f, gi, gi), Theme.TextFaint);
                Ui.Text(g, _eyebrow, Theme.SmallBold, Theme.TextFaint, pad + gi + Ui.S(8), y, w - gi);
                y += Theme.LineHeight(Theme.Small) + Ui.S(8);

                if (!string.IsNullOrEmpty(Title))
                {
                    Ui.Text(g, Title, Theme.H2, Theme.Text, pad, y, w);
                    y += Theme.LineHeight(Theme.H2) + Ui.S(14);
                }

                foreach (var r in _rows)
                {
                    int h = RowHeight(r, (int)Width);
                    int labelW = (int)(w * 0.42f);
                    Font vf = r.Strong ? Theme.H3 : Theme.Body;
                    Color vc = r.Good == true ? Theme.Success : r.Good == false ? Theme.TextFaint : Theme.Text;
                    Ui.Text(g, r.Label, Theme.Caption, Theme.TextFaint, pad, y + Ui.S(3), labelW);
                    Ui.Text(g, r.Value, vf, vc, pad + labelW + Ui.S(12), y, Math.Max(Ui.S(50), w - labelW - Ui.S(12)), "left", 3);
                    y += h + Ui.S(8);
                }

                y += Ui.S(2);

                /* The actions sit at the bottom of the card, so two cards side by side line up
                   whatever their rows are. */
                int avail = (int)w;
                int by = Height - Theme.Field - Ui.S(16);
                bool hasSecond = !string.IsNullOrEmpty(Secondary.Text);
                int primW = Primary.PreferredWidth(Ui.S(140));
                int secW = hasSecond ? Secondary.PreferredWidth(Ui.S(140)) : 0;
                if (primW + secW + Ui.S(8) > avail)
                {
                    /* Too narrow for both: the primary keeps the width it needs, the secondary
                       takes what is left on its own line above rather than being cut. */
                    Primary.SetBounds((int)pad, by, Math.Min(primW, avail), Theme.Field);
                    Secondary.SetBounds((int)pad, by - Theme.Field - Ui.S(8), Math.Min(secW, avail), Theme.Field);
                }
                else
                {
                    Secondary.SetBounds((int)pad, by, secW, Theme.Field);
                    Primary.SetBounds((int)(pad + secW + Ui.S(8)), by, primW, Theme.Field);
                }
                Primary.Visible = Primary.Text.Length > 0;
                Secondary.Visible = hasSecond;
            }
        }

        /// <summary>One other account on this PC, as a row with its own remove action.</summary>
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
                _remove = new JBButton { Kind = BtnKind.Ghost, Text = L10n.T("acct.remove"), Height = Ui.S(32) };
                _remove.Click += delegate
                {
                    var h = _page.DeleteAccountRequested;
                    if (h != null) h(_page, _account.Email);
                };
                Controls.Add(_remove);
                ToolTips.Attach(this, string.IsNullOrEmpty(account.Email) ? "" : account.Email);
            }

            public int RowHeight { get { return Math.Max(Ui.S(64), Theme.LineHeight(Theme.H3) + Theme.LineHeight(Theme.Caption) + Ui.S(22)); } }

            protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutRemove(); }
            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); LayoutRemove(); }

            private void LayoutRemove()
            {
                int bw = _remove.PreferredWidth(Ui.S(96));
                if (bw > Math.Max(Ui.S(40), Width - Ui.S(24))) bw = Math.Max(Ui.S(40), Width - Ui.S(24));
                _remove.SetBounds(Math.Max(Ui.S(6), Width - bw - Ui.S(12)),
                    (RowHeight - Ui.S(32)) / 2, bw, Ui.S(32));
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
                Ui.Fill(g, box, Ui.S(Theme.CardRadius), _hover ? Theme.PanelHover : Theme.Card);
                Ui.Stroke(g, box, Ui.S(Theme.CardRadius), _hover ? Theme.HoverStrong : Theme.Line);

                float left = Ui.S(14);
                int removeW = _remove.Width + Ui.S(12);
                float w = Math.Max(Ui.S(20), Width - left - removeW - Ui.S(4));
                string name = string.IsNullOrEmpty(_account.DisplayName) ? _account.Email : _account.DisplayName;
                Ui.Text(g, name, Theme.H3, Theme.Text, left, Ui.S(10), w);
                Ui.Text(g, _account.Email, Theme.Caption, Theme.TextFaint, left, Ui.S(10) + Theme.LineHeight(Theme.H3) + Ui.S(2), w);
            }
        }
    }
}
