using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Kernel manager: one build per row, with what this machine has and what can be fetched.
    /// A profile pinned to a build keeps that build even after the everyday browser updates,
    /// which is the whole reason these live side by side instead of in Program Files.
    /// </summary>
    internal sealed class KernelForm : JBForm
    {
        private readonly JBSegment _engines;
        private readonly JBScrollHost _host;
        private readonly Panel _column = new Panel();
        private readonly List<KernelRow> _rows = new List<KernelRow>();
        private readonly JBLabel _note;
        private readonly JBButton _refresh, _close;
        private readonly JBIconButton _x;
        private string _engine = "chrome";
        private bool _loading;
        private bool _built;
        private bool _fitting;
        private bool _userSized;

        private static readonly string[] EngineKeys = { "chrome", "brave", "chromium" };
        private static readonly string[] EngineLabels = { "Chrome", "Brave", "Chromium" };

        public KernelForm()
        {
            Text = "Browser kernels";
            Resizable = true;
            DraggableSidebar = false;
            HeaderHit = 60;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            MinimumSize = new Size(Ui.S(520), Ui.S(360));
            Size = Ui.FitToScreen(this, Ui.S(660), Ui.S(560));

            _engines = new JBSegment();
            _engines.SetItems(EngineLabels, 0);
            _engines.SelectedIndexChanged += delegate
            {
                int i = _engines.SelectedIndex;
                _engine = i >= 0 && i < EngineKeys.Length ? EngineKeys[i] : "chrome";
                LoadAsync();
            };
            Controls.Add(_engines);

            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            _note = new JBLabel(LabelRole.Faint,
                "Builds are portable copies kept in this app's folder. Pin one in a profile's General tab.");
            _note.Wrap = true;
            Controls.Add(_note);

            _refresh = new JBButton { Kind = BtnKind.Soft, Icon = "refresh", Text = "Refresh", Radius = 4, Height = Ui.S(33) };
            _refresh.Click += delegate { LoadAsync(); };
            Controls.Add(_refresh);

            _close = new JBButton { Kind = BtnKind.Ghost, Text = "Close", Radius = 4, Height = Ui.S(33) };
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            _x = new JBIconButton { Icon = "close", Danger = true, IconSize = 13, Tip = "Close" };
            _x.Click += delegate { Close(); };
            Controls.Add(_x);

            _built = true;
            LoadAsync();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterToScreen();
            LayoutCore();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // A manual drag should stick; our own auto-fit must not count as one.
            if (_built && !_fitting) _userSized = true;
            LayoutCore();
        }

        /// <summary>
        /// Shrink to the number of builds on offer so the dialog is not mostly empty space.
        /// Skipped as soon as the user has resized the window themselves.
        /// </summary>
        private void FitHeight()
        {
            if (!_built || _userSized) return;
            int rows = Math.Max(1, _rows.Count);
            int want = Ui.S(100) + rows * (Ui.S(58) + Ui.S(8)) + Ui.S(64);
            int max = Math.Max(Ui.S(360), Screen.FromControl(this).WorkingArea.Height - Ui.S(140));
            want = Math.Min(Math.Max(want, Ui.S(360)), max);
            if (Math.Abs(want - ClientSize.Height) < Ui.S(2)) return;
            _fitting = true;
            try { ClientSize = new Size(ClientSize.Width, want); }
            finally { _fitting = false; }
            LayoutCore();
        }

        private void LayoutCore()
        {
            if (!_built) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(24);
            _x.SetBounds(w - pad - Ui.S(30), Ui.S(14), Ui.S(30), Ui.S(30));
            _engines.SetBounds(pad, Ui.S(52), Math.Min(Ui.S(420), w - pad * 2), Ui.S(36));

            int top = Ui.S(100);
            int footer = Ui.S(52);
            _host.SetBounds(pad, top, w - pad * 2, Math.Max(Ui.S(120), h - top - footer - Ui.S(8)));

            int cw = Math.Max(Ui.S(200), _host.Width - Ui.S(10));
            int y = Ui.S(2);
            foreach (var r in _rows)
            {
                r.SetBounds(0, y, cw, Ui.S(58));
                y += Ui.S(58) + Ui.S(8);
            }
            _column.SetBounds(0, 0, Math.Max(cw, _host.Width - Ui.S(10)), Math.Max(y, _host.Height));
            _host.SyncExtent();

            int rw = _refresh.PreferredWidth(Ui.S(110));
            int cwid = _close.PreferredWidth(Ui.S(84));
            _refresh.SetBounds(pad, h - Ui.S(16) - Ui.S(33), rw, Ui.S(33));
            _close.SetBounds(pad + rw + Ui.S(8), h - Ui.S(16) - Ui.S(33), cwid, Ui.S(33));
            /* The note shares the footer row with two buttons whose widths depend on the
               language, so the space it is given is clamped at zero rather than allowed to
               become a negative width that draws nothing at all. */
            int noteX = pad + rw + cwid + Ui.S(24);
            int noteW = Math.Max(0, w - pad - noteX);
            _note.Visible = noteW >= Ui.S(80);
            if (_note.Visible) _note.SetBounds(noteX, h - Ui.S(16) - Ui.S(30), noteW, Ui.S(30));
        }

        private void ClearRows()
        {
            foreach (var r in _rows) { _column.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();
        }

        private async System.Threading.Tasks.Task LoadAsyncCore()
        {
            /* The busy flag and the Refresh button are restored on every exit path: a failure
               half-way through the list used to leave the page stuck on "Loading builds". */
            try
            {
                if (_loading) return;
                _loading = true;
                _refresh.Enabled = false;
                ClearRows();
                var wait = new KernelRow(new KernelRelease(_engine, "Loading builds\u2026", "", "", ""), false, null);
                wait.ShowBusy("Contacting the publisher\u2026");
                AddRow(wait);
                LayoutCore();

                List<KernelRelease> releases = null;
                string error = null;
                try { releases = await KernelStore.CatalogAsync(_engine).ConfigureAwait(true); }
                catch (Exception ex) { error = ex.Message; }

                if (IsDisposed) return;
                ClearRows();

                var installed = new Dictionary<string, KernelInstall>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in KernelStore.Installed())
                    if (string.Equals(k.Engine, _engine, StringComparison.OrdinalIgnoreCase))
                        installed[k.Version] = k;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (releases != null)
                {
                    foreach (var rel in releases)
                    {
                        if (!seen.Add(rel.Version)) continue;
                        KernelInstall have;
                        installed.TryGetValue(rel.Version, out have);
                        AddRow(new KernelRow(rel, have != null, have));
                    }
                }
                // Builds on disk that the publisher no longer lists still belong in the list.
                foreach (var kv in installed)
                {
                    if (seen.Contains(kv.Key)) continue;
                    AddRow(new KernelRow(new KernelRelease(_engine, kv.Key, "", "", ""), true, kv.Value));
                }

                if (_rows.Count == 0)
                {
                    var none = new KernelRow(new KernelRelease(_engine,
                        error != null ? "Could not reach the publisher" : "No builds published",
                        error ?? "Check the connection and press Refresh.", "", ""), false, null);
                    AddRow(none);
                }

                FitHeight();
                LayoutCore();
            }
            finally
            {
                _loading = false;
                if (_refresh != null) _refresh.Enabled = true;
            }
        }

        /// <summary>Guarded entry point so a failed await never reaches the crash dialog.</summary>
        private void LoadAsync() { Guarded.Run("LoadAsync", this, delegate { return LoadAsyncCore(); }); }

        private void AddRow(KernelRow row)
        {
            row.ActionRequested += delegate(object s, EventArgs e)
            {
                var r = (KernelRow)s;
                if (r.Installed) Delete(r);
                else Download(r);
            };
            _rows.Add(row);
            _column.Controls.Add(row);
        }

        private void Delete(KernelRow row)
        {
            if (!ConfirmForm.Ask(this, "Remove this build",
                "Delete " + row.Rel.Version + " from this PC? Profiles pinned to it will fall back to the installed browser.",
                null, "Remove", BtnKind.Danger))
                return;
            try
            {
                KernelStore.Delete(row.Install);
                LoadAsync();
            }
            catch (Exception ex)
            {
                ConfirmForm.Tell(this, "Remove build",
                    "The build could not be removed.", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task DownloadCore(KernelRow row)
        {
            row.ShowBusy("Starting\u2026");
            LayoutCore();
            var dlg = new DownloadDialog("Get " + KernelStoreLabel(row.Rel));
            dlg.CenterOn(this);
            dlg.Show(this);
            try
            {
                await KernelStore.InstallAsync(row.Rel, dlg.UpdateProgress, dlg.Token).ConfigureAwait(true);
                dlg.Close();
                LoadAsync();
            }
            catch (OperationCanceledException) { dlg.Close(); LoadAsync(); }
            catch (Exception ex)
            {
                dlg.Close();
                ConfirmForm.Tell(this, "Kernel download",
                    "The build could not be installed.", ex.Message);
                LoadAsync();
            }
        }

        /// <summary>Guarded entry point so a failed await never reaches the crash dialog.</summary>
        private void Download(KernelRow row) { Guarded.Run("Download", this, delegate { return DownloadCore(row); }); }

        private static string KernelStoreLabel(KernelRelease rel)
        {
            string name = rel.Engine == "chrome" ? "Chrome"
                : rel.Engine == "brave" ? "Brave" : "Chromium";
            return name + " " + rel.Version;
        }
    }

    /// <summary>One build in the list: name, channel and size, with a single contextual action.</summary>
    internal sealed class KernelRow : Control, IReStyle
    {
        public readonly KernelRelease Rel;
        public bool Installed { get; private set; }
        public KernelInstall Install { get; private set; }
        public event EventHandler ActionRequested;

        private readonly JBButton _action;
        private bool _busy;
        private string _busyText = "Working\u2026";
        private bool _hover;

        public KernelRow(KernelRelease rel, bool installed, KernelInstall install)
        {
            Rel = rel;
            Installed = installed;
            Install = install;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Ui.Opaque(this);
            _action = new JBButton { Radius = 4, Height = Ui.S(29) };
            _action.Click += delegate { if (!_busy) { var h = ActionRequested; if (h != null) h(this, EventArgs.Empty); } };
            Controls.Add(_action);
            Apply();
            Height = Ui.S(58);
        }

        public void ShowBusy(string text)
        {
            _busy = true;
            _busyText = text;
            _action.Enabled = false;
            Apply();
            Invalidate();
        }

        private void Apply()
        {
            if (_busy)
            {
                _action.Kind = BtnKind.Soft;
                _action.Icon = "download";
                _action.Text = "Working\u2026";
                _action.Enabled = false;
                return;
            }
            if (Installed)
            {
                _action.Kind = BtnKind.Soft;
                _action.Icon = "trash";
                _action.Text = "Remove";
            }
            else
            {
                _action.Kind = BtnKind.Primary;
                _action.Icon = "download";
                _action.Text = "Download";
            }
            _action.Enabled = !string.IsNullOrEmpty(Rel.Url);
            if (string.IsNullOrEmpty(Rel.Url) && !Installed)
            {
                _action.Kind = BtnKind.Soft;
                _action.Text = "Unavailable";
            }
        }

        public void ReStyle() { Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_action == null) return;
            int w = _action.PreferredWidth(Ui.S(112));
            _action.SetBounds(Math.Max(Ui.S(8), Width - Ui.S(14) - w), (Height - Ui.S(29)) / 2, w, Ui.S(29));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(8), _hover ? Theme.PanelHover : Theme.Card);
            Ui.Stroke(g, r, Ui.S(8), _hover ? Theme.AccentBorder : Theme.Line, 1f);

            float x = Ui.S(16);
            float mid = Height / 2f;
            float tw = Math.Max(Ui.S(80), _action.Left - x - Ui.S(12));

            string title = Rel.Version;
            string sub;
            if (Installed && Install != null)
                sub = "Installed" + (string.IsNullOrEmpty(Install.FileVersion) ? "" : "  \u00b7  " + Install.FileVersion);
            else if (!string.IsNullOrEmpty(Rel.Url))
                sub = (string.IsNullOrEmpty(Rel.Channel) ? "" : Rel.Channel + "  \u00b7  ") +
                      (Rel.SizeMb > 0 ? Rel.SizeMb + " MB" : "Portable build");
            else sub = Rel.Channel;

            Ui.Text(g, title, Theme.H3, Theme.Text, x, mid - Ui.S(20), tw);
            Ui.Text(g, sub, Theme.Caption, Installed ? Theme.Success : Theme.TextDim, x, mid + Ui.S(4), tw);
        }
    }
}
