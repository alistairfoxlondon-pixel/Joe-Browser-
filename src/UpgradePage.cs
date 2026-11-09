using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal sealed class UpgradeActionEventArgs : EventArgs
    {
        public readonly string Key;
        public UpgradeActionEventArgs(string key) { Key = key; }
    }

    /// <summary>
    /// A pickable card carrying the official coin or network mark.
    ///
    /// Own-drawn rather than a button with an image list: the selected, hover and theme states
    /// have to change the fill, the border and the text colour together, and the mark has to
    /// stay crisp at every DPI.
    /// </summary>
    internal sealed class CoinChip : Control, IReStyle
    {
        public string Logo = "";
        public string Caption = "";
        public string Sub = "";
        public bool Selected;

        private bool _hover;

        public CoinChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Ui.Opaque(this);
            /* The only way to choose a network or a coin. Without a tab stop the whole payment
               form was unusable from the keyboard. */
            TabStop = true;
        }

        public void ReStyle() { Invalidate(); }

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
                // Same path as a click, so a chip chosen with the keyboard updates the invoice.
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = Selected ? Theme.AccentOnBg : _hover ? Theme.PanelHover : Theme.Card;
            Ui.Fill(g, r, Ui.S(6), fill);
            Ui.Stroke(g, r, Ui.S(6), Selected ? Theme.AccentBorder : Theme.Line, Selected ? 1.4f : 1f);
            if (Ui.FocusCues(this)) Ui.FocusRing(g, r, Ui.S(6));

            float pad = Ui.S(10);
            float box = Ui.S(22);
            float x = pad;

            if (!string.IsNullOrEmpty(Logo))
            {
                if (BrowserLogo.TryDraw(g, new RectangleF(x, (Height - box) / 2f, box, box), Logo)) x += box + Ui.S(9);
            }

            float textW = Width - x - pad;
            bool two = !string.IsNullOrEmpty(Sub);
            Color main = Selected ? Theme.AccentText : Theme.Text;

            if (two)
            {
                Ui.Text(g, Caption, Theme.Body, main, x, Ui.S(7), textW);
                Ui.Text(g, Sub, Theme.Small, Selected ? Theme.AccentText : Theme.TextFaint, x, Ui.S(24), textW);
            }
            else
            {
                Ui.Text(g, Caption, Theme.Body, main, x, (Height - Theme.Body.Height) / 2f, textW);
            }
        }
    }

    /// <summary>
    /// A step header: a small ringed numeral followed by the step name, or an icon in place of
    /// the numeral for a section that is not part of the numbered flow. The page is a sequence
    /// and the numbers are what make that visible without a paragraph of instructions.
    /// </summary>
    internal sealed class StepHeader : Control, IReStyle
    {
        public int Number;
        public string Caption = "";
        public string IconName = "";

        public StepHeader(int number, string caption)
        {
            Number = number;
            Caption = caption;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Cursor = Cursors.Arrow;
            Ui.Opaque(this);
        }

        public void ReStyle() { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            g.Clear(BackColor);

            float d = Ui.S(22);
            float cy = (Height - d) / 2f;
            var ring = new RectangleF(0.5f, cy + 0.5f, d - 1f, d - 1f);
            Ui.Fill(g, ring, d / 2f, Theme.AccentOnBg);
            Ui.Stroke(g, ring, d / 2f, Theme.AccentBorder, 1f);

            if (!string.IsNullOrEmpty(IconName))
            {
                /* A Material glyph below ~14 px turns to mush, so the icon gets the inside of
                   the ring rather than a fixed inset. */
                float ic = d - Ui.S(4);
                Icons.Draw(g, IconName, new RectangleF(ring.X + (d - ic) / 2f, ring.Y + (d - ic) / 2f, ic, ic),
                    Theme.AccentText);
            }
            else
            {
                /* Micro is the letter-spaced section face: a step numeral set in it reads as a
                   speck inside the ring. The numeral shares the caption's weight instead. */
                Ui.Text(g, Number.ToString(), Theme.SmallBold, Theme.AccentText, ring.X,
                    cy + (d - Theme.SmallBold.GetHeight(g)) / 2f, d, "center");
            }

            float tx = ring.Right + Ui.S(9);
            Ui.Text(g, Caption, Theme.SmallBold, Theme.Text, tx,
                (Height - Theme.SmallBold.GetHeight(g)) / 2f, Math.Max(0, Width - tx));
        }
    }

    /// <summary>
    /// An inline warning strip: a tinted band with a mark and one line of text.
    ///
    /// The chain the user picks is the only decision on this page that destroys money when it
    /// is wrong, and the hint under it used to be another line of grey caption text that the
    /// eye slides past. A band with a warning mark is read without being looked for.
    /// </summary>
    internal sealed class NoteBar : Control, IReStyle
    {
        public string Message = "";

        private static readonly Color Amber = Color.FromArgb(0xF5, 0x9E, 0x0B);

        public NoteBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Cursor = Cursors.Arrow;
            Ui.Opaque(this);
        }

        public void ReStyle() { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            g.Clear(BackColor);
            if (string.IsNullOrEmpty(Message) || Width < Ui.S(60)) return;

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(6), Ui.Blend(BackColor, Amber, Theme.Dark ? 0.15f : 0.10f));
            Ui.Stroke(g, r, Ui.S(6), Ui.Blend(BackColor, Amber, Theme.Dark ? 0.45f : 0.35f), 1f);

            float pad = Ui.S(10);
            float ic = Ui.S(15);
            Icons.Draw(g, "alert", new RectangleF(pad, (Height - ic) / 2f, ic, ic), Amber);

            Color ink = Theme.Dark ? Color.FromArgb(0xFF, 0xC8, 0x77) : Color.FromArgb(0x7A, 0x4E, 0x00);
            float tx = pad + ic + Ui.S(8);
            Ui.Text(g, Message, Theme.Small, ink, tx, (Height - Theme.Small.GetHeight(g)) / 2f,
                Math.Max(0, Width - tx - pad));
        }
    }

    /// <summary>
    /// Plan and payment.
    ///
    /// The order is deliberate: network first, then coin, then terms, then the transfer. Sending
    /// a token on the wrong chain destroys it, so the network is never inferred from the coin -
    /// the same USDT exists on three chains here and only the user knows which one they hold.
    /// </summary>
    internal sealed class UpgradePage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly JBPanel _plan;
        private readonly List<CoinChip> _netChips = new List<CoinChip>();
        private readonly List<CoinChip> _coinChips = new List<CoinChip>();
        private readonly List<NetworkOption> _networks = NetworkCatalog.All;

        private NetworkOption _network;
        private CoinOption _selected;

        private StepHeader _sectionNet, _sectionCoin, _sectionPay;
        private StepHeader _sectionInvite;
        private NoteBar _netNote;
        private JBLabel _inviteLine;
        private JBButton _copyInvite, _claim;
        private JBLabel _amount, _amountSub, _address, _status, _timer;
        private JBButton _copy, _copyAmount, _check, _restore, _termsLink;
        private JBPanel _termsPanel;
        private JBButton _readTerms, _acceptTerms;
        private PictureBox _qr;
        private bool _layingOut, _ready, _restoring;

        public event EventHandler<UpgradeActionEventArgs> ActionRequested;

        public Invoice Current { get; private set; }
        public string Status { get { return _status == null ? "" : _status.Text; } }
        public CoinOption Selected { get { return _selected; } }
        public NetworkOption Network { get { return _network; } }

        /// <summary>False until the payment terms have been read and accepted.</summary>
        public bool TermsAccepted { get; private set; }

        public UpgradePage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            _plan = new JBPanel { Hoverable = false };
            _plan.Paint += PaintPlan;
            _column.Controls.Add(_plan);

            _sectionNet = new StepHeader(1, "NETWORK");
            _column.Controls.Add(_sectionNet);

            foreach (var net in _networks)
            {
                var chip = new CoinChip { Logo = net.Logo, Caption = net.Name, Sub = net.Standard, Height = Ui.S(44) };
                chip.Tag = net;
                chip.Click += delegate(object s, EventArgs e) { SelectNetwork(((CoinChip)s).Tag as NetworkOption); };
                _column.Controls.Add(chip);
                _netChips.Add(chip);
            }

            _netNote = new NoteBar();
            _column.Controls.Add(_netNote);

            _sectionCoin = new StepHeader(2, "COIN");
            _column.Controls.Add(_sectionCoin);

            _sectionPay = new StepHeader(3, "PAYMENT");
            _column.Controls.Add(_sectionPay);

            _amount = new JBLabel(LabelRole.Strong, "\u2014");
            _amountSub = new JBLabel(LabelRole.Faint, "");
            _address = new JBLabel(LabelRole.Mono, "");
            _address.AutoEllipsis = true;
            _timer = new JBLabel(LabelRole.Body, "");
            _status = new JBLabel(LabelRole.Faint, "");
            _status.Wrap = true;

            _copy = new JBButton { Kind = BtnKind.Soft, Icon = "copy", Text = "Copy address", Radius = 6, Height = Theme.Field };
            /* The amount is the other half of a crypto transfer and it used to have to be read
               off the screen and retyped: one wrong digit sends the money nowhere. */
            _copyAmount = new JBButton { Kind = BtnKind.Soft, Icon = "copy", Text = "Copy amount", Radius = 6, Height = Theme.Field };
            _check = new JBButton { Kind = BtnKind.Primary, Icon = "refresh", Text = "I have paid", Radius = 6, Height = Theme.Field };
            _restore = new JBButton { Kind = BtnKind.Ghost, Text = "Restore a purchase", Radius = 6, Height = Theme.Field };
            _termsLink = new JBButton { Kind = BtnKind.Ghost, Text = "Terms", Radius = 6, Height = Theme.Field };

            _copy.Click += delegate { Raise("copy"); };
            _copyAmount.Click += delegate { Raise("copyamount"); };
            /* Clicking the address copies it too - the obvious gesture on a long hex string. */
            _address.Cursor = Cursors.Hand;
            _address.Click += delegate { Raise("copy"); };
            _check.Click += delegate { Raise("check"); };
            _restore.Click += delegate { Raise("restore"); };
            _termsLink.Click += delegate { Raise("terms"); };

            _sectionInvite = new StepHeader(0, "INVITE") { IconName = "gift" };
            _inviteLine = new JBLabel(LabelRole.Faint, "");
            _copyInvite = new JBButton { Kind = BtnKind.Soft, Icon = "copy", Text = "Copy invite", Radius = 6, Height = Ui.S(33) };
            _claim = new JBButton { Kind = BtnKind.Soft, Text = "Claim a receipt", Radius = 6, Height = Ui.S(33) };
            _copyInvite.Click += delegate { Raise("invite"); };
            _claim.Click += delegate { Raise("claim"); };
            _column.Controls.Add(_sectionInvite);
            _column.Controls.Add(_inviteLine);
            _column.Controls.Add(_copyInvite);
            _column.Controls.Add(_claim);

            _termsPanel = new JBPanel { Hoverable = false };
            _termsPanel.Paint += PaintTerms;
            _column.Controls.Add(_termsPanel);

            _readTerms = new JBButton { Kind = BtnKind.Soft, Text = "Read the terms", Radius = 6, Height = Ui.S(33) };
            _acceptTerms = new JBButton { Kind = BtnKind.Primary, Text = "I accept", Radius = 6, Height = Ui.S(33) };
            _readTerms.Click += delegate { Raise("terms"); };
            _acceptTerms.Click += delegate { AcceptTerms(); };
            /* Children of the card, not siblings of it. As siblings they depended on z-order to
               stay visible, which is exactly the kind of thing that breaks quietly when the
               card is repainted; as children they are painted with the card, in order. */
            _termsPanel.Controls.Add(_readTerms);
            _termsPanel.Controls.Add(_acceptTerms);

            _qr = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Visible = false };

            _column.Controls.Add(_amount);
            _column.Controls.Add(_amountSub);
            _column.Controls.Add(_address);
            _column.Controls.Add(_timer);
            _column.Controls.Add(_status);
            _column.Controls.Add(_copy);
            _column.Controls.Add(_copyAmount);
            _column.Controls.Add(_check);
            _column.Controls.Add(_restore);
            _column.Controls.Add(_termsLink);
            _column.Controls.Add(_qr);

            TermsAccepted = Terms.Accepted;
            _ready = true;
            RefreshInvite();
            SelectNetwork(_networks[0]);
            LayoutCore();
        }

        private void Raise(string key)
        {
            var h = ActionRequested;
            if (h != null) h(this, new UpgradeActionEventArgs(key));
        }

        /// <summary>Called by the host once the terms were read and accepted.</summary>
        public void AcceptTerms()
        {
            if (!Terms.Accept()) { SetStatus("Could not save the acceptance. Try again.", Theme.Danger); return; }
            TermsAccepted = true;
            ApplyTermsState();
            NewInvoice(_selected);
            LayoutCore();
        }

        private void ApplyTermsState()
        {
            bool pay = TermsAccepted && _selected != null;
            _termsPanel.Visible = !pay;
            _readTerms.Visible = !pay;
            _acceptTerms.Visible = !pay;
            _sectionPay.Visible = pay;
            _amount.Visible = pay;
            _amountSub.Visible = pay;
            _address.Visible = pay;
            _timer.Visible = pay;
            _copy.Visible = pay;
            _copyAmount.Visible = pay;
            _check.Visible = pay;
            _termsLink.Visible = pay;
            _qr.Visible = pay && _qr.Image != null;
            _status.Visible = true;
        }

        private void SelectNetwork(NetworkOption net)
        {
            _network = net;
            foreach (var c in _netChips)
            {
                c.Selected = ReferenceEquals(c.Tag, net);
                c.Invalidate();
            }
            _netNote.Message = net == null ? "" : net.Note;
            _netNote.Invalidate();

            /* The token list belongs to the network, so it is rebuilt rather than filtered. */
            foreach (var c in _coinChips) { _column.Controls.Remove(c); c.Dispose(); }
            _coinChips.Clear();

            if (net == null) { ApplyTermsState(); LayoutCore(); return; }

            foreach (var coin in CoinCatalog.OnChain(net.Key))
            {
                var chip = new CoinChip { Logo = coin.Logo, Caption = coin.Symbol, Sub = CoinSub(coin), Height = Ui.S(44) };
                chip.Tag = coin;
                chip.Click += delegate(object s, EventArgs e) { SelectCoin(((CoinChip)s).Tag as CoinOption); };
                _column.Controls.Add(chip);
                _coinChips.Add(chip);
            }

            SelectCoin(_coinChips.Count > 0 ? _coinChips[0].Tag as CoinOption : null);
        }

        private static string CoinSub(CoinOption coin)
        {
            if (coin.Native) return "Native";
            if (coin.Symbol == "USDC" || coin.Symbol == "USDT" || coin.Symbol == "DAI") return "Stablecoin";
            return "";
        }

        private void SelectCoin(CoinOption coin)
        {
            _selected = coin;
            foreach (var c in _coinChips)
            {
                c.Selected = ReferenceEquals(c.Tag, coin);
                c.Invalidate();
            }
            ApplyTermsState();
            if (coin != null && TermsAccepted) NewInvoice(coin);
            LayoutCore();
        }

        /// <summary>Starts a fresh 30-minute session at the current rate.</summary>
        public void NewInvoice(CoinOption coin) { NewInvoice(coin, null, null); }

        /// <summary>
        /// Starts a session. The rate is read once here and then locked: a quote that moves
        /// after the user has already sent the money is worse than a slightly stale one.
        /// </summary>
        public void NewInvoice(CoinOption coin, DateTime? createdUtc, decimal? fixedAmount)
        {
            if (coin == null) return;
            _selected = coin;

            double usd = Rates.Usd(coin.RateId);
            if (usd <= 0) usd = 1;

            decimal amount;
            if (fixedAmount.HasValue && fixedAmount.Value > 0) amount = fixedAmount.Value;
            else
            {
                amount = (decimal)(1.0 / usd);
                /* Stablecoins are quoted to the cent; volatile coins keep enough digits to be
                   exact. */
                bool stableQuote = usd > 0.95 && usd < 1.05;
                amount = stableQuote ? Math.Round(amount, 2) : Math.Round(amount, 6);
            }
            bool stable = usd > 0.95 && usd < 1.05;

            var created = createdUtc ?? DateTime.UtcNow;
            Current = new Invoice
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant(),
                Coin = coin,
                Usd = 1.0,
                Amount = amount,
                CreatedUtc = created,
                ExpiresUtc = created.AddMinutes(30)
            };

            _amount.Text = Invoice.Fmt(amount) + " " + coin.Symbol;
            _amountSub.Text = "$1.00  \u00b7  1 " + coin.Symbol + " = " +
                              (stable ? "$1.00" : "$" + usd.ToString("0.00##", System.Globalization.CultureInfo.InvariantCulture));
            _address.Text = coin.Address;
            ToolTips.Attach(_address, coin.Address);
            SetStatus("Send exactly this amount on " + (_network == null ? coin.Network : _network.Label) + ".",
                Theme.TextFaint);
            ShowQr(Current.Uri);
            TickTimer();
            LayoutCore();

            /* Persisted so a payment made just before closing the app is still honoured. The
               intermediate session built while restoring must not overwrite the saved one. */
            if (!createdUtc.HasValue && !_restoring) Plan.SavePending(coin.Key, created, amount, coin.Address);
        }

        /// <summary>
        /// Re-opens the session that was live when the app last closed, when it is still recent.
        /// Without this, a user who paid and closed the app would come back to a fresh session
        /// that refuses their transfer as "too old".
        /// </summary>
        public void RestorePending()
        {
            if (Plan.IsPro) return;
            string key = Plan.PendingCoin;
            if (string.IsNullOrEmpty(key)) return;

            var created = Plan.PendingCreated;
            var amount = Plan.PendingAmount;
            if (!created.HasValue || !amount.HasValue) return;
            if (DateTime.UtcNow - created.Value > TimeSpan.FromHours(2)) { Plan.ClearPending(); return; }

            var coin = CoinCatalog.ByKey(key);
            if (coin == null) return;

            var net = NetworkCatalog.For(coin.Chain);
            _restoring = true;
            try
            {
                if (net != null) SelectNetwork(net);
                SelectCoin(coin);
            }
            finally { _restoring = false; }
            if (TermsAccepted) NewInvoice(coin, created, amount);
        }

        private void ShowQr(string uri)
        {
            try
            {
                var code = QrCode.Encode(uri);
                if (code == null) { _qr.Visible = false; return; }
                var old = _qr.Image;
                _qr.Image = code.ToBitmap(5, 3);
                if (old != null) old.Dispose();
                _qr.Visible = TermsAccepted;
            }
            catch { _qr.Visible = false; }
        }

        public void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.Text = text ?? "";
            _status.ForeColor = color;
            _status.Invalidate();
        }

        public void SetBusy(bool busy)
        {
            if (busy) { _check.Enabled = false; _check.Text = "Checking\u2026"; }
            else { _check.Enabled = true; _check.Text = "I have paid"; }
            _check.Invalidate();
        }

        public void TickTimer()
        {
            if (Current == null) { _timer.Text = ""; return; }

            var left = Current.Left;
            if (left <= TimeSpan.Zero)
            {
                _timer.Text = "Session expired";
                _timer.ForeColor = Theme.Danger;
                if (_check.Text != "New session") { _check.Text = "New session"; _check.Invalidate(); }
                return;
            }
            if (_check.Text == "New session") { _check.Text = "I have paid"; _check.Invalidate(); }
            _timer.Text = "Session " + ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
            _timer.ForeColor = left.TotalMinutes < 5 ? Theme.Danger : Theme.TextDim;
            _timer.Invalidate();
        }

        public void RefreshPlan()
        {
            _plan.Invalidate();
            RefreshInvite();
            /* The card changes height when Pro starts or lapses, so everything below it has to
               move up or down rather than overlap the bar. */
            LayoutCore();
        }

        /// <summary>Keeps the invite line in step with the signed-in account and earned days.</summary>
        public void RefreshInvite()
        {
            if (_inviteLine == null) return;
            int earned = Referral.EarnedDays();
            _inviteLine.Text = "Your code " + Referral.MyCode + "  \u00b7  +" + Referral.Days +
                               " days per friend" + (earned > 0 ? "  \u00b7  " + earned + " earned" : "");
            _inviteLine.Invalidate();
        }

        private void PaintPlan(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            float w = _plan.Width;
            bool pro = Plan.IsPro;
            var r = new RectangleF(0.5f, 0.5f, w - 1.5f, _plan.Height - 1.5f);
            Ui.Fill(g, r, Ui.S(10), Theme.Card);
            Ui.Stroke(g, r, Ui.S(8), pro ? Theme.AccentBorder : Theme.Line, 1f);

            /* Icon chip, title block, state pill - the same three-part shape as a settings row.
               The plan is a state, not three loose lines of text, and it should read that way
               at a glance from across the room. */
            float chip = Ui.S(42);
            var cr = new RectangleF(Ui.S(18), Ui.S(17), chip, chip);
            Color tint = pro ? Theme.Accent : Theme.TextDim;
            Ui.Fill(g, cr, Ui.S(10), Ui.Blend(Theme.Card, tint, Theme.Dark ? 0.24f : 0.13f));
            float ic = Ui.S(22);
            Icons.Draw(g, pro ? "crown" : "sparkle",
                new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), tint);

            /* The pill is measured, not assumed, so a translation cannot clip it. */
            string pill = pro ? "ACTIVE" : "FREE";
            float ph = Ui.S(20);
            float pw = Math.Max(Ui.S(52), Ui.Measure(g, pill, Theme.Micro).Width + Ui.S(18));
            var pr = new RectangleF(w - pw - Ui.S(18), Ui.S(19), pw, ph);
            Ui.Fill(g, pr, ph / 2f, pro ? Ui.Blend(Theme.Card, Theme.Success, Theme.Dark ? 0.24f : 0.14f) : Theme.Panel2);
            Ui.Stroke(g, pr, ph / 2f, pro ? Ui.Blend(Theme.Success, Theme.Card, 0.45f) : Theme.Line, 1f);
            Ui.Text(g, pill, Theme.Micro, pro ? Theme.Success : Theme.TextDim,
                pr.X, pr.Y + (ph - Theme.Micro.GetHeight(g)) / 2f, pw, "center");

            float textLeft = cr.Right + Ui.S(14);
            float textW = Math.Max(Ui.S(120), w - textLeft - Ui.S(18) - pw - Ui.S(12));

            Ui.Text(g, pro ? "Pro plan" : "Free plan", Theme.H1, Theme.Text, textLeft, Ui.S(14), textW);
            Ui.Text(g, pro ? "Unlimited profiles, unlimited at once" : "3 profiles, one open at a time",
                Theme.Caption, Theme.TextDim, textLeft, Ui.S(40), textW);

            var until = Plan.Until;
            if (!pro)
            {
                Ui.Text(g, "$1 / month for unlimited", Theme.H3, Theme.Accent, textLeft, Ui.S(64), textW);
                return;
            }

            /* A complimentary account has no end date. Without this branch the countdown fell
               back to zero and told the owner of the lifetime account "0 days left" in red on
               their own plan page. */
            if (Plan.IsLifetime)
            {
                Ui.Text(g, "Lifetime, no renewal needed", Theme.H3, Theme.Success,
                    textLeft, Ui.S(63), textW);
                return;
            }
            int days = until.HasValue
                ? Math.Max(0, (int)Math.Ceiling((until.Value - DateTime.UtcNow).TotalDays))
                : 0;
            Ui.Text(g, until.HasValue
                        ? days + (days == 1 ? " day left" : " days left") + "  \u00b7  until " +
                          until.Value.ToLocalTime().ToString("d MMMM")
                        : "Active",
                Theme.H3, until.HasValue && days <= 5 ? Theme.Danger : Theme.Success, textLeft, Ui.S(63), textW);

            /* A countdown bar for the 30-day period. The text says how long is left; the bar
               says it without being read, which is what a subscription timer is for. */
            float bx = Ui.S(18), by = Ui.S(94), bw = w - Ui.S(36), bh = Ui.S(6);
            Ui.Fill(g, new RectangleF(bx, by, bw, bh), bh / 2f, Theme.Panel2);
            if (until.HasValue)
            {
                double left = (until.Value - DateTime.UtcNow).TotalDays;
                float frac = (float)Math.Max(0d, Math.Min(1d, left / 30d));
                if (frac > 0.005f)
                    Ui.Fill(g, new RectangleF(bx, by, Math.Max(bh, bw * frac), bh), bh / 2f,
                        days <= 5 ? Theme.Danger : Theme.Success);
            }
        }

        private void PaintTerms(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            float w = _termsPanel.Width;
            var r = new RectangleF(0.5f, 0.5f, w - 1.5f, _termsPanel.Height - 1.5f);
            Ui.Fill(g, r, Ui.S(10), Theme.Card);
            Ui.Stroke(g, r, Ui.S(10), Theme.Line);

            float x = Ui.S(20), y = Ui.S(16);
            Ui.Text(g, "Before you pay", Theme.H3, Theme.Text, x, y, w - Ui.S(40));
            Ui.Text(g, "Crypto payments cannot be reversed. Read the terms, then accept them.",
                Theme.Caption, Theme.TextDim, x, y + Ui.S(24), w - Ui.S(40));
            Ui.Text(g, "Network, amount and the 30-day licence are all explained there.",
                Theme.Caption, Theme.TextFaint, x, y + Ui.S(44), w - Ui.S(40));
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            foreach (var c in _netChips) c.Invalidate();
            foreach (var c in _coinChips) c.Invalidate();
            _plan.Invalidate();
            _termsPanel.Invalidate();
            Invalidate(true);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutCore(); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); LayoutCore(); }

        /// <summary>Width the widest label in a chip row needs, logo and padding included.</summary>
        private static int MeasureChip(List<CoinChip> chips)
        {
            int need = 0;
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    foreach (var chip in chips)
                    {
                        if (chip == null) continue;
                        float logo = string.IsNullOrEmpty(chip.Logo) ? 0 : Ui.S(22) + Ui.S(9);
                        float caption = Ui.Measure(g, chip.Caption ?? "", Theme.Body).Width;
                        float sub = Ui.Measure(g, chip.Sub ?? "", Theme.Small).Width;
                        need = Math.Max(need, (int)Math.Ceiling(logo + Math.Max(caption, sub)) + Ui.S(10) * 2);
                    }
                }
            }
            catch { }
            return need;
        }

        /// <summary>One more pass once the scroll host has fixed its width.</summary>
        public void LayoutAtWidth() { LayoutCore(); }

        private void LayoutCore()
        {
            if (_layingOut || !_ready || _host == null) return;
            _layingOut = true;
            try
            {
                _host.SetBounds(0, 0, Math.Max(1, Width), Math.Max(1, Height));
                int pad = Ui.S(28);
                /* A settings column that stretches across a 4K window reads like a spreadsheet:
                   the plan card becomes a 1400 px bar with 300 px of content in it. Capping the
                   measure keeps the chips and the card at a comfortable reading width, and the
                   column itself still fills the host so the scrollbar stays at the right edge. */
                int avail = Math.Max(Ui.S(300), _host.Width - Ui.S(10));
                int inner = Math.Min(Ui.S(940), avail - pad * 2);
                if (inner < Ui.S(300)) inner = Ui.S(300);
                int gap = Ui.S(8);

                int y = Ui.S(6);
                /* The paid card carries a countdown bar, the free one does not, so the card is
                   sized to its content instead of leaving a dead strip under the price. */
                int planH = Ui.S(Plan.IsPro ? 120 : 98);
                _plan.SetBounds(pad, y, inner, planH);
                y += planH + Ui.S(22);

                /* Chip labels are measured, not assumed. Three per row at 120% scale gave each
                   one a third of the column, so "TRON" and its address prefix were cut off with an
                   ellipsis and the two networks became hard to tell apart. */
                int chipNeed = Math.Max(Ui.S(112), Math.Max(MeasureChip(_netChips), MeasureChip(_coinChips)));
                int perRowMax = Math.Max(2, (inner + gap) / (chipNeed + gap));
                int perRow = inner >= Ui.S(760) ? Math.Min(5, perRowMax)
                    : inner >= Ui.S(520) ? Math.Min(3, perRowMax)
                    : Math.Min(2, perRowMax);
                if (perRow < 2) perRow = 2;
                int chipW = (inner - gap * (perRow - 1)) / perRow;
                int chipH = Ui.S(44);

                _sectionNet.SetBounds(pad, y, inner, Ui.S(22));
                y += Ui.S(22) + Ui.S(6);

                int rows = (_netChips.Count + perRow - 1) / perRow;
                for (int i = 0; i < _netChips.Count; i++)
                {
                    int col = i % perRow, row = i / perRow;
                    _netChips[i].SetBounds(pad + col * (chipW + gap), y + row * (chipH + gap), chipW, chipH);
                }
                y += rows * (chipH + gap) + Ui.S(6);

                _netNote.SetBounds(pad, y, inner, Ui.S(34));
                y += Ui.S(34) + Ui.S(16);

                _sectionCoin.SetBounds(pad, y, inner, Ui.S(22));
                y += Ui.S(22) + Ui.S(6);

                int rows2 = Math.Max(1, (_coinChips.Count + perRow - 1) / perRow);
                for (int i = 0; i < _coinChips.Count; i++)
                {
                    int col = i % perRow, row = i / perRow;
                    _coinChips[i].SetBounds(pad + col * (chipW + gap), y + row * (chipH + gap), chipW, chipH);
                }
                y += rows2 * (chipH + gap) + Ui.S(16);

                _sectionPay.SetBounds(pad, y, inner, Ui.S(22));
                y += Ui.S(22) + Ui.S(6);

                if (!TermsAccepted || _selected == null)
                {
                    /* Tall enough to hold its three lines AND the buttons underneath them. At
                       the old 86 px the buttons sat on top of the last caption line. */
                    int w1 = _readTerms.PreferredWidth(Ui.S(150));
                    int w2 = _acceptTerms.PreferredWidth(Ui.S(120));
                    /* Panel-relative: the buttons live inside the card, and the card grows to
                       hold a second row when the window is too narrow for both side by side.
                       Placing them at a fixed offset put the accept button outside the card on
                       a narrow window, on top of the page behind it. */
                    bool twoRows = Ui.S(20) + w1 + Ui.S(10) + w2 > inner - Ui.S(20);
                    int cardH = Ui.S(86) + (twoRows ? Ui.S(32) + Ui.S(10) : 0) + Ui.S(34);
                    _termsPanel.SetBounds(pad, y, inner, cardH);
                    int by = y + Ui.S(86);
                    _readTerms.SetBounds(Ui.S(20), Ui.S(86), w1, Ui.S(32));
                    _acceptTerms.SetBounds(twoRows ? Ui.S(20) : Ui.S(20) + w1 + Ui.S(10),
                        twoRows ? Ui.S(86) + Ui.S(32) + Ui.S(10) : Ui.S(86), w2, Ui.S(32));
                    y = by + (twoRows ? Ui.S(74) : Ui.S(32)) + Ui.S(26);
                }
                else
                {
                    int qr = Ui.S(132);
                    /* The details column and the code share one row that spans the measure: the
                       code sits on the right edge of the content, not floating in the middle of
                       a wide window with dead space beside it. */
                    int textW = Math.Min(Math.Max(Ui.S(220), inner - qr - Ui.S(32)), Ui.S(520));

                    _amount.SetBounds(pad, y, textW, Ui.S(30));
                    _amountSub.SetBounds(pad, y + Ui.S(32), textW, Ui.S(20));
                    _address.SetBounds(pad, y + Ui.S(60), textW, Ui.S(22));
                    _timer.SetBounds(pad, y + Ui.S(88), textW, Ui.S(22));

                    int by = y + Ui.S(120);
                    int w1 = _copy.PreferredWidth(Ui.S(150));
                    int wa = _copyAmount.PreferredWidth(Ui.S(150));
                    int w2 = _check.PreferredWidth(Ui.S(140));
                    /* All three are measured against the column they sit in, and each drops to
                       a second row rather than crossing into the QR code's space. The second
                       button used to be placed at a fixed offset with no check at all, so on a
                       narrow window it was drawn on top of the code. */
                    _copy.SetBounds(pad, by, w1, Ui.S(33));
                    bool secondRow = pad + w1 + Ui.S(10) + wa > pad + textW;
                    _copyAmount.SetBounds(secondRow ? pad : pad + w1 + Ui.S(10),
                        secondRow ? by + Ui.S(43) : by, wa, Ui.S(33));

                    int paidY = secondRow ? by + Ui.S(86) : by;
                    int paidX = secondRow ? pad : pad + w1 + wa + Ui.S(20);
                    bool thirdRow = paidX + w2 > pad + textW;
                    if (thirdRow) { paidX = pad; paidY += Ui.S(43); }
                    _check.SetBounds(paidX, paidY, w2, Ui.S(33));

                    _qr.SetBounds(pad + inner - qr, y, qr, qr);

                    int bottom = Math.Max(by, _check.Bottom);
                    y = bottom + Ui.S(14);
                    _status.SetBounds(pad, y, Math.Min(inner, Ui.S(720)), Ui.S(40));
                    y += Ui.S(44);
                }

                _termsLink.Visible = TermsAccepted;
                JBButton[] bottomActions = TermsAccepted
                    ? new[] { _restore, _termsLink }
                    : new[] { _restore };
                y = Ui.FlowButtons(bottomActions, pad, y, inner, Ui.S(10), Ui.S(33)) + Ui.S(26);

                _sectionInvite.SetBounds(pad, y, inner, Ui.S(22));
                y += Ui.S(22) + Ui.S(6);
                _inviteLine.SetBounds(pad, y, inner, Ui.S(20));
                y += Ui.S(20) + Ui.S(10);

                JBButton[] invite = { _copyInvite, _claim };
                y = Ui.FlowButtons(invite, pad, y, inner, Ui.S(10), Ui.S(32)) + Ui.S(28);

                _column.SetBounds(0, 0, Math.Max(inner + pad * 2, avail), Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }
    }
}
