using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Network diagnostics: what the outside world sees for this machine or for a saved proxy.
    /// Three lookup services are compared, the resolver is read back, and the clock is checked
    /// against the internet. Anything that would show up on a leak test is called out in words.
    /// </summary>
    internal sealed class NetworkForm : JBForm
    {
        private sealed class Line
        {
            public JBLabel Name;
            public JBLabel Value;
        }

        private readonly List<ProxyEntry> _proxies;
        private readonly JBSelect _target;
        private readonly List<string> _keys = new List<string>();
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<JBLabel> _warnings = new List<JBLabel>();
        private readonly JBButton _run, _close;
        private readonly JBIconButton _x;
        private readonly JBLabel _empty;
        private IpReport _report;
        private bool _busy;
        private bool _built;

        private static readonly string[] Fields =
        {
            "Exit IP", "Country", "City", "Timezone", "Provider", "DNS resolver", "Latency", "Clock", "Lookups"
        };

        public NetworkForm(List<ProxyEntry> proxies)
        {
            _proxies = proxies ?? new List<ProxyEntry>();
            Text = "Network diagnostics";
            Resizable = true;
            DraggableSidebar = false;
            HeaderHit = 96;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            MinimumSize = new Size(Ui.S(520), Ui.S(500));
            // Nine label/value pairs plus the footer have to fit without scrolling on a normal
            // screen; 9 x 46 px of rows + 112 px of header + 60 px of footer = 586 px.
            Size = Ui.FitToScreen(this, Ui.S(660), Ui.S(632));

            _target = new JBSelect { Icon = "globe" };
            var labels = new List<string> { "This PC (direct)" };
            _keys.Add("");
            foreach (var p in _proxies)
            {
                labels.Add(string.IsNullOrWhiteSpace(p.Name) ? p.Connection : p.Name);
                _keys.Add(p.Id);
            }
            _target.SetItems(labels, 0);
            Controls.Add(_target);

            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            foreach (string f in Fields)
            {
                var line = new Line
                {
                    Name = new JBLabel(LabelRole.Accent, f.ToUpperInvariant()),
                    Value = new JBLabel(LabelRole.Strong, "\u2014")
                };
                line.Value.AutoEllipsis = true;
                _column.Controls.Add(line.Name);
                _column.Controls.Add(line.Value);
                _lines.Add(line);
            }

            _empty = new JBLabel(LabelRole.Faint, "Press Check to read this connection.");
            _empty.Wrap = true;
            _column.Controls.Add(_empty);

            _run = new JBButton { Kind = BtnKind.Primary, Icon = "wifi", Text = "Check", Radius = 4, Height = Ui.S(33) };
            _run.Click += delegate { RunCheck(); };
            Controls.Add(_run);

            _close = new JBButton { Kind = BtnKind.Ghost, Text = "Close", Radius = 4, Height = Ui.S(33) };
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

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
            RunCheck();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCore();
        }

        private ProxyConfig SelectedProxy()
        {
            int i = _target.SelectedIndex;
            if (i <= 0 || i >= _keys.Count) return null;
            string id = _keys[i];
            foreach (var p in _proxies) if (p.Id == id) return p.Config;
            return null;
        }

        private async System.Threading.Tasks.Task RunCheckCore()
        {
            if (_busy) return;
            _busy = true;
            _run.Enabled = false;
            _empty.Visible = true;
            _empty.Text = "Checking the connection\u2026";
            SetValue(0, "\u2014");
            LayoutCore();
            try
            {
                var cfg = SelectedProxy();
                _report = await NetCheck.CheckAsync(cfg).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _report = new IpReport { Error = ex.Message };
            }
            if (IsDisposed) return;
            _busy = false;
            _run.Enabled = true;
            Apply();
            LayoutCore();
            Invalidate();
        }

        /// <summary>Guarded entry point so a failed await never reaches the crash dialog.</summary>
        private void RunCheck() { Guarded.Run("RunCheck", this, delegate { return RunCheckCore(); }); }

        private void Apply()
        {
            var r = _report;
            _empty.Visible = r == null || !r.Ok;
            if (r == null) return;
            if (!r.Ok)
            {
                _empty.Text = string.IsNullOrEmpty(r.Error) ? "The check did not complete." : r.Error;
                for (int i = 0; i < _lines.Count; i++) SetValue(i, "\u2014");
                return;
            }
            SetValue(0, r.Ip);
            SetValue(1, string.IsNullOrEmpty(r.Country) ? r.CountryCode : r.Country);
            SetValue(2, string.IsNullOrEmpty(r.City) ? r.Region : r.City + (string.IsNullOrEmpty(r.Region) ? "" : ", " + r.Region));
            SetValue(3, r.Timezone);
            SetValue(4, r.Isp);
            SetValue(5, string.IsNullOrEmpty(r.ResolverIp) ? "\u2014" : r.ResolverIp + (string.IsNullOrEmpty(r.ResolverGeo) ? "" : "  \u00b7  " + r.ResolverGeo));
            SetValue(6, r.LatencyMs + " ms");
            SetValue(7, Math.Abs(r.ClockSkewSeconds) <= 2 ? "In sync" : (r.ClockSkewSeconds > 0 ? "+" : "") + r.ClockSkewSeconds + " s off");
            SetValue(8, r.Sources.Count == 0 ? "\u2014" : string.Join(", ", r.Sources.ToArray()));

            foreach (var w in _warnings) { _column.Controls.Remove(w); w.Dispose(); }
            _warnings.Clear();
            foreach (string text in r.Warnings)
            {
                var l = new JBLabel(LabelRole.Faint, text);
                l.Wrap = true;
                l.TextAlign = ContentAlignment.TopLeft;
                _column.Controls.Add(l);
                _warnings.Add(l);
            }
        }

        private void SetValue(int i, string text)
        {
            if (i < 0 || i >= _lines.Count) return;
            _lines[i].Value.Text = text ?? "";
        }

        private void LayoutCore()
        {
            if (!_built) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(24);
            _x.SetBounds(w - pad - Ui.S(30), Ui.S(14), Ui.S(30), Ui.S(30));
            _target.SetBounds(pad, Ui.S(64), Math.Min(Ui.S(320), w - pad * 2), Ui.S(33));

            int top = Ui.S(112);
            int footer = Ui.S(56);
            _host.SetBounds(pad, top, w - pad * 2, Math.Max(Ui.S(120), h - top - footer - Ui.S(8)));
            int cw = Math.Max(Ui.S(220), _host.Width - Ui.S(10));

            int y = Ui.S(2);
            foreach (var line in _lines)
            {
                line.Name.SetBounds(0, y, cw, Ui.S(18));
                y += Ui.S(18) + Ui.S(2);
                line.Value.SetBounds(0, y, cw, Ui.S(22));
                y += Ui.S(22) + Ui.S(8);
            }
            if (_empty.Visible)
            {
                _empty.SetBounds(0, y + Ui.S(4), cw, Ui.S(40));
                y += Ui.S(48);
            }
            foreach (var warn in _warnings)
            {
                int lines = EstimateLines(warn.Text, cw, Theme.Caption);
                int wh = Ui.S(18) * Math.Max(1, lines) + Ui.S(6);
                warn.SetBounds(0, y, cw, wh);
                y += wh + Ui.S(6);
            }

            _column.SetBounds(0, 0, Math.Max(cw, _host.Width - Ui.S(10)), Math.Max(y, _host.Height));
            _host.SyncExtent();

            int rw = _run.PreferredWidth(Ui.S(110));
            int cwid = _close.PreferredWidth(Ui.S(84));
            _run.SetBounds(pad, h - Ui.S(16) - Ui.S(33), rw, Ui.S(33));
            _close.SetBounds(pad + rw + Ui.S(8), h - Ui.S(16) - Ui.S(33), cwid, Ui.S(33));
        }

        private static int EstimateLines(string text, int width, Font font)
        {
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    var size = TextRenderer.MeasureText(g, text ?? "", font,
                        new Size(Math.Max(40, width), int.MaxValue), TextFormatFlags.WordBreak);
                    return Math.Max(1, (int)Math.Ceiling(size.Height / (double)Math.Max(1, font.GetHeight(g))));
                }
            }
            catch { return 2; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Quality(g);
            int pad = Ui.S(24);
            int w = ClientSize.Width;
            Ui.Text(g, "Network diagnostics", Theme.H2, Theme.Text, pad, Ui.S(14), w - pad * 2 - Ui.S(40));
            string sub = _report == null ? "Choose a connection and press Check"
                : (_report.Ok ? _report.Summary : _report.Error);
            Ui.Text(g, sub, Theme.Small, _report != null && _report.Ok ? Theme.TextDim : Theme.TextFaint,
                pad, Ui.S(42), w - pad * 2 - Ui.S(40));
        }
    }
}
