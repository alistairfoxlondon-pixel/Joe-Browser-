using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Account page: who is signed in, what the plan covers, how the workspace is using it, and
    /// whether the account is protected - the four things a person opens an account page to see.
    ///
    /// The previous page answered three of the four, repeated its security answers twice, showed
    /// a plan card to a signed-out visitor, printed a raw ISO-8601 timestamp as "member since",
    /// and stretched a narrow column across a wide window. This version: an identity header with
    /// a readable date, a plan card, a security card that says each protection once, a workspace
    /// card with the counts the rest of the app is already tracking, and the account list - all
    /// inside a centred column that stops growing at the width good text columns stop at.
    /// </summary>
    internal sealed class AccountPage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;

        private readonly IdentityCard _identity;
        private readonly InfoCard _plan;
        private readonly InfoCard _security;
        private readonly InfoCard _workspace;
        private readonly UsageCard _usage;
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

        /// <summary>
        /// The numbers the workspace card shows. The window owns the live lists, so the page asks
        /// instead of keeping its own copies; when no provider is bound the card hides rather
        /// than showing zeros that would read as broken.
        /// </summary>
        public static Func<WorkspaceStats> StatsProvider;

        internal sealed class WorkspaceStats
        {
            public int Profiles, Running, Proxies, Extensions, HistoryPages;
        }

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

            _workspace = new InfoCard("WORKSPACE", "layers");
            _workspace.Secondary.Visible = false;
            _column.Controls.Add(_workspace);

            _usage = new UsageCard();
            _column.Controls.Add(_usage);

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

            /* A signed-out visitor (a session marker that expired, say) gets an identity card
               and the workspace numbers - not a PLAN card offering an upgrade to somebody who
               has not signed in, and not a SECURITY card about an account that is not open. */
            bool signedIn = me != null;
            _plan.Visible = signedIn;
            _security.Visible = signedIn;

            if (signedIn)
            {
                bool pro = Plan.IsPro;
                _plan.Title = L10n.T("acct.planhead");
                _plan.Primary.Text = pro ? L10n.T("acct.manageplan") : L10n.T("acct.upgrade");
                _plan.Primary.Icon = pro ? "settings" : "crown";
                _plan.Primary.Kind = pro ? BtnKind.Soft : BtnKind.Primary;
                /* The filler "Renews: upgrade for unlimited profiles" row is gone - it restated
                   the button underneath it. Free accounts see what the plan covers, Pro accounts
                   see when it renews. */
                var planRows = new List<InfoCard.Row>
                {
                    new InfoCard.Row(L10n.T("acct.plannow"), Plan.Summary(), true),
                    new InfoCard.Row(L10n.T("acct.planprofiles"), Plan.IsPro
                        ? L10n.T("acct.planunlimited")
                        : UsedProfiles() + " / " + Entitlements.FreeProfiles, false)
                };
                if (Plan.IsLifetime || (Plan.IsPro && Plan.Until != null))
                {
                    string note = PlanNote();
                    if (!string.IsNullOrWhiteSpace(note)) planRows.Add(new InfoCard.Row(L10n.T("acct.planrenew"), note, false));
                }
                _plan.SetRows(planRows.ToArray());

                bool pin = !string.IsNullOrEmpty(me.PinHash);
                bool two = !string.IsNullOrEmpty(me.TotpSecret);
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
            }

            /* Workspace: the four counts the rest of the app is already holding. When no window
               has bound a provider there is nothing honest to show, so the card hides. */
            var stats = StatsProvider != null ? StatsProvider() : null;
            _workspace.Visible = stats != null;
            if (stats != null)
            {
                _workspace.Title = "Workspace";
                _workspace.Primary.Visible = false;
                _workspace.SetRows(new[]
                {
                    new InfoCard.Row("Profiles", stats.Profiles + (stats.Running > 0
                        ? "  ·  " + stats.Running + " running now" : ""), true),
                    new InfoCard.Row("Proxies saved", stats.Proxies.ToString(), false),
                    new InfoCard.Row("Extensions", stats.Extensions.ToString(), false),
                    new InfoCard.Row("Pages in history", stats.HistoryPages.ToString("N0"), false)
                });
            }

            _usage.Visible = signedIn;
            if (signedIn) _usage.Reload();

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
            return "";
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            _identity.Invalidate();
            _plan.Invalidate();
            _security.Invalidate();
            _workspace.Invalidate();
            _usage.Invalidate();
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
                /* A text column does not get better past a certain width: every extra hundred
                   px is dead right margin or a card row that reads like a shelf. The column now
                   centres at up to 980 logical px, the same cap the plan page uses. */
                int maxInner = Ui.S(980);
                int pad = Ui.S(30);
                int inner = Math.Max(Ui.S(240), Math.Min(maxInner, w - pad * 2));
                int left = Math.Max(pad, (w - inner) / 2);
                int gap = Ui.S(14);
                int y = pad;

                _identity.SetBounds(left, y, inner, _identity.NeededHeight(inner));
                y += _identity.Height + gap;

                /* Two cards side by side when there is room for two readable columns, stacked
                   otherwise. The break point is the width at which a "label   value" row stops
                   being one line, not a device class. */
                bool two = inner >= Ui.S(700);
                if (_plan.Visible || _security.Visible)
                {
                    if (two && _plan.Visible && _security.Visible)
                    {
                        int colW = (inner - gap) / 2;
                        int h = Math.Max(_plan.NeededHeight(colW), _security.NeededHeight(colW));
                        _plan.SetBounds(left, y, colW, h);
                        _security.SetBounds(left + colW + gap, y, colW, h);
                        y += h + gap;
                    }
                    else
                    {
                        int h = _plan.Visible ? _plan.NeededHeight(inner) : 0;
                        if (h > 0) { _plan.SetBounds(left, y, inner, h); y += h + gap; }
                        h = _security.Visible ? _security.NeededHeight(inner) : 0;
                        if (h > 0) { _security.SetBounds(left, y, inner, h); y += h + gap; }
                    }
                }

                if (_workspace.Visible)
                {
                    int h = _workspace.NeededHeight(inner);
                    _workspace.SetBounds(left, y, inner, h);
                    y += h + gap;
                }

                if (_usage.Visible)
                {
                    int h = _usage.NeededHeight(inner);
                    _usage.SetBounds(left, y, inner, h);
                    y += h + gap;
                }

                if (_accountsHeading.Visible)
                {
                    int headH = Theme.LineHeight(Theme.Small);
                    _accountsHeading.SetBounds(left, y, inner, headH);
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
                    _accountsBox.SetBounds(left, y, inner, Math.Max(0, yy - Ui.S(8)));
                    y += _accountsBox.Height + gap;
                }
                if (_emptyNote.Visible)
                {
                    int h = Measure(_emptyNote.Text, Theme.Caption, inner);
                    _emptyNote.SetBounds(left, y, inner, h);
                    y += h + gap;
                }

                y += Ui.S(6);
                _column.SetBounds(0, 0, Math.Max(inner + left + pad, w), Math.Max(y, _host.Height));
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
                {
                    /* The stored timestamp is ISO-8601 UTC - "2025-05-01T09:12:33.1234567Z" -
                       which is a fact for a log file, not a sentence for a person. The local
                       date, spelled out, is what a member page shows. */
                    string since = "";
                    try
                    {
                        DateTime created;
                        if (!string.IsNullOrEmpty(me.Created) &&
                            DateTime.TryParse(me.Created, null, System.Globalization.DateTimeStyles.RoundtripKind, out created))
                            since = L10n.T("acct.since") + " " + created.ToLocalTime().ToString("d MMMM yyyy");
                        else if (!string.IsNullOrEmpty(me.Created))
                            since = L10n.T("acct.since") + " " + me.Created;
                    }
                    catch { }
                    _line = me.Email + (since.Length == 0 ? "" : "  ·  " + since);
                }
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
                int wanted = Switch.PreferredWidth(Ui.S(150)) + SignOut.PreferredWidth(Ui.S(110)) + Ui.S(8);
                int btnW = Math.Min(wanted, Math.Max(Ui.S(120), (int)(Width - left - Ui.S(20))));
                bool stack = Width < Ui.S(560);
                float textW = stack ? Width - left - Ui.S(20) : Math.Max((float)Ui.S(80), Width - left - btnW - Ui.S(28));

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

        /// <summary>
        /// Where the account stands inside its plan, drawn rather than written: a bar for the
        /// profile slots in use, the days the subscription still runs, and the invite code with
        /// the days it has earned. The plan card says what the plan is; this card says where the
        /// account stands inside it.
        /// </summary>
        private sealed class UsageCard : Control
        {
            private int _used, _limit;          // _limit <= 0 means unlimited
            private int _daysLeft = -1;         // -1: not applicable
            private string _invite = "";
            private int _earned;
            private readonly JBButton _copy;

            public UsageCard()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Ui.Opaque(this);
                BackColor = Theme.Bg;
                _copy = MakeButton(L10n.T("acct.referral.copy"), "copy", BtnKind.Soft);
                _copy.Click += delegate
                {
                    try { if (!string.IsNullOrEmpty(_invite)) Clipboard.SetText(_invite); } catch { }
                };
                Controls.Add(_copy);
            }

            public void Reload()
            {
                _used = UsedProfiles();
                _limit = Plan.IsPro ? 0 : Entitlements.FreeProfiles; // 0 = unlimited
                var until = Plan.Until;
                _daysLeft = (!Plan.IsLifetime && Plan.IsPro && until != null)
                    ? Math.Max(0, (int)Math.Ceiling((until.Value.ToLocalTime() - DateTime.Now).TotalDays)) : -1;
                try { _invite = Referral.MyCode ?? ""; _earned = Referral.EarnedDays(); }
                catch { _invite = ""; _earned = 0; }
                _copy.Visible = !string.IsNullOrEmpty(_invite);
                Invalidate();
            }

            public int NeededHeight(int width)
            {
                int lines = Theme.LineHeight(Theme.H3) + Ui.S(8);
                int bar = Ui.S(8) + Ui.S(10);
                return Ui.S(40) + lines + bar + Ui.S(14);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                g.Clear(Theme.Bg);
                var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                Ui.Fill(g, box, Ui.S(Theme.CardRadius), Theme.Card);
                Ui.Stroke(g, box, Ui.S(Theme.CardRadius), Theme.Line);

                float pad = Ui.S(18);
                float y = Ui.S(14);
                Ui.Text(g, L10n.T("acct.usage.head"), Theme.Micro, Theme.TextFaint, pad, y, Width - pad * 2 - Ui.S(130));
                y += Theme.LineHeight(Theme.Micro) + Ui.S(4);

                string slots = _limit <= 0
                    ? _used + L10n.T("acct.usage.unlimited")
                    : _used + " / " + _limit + L10n.T("acct.usage.profiles");
                Color slotColor = _limit > 0 && _used >= _limit ? Theme.Warn : Theme.Text;
                Ui.Text(g, slots, Theme.H3, slotColor, pad, y, Width - pad * 2 - Ui.S(130));
                y += Theme.LineHeight(Theme.H3) + Ui.S(8);

                /* The bar. Square ends, like every corner in this app. */
                float barW = Math.Max(Ui.S(40), Width - pad * 2);
                var track = new RectangleF(pad, y, barW, Ui.S(8));
                Ui.Fill(g, track, Ui.S(Theme.ButtonRadius), Theme.Input);
                float frac = _limit > 0 ? Math.Min(1f, _used / (float)_limit) : 1f;
                var fillR = new RectangleF(pad, y, Math.Max(0f, barW * frac), Ui.S(8));
                if (fillR.Width > 0.5f)
                    Ui.Fill(g, fillR, Ui.S(Theme.ButtonRadius),
                        _limit > 0 && _used >= _limit ? Theme.Warn : Theme.Accent);

                float rightX = Width - pad - Ui.S(120);
                float rightW = Ui.S(120);
                float ry = Ui.S(12);
                if (_daysLeft >= 0)
                {
                    string days = _daysLeft == 1 ? "1 day left" : _daysLeft + L10n.T("acct.usage.daysleft");
                    Ui.Text(g, days, Theme.Body, _daysLeft <= 3 ? Theme.Warn : Theme.TextDim, rightX, ry, rightW);
                    ry += Theme.LineHeight(Theme.Body) + Ui.S(6);
                }
                if (!string.IsNullOrEmpty(_invite))
                {
                    if (_earned > 0)
                        Ui.Text(g, "+" + _earned + L10n.T("acct.referral.earned"), Theme.Body, Theme.Success, rightX, ry, rightW);
                    Ui.Text(g, _invite, Theme.Body, Theme.Text, rightX, ry + (_earned > 0 ? Theme.LineHeight(Theme.Body) + Ui.S(4) : 0), rightW);
                }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                int bw = _copy.PreferredWidth(Ui.S(96));
                _copy.SetBounds(Width - Ui.S(18) - bw, Ui.S(12), bw, Ui.S(32));
            }
        }
    }
}
