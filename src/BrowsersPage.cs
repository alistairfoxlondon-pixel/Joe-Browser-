using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    /// The browser manager.
    ///
    /// Everything about the engines is in one place, the way AdsPower and GoLogin keep it:
    /// which engine a build belongs to, which release channel it came from, which exact
    /// versions are on this machine, and which ones can be fetched. The page does three
    /// things and nothing else - it lists what is installed, it offers what can be
    /// installed, and it installs or removes a build without leaving the application.
    ///
    /// Nothing is downloaded when the application starts. A build is fetched the first
    /// time somebody asks for it: from this page, or from opening a profile that is
    /// pinned to a build this machine does not have yet.
    /// </summary>
    internal sealed class BrowsersPage : Panel, IReStyle, ISyncLayout
    {
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly JBSegment _engines, _channels;
        private readonly JBButton _refresh, _openFolder;
        private readonly List<Control> _blocks = new List<Control>();
        private bool _layingOut;
        private bool _ready;

        /// <summary>Engine keys, in the order they are offered.</summary>
        public static readonly string[] EngineKeys = { "chromium", "chrome", "brave", "firefox", "edge" };
        public static readonly string[] EngineLabels = { "Chromium", "Chrome", "Brave", "Firefox", "Edge" };

        /// <summary>Release channels, in the order they are offered.</summary>
        public static readonly string[] ChannelKeys = { "All", "Stable", "Beta", "Dev", "Canary", "ESR", "Nightly" };

        private string _engine = "chromium";
        private string _channel = "All";
        private int _token;

        /// <summary>True while a catalogue is being fetched, so a second one is not started.</summary>
        private bool _loading;

        /// <summary>Set when a load is asked for while another one is still running.</summary>
        private bool _reload;

        /// <summary>Raised for the actions this page cannot carry out itself.</summary>
        public event EventHandler<BrowserActionEventArgs> ActionRequested;

        /// <summary>Raised when the page has something to tell the user.</summary>
        public event EventHandler<BrowserMessageEventArgs> MessageRaised;

        /// <summary>
        /// A publisher that does not answer must not leave the page saying "loading" for
        /// ever. After this long the row says so, and the answer that arrives later replaces
        /// it.
        /// </summary>
        private readonly System.Windows.Forms.Timer _slow = new System.Windows.Forms.Timer { Interval = 22000 };

        internal sealed class BrowserMessageEventArgs : EventArgs
        {
            public readonly string Text;
            public readonly ToastKind Kind;
            public BrowserMessageEventArgs(string text, ToastKind kind) { Text = text; Kind = kind; }
        }

        private void Say(string text, ToastKind kind)
        {
            var h = MessageRaised;
            if (h != null) h(this, new BrowserMessageEventArgs(text, kind));
        }

        public BrowsersPage()
        {
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            /* Engine first: it is the bigger decision, and everything below it follows from
               it. The channel picker narrows what the engine offers. */
            _engines = new JBSegment { Height = Ui.S(38) };
            _engines.SetItems(EngineLabels, 0);
            _engines.SelectedIndexChanged += delegate
            {
                int i = _engines.SelectedIndex;
                _engine = i >= 0 && i < EngineKeys.Length ? EngineKeys[i] : "chromium";
                Load();
            };
            Controls.Add(_engines);

            _channels = new JBSegment { Height = Ui.S(34) };
            _channels.SetItems(ChannelKeys, 0);
            _channels.SelectedIndexChanged += delegate
            {
                int i = _channels.SelectedIndex;
                _channel = i >= 0 && i < ChannelKeys.Length ? ChannelKeys[i] : "All";
                Load();
            };
            Controls.Add(_channels);

            _refresh = new JBButton { Kind = BtnKind.Soft, Icon = "refresh", Text = "Refresh", Radius = Theme.ButtonRadius, Height = Theme.Field };
            _refresh.Click += delegate { Load(); };
            Controls.Add(_refresh);

            _openFolder = new JBButton { Kind = BtnKind.Soft, Icon = "folder", Text = "Builds folder", Radius = Theme.ButtonRadius, Height = Theme.Field };
            _openFolder.Click += delegate { OpenFolder(); };
            Controls.Add(_openFolder);

            _slow.Tick += delegate
            {
                _slow.Stop();
                if (IsDisposed || Disposing) return;
                string engine = _engine;
                /* Still waiting: say so, and offer the one thing that helps. */
                if (_blocks.Count == 0) return;
                var last = _blocks[_blocks.Count - 1] as BuildRow;
                if (last == null || !last.Busy) return;
                ClearBlocks();
                AddInstalled(engine);
                AddHead("AVAILABLE BUILDS");
                AddMessage("Publisher not answering.");
                var again = new BuildRow("Try again", engine, "", "", false);
                again.ActionRequested += delegate { Load(); };
                AddBlock(again);
                LayoutCore();
            };

            _ready = true;
            /* The catalogue is not read here. The page is built while the window is still
               opening, and asking a publisher for its list of builds is not something the
               start-up has to wait for: the list is fetched the first time the page is
               opened, and a page nobody opens costs nothing. */
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(KernelStore.Root);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + KernelStore.Root + "\"");
            }
            catch (Exception ex) { Log.Error("Could not open the builds folder", ex); }
        }

        /// <summary>
        /// Rebuilds the list: what this machine has for the chosen engine, what the engine
        /// offers, and what is installed on this PC outside the application. The catalogue is
        /// fetched in the background so the page is never blocked by a slow publisher.
        /// </summary>
        public void Load()
        {
            if (!_ready) return;
            /* One catalogue at a time: a second request while the first is in flight is
               wasted traffic and a wasted second on the page. The request is remembered
               and run when the first one is done, so switching tabs quickly still ends
               on the engine that was asked for last. */
            if (_loading) { _reload = true; return; }
            _loading = true;
            string engine = _engine;
            string channel = _channel;
            int token = ++_token;

            ClearBlocks();
            AddInstalled(engine);
            AddHead("AVAILABLE BUILDS");
            var wait = new BuildRow("Contacting the publisher\u2026", engine, "", "", false);
            wait.Busy = true;
            wait.BusyText = "Loading the catalogue";
            wait.Progress = 0;
            AddBlock(wait);
            LayoutCore();

            _slow.Stop();
            _slow.Start();

            Task.Run(new Func<object[]>(delegate
            {
                List<KernelRelease> catalog = null;
                try { catalog = KernelStore.CatalogCached(engine); }
                catch (Exception ex) { Log.Error("Browser catalogue failed", ex); }
                /* The browsers installed on this PC are found on the same thread: the search
                   reads a version resource off disk for every candidate, which is slow enough
                   to be felt as a stutter on a page that is supposed to answer instantly. */
                List<BrowserInfo> local = null;
                try { local = BrowserLocator.FindAll(); } catch { }
                return new object[] { catalog, local };
            }))
                .ContinueWith(delegate(Task<object[]> t)
                {
                    List<KernelRelease> catalog = null;
                    List<BrowserInfo> local = null;
                    try
                    {
                        var r = t.Result;
                        catalog = r[0] as List<KernelRelease>;
                        local = r[1] as List<BrowserInfo>;
                    }
                    catch (Exception ex) { Log.Error("Browser catalogue failed", ex); }
                    try
                    {
                        if (IsDisposed) return;
                        if (InvokeRequired) BeginInvoke(new Action(delegate { Fill(engine, channel, catalog, local, token); }));
                        else Fill(engine, channel, catalog, local, token);
                    }
                    catch { }
                    finally
                    {
                        _loading = false;
                        if (_reload) { _reload = false; Load(); }
                    }
                });
        }

        private void Fill(string engine, string channel, List<KernelRelease> catalog, List<BrowserInfo> local, int token)
        {
            /* The user may have switched engine while the catalogue was in flight. */
            if (token != _token || engine != _engine) return;

            ClearBlocks();
            AddInstalled(engine);
            AddHead("AVAILABLE BUILDS");

            var mine = InstalledFor(engine);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int shown = 0;
            if (catalog != null)
            {
                foreach (var rel in catalog)
                {
                    if (rel == null) continue;
                    if (channel != "All" && !string.Equals(rel.Channel, channel, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(rel.Version)) continue;
                    bool have = false;
                    foreach (var k in mine)
                        if (string.Equals(k.Version, rel.Version, StringComparison.OrdinalIgnoreCase)) { have = true; break; }
                    if (have) continue;
                    var row = new BuildRow(rel.Version, engine, rel.Version,
                        (string.IsNullOrEmpty(rel.Channel) ? "" : rel.Channel + "  \u00b7  ") +
                        (rel.SizeMb > 0 ? rel.SizeMb + " MB" : "Portable"), false);
                    row.ActionRequested += delegate { Install(rel, row); };
                    AddBlock(row);
                    if (++shown >= 60) break;
                }
            }
            if (shown == 0)
            {
                AddMessage(catalog == null ? "Publisher not reachable." : "None published.");
            }

            /* --- browsers that are already on this PC --- */
            AddHead("ON THIS PC");
            if (local == null) local = new List<BrowserInfo>();
            int found = 0;
            foreach (var b in local)
            {
                if (b == null || !b.Found) continue;
                var row = new BuildRow(b.Name, KeyOf(b), "", "v" + (b.Version ?? ""), false);
                row.ActionRequested += delegate { Reveal(b); };
                AddBlock(row);
                found++;
            }
            if (found == 0) AddMessage("None found on this PC.");

            LayoutCore();
        }

        private void AddInstalled(string engine)
        {
            AddHead("INSTALLED BUILDS");
            var mine = InstalledFor(engine);
            if (mine.Count == 0)
            {
                AddMessage("None installed yet.");
                return;
            }
            foreach (var k in mine)
            {
                var row = new BuildRow(Label(engine) + " " + k.Version, engine, k.Version,
                    string.IsNullOrEmpty(k.FileVersion) ? "Installed" : "Installed  \u00b7  v" + k.FileVersion, true);
                row.ActionRequested += delegate { RemoveBuild(k); };
                AddBlock(row);
            }

            /* A pre-release build reports a version the fingerprint sites have never seen, and
               a version they do not know is read as a mask: the browser is pretending to be a
               Chrome that does not exist yet. It says so here, where the build is chosen,
               because it is the one thing that turns a perfect score into a warning and it
               has nothing to do with the profile itself. */
            var catalogue = KernelStore.CatalogCached(engine);
            if (catalogue != null && catalogue.Count > 0)
            {
                foreach (var k in mine)
                {
                    bool stable = false;
                    foreach (var r in catalogue)
                        if (r != null && string.Equals(r.Version, k.Version, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(r.Channel, "Stable", StringComparison.OrdinalIgnoreCase)) { stable = true; break; }
                    if (!stable)
                    {
                        AddMessage("This build is a pre-release. A profile that runs it reports a version " +
                            "the fingerprint sites do not know yet, and they read that as a mask. " +
                            "Install the Stable channel for a profile that has to look untouched.");
                        break;
                    }
                }
            }
        }

        private static List<KernelInstall> InstalledFor(string engine)
        {
            var mine = new List<KernelInstall>();
            try
            {
                foreach (var k in KernelStore.Installed())
                    if (k != null && string.Equals(k.Engine, engine, StringComparison.OrdinalIgnoreCase)) mine.Add(k);
            }
            catch { }
            mine.Sort(delegate(KernelInstall a, KernelInstall b)
            {
                return string.CompareOrdinal(b.FileVersion ?? "", a.FileVersion ?? "");
            });
            return mine;
        }

        private static string KeyOf(BrowserInfo b)
        {
            if (b.IsEdge) return "edge";
            if (b.IsBrave) return "brave";
            if (b.IsFirefox) return "firefox";
            if (b.Name == "Chrome") return "chrome";
            return "chromium";
        }

        private async void Install(KernelRelease rel, BuildRow row)
        {
            if (rel == null || row == null || row.Busy) return;
            row.Busy = true;
            row.BusyText = "Starting\u2026";
            row.Invalidate();
            try
            {
                await KernelStore.InstallAsync(rel,
                    delegate(int pct, string msg) { row.Progress = pct; row.BusyText = msg ?? "Working\u2026"; },
                    CancellationToken.None).ConfigureAwait(true);
                KernelDiversity.Invalidate();
                Say(Label(rel.Engine) + " " + rel.Version + " is installed", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("Build install failed", ex);
                Say("The build could not be installed: " + OneLine(ex.Message), ToastKind.Error);
            }
            finally
            {
                row.Busy = false;
                Load();
            }
        }

        private void RemoveBuild(KernelInstall k)
        {
            if (k == null) return;
            if (!ConfirmForm.Ask(this, "Remove this build",
                "Remove " + Label(k.Engine) + " " + k.Version + " from this PC?",
                "Profiles pinned to it will use the installed browser instead.",
                "Remove", BtnKind.Danger)) return;
            try
            {
                KernelStore.Delete(k);
                Say("Build removed", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Say("The build could not be removed: " + OneLine(ex.Message), ToastKind.Error);
            }
            Load();
        }

        private void Reveal(BrowserInfo b)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + b.Path + "\""); }
            catch (Exception ex) { Log.Error("Could not reveal the browser", ex); }
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown error";
            int i = s.IndexOf('\n');
            if (i > 0) s = s.Substring(0, i);
            return s.Trim();
        }

        /// <summary>The name of an engine, for the interface.</summary>
        public static string Label(string engine)
        {
            if (string.IsNullOrEmpty(engine)) return "Browser";
            for (int i = 0; i < EngineKeys.Length; i++)
                if (string.Equals(EngineKeys[i], engine, StringComparison.OrdinalIgnoreCase)) return EngineLabels[i];
            return engine;
        }

        // ------------------------------------------------------------------ layout

        private void ClearBlocks()
        {
            foreach (var b in _blocks) { _column.Controls.Remove(b); b.Dispose(); }
            _blocks.Clear();
        }

        private void AddBlock(Control c) { _column.Controls.Add(c); _blocks.Add(c); }

        private void AddHead(string title)
        {
            AddBlock(new JBLabel(LabelRole.Accent, title));
        }

        private void AddMessage(string text)
        {
            var l = new JBLabel(LabelRole.Faint, text);
            l.Wrap = true;
            AddBlock(l);
        }

        public void ReStyle()
        {
            BackColor = Theme.Bg;
            _column.BackColor = Theme.Bg;
            foreach (var b in _blocks)
            {
                var re = b as IReStyle;
                if (re != null) re.ReStyle();
            }
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
                int w = Width, h = Height;
                int pad = Ui.S(28);
                int inner = Math.Max(Ui.S(300), w - Ui.S(10) - pad * 2);

                /* Engine tabs across the top, then the channel picker under them. Both are
                   measured, so a long name never runs into the one next to it. */
                int engW = Math.Max(Ui.S(260), Math.Min(_engines.PreferredWidth(Ui.S(90)), inner));
                _engines.SetBounds(pad, Ui.S(6), engW, Ui.S(38));

                int chW = Math.Max(Ui.S(240), Math.Min(_channels.PreferredWidth(Ui.S(64)), inner));
                _channels.SetBounds(pad, Ui.S(52), chW, Ui.S(34));

                /* The two buttons sit to the right of the channel picker when there is room,
                   and under it when there is not. */
                int btnW = Math.Min(Ui.S(150), Math.Max(Ui.S(110), _refresh.PreferredWidth(Ui.S(110))));
                int folderW = Math.Min(Ui.S(170), Math.Max(Ui.S(120), _openFolder.PreferredWidth(Ui.S(120))));
                int right = pad + inner;
                int contentTop = Ui.S(96);
                if (right - (pad + chW) >= btnW + folderW + Ui.S(20))
                {
                    _refresh.SetBounds(right - btnW, Ui.S(52), btnW, Ui.S(34));
                    _openFolder.SetBounds(right - btnW - folderW - Ui.S(8), Ui.S(52), folderW, Ui.S(34));
                }
                else
                {
                    _refresh.SetBounds(right - btnW, contentTop, btnW, Ui.S(34));
                    _openFolder.SetBounds(right - btnW - folderW - Ui.S(8), contentTop, folderW, Ui.S(34));
                    contentTop += Ui.S(34) + Ui.S(10);
                }

                /* The list scrolls; the filters do not. The host therefore starts under the
                   filter row instead of under the page, which also keeps the two from
                   covering each other. */
                _host.SetBounds(0, contentTop, Math.Max(1, w), Math.Max(1, h - contentTop));

                /* The blocks are laid out in the order they were added: a heading, then the
                   rows and messages under it, then the next heading. One list, so a section
                   can never drift out of step with the rows it introduces. */
                int y = 0;
                foreach (var b in _blocks)
                {
                    var head = b as JBLabel;
                    var row = b as BuildRow;
                    if (row != null)
                    {
                        row.SetBounds(pad, y, inner, BuildRow.RowH);
                        y += BuildRow.RowH + Ui.S(8);
                    }
                    else if (head != null && head.Role == LabelRole.Accent)
                    {
                        head.SetBounds(pad, y, inner, Ui.S(18));
                        y += Ui.S(18) + Ui.S(10);
                    }
                    else
                    {
                        head.SetBounds(pad, y, inner, Ui.S(22));
                        y += Ui.S(22) + Ui.S(8);
                    }
                }

                y += Ui.S(12);
                _column.SetBounds(0, 0, Math.Max(inner + pad * 2, _host.Width - Ui.S(10)), Math.Max(y, _host.Height));
                _host.SyncExtent();
            }
            finally { _layingOut = false; }
        }
    }

    /// <summary>
    /// One build in the list: the version, where it came from, and the one thing that can be
    /// done with it. While a build is being fetched the row shows the progress instead of a
    /// button, so the download is visible in the place it was started from.
    /// </summary>
    internal sealed class BuildRow : Control, IReStyle
    {
        public readonly string Title;
        public readonly string Engine;
        public readonly string Version;
        public readonly string Subtitle;
        public readonly bool Installed;

        public bool Busy;
        public string BusyText = "Working\u2026";
        public int Progress;

        public event EventHandler ActionRequested;

        private readonly JBButton _action;
        private bool _hover;
        private readonly AnimValue _hoverT;

        public BuildRow(string title, string engine, string version, string subtitle, bool installed)
        {
            Title = title ?? "";
            Engine = engine ?? "";
            Version = version ?? "";
            Subtitle = subtitle ?? "";
            Installed = installed;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
            _hoverT = new AnimValue(this);
            _action = new JBButton { Radius = Theme.ButtonRadius, Height = Ui.S(32) };
            _action.Click += delegate { if (!Busy) { var h = ActionRequested; if (h != null) h(this, EventArgs.Empty); } };
            Controls.Add(_action);
            Apply();
            Height = RowH;
        }

        private void Apply()
        {
            if (Busy)
            {
                _action.Kind = BtnKind.Soft;
                _action.Icon = "download";
                _action.Text = Progress > 0 ? Progress + "%" : "Working\u2026";
                _action.Enabled = false;
            }
            else if (Installed)
            {
                _action.Kind = BtnKind.Soft;
                _action.Icon = "trash";
                _action.Text = "Remove";
            }
            else
            {
                _action.Kind = BtnKind.Primary;
                _action.Icon = "download";
                _action.Text = "Install";
            }
            OnResize(EventArgs.Empty);
        }

        public void ReStyle() { Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        /// <summary>Height of one row: two lines of text and a button, with room to breathe.</summary>
        public static int RowH
        {
            get
            {
                int text = Theme.LineHeight(Theme.H3) + Ui.S(4) + Theme.LineHeight(Theme.Caption);
                return Math.Max(Ui.S(58), Math.Max(text + Ui.S(20), Theme.Field + Ui.S(16)));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_action == null) return;
            int bh = Math.Min(Theme.Field, Math.Max(Ui.S(28), Height - Ui.S(16)));
            int w = Math.Min(Math.Max(Ui.S(92), Width - Ui.S(110)), _action.PreferredWidth(Ui.S(108)));
            _action.SetBounds(Math.Max(Ui.S(8), Width - Ui.S(14) - w), (Height - bh) / 2, w, bh);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; _hoverT.Toward(true, 0.28f); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverT.Toward(false, 0.2f); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), Ui.Blend(Theme.Card, Theme.PanelHover, _hoverT.Value));
            Ui.Stroke(g, r, Ui.S(Theme.ButtonRadius), Ui.Blend(Theme.Line, Theme.AccentBorder, _hoverT.Value * 0.7f));

            float x = Ui.S(16);
            float tw = Math.Max(Ui.S(80), _action.Left - x - Ui.S(12));

            float titleH = Theme.LineHeight(Theme.H3);
            float capH = Theme.LineHeight(Theme.Caption);
            float blockH = titleH + Ui.S(4) + capH;
            float top = Math.Max(Ui.S(8), (Height - blockH) / 2f);

            Ui.Text(g, Title, Theme.H3, Theme.Text, x, top, tw);

            Color ink = Installed ? Theme.Success : Theme.TextDim;
            string sub = Busy ? (BusyText ?? "") : Subtitle;
            if (Busy && Progress > 0) sub = Progress + "%  \u00b7  " + sub;
            Ui.Text(g, sub, Theme.Caption, Busy ? Theme.Accent : ink, x, top + titleH + Ui.S(4), tw);

            /* A progress bar under the text, so a download that takes a minute is visibly
               making progress instead of only saying that it is. */
            if (Busy && Progress > 0)
            {
                float barY = Math.Min(Height - Ui.S(12), top + blockH + Ui.S(6));
                var track = new RectangleF(x, barY, tw, Ui.S(4));
                Ui.Fill(g, track, Ui.S(2), Theme.Panel2);
                float w = tw * Math.Max(0.02f, Math.Min(1f, Progress / 100f));
                Ui.Fill(g, new RectangleF(track.X, track.Y, w, track.Height), Ui.S(2), Theme.Accent);
            }
        }
    }
}
