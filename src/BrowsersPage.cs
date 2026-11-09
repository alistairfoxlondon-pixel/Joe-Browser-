using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal sealed class BrowserActionEventArgs : EventArgs
    {
        public readonly string Key;
        public readonly string Engine;
        public BrowserActionEventArgs(string key, string engine) { Key = key; Engine = engine; }
    }

    /// <summary>
    /// Everything about the five engines in one place: what is on this PC, which version,
    /// where it lives, and a Download button that fetches the official installer without
    /// leaving the app. Reached from the sidebar so a user never has to hunt through Settings.
    /// </summary>
    internal sealed class BrowsersPage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly List<EngineRow> _rows = new List<EngineRow>();
        private readonly List<EngineInfo> _engines = new List<EngineInfo>();
        private JBButton _rescan, _openFolder, _kernels;
        private JBLabel _section, _note;
        private bool _layingOut;
        private bool _ready;

        public event EventHandler<BrowserActionEventArgs> ActionRequested;

        public BrowsersPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            /* No page title here: the header band above already names the page and says what it
               is for. Repeating it inside the scroll area was the single most common piece of
               duplicated text across the app. */
            _rescan = MakeButton("Re-scan", "refresh", BtnKind.Soft, "rescan");
            _openFolder = MakeButton("Open browsers folder", "folder", BtnKind.Soft, "openfolder");
            /* Kernels are the pinned builds a profile can be locked to - the same idea as the
               version picker in an anti-detect browser, and the only way to keep a profile on
               one exact version after the everyday browser updates. */
            _kernels = MakeButton("Kernels", "layers", BtnKind.Primary, "kernels");
            _column.Controls.Add(_rescan);
            _column.Controls.Add(_openFolder);
            _column.Controls.Add(_kernels);

            _section = new JBLabel(LabelRole.Accent, "SUPPORTED ENGINES");
            _column.Controls.Add(_section);

            foreach (var e in EngineCatalog())
            {
                var row = new EngineRow(e);
                row.ActionRequested += delegate(object s, EventArgs ev)
                {
                    var r = (EngineRow)s;
                    // The button says what it does; the action has to match the label.
                    if (!r.Info.Checked) Rescan();
                    else Raise(r.Info.Installed ? "reveal" : "install", r.Info.Key);
                };
                _column.Controls.Add(row);
                _rows.Add(row);
                _engines.Add(e);
            }

            _note = new JBLabel(LabelRole.Faint,
                "Chromium installs into this app's folder. The others use their official installer.");
            _note.Wrap = true;
            _column.Controls.Add(_note);

            _ready = true;
            Rescan();
        }

        private JBButton MakeButton(string text, string icon, BtnKind kind, string key)
        {
            var b = new JBButton { Kind = kind, Icon = icon, Text = text, Radius = Theme.ButtonRadius, Height = Theme.Field };
            b.Click += delegate { Raise(key, null); };
            return b;
        }

        private void Raise(string key, string engine)
        {
            var h = ActionRequested;
            if (h != null) h(this, new BrowserActionEventArgs(key, engine));
        }

        /// <summary>Re-reads the disk. Cheap enough to run on every visit to the page.</summary>
        public void Rescan()
        {
            bool ok = true;
            List<BrowserInfo> found;
            try { found = BrowserLocator.FindAll(); }
            catch (Exception ex)
            {
                /* Detection runs on whatever Windows will let us read. When it throws, every
                   engine used to end up "Not installed" - the app then offered to download a
                   browser the user already had. Say "could not check" instead. */
                Log.Error("Browser detection failed", ex);
                found = new List<BrowserInfo>();
                ok = false;
            }
            foreach (var e in _engines)
            {
                e.Found = FindInstalled(found, e.Key);
                e.Checked = ok || e.Found != null;
            }
            foreach (var r in _rows) { r.Apply(); r.Invalidate(); }
            LayoutCore();
        }

        /// <summary>The install of <paramref name="key"/> this PC actually has, or null.</summary>
        public static BrowserInfo FindInstalled(string key)
        {
            return FindInstalled(BrowserLocator.FindAll(), key);
        }

        private static BrowserInfo FindInstalled(List<BrowserInfo> all, string key)
        {
            foreach (var b in all)
            {
                if (key == "chrome" && b.Name == "Chrome" && !b.IsEdge && !b.IsBrave && !b.IsFirefox && !IsPortable(b)) return b;
                if (key == "edge" && b.IsEdge) return b;
                if (key == "brave" && b.IsBrave) return b;
                if (key == "firefox" && b.IsFirefox) return b;
                if (key == "chromium" && !b.IsEdge && !b.IsBrave && !b.IsFirefox &&
                    (b.Name != "Chrome" || IsPortable(b))) return b;
            }
            return null;
        }

        private static bool IsPortable(BrowserInfo b)
        {
            return b.Path != null && b.Path.StartsWith(AppPaths.Browsers + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The engines Joe Browser supports, in the order a new user meets them.</summary>
        public static List<EngineInfo> EngineCatalog()
        {
            return new List<EngineInfo>
            {
                new EngineInfo("chrome", "Google Chrome",
                    "Stable build. Best extension support.", true),
                new EngineInfo("chromium", "Chromium",
                    "Open-source build, installed here. No admin rights.", true),
                new EngineInfo("brave", "Brave",
                    "Chromium with ad blocking. Store extensions work.", true),
                new EngineInfo("firefox", "Mozilla Firefox",
                    "Gecko engine. No Chrome extensions.", true),
                new EngineInfo("edge", "Microsoft Edge",
                    "Included with Windows.", false)
            };
        }

        public void SetBusy(string engine, bool busy, string text)
        {
            foreach (var r in _rows)
            {
                if (engine == null || r.Info.Key == engine)
                {
                    r.Busy = busy;
                    r.BusyText = text;
                    r.Apply();
                    r.Invalidate();
                }
            }
            _rescan.Enabled = !busy;
            _rescan.Invalidate();
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            foreach (var r in _rows) r.ReStyle();
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
                int pad = Ui.S(28);
                int inner = Math.Max(Ui.S(300), _host.Width - Ui.S(10) - pad * 2);

                /* Shared flow: the three wrap on a narrow window instead of the last one
                   running off the right edge of the page. */
                int by = Ui.S(6);
                JBButton[] actions = { _rescan, _openFolder, _kernels };
                int y = Ui.FlowButtons(actions, pad, by, inner, Ui.S(10), Ui.S(Theme.ControlHeight)) + Ui.S(24);
                _section.SetBounds(pad, y, inner, Ui.S(18));
                y += Ui.S(18) + Ui.S(12);

                foreach (var r in _rows)
                {
                    r.SetBounds(pad, y, inner, EngineRow.RowH);
                    y += Ui.S(84) + Ui.S(10);
                }

                y += Ui.S(10);
                _note.SetBounds(pad, y, Math.Min(inner, Ui.S(820)), Ui.S(24));
                y += Ui.S(24) + Ui.S(24);

                _column.SetBounds(0, 0, Math.Max(inner + pad * 2, _host.Width - Ui.S(10)),
                    Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }
    }

    /// <summary>One supported engine and what this PC has of it.</summary>
    internal sealed class EngineInfo
    {
        public readonly string Key, Name, Desc;
        public readonly bool CanDownload;
        public BrowserInfo Found;

        public EngineInfo(string key, string name, string desc, bool canDownload)
        {
            Key = key; Name = name; Desc = desc; CanDownload = canDownload;
        }

        public bool Installed { get { return Found != null && Found.Found; } }
        public string Version { get { return Found == null ? "" : Found.Version ?? ""; } }

        /// <summary>
        /// False when this engine's detection could not run at all, as opposed to having run and
        /// found nothing. The row must not say "Not installed" for the former: it is a claim
        /// about the user's PC that a failed probe cannot support.
        /// </summary>
        public bool Checked = true;
    }

    /// <summary>A single engine card: mark, name, status line and one contextual action.</summary>
    internal sealed class EngineRow : Control, IReStyle
    {
        public readonly EngineInfo Info;
        public bool Busy;
        public string BusyText = "Installing\u2026";
        public event EventHandler ActionRequested;

        private readonly JBButton _action;
        private bool _hover;

        public EngineRow(EngineInfo info)
        {
            Info = info;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
            _action = new JBButton { Radius = Theme.ButtonRadius, Height = Ui.S(32) };
            _action.Click += delegate { var h = ActionRequested; if (h != null) h(this, EventArgs.Empty); };
            Controls.Add(_action);
            Apply();
            /* Height last: setting it raises OnResize, which positions the button. */
            Height = Ui.S(84);
        }

        public void Apply()
        {
            if (Busy)
            {
                _action.Kind = BtnKind.Soft;
                _action.Icon = "download";
                _action.Text = "Working\u2026";
                _action.Enabled = false;
                return;
            }
            if (Info.Installed)
            {
                _action.Kind = BtnKind.Soft;
                _action.Icon = "folder";
                _action.Text = "Show file";
            }
            else if (!Info.Checked)
            {
                /* Unknown, not missing: the honest action is to look again. */
                _action.Kind = BtnKind.Soft;
                _action.Icon = "refresh";
                _action.Text = "Re-scan";
            }
            else if (Info.CanDownload)
            {
                _action.Kind = BtnKind.Primary;
                _action.Icon = "download";
                _action.Text = "Download";
            }
            else
            {
                _action.Kind = BtnKind.Soft;
                _action.Icon = "external";
                _action.Text = "Get it";
            }
            _action.Enabled = !Busy;
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        /// <summary>A mark, a title, a status line and a description, with the same padding above and below.</summary>
        public static int RowH
        {
            get
            {
                int text = Theme.LineHeight(Theme.H3) + Ui.S(4) + Theme.LineHeight(Theme.Caption) * 2;
                return Math.Max(Ui.S(84), Math.Max(text + Ui.S(24), Ui.S(44) + Ui.S(28)));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_action == null) return;
            /* The button is kept inside the row: a long translation makes a preferred width
               wider than the row, and the old placement then put its left edge at 8 px while its
               right edge stayed off the card. */
            int bh = Math.Min(Theme.Field, Math.Max(Ui.S(28), Height - Ui.S(16)));
            int w = Math.Min(Math.Max(Ui.S(96), Width - Ui.S(120)), _action.PreferredWidth(Ui.S(132)));
            _action.SetBounds(Math.Max(Ui.S(8), Width - Ui.S(16) - w), (Height - bh) / 2, w, bh);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), _hover ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), _hover ? Theme.AccentBorder : Theme.Line, 1f);

            float d = Ui.S(44);
            var mark = new RectangleF(Ui.S(18), (Height - d) / 2f, d, d);
            BrowserMark.Draw(g, mark, Info.Key);

            float x = mark.Right + Ui.S(16);
            float rightEdge = _action.Left - Ui.S(14);
            float tw = Math.Max(Ui.S(120), rightEdge - x);

            /* Three lines, measured and centred as a block. They used to be pinned to the centre
               with fixed offsets of 24, 6 and 12, which only lines up for one font size: at 150%
               the description sat on the status line. */
            float titleH = Theme.LineHeight(Theme.H3);
            float capH = Theme.LineHeight(Theme.Caption);
            float blockH = titleH + Ui.S(2) + capH + Ui.S(2) + capH;
            float top = Math.Max(Ui.S(8), (Height - blockH) / 2f);

            Ui.Text(g, Info.Name, Theme.H3, Theme.Text, x, top, tw);

            // status line: a coloured dot, then "Installed · version" or "Not installed"
            float dot = Ui.S(7);
            float statusY = top + titleH + Ui.S(2);
            var dr = new RectangleF(x, statusY + (capH - dot) / 2f, dot, dot);
            Color dotColor = Busy ? Theme.Accent : Info.Installed ? Theme.Success : Theme.TextFaint;
            using (var b = new SolidBrush(dotColor)) g.FillEllipse(b, dr);

            /* "Not installed" is a claim about the user's PC. When the detector itself could not
               run, saying so is the difference between "install a browser" and "this app is
               broken" - and it stops a Download from being offered for something already there. */
            string status = Busy
                ? (string.IsNullOrEmpty(BusyText) ? "Working\u2026" : BusyText)
                : Info.Installed
                    ? (string.IsNullOrEmpty(Info.Version) ? "Installed" : "Installed  \u00b7  " + ShortVersion(Info.Version))
                    : !Info.Checked ? "Could not check, re-scan" : "Not installed";
            /* Theme has no warning colour of its own, so the amber the toasts use is mixed in
               place; a plain faint grey would read as another "not installed". */
            Color statusInk = Busy ? Theme.Accent
                : Info.Installed || Info.Checked ? Theme.TextDim
                : Ui.Blend(Theme.TextDim, Color.FromArgb(0xF2, 0xA8, 0x1B), 0.75f);
            Ui.Text(g, status, Theme.Caption, statusInk, dr.Right + Ui.S(8), statusY, tw - dot - Ui.S(8));

            Ui.Text(g, Info.Desc, Theme.Caption, Theme.TextFaint, x, statusY + capH + Ui.S(2), tw, "left", 1);
        }

        /// <summary>Chrome reports a four-part product version; two parts is plenty on one line.</summary>
        private static string ShortVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            string[] bits = v.Split('.');
            if (bits.Length >= 3) return "v" + bits[0] + "." + bits[1] + "." + bits[2];
            return "v" + v;
        }
    }
}
