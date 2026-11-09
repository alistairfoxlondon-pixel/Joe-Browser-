using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// A profile card: browser mark, identity, device and proxy chips, then the actions.
    /// Geometry is derived from the live width so chips truncate instead of overflowing, and the
    /// selection box has a reserved slot so it never lands on top of the name.
    /// </summary>
    internal sealed class ProfileCard : Control, IReStyle
    {
        public Profile Model;
        public bool Running;
        public bool Launching;
        public bool Picked;
        /* Set when the plan no longer covers this profile: over the free cap after a paid plan
           lapsed, or never covered at all. The card says so and the launch is refused with the
           upgrade offer instead of opening a browser the plan does not allow. */
        public bool Locked;

        public event EventHandler OpenRequested;
        public event EventHandler EditRequested;
        public event EventHandler ProxyRequested;
        public event EventHandler DuplicateRequested;
        public event EventHandler DeleteRequested;
        public event EventHandler FocusRequested;
        public event EventHandler CloseRequested;
        public event EventHandler PickToggled;
        public event EventHandler RenameRequested;
        public event EventHandler ClearCacheRequested;
        public event EventHandler LeakTestRequested;
        public event EventHandler HistoryRequested;

        /* Card metrics, derived from the type scale rather than guessed. The card was 200 tall
           with the action bar 14 px from the bottom edge and a 28 px chip row that started at
           y=72: at the raised type scale the name, its subtitle and the status pill no longer
           fitted between the top edge and the first chip, which is the overcrowding that reads
           as a broken layout. Each band below is a line of type plus its padding.
           248 is the height at which a wrapped proxy chip, the tag row and the action bar all
           clear each other: tags land at 154 and end by 180, the actions start at 196, and the
           old 224 left the tag row ten pixels inside the action divider. */
        public const double CardH = 248;
        public const double CardMinW = 320;
        private const double Pad = 20;
        private const double CheckSize = 20;
        private const double LogoSize = 46;
        private const double ChipH = 30;
        private const double TagH = 24;
        private const double BtnH = 36;
        private const double KebabSize = 30;

        private bool _hover, _kebabHover, _openHover, _checkHover;
        /// <summary>Set once, on the first paint whose name had to be ellipsised.</summary>
        private bool _nameTip;
        private ContextMenuStrip _menu;

        /// <summary>
        /// Short signature of the whole fingerprint, set by the list once it can compare cards.
        /// It is computed there, not here, because a card cannot know whether its own signature
        /// also appears on a sibling - and that is the whole point of showing it.
        /// </summary>
        private string _sig = "";

        /// <summary>
        /// Sets the signature chip and the hint behind it, once. The card is recreated on every
        /// reload, so the tooltip is written here rather than per paint: TextRenderer costs
        /// nothing next to a ToolTip that is set a hundred times a second.
        /// </summary>
        public void SetSignature(string sig, string tooltip)
        {
            _sig = string.IsNullOrEmpty(sig) ? "" : sig;
            if (!string.IsNullOrEmpty(tooltip)) ToolTips.Attach(this, tooltip);
            else ToolTips.Clear(this);
        }

        public ProfileCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Ui.Opaque(this);
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = Theme.Body;
            Disposed += delegate { if (_menu != null) _menu.Dispose(); };
        }

        public static int PreferredHeight() { return Ui.S(CardH); }
        public static int PreferredMinWidth() { return Ui.S(CardMinW); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; _kebabHover = false; _openHover = false; _checkHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        // ---- geometry -------------------------------------------------------------

        private RectangleF CheckRect
        {
            get
            {
                float s = Ui.S(CheckSize);
                return new RectangleF(Ui.S(Pad), Ui.S(18 + LogoSize / 2 - CheckSize / 2), s, s);
            }
        }

        private RectangleF LogoRect
        {
            get
            {
                float s = Ui.S(LogoSize);
                return new RectangleF(CheckRect.Right + Ui.S(12), Ui.S(18), s, s);
            }
        }

        private RectangleF KebabRect
        {
            get
            {
                float s = Ui.S(KebabSize);
                float oy = Height - Ui.S(16) - Ui.S(BtnH);
                return new RectangleF(Width - Ui.S(Pad) - s, oy + (Ui.S(BtnH) - s) / 2f, s, s);
            }
        }

        private RectangleF OpenRect
        {
            get
            {
                float h = Ui.S(BtnH);
                float right = Width - Ui.S(Pad) - Ui.S(KebabSize) - Ui.S(10);
                float w = Math.Max(Ui.S(72), right - Ui.S(Pad));
                return new RectangleF(right - w, Height - Ui.S(16) - h, w, h);
            }
        }

        // ---- interaction ----------------------------------------------------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool k = KebabRect.Contains(e.Location);
            bool o = OpenRect.Contains(e.Location);
            bool c = CheckRect.Contains(e.Location);
            if (k != _kebabHover || o != _openHover || c != _checkHover)
            {
                _kebabHover = k; _openHover = o; _checkHover = c;
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
            if (CheckRect.Contains(e.Location) || ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0 && !KebabRect.Contains(e.Location) && !OpenRect.Contains(e.Location)))
            {
                if (CheckRect.Contains(e.Location) || (ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
                {
                    var h = PickToggled;
                    if (h != null) h(this, EventArgs.Empty);
                    return;
                }
            }
            if (KebabRect.Contains(e.Location)) { ShowMenu(); return; }
            if (OpenRect.Contains(e.Location)) { RaiseOpen(); return; }
            // A click anywhere else on the card opens it too: the whole card is the target.
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

        /// <summary>
        /// Opens the row menu from the keyboard. Public because Shift+F10 is resolved by the
        /// window's command-key pass, not by the card: the card never constructs that key
        /// sequence itself.
        /// </summary>
        public void ShowMenuKey()
        {
            if (Model == null) return;
            try { ShowMenu(); } catch { }
        }

        private void ShowMenu()
        {
            if (_menu != null) { _menu.Dispose(); _menu = null; }
            var menu = JBMenu.Create();
            _menu = menu;
            menu.Font = Theme.Body;
            JBMenu.Add(menu, Running ? "Focus window" : Launching ? "Starting…" : "Open profile", "play",
                delegate { RaiseOpen(); }, !Launching);
            if (Running)
                JBMenu.Add(menu, "Close browser", "power", delegate { var h = CloseRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Edit profile", "pencil", delegate { var h = EditRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Rename", "file", delegate { var h = RenameRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Change proxy (live, keep tab open)", "route",
                delegate { var h = ProxyRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, Running ? "Check fingerprint" : "Clear cache and cookies", "shield",
                delegate
                {
                    var h = Running ? LeakTestRequested : ClearCacheRequested;
                    if (h != null) h(this, EventArgs.Empty);
                });
            if (Running)
                JBMenu.Add(menu, "Clear cache (keeps tab open)", "refresh",
                    delegate { var h = ClearCacheRequested; if (h != null) h(this, EventArgs.Empty); });
            /* History belongs to one profile, and the History page otherwise shows whichever
               profile was opened last - which is not a way to find a page in a particular one. */
            JBMenu.Add(menu, "Browsing history", "clock",
                delegate { var h = HistoryRequested; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, Picked ? "Remove from selection" : "Add to selection", "check",
                delegate { var h = PickToggled; if (h != null) h(this, EventArgs.Empty); });
            JBMenu.Add(menu, "Duplicate", "copy", delegate { var h = DuplicateRequested; if (h != null) h(this, EventArgs.Empty); });
            menu.Items.Add(new ToolStripSeparator());
            JBMenu.Add(menu, "Delete profile", "trash", delegate { var h = DeleteRequested; if (h != null) h(this, EventArgs.Empty); });
            int mw = menu.GetPreferredSize(Size.Empty).Width;
            JBMenu.ShowNear(menu, this, new Point(Math.Max(Ui.S(6), Width - mw - Ui.S(10)), Ui.S(40)));
        }

        public void ReStyle() { Font = Theme.Body; Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.SurfaceColor(this));
        }

        // ---- painting -------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            if (Model == null) return;

            float rad = Ui.S(Theme.ButtonRadius);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

            /* A card is a surface, not a border: the fill carries it and the hairline is only
               there to keep two adjacent cards apart on a wide window. A running card lifts by
               one step and an accent hairline, which is a state you can see from across the
               grid instead of only in the status pill. */
            if (Running) Ui.Shadow(g, r, rad, 2, Theme.Dark);
            Ui.Fill(g, r, rad, Picked ? Theme.AccentOnBg : (_hover ? Theme.PanelHover : Theme.Card));
            Ui.Stroke(g, r, rad,
                Picked ? Theme.AccentBorder : Running ? Theme.AccentBorder : (_hover ? Theme.HoverStrong : Theme.Line));

            PaintHeader(g, r);
            bool proxyWrapped;
            PaintChips(g, out proxyWrapped);
            /* The tag and meta row used to be skipped outright when the proxy chip wrapped, so a
               narrow card silently lost the profile's timezone, language and tags to make room
               for one chip. The row moves down a line instead; the card is tall enough for it and
               the action bar is anchored to the bottom edge. */
            PaintTags(g, proxyWrapped ? Ui.S(34) : 0);
            PaintActions(g);

            if (Ui.FocusCues(this)) Ui.FocusRing(g, r, rad);
        }

        private void PaintHeader(Graphics g, RectangleF r)
        {
            // Selection box: the slot is always reserved, the box only draws when it is usable.
            var box = new RectangleF(CheckRect.X + 0.5f, CheckRect.Y + 0.5f,
                CheckRect.Width - 1f, CheckRect.Height - 1f);
            if (Picked || _hover || _checkHover)
            {
                Ui.Fill(g, box, Ui.S(Theme.ButtonRadius), Picked ? Theme.Accent : Theme.Bg);
                Ui.Stroke(g, box, Ui.S(Theme.ButtonRadius), Picked ? Theme.Accent : Theme.ButtonBorder, 1f);
                if (Picked)
                {
                    float ci = Ui.S(12);
                    Icons.Draw(g, "check", new RectangleF(box.X + (box.Width - ci) / 2f,
                        box.Y + (box.Height - ci) / 2f, ci, ci), Theme.OnAccent);
                }
            }

            BrowserMark.Draw(g, LogoRect, BrowserMark.KindFor(Model));

            float tx = LogoRect.Right + Ui.S(12);
            float nameRight = Width - Ui.S(Pad);
            string status = Running ? "Running" : Launching ? "Starting…" : Locked ? "Plan expired" : "Stopped";
            Color statusColor = Running ? Theme.Success : Launching ? Theme.Accent :
                                Locked ? Theme.Warn : Theme.TextFaint;

            // The pill is sized from the measured label so the word is never ellipsised.
            float dot = Ui.S(7);
            float statusTextW = Ui.Measure(g, status, Theme.Micro).Width;
            float pillW = Ui.S(10) + dot + Ui.S(6) + statusTextW + Ui.S(10);
            float pillH = Ui.S(20);
            var pill = new RectangleF(nameRight - pillW, Ui.S(20), pillW, pillH);
            nameRight = pill.Left - Ui.S(10);

            string name = string.IsNullOrWhiteSpace(Model.Name) ? "Untitled profile" : Model.Name;
            float avail = Math.Max(Ui.S(40), nameRight - tx);
            /* A name longer than the header is ellipsised into a tooltip rather than vanished
               into it: the card says "it is called something longer", and hovering says what. */
            if (!_nameTip && Model != null && Ui.Measure(g, name, Theme.H2).Width > avail + Ui.S(2))
            {
                _nameTip = true;
                ToolTips.Attach(this, name);
            }
            Ui.Text(g, name, Theme.H2, Theme.Text, tx, Ui.S(18), avail);

            string sub = HeaderSub();
            Ui.Text(g, sub, Theme.Caption, Theme.TextFaint, tx, Ui.S(18) + Theme.LineHeight(Theme.H2) + Ui.S(4), avail);

            Ui.Fill(g, pill, Ui.S(Theme.ButtonRadius), Theme.Panel2);
            using (var b = new SolidBrush(statusColor))
                g.FillEllipse(b, pill.X + Ui.S(10), pill.Y + (pillH - dot) / 2f, dot, dot);
            Ui.Text(g, status, Theme.Micro, statusColor, pill.X + Ui.S(10) + dot + Ui.S(6),
                pill.Y + (pillH - Theme.Micro.GetHeight(g)) / 2f, statusTextW + Ui.S(2));
        }

        private string HeaderSub()
        {
            string kind = BrowserMark.LabelFor(BrowserMark.KindFor(Model));
            string group = string.IsNullOrWhiteSpace(Model.Group) ? "" : Model.Group.Trim();
            /* A profile that wipes itself on close says so on its face. It is a promise about the
               session, and a user who cannot see which profiles make it will not trust it. */
            string fresh = Model.FreshSession ? "  ·  fresh" : "";
            if (group.Length == 0) return kind + fresh;
            return kind + "  ·  " + group + fresh;
        }

        /// <summary>
        /// Device and exit-node chips. Both share one line when the card is wide enough; otherwise
        /// the proxy chip drops to the second line and the tags move down a row. Nothing truncates
        /// into an unreadable fragment and nothing overflows the card.
        /// </summary>
        private void PaintChips(Graphics g, out bool proxyOnSecondLine)
        {
            proxyOnSecondLine = false;
            float h = Ui.S(ChipH);
            float x = Ui.S(Pad);
            float right = Width - Ui.S(Pad);
            float lineA = Ui.S(84);
            float lineB = Ui.S(120);

            string os = Model.Fp != null ? (Model.Fp.Os ?? "") : "";
            if (string.IsNullOrWhiteSpace(os)) os = "Windows";
            string device = FingerprintFactory.DeviceIcon(os);
            string screen = Model.Fp != null && Model.Fp.ScreenW > 0
                ? Model.Fp.ScreenW + "×" + Model.Fp.ScreenH : "";

            var proxy = Model.Proxy;
            bool hasProxy = proxy != null && proxy.Enabled;
            string flag = null;
            string proxyText;
            if (hasProxy)
            {
                flag = !string.IsNullOrWhiteSpace(proxy.LastCountryCode) ? proxy.LastCountryCode : null;
                string place = !string.IsNullOrWhiteSpace(proxy.LastCity) ? proxy.LastCity
                    : !string.IsNullOrWhiteSpace(proxy.LastCountry) ? proxy.LastCountry : null;
                proxyText = proxy.Type.ToUpperInvariant() + (place != null ? " · " + place : " · " + proxy.Host);
            }
            else proxyText = "Direct";

            float afterDevice = DrawChip(g, x, lineA, h, right - x, device, os, screen, Theme.TextDim, null);
            float remaining = right - (afterDevice + Ui.S(8));

            /* Signature chip: two profiles with the same ten characters are a collision the user
               can see across the grid without opening either profile. Dropped rather than
               truncated when the card is narrow, because a partial hash proves nothing. */
            if (_sig.Length > 0)
            {
                float need = Ui.S(8) + ChipWidth(g, "fingerprint", _sig, null, null);
                if (need <= remaining)
                {
                    afterDevice = DrawChip(g, afterDevice + Ui.S(8), lineA, h, remaining,
                        "fingerprint", _sig, null, Theme.TextFaint, null);
                    remaining = right - (afterDevice + Ui.S(8));
                }
            }

            // Measure the proxy chip before committing to it, so a tight card wraps instead of clipping.
            float needed = ChipWidth(g, hasProxy ? "route" : "globe", proxyText, null, flag);
            if (needed <= remaining)
                DrawChip(g, afterDevice + Ui.S(8), lineA, h, remaining, hasProxy ? "route" : "globe",
                    proxyText, null, hasProxy ? Theme.Accent : Theme.TextFaint, flag);
            else
            {
                DrawChip(g, x, lineB, h, right - x, hasProxy ? "route" : "globe", proxyText, null,
                    hasProxy ? Theme.Accent : Theme.TextFaint, flag);
                proxyOnSecondLine = true;
            }
        }

        private float ChipWidth(Graphics g, string icon, string text, string trailing, string flagCode)
        {
            float padX = Ui.S(9);
            float ic = Ui.S(13);
            float flagW = flagCode != null ? Ui.S(15) + Ui.S(6) : 0f;
            float iconW = icon != null ? ic + Ui.S(6) : 0f;
            float trailW = string.IsNullOrEmpty(trailing) ? 0f
                : Ui.S(6) + Ui.Measure(g, trailing, Theme.Caption).Width;
            return padX * 2 + iconW + flagW + Ui.Measure(g, text ?? "", Theme.Caption).Width + trailW;
        }

        /// <summary>Draw one chip into at most <paramref name="maxRight"/> px and return its right edge.</summary>
        private float DrawChip(Graphics g, float x, float y, float h, float maxW, string icon,
            string text, string trailing, Color tint, string flagCode)
        {
            if (maxW < Ui.S(48)) return x;
            float padX = Ui.S(9);
            float ic = Ui.S(13);
            float inner = x + padX;

            float flagW = flagCode != null ? Ui.S(15) + Ui.S(6) : 0f;
            float iconW = icon != null ? ic + Ui.S(6) : 0f;
            float trailW = string.IsNullOrEmpty(trailing) ? 0f
                : Ui.S(6) + Ui.Measure(g, trailing, Theme.Caption).Width;
            float textBudget = maxW - padX * 2 - iconW - flagW - trailW;
            string shown = Ui.Clip(g, text ?? "", Theme.Caption, Math.Max(Ui.S(20), textBudget));
            float textW = Ui.Measure(g, shown, Theme.Caption).Width;

            float w = Math.Min(maxW, padX * 2 + iconW + flagW + textW + trailW);
            var rect = new RectangleF(x, y, w, h);
            Ui.Fill(g, rect, Ui.S(Theme.ButtonRadius), Theme.Panel2);

            float cy = y + (h - Theme.Caption.GetHeight(g)) / 2f;
            if (flagCode != null)
            {
                float fs = Ui.S(15);
                Flags.Draw(g, flagCode, new RectangleF(inner, y + (h - fs) / 2f, fs, fs));
                inner += fs + Ui.S(6);
            }
            if (icon != null)
            {
                Icons.Draw(g, icon, new RectangleF(inner, y + (h - ic) / 2f, ic, ic), tint);
                inner += ic + Ui.S(6);
            }
            Ui.Text(g, shown, Theme.Caption, Theme.TextDim, inner, cy, textW + Ui.S(2));
            if (!string.IsNullOrEmpty(trailing))
                Ui.Text(g, trailing, Theme.Caption, Theme.TextFaint,
                    inner + textW + Ui.S(6), cy, maxW - (inner + textW + Ui.S(6) - x) - padX);
            return x + w;
        }

        private void PaintTags(Graphics g, int shift)
        {
            var tags = SplitTags(Model.Tags);
            if (tags.Count == 0 && string.IsNullOrWhiteSpace(Model.Status))
            {
                PaintMeta(g, shift);
                return;
            }

            float y = Ui.S(120) + shift;
            float h = Ui.S(TagH);
            float x = Ui.S(Pad);
            float right = Width - Ui.S(Pad);

            if (!string.IsNullOrWhiteSpace(Model.Status))
            {
                string s = Model.Status.Trim();
                float w = Ui.S(9) * 2 + Ui.Measure(g, s, Theme.Micro).Width;
                w = Math.Min(w, right - x);
                var rect = new RectangleF(x, y, w, h);
                Ui.Fill(g, rect, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
                Ui.Text(g, s, Theme.Micro, Theme.AccentText, rect.X + Ui.S(9),
                    rect.Y + (h - Theme.Micro.GetHeight(g)) / 2f, w - Ui.S(18));
                x = rect.Right + Ui.S(6);
            }

            int shown = 0;
            foreach (string t in tags)
            {
                float w = Ui.S(8) * 2 + Ui.Measure(g, t, Theme.Micro).Width;
                if (x + w > right) break;
                var rect = new RectangleF(x, y, w, h);
                Ui.Fill(g, rect, Ui.S(Theme.ButtonRadius), Theme.Panel2);
                Ui.Text(g, t, Theme.Micro, Theme.TextDim, rect.X + Ui.S(8),
                    rect.Y + (h - Theme.Micro.GetHeight(g)) / 2f, w - Ui.S(16));
                x = rect.Right + Ui.S(6);
                shown++;
            }
            int hidden = tags.Count - shown;
            if (hidden > 0 && x + Ui.S(30) <= right)
            {
                var rect = new RectangleF(x, y, Ui.S(30), h);
                Ui.Text(g, "+" + hidden, Theme.Micro, Theme.TextFaint, rect.X,
                    rect.Y + (h - Theme.Micro.GetHeight(g)) / 2f, rect.Width);
            }
        }

        /// <summary>
        /// A profile with no tags and no status would otherwise leave the meta row empty, which
        /// left a hole between the chips and the action bar on most cards. The row is filled with
        /// the two fingerprint facts the card does not show anywhere else - timezone and
        /// language - so every card keeps the same height and the row still carries information.
        /// </summary>
        private void PaintMeta(Graphics g, int shift)
        {
            if (Model.Fp == null) return;
            float y = Ui.S(120) + shift;
            float h = Ui.S(TagH);
            float x = Ui.S(Pad);
            float right = Width - Ui.S(Pad);

            string tz = (Model.Fp.Timezone ?? "").Trim();
            if (tz.Length > 0)
                x = DrawMetaChip(g, x, y, h, right, "clock", tz);

            string lang = FingerprintFactory.LanguageName(Model.Fp.Language);
            if (!string.IsNullOrWhiteSpace(lang) && x + Ui.S(60) <= right)
                DrawMetaChip(g, x + Ui.S(6), y, h, right, "language", lang.Trim());
        }

        /// <summary>One low-emphasis chip; returns the right edge it consumed.</summary>
        private float DrawMetaChip(Graphics g, float x, float y, float h, float right,
            string icon, string text)
        {
            float padX = Ui.S(8);
            float ic = Ui.S(12);
            float gap = Ui.S(5);
            float budget = Math.Max(Ui.S(24), right - x - padX * 2 - ic - gap);
            string shown = Ui.Clip(g, text, Theme.Micro, budget);
            float tw = Ui.Measure(g, shown, Theme.Micro).Width;
            float w = Math.Min(right - x, padX * 2 + ic + gap + tw);

            var rect = new RectangleF(x, y, w, h);
            Ui.Fill(g, rect, Ui.S(Theme.ButtonRadius), Theme.Panel2);
            Icons.Draw(g, icon, new RectangleF(x + padX, y + (h - ic) / 2f, ic, ic), Theme.TextFaint);
            Ui.Text(g, shown, Theme.Micro, Theme.TextDim, x + padX + ic + gap,
                y + (h - Theme.Micro.GetHeight(g)) / 2f, tw + Ui.S(2));
            return rect.Right;
        }

        private static List<string> SplitTags(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            foreach (string part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                if (!t.StartsWith("#", StringComparison.Ordinal)) t = "#" + t;
                list.Add(t);
            }
            return list;
        }

        private void PaintActions(Graphics g)
        {
            using (var p = new Pen(Theme.LineSoft))
            {
                float dy = Height - Ui.S(14) - Ui.S(BtnH) - Ui.S(10);
                g.DrawLine(p, Ui.S(Pad), dy, Width - Ui.S(Pad), dy);
            }

            /* A pressed card's action is a button outline; the same action on a stopped card is
               the filled primary. That contrast is the single clearest signal of what clicking
               the card will do, and it costs no text. */
            var or = OpenRect;
            bool enabled = !Launching;
            float rad = Ui.S(Theme.ButtonRadius);
            if (Running || Locked) Ui.Fill(g, or, rad, _openHover ? Theme.PanelHover : Theme.Panel2);
            else Ui.Fill(g, or, rad, Launching ? Theme.Panel2 : _openHover ? Theme.AccentHi : Theme.Accent);
            if (!Running && !Locked && !Launching && _openHover) { /* accent fill already brightened */ }
            if (Running || Locked) Ui.Stroke(g, or, rad, _openHover ? Theme.HoverStrong : Theme.ButtonBorder);

            string openLabel = Launching ? "Starting" : Running ? "Focus window" :
                               Locked ? "Renew to open" : "Open profile";
            Color openFg = Locked || Launching ? Theme.TextFaint
                : Running ? Theme.Text : Theme.OnAccent;
            float ic = Ui.S(13);
            float ow = Ui.Measure(g, openLabel, Theme.SmallBold).Width;
            float content = (enabled ? ic + Ui.S(6) : 0) + ow;
            float ox = or.X + (or.Width - content) / 2f;
            if (enabled)
            {
                Icons.Draw(g, Running ? "window" : Locked ? "lock" : "play",
                    new RectangleF(ox, or.Y + (or.Height - ic) / 2f, ic, ic), openFg);
                ox += ic + Ui.S(6);
            }
            Ui.Text(g, openLabel, Theme.SmallBold, openFg, ox,
                or.Y + (or.Height - Theme.SmallBold.GetHeight(g)) / 2f);

            if (_kebabHover)
            {
                Ui.Fill(g, KebabRect, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay);
                Ui.Stroke(g, KebabRect, Ui.S(Theme.ButtonRadius), Theme.Line);
            }
            float kd = Ui.S(16);
            Icons.Draw(g, "dots", new RectangleF(KebabRect.X + (KebabRect.Width - kd) / 2f,
                KebabRect.Y + (KebabRect.Height - kd) / 2f, kd, kd), _kebabHover ? Theme.Text : Theme.TextDim);
        }
    }
}
