using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>Result of the in-session fingerprint check: one line per question asked.</summary>
    internal sealed class LeakForm : JBForm
    {
        private readonly LeakResult _result;
        private readonly string _profileName;
        private readonly JBScrollHost _host;
        private readonly Panel _column = new Panel();
        private readonly JBButton _close;
        private readonly JBButton _browserScan, _ipFighter, _report;
        private readonly BrowserSession _session;
        private readonly JBIconButton _x;
        private readonly List<LeakRow> _rows = new List<LeakRow>();
        private bool _built;

        public LeakForm(LeakResult result, string profileName) : this(result, profileName, null) { }

        public LeakForm(LeakResult result, string profileName, BrowserSession session)
        {
            _result = result ?? new LeakResult();
            _profileName = profileName ?? "";
            _session = session;
            Text = "Fingerprint check";
            Resizable = true;
            DraggableSidebar = false;
            HeaderHit = 76;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            MinimumSize = new Size(Ui.S(480), Ui.S(360));
            Size = Ui.FitToScreen(this, Ui.S(620), Ui.S(520));

            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            foreach (var item in _result.Items)
            {
                var row = new LeakRow(item);
                _rows.Add(row);
                _column.Controls.Add(row);
            }

            _close = new JBButton { Kind = BtnKind.Primary, Icon = "check", Text = "Done", Radius = 6, Height = Theme.Field };
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            /* The two pages people actually use to judge a profile. Opening them inside this
               session is the only way the result describes this profile and not the desktop. */
            _browserScan = new JBButton { Kind = BtnKind.Soft, Icon = "external", Text = "browserscan.net", Radius = 6, Height = Theme.Field };
            _browserScan.Click += delegate { Open("https://www.browserscan.net/"); };
            Controls.Add(_browserScan);
            _ipFighter = new JBButton { Kind = BtnKind.Soft, Icon = "external", Text = "ipfighter.com", Radius = 6, Height = Theme.Field };
            _ipFighter.Click += delegate { Open("https://ipfighter.com/"); };
            Controls.Add(_ipFighter);
            bool canOpen = session != null && session.IsRunning;
            _browserScan.Visible = canOpen;
            _ipFighter.Visible = canOpen;

            /* A failed check is exactly when a user wants a human: one button, the report is
               pre-filled with the failed lines so the message needs no technical wording. */
            _report = new JBButton { Kind = BtnKind.Soft, Icon = "error", Text = "Report", Radius = 6, Height = Theme.Field, Tip = "Tell the developer on Telegram" };
            _report.Click += delegate
            {
                var bad = new List<string>();
                foreach (var i in _result.Items) if (!i.Ok && !i.Skipped) bad.Add(i.Name + ": " + (i.Detail ?? ""));
                Report.OpenForm(this, string.Join(Environment.NewLine, bad.ToArray()));
            };
            Controls.Add(_report);

            _x = new JBIconButton { Icon = "close", Danger = true, IconSize = 13, Tip = "Close" };
            _x.Click += delegate { Close(); };
            Controls.Add(_x);

            _built = true;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterToScreen();
            LayoutCore();
        }

        /// <summary>
        /// Esc closes. This is a report, not a form: the only reason it exists is to be read, and
        /// every other window in the app dismisses on Esc. Missing here it was the one dialog the
        /// keyboard could not leave.
        /// </summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCore();
        }

        private void LayoutCore()
        {
            if (!_built) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(24);
            _x.SetBounds(w - pad - Ui.S(30), Ui.S(14), Ui.S(30), Ui.S(30));

            int top = Ui.S(96);
            _host.SetBounds(pad, top, w - pad * 2, Math.Max(Ui.S(100), h - top - Ui.S(56)));
            int cw = Math.Max(Ui.S(200), _host.Width - Ui.S(10));
            int y = 0;
            foreach (var r in _rows)
            {
                r.SetBounds(0, y, cw, Ui.S(48));
                y += Ui.S(48) + Ui.S(6);
            }
            _column.SetBounds(0, 0, Math.Max(cw, _host.Width - Ui.S(10)), Math.Max(y, _host.Height));
            _host.SyncExtent();

            int bw = _close.PreferredWidth(Ui.S(96));
            _close.SetBounds(w - pad - bw, h - Ui.S(16) - Ui.S(33), bw, Ui.S(33));

            /* The footer buttons used to be laid out from the left with no regard for Close on
               the right, so on a narrow window the report button was drawn under it. They flow
               now and wrap, and the measured widths of all four are taken into account. */
            JBButton[] footer = _browserScan.Visible
                ? new[] { _browserScan, _ipFighter, _report }
                : new[] { _report };
            int avail = Math.Max(Ui.S(120), _close.Left - Ui.S(8) - pad);
            Ui.FlowButtons(footer, pad, h - Ui.S(16) - Ui.S(33), avail, Ui.S(8), Ui.S(33));
        }

        private void Open(string url)
        {
            if (_session == null || !_session.IsRunning) return;
            var ignored = _session.NavigateAsync(url);
            _session.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Quality(g);
            int pad = Ui.S(24);
            int w = ClientSize.Width;

            Ui.Text(g, "Fingerprint check", Theme.H2, Theme.Text, pad, Ui.S(14), w - pad * 2 - Ui.S(40));
            string sub = string.IsNullOrEmpty(_profileName) ? "" : _profileName + "  \u00b7  ";
            sub += string.IsNullOrEmpty(_result.Error) ? _result.Verdict : _result.Error;
            Ui.Text(g, sub, Theme.Small, _result.Clean ? Theme.Success : Theme.TextDim, pad, Ui.S(44),
                w - pad * 2 - Ui.S(40));

            if (_result.Total > 0)
            {
                int scoreW = Ui.S(96);
                var bar = new RectangleF(w - pad - scoreW, Ui.S(46), scoreW, Ui.S(6));
                Ui.Fill(g, bar, Ui.S(3), Theme.Panel2);
                float frac = _result.Score / 100f;
                Ui.Fill(g, new RectangleF(bar.X, bar.Y, bar.Width * frac, bar.Height), Ui.S(3),
                    _result.Clean ? Theme.Success : Theme.Accent);
                Ui.Text(g, _result.Score + "%", Theme.Caption, Theme.TextDim, w - pad - scoreW,
                    Ui.S(56), scoreW, "right");
            }

            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, pad, Ui.S(88), w - pad, Ui.S(88));
        }
    }

    internal sealed class LeakRow : Control, IReStyle
    {
        private readonly LeakItem _item;

        public LeakRow(LeakItem item)
        {
            _item = item;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(10), Theme.Card);
            Ui.Stroke(g, r, Ui.S(10), Theme.Line);

            float ic = Ui.S(15);
            float cy = Height / 2f;
            string icon = _item.Skipped ? "info" : _item.Ok ? "check-circle" : "alert";
            Color tint = _item.Skipped ? Theme.TextFaint : _item.Ok ? Theme.Success : Theme.Accent;
            Icons.Draw(g, icon, new RectangleF(Ui.S(14), cy - ic / 2f, ic, ic), tint);

            float x = Ui.S(14) + ic + Ui.S(10);
            Ui.Text(g, _item.Name, Theme.SmallBold, Theme.Text, x, cy - Ui.S(19),
                Math.Max(Ui.S(80), Width - x - Ui.S(14)));
            Ui.Text(g, _item.Detail ?? "", Theme.Caption, Theme.TextDim, x, cy - Ui.S(1),
                Math.Max(Ui.S(80), Width - x - Ui.S(14)));
        }
    }
}
