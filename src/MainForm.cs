using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal sealed class MainForm : JBForm
    {
        private const int SidebarW = 280;
        private const int HeaderH = 68;
        private const int ContentPad = 28;

        private readonly ProfileStore _store = new ProfileStore();
        private AppSettings _settings;
        private List<Profile> _profiles = new List<Profile>();
        private readonly Dictionary<string, BrowserSession> _sessions = new Dictionary<string, BrowserSession>();
        private readonly HashSet<string> _launching = new HashSet<string>();
        private readonly Dictionary<string, ProfileCard> _cards = new Dictionary<string, ProfileCard>();

        // Sidebar
        private JBButton _navProfiles, _navSettings, _navAbout;
        private JBThemeSwitch _themeSwitch;

        // Caption buttons
        private JBIconButton _minBtn, _maxBtn, _closeBtn;

        // Header toolbar
        private JBTextBox _search;
        private JBButton _newBtn, _saveSettings, _sortBtn;

        // Multi-select
        private readonly List<string> _picks = new List<string>();
        private string _pickAnchor;
        private JBPanel _bulk;
        private Label _bulkCount;
        private JBButton _bulkOpen, _bulkStop, _bulkDup, _bulkDel, _bulkCancel;

        // Pages
        private JBScrollHost _host;
        private JBGrid _grid;
        private Panel _settingsPage, _aboutPage, _emptyPanel;
        private JBButton _emptyBtn, _aboutTelegram;
        private JBToast _toast;

        // Settings controls
        private JBLabel _setBrowserInfo, _setBrowserHint, _setStartHint, _setStoragePath, _setStorageHint;
        private JBTextBox _setBrowserPath, _setStartUrl;
        private JBButton _setBrowse, _setDetect, _setOpenData, _setDownload;
        private JBPanel _cardBrowser, _cardStart, _cardStorage;
        private Panel _settingsColumn;
        private JBScrollHost _settingsHost;

        private int _page;
        private bool _built;
        private string _filter = "";
        private Timer _pulse;

        public MainForm()
        {
            Text = "Joe Browser";
            DraggableSidebar = true;
            HeaderHit = 64;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            MinimizeBox = true;

            _settings = _store.LoadSettings();
            Theme.SetMode(_settings.ThemeMode != 2);
            Build();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyScale();
            ReloadCards(true);
            SetPage(0);
            LayoutAll();
            ShowInTaskbar = true;
            _search.Box.AccessibleName = "Search profiles";
        }

        private void ApplyScale()
        {
            try
            {
                using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f;
            }
            catch { Ui.Scale = 1f; }
            if (Ui.Scale < 1f) Ui.Scale = 1f;

            MinimumSize = new Size(Ui.S(1000), Ui.S(660));
            Size max = Ui.FitToScreen(this, Ui.S(1280), Ui.S(820));
            SuppressSurfaceEvents(() => ClientSize = new Size(Math.Max(MinimumSize.Width, max.Width), Math.Max(MinimumSize.Height, max.Height)));
            FitToWorkArea();
            CenterToScreen();
        }

        protected override void OnScaleChanged()
        {
            LayoutAll();
        }

        public override Color SurfaceAt(Point clientLocation)
        {
            return clientLocation.X < Ui.S(SidebarW) ? Theme.Sidebar : BackColor;
        }

        protected override void OnSurfaceChanged()
        {
            if (!_built) return;
            if (_maxBtn != null)
            {
                _maxBtn.Icon = Maximized ? "restore" : "max";
                _maxBtn.Tip = Maximized ? "Restore" : "Maximize";
                _maxBtn.Invalidate();
            }
            LayoutAll();
        }

        private JBButton Nav(string text, string icon, EventHandler click)
        {
            var b = new JBButton { Kind = BtnKind.Nav, Icon = icon, Text = text, Radius = 4, Height = Ui.S(40) };
            b.Click += click;
            return b;
        }

        private void Build()
        {
            _navProfiles = Nav("Profiles", "layers", delegate { SetPage(0); });
            _navSettings = Nav("Settings", "sliders", delegate { SetPage(1); });
            _navAbout = Nav("About", "info", delegate { SetPage(2); });

            _themeSwitch = new JBThemeSwitch { Checked = Theme.Dark };
            _themeSwitch.CheckedChanged += delegate { ToggleTheme(); };

            _search = new JBTextBox { Icon = "search", Placeholder = "Search profiles", Height = Ui.S(33) };
            _search.Box.TextChanged += delegate
            {
                _filter = (_search.Value ?? "").Trim();
                ApplyFilter();
            };

            _newBtn = new JBButton { Kind = BtnKind.Primary, Icon = "plus", Text = "New profile", Radius = 4, Height = Ui.S(33) };
            _newBtn.Click += delegate { NewProfile(); };

            _sortBtn = new JBButton { Text = "Sort: recent", Radius = 4, Height = Ui.S(33), Tip = "Change the order of the list (Ctrl+O)" };
            _sortBtn.Click += delegate { CycleSort(); };

            _bulk = new JBPanel { Visible = false };
            _bulkCount = new Label { AutoSize = true, Font = Theme.H3, ForeColor = Theme.Text, BackColor = Color.Transparent };
            _bulkOpen = new JBButton { Kind = BtnKind.Primary, Icon = "play", Text = "Open all", Radius = 4, Height = Ui.S(31) };
            _bulkStop = new JBButton { Kind = BtnKind.Soft, Icon = "power", Text = "Stop", Radius = 4, Height = Ui.S(31) };
            _bulkDup = new JBButton { Kind = BtnKind.Soft, Icon = "copy", Text = "Duplicate", Radius = 4, Height = Ui.S(31) };
            _bulkDel = new JBButton { Kind = BtnKind.Danger, Icon = "trash", Text = "Delete", Radius = 4, Height = Ui.S(31) };
            _bulkCancel = new JBButton { Kind = BtnKind.Ghost, Icon = "close", Radius = 4, Height = Ui.S(31), Tip = "Clear selection (Esc)" };
            _bulkOpen.Click += delegate { BulkOpen(); };
            _bulkStop.Click += delegate { BulkStop(); };
            _bulkDup.Click += delegate { BulkDuplicate(); };
            _bulkDel.Click += delegate { BulkDelete(); };
            _bulkCancel.Click += delegate { ClearPicks(); };
            _bulk.Controls.Add(_bulkCount);
            _bulk.Controls.Add(_bulkOpen);
            _bulk.Controls.Add(_bulkStop);
            _bulk.Controls.Add(_bulkDup);
            _bulk.Controls.Add(_bulkDel);
            _bulk.Controls.Add(_bulkCancel);

            _saveSettings = new JBButton { Kind = BtnKind.Primary, Icon = "check", Text = "Save changes", Radius = 4, Height = Ui.S(33) };
            _saveSettings.Click += delegate { SaveSettings(); };

            _minBtn = new JBIconButton { Icon = "min", IconSize = 11, Caption = true, Tip = "Minimize" };
            _maxBtn = new JBIconButton { Icon = "max", IconSize = 11, Caption = true, Tip = "Maximize" };
            _closeBtn = new JBIconButton { Icon = "close", IconSize = 12, Caption = true, Danger = true, Tip = "Close" };
            _minBtn.Click += delegate { WindowState = FormWindowState.Minimized; };
            _maxBtn.Click += delegate { ToggleMaximize(); };
            _closeBtn.Click += delegate { Close(); };

            _grid = new JBGrid { MinCell = 240, CellHeight = 172, Gap = 16 };
            _host = new JBScrollHost(_grid);

            _emptyPanel = new Panel { BackColor = Theme.Bg, Visible = false };
            _emptyPanel.Paint += EmptyPaint;
            _emptyBtn = new JBButton { Kind = BtnKind.Primary, Icon = "plus", Text = "Create profile", Radius = 4, Height = Ui.S(33) };
            _emptyBtn.Click += delegate
            {
                if (_filter.Length > 0) { _search.Value = ""; _filter = ""; ApplyFilter(); }
                else NewProfile();
            };
            _emptyPanel.Controls.Add(_emptyBtn);

            _toast = new JBToast();

            _settingsColumn = new Panel { BackColor = Theme.Bg };
            BuildSettingsPage(_settingsColumn);
            _settingsHost = new JBScrollHost(_settingsColumn);
            _settingsPage = new Panel { BackColor = Theme.Bg, Visible = false };
            _settingsPage.Controls.Add(_settingsHost);

            _aboutPage = new Panel { BackColor = Theme.Bg };
            _aboutPage.Paint += AboutPaint;
            _aboutTelegram = new JBButton { Kind = BtnKind.Soft, BrandIcon = "telegram", Text = "@joegoldberg2025", Radius = 4, Height = Ui.S(33) };
            _aboutTelegram.Click += delegate { OpenUrl("https://t.me/joegoldberg2025"); };
            _aboutPage.Controls.Add(_aboutTelegram);

            Controls.Add(_navProfiles);
            Controls.Add(_navSettings);
            Controls.Add(_navAbout);
            Controls.Add(_themeSwitch);
            Controls.Add(_search);
            Controls.Add(_sortBtn);
            Controls.Add(_newBtn);
            Controls.Add(_saveSettings);
            Controls.Add(_bulk);
            Controls.Add(_host);
            Controls.Add(_emptyPanel);
            Controls.Add(_settingsPage);
            Controls.Add(_aboutPage);
            Controls.Add(_toast);
            Controls.Add(_minBtn);
            Controls.Add(_maxBtn);
            Controls.Add(_closeBtn);

            _pulse = new Timer { Interval = 90 };
            _pulse.Tick += delegate
            {
                if (_sessions.Count == 0) { _pulse.Stop(); return; }
                foreach (var c in _cards.Values) if (c.Running) c.Invalidate();
            };

            _built = true;
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Open url failed", ex); Toast("Could not open the link", ToastKind.Error); }
        }

        private void BuildSettingsPage(Panel page)
        {
            _setBrowserInfo = Label(LabelRole.Strong, "Detecting…");
            _setBrowserHint = Label(LabelRole.Body,
                "Leave empty to use the Chromium browser installed on this PC.");

            _cardBrowser = Section("BROWSER ENGINE", 146);
            _cardBrowser.Controls.Add(_setBrowserInfo);
            _setBrowserPath = Field("Automatic (recommended)");
            _setBrowse = new JBButton { Kind = BtnKind.Soft, Text = "Browse", Radius = 4, Height = Ui.S(33) };
            _setDetect = new JBButton { Kind = BtnKind.Soft, Icon = "refresh", Text = "Auto-detect", Radius = 4, Height = Ui.S(33) };
            _cardBrowser.Controls.Add(_setBrowserPath);
            _cardBrowser.Controls.Add(_setBrowse);
            _cardBrowser.Controls.Add(_setDetect);
            _cardBrowser.Controls.Add(_setBrowserHint);

            _setStartHint = Label(LabelRole.Body, "Opened in a new tab every time a profile starts.");
            _cardStart = Section("START PAGE", 120);
            _setStartUrl = Field("https://www.google.com");
            _cardStart.Controls.Add(_setStartUrl);
            _cardStart.Controls.Add(_setStartHint);

            _setStoragePath = Label(LabelRole.Body, AppPaths.Root);
            _setStorageHint = Label(LabelRole.Body, "Everything stays on this device. No account, no cloud.");
            _cardStorage = Section("STORAGE", 144);
            _cardStorage.Controls.Add(_setStoragePath);
            _setOpenData = new JBButton { Kind = BtnKind.Soft, Text = "Open data folder", Radius = 4, Height = Ui.S(33) };
            _setDownload = new JBButton { Kind = BtnKind.Soft, Text = "Download Chromium", Radius = 4, Height = Ui.S(33) };
            _cardStorage.Controls.Add(_setOpenData);
            _cardStorage.Controls.Add(_setDownload);
            _cardStorage.Controls.Add(_setStorageHint);

            _setBrowse.Click += delegate { BrowseBrowser(); };
            _setDetect.Click += delegate { _setBrowserPath.Value = ""; UpdateBrowserInfo(); };
            _setOpenData.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.Root + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { Toast(ex.Message, ToastKind.Error); }
            };
            _setDownload.Click += delegate { var _ = DownloadBrowserAsync("chromium"); };

            page.Controls.Add(_cardBrowser);
            page.Controls.Add(_cardStart);
            page.Controls.Add(_cardStorage);
        }

        private JBPanel Section(string title, int height)
        {
            var card = new JBPanel { Size = new Size(Ui.S(680), Ui.S(height)) };
            var lbl = Label(LabelRole.Accent, title);
            lbl.Location = new Point(Ui.S(18), Ui.S(16));
            lbl.Size = new Size(Ui.S(300), Ui.S(16));
            card.Controls.Add(lbl);
            return card;
        }

        private JBLabel Label(LabelRole role, string text)
        {
            return new JBLabel(role, text);
        }

        private JBTextBox Field(string placeholder)
        {
            return new JBTextBox { Placeholder = placeholder, Height = Ui.S(33) };
        }

        private void BrowseBrowser()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select a Chromium-based browser";
                dlg.Filter = "Executables (*.exe)|*.exe";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _setBrowserPath.Value = dlg.FileName;
                    UpdateBrowserInfo();
                }
            }
        }

        private void UpdateBrowserInfo()
        {
            if (_setBrowserInfo == null) return;
            var info = BrowserLocator.Detect("auto", _setBrowserPath.Value.Trim());
            _setBrowserInfo.Text = info.Found
                ? info.Name + " " + info.Version
                : "No Chromium browser found. Install Chrome or Edge, or download Chromium.";
            _setBrowserInfo.Invalidate();
        }

        private void SaveSettings()
        {
            _settings.BrowserPath = _setBrowserPath.Value.Trim();
            string url = _setStartUrl.Value.Trim();
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("about:"))
                url = "https://" + url;
            _settings.DefaultStartUrl = string.IsNullOrEmpty(url) ? "https://www.google.com" : url;
            _settings.ThemeMode = Theme.Dark ? 1 : 2;
            _store.SaveSettings(_settings);
            Toast("Settings saved", ToastKind.Success);
        }

        private void ToggleTheme()
        {
            Theme.SetMode(_themeSwitch.Checked);
            if (_settings != null)
            {
                _settings.ThemeMode = Theme.Dark ? 1 : 2;
                try { _store.SaveSettings(_settings); } catch { }
            }
            _themeSwitch.Invalidate();
            LayoutAll();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            int w = ClientSize.Width, h = ClientSize.Height;
            int sb = Ui.S(SidebarW);
            int headerH = Ui.S(HeaderH);

            g.Clear(Theme.Bg);

            // ---- navbar: windows-ui #202020 sidebar, 280px ----
            using (var b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, 0, 0, sb, h);

            float logoSize = Ui.S(24);
            var logoBox = new RectangleF(Ui.S(20), Ui.S(16), logoSize, logoSize);
            Logo.Draw(g, logoBox);
            Ui.Text(g, "Joe Browser", Theme.BodyBold, Theme.Text, Ui.S(52), Ui.S(18), sb - Ui.S(70));

            using (var p = new Pen(Theme.Line))
            {
                g.DrawLine(p, Ui.S(20), Ui.S(114), sb - Ui.S(20), Ui.S(114));
                g.DrawLine(p, Ui.S(20), h - Ui.S(104), sb - Ui.S(20), h - Ui.S(104));
            }

            // ---- running status footer ----
            float fy = h - Ui.S(92);
            Icons.Draw(g, _sessions.Count > 0 ? "window" : "shield",
                new RectangleF(Ui.S(20), fy + (Ui.S(40) - Ui.S(16)) / 2f, Ui.S(16), Ui.S(16)),
                _sessions.Count > 0 ? Theme.Success : Theme.TextFaint);
            string running = _sessions.Count > 0
                ? _sessions.Count + " browser" + (_sessions.Count == 1 ? "" : "s") + " running"
                : "Local-first · private";
            Ui.Text(g, running, Theme.Small, Theme.TextDim, Ui.S(44), fy + (Ui.S(40) - Theme.Small.GetHeight(g)) / 2f, sb - Ui.S(64));
            Ui.Text(g, "Version 1.0.0", Theme.Small, Theme.TextFaint, Ui.S(20), h - Ui.S(60), sb - Ui.S(40));

            // ---- page header band ----
            string title = _page == 0 ? "Profiles" : _page == 1 ? "Settings" : "About";
            string sub;
            if (_page == 0)
            {
                int shown = ShownCount();
                sub = _filter.Length > 0
                    ? shown + " of " + _profiles.Count + " profiles"
                    : _profiles.Count + (_profiles.Count == 1 ? " profile" : " profiles");
            }
            else if (_page == 1) sub = "Browser, storage and start page";
            else sub = "Version 1.0.0 · MIT License";

            Ui.Text(g, title, Theme.H1, Theme.Text, sb + Ui.S(ContentPad), Ui.S(12), w - sb - Ui.S(ContentPad) - Ui.S(180));
            Ui.Text(g, sub, Theme.Small, Theme.TextDim, sb + Ui.S(ContentPad), Ui.S(36), w - sb - Ui.S(ContentPad) - Ui.S(180));

            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, sb, headerH, w, headerH);
        }

        private int ShownCount()
        {
            int n = 0;
            foreach (var c in _cards.Values) if (c.Visible) n++;
            return n;
        }

        private void LayoutAll()
        {
            if (!_built) return;
            SuspendLayout();
            int w = ClientSize.Width, h = ClientSize.Height;
            int sb = Ui.S(SidebarW);
            int headerH = Ui.S(HeaderH);

            // navbar rows
            int navX = Ui.S(10), navW = sb - Ui.S(20);
            _themeSwitch.SetBounds(navX, Ui.S(64), navW, Ui.S(42));
            _navProfiles.SetBounds(navX, Ui.S(122), navW, Ui.S(40));
            _navSettings.SetBounds(navX, Ui.S(167), navW, Ui.S(40));
            _navAbout.SetBounds(navX, Ui.S(212), navW, Ui.S(40));

            // caption buttons, flush with the top right corner
            int cw = Ui.S(46), ch = Ui.S(32);
            _closeBtn.SetBounds(w - cw, 0, cw, ch);
            _maxBtn.SetBounds(w - cw * 2, 0, cw, ch);
            _minBtn.SetBounds(w - cw * 3, 0, cw, ch);

            // toolbar sits left of the caption area so nothing overlaps
            int toolbarRight = w - cw * 3 - Ui.S(14);
            int newW = Math.Min(Ui.S(150), _newBtn.PreferredWidth(Ui.S(132)));
            _newBtn.SetBounds(toolbarRight - newW, Ui.S(17), newW, Ui.S(33));
            int searchW = Math.Min(Ui.S(250), Math.Max(Ui.S(150),
                _newBtn.Left - Ui.S(16) - (sb + Ui.S(12)) - _sortBtn.PreferredWidth(Ui.S(110))));
            _search.SetBounds(_newBtn.Left - Ui.S(8) - searchW, Ui.S(17), searchW, Ui.S(33));
            int sortW = _sortBtn.PreferredWidth(Ui.S(110));
            _sortBtn.SetBounds(_search.Left - Ui.S(8) - sortW, Ui.S(17), sortW, Ui.S(33));
            int saveW = Math.Min(Ui.S(160), _saveSettings.PreferredWidth(Ui.S(140)));
            _saveSettings.SetBounds(toolbarRight - saveW, Ui.S(17), saveW, Ui.S(33));

            int contentX = sb;
            int contentW = Math.Max(Ui.S(200), w - sb);
            int gridTop = headerH + Ui.S(16);

            _host.SetBounds(contentX + Ui.S(ContentPad), gridTop,
                Math.Max(Ui.S(160), contentW - Ui.S(ContentPad) * 2), Math.Max(Ui.S(120), h - gridTop - Ui.S(16)));
            _grid.Width = Math.Max(Ui.S(120), _host.Width - Ui.S(8));
            _grid.LayoutGrid();
            LayoutBulk();

            _emptyPanel.SetBounds(_host.Left, _host.Top, _host.Width, _host.Height);
            int emptyW = _emptyBtn.PreferredWidth(Ui.S(160));
            _emptyBtn.SetBounds((_emptyPanel.Width - emptyW) / 2, _emptyPanel.Height / 2 + Ui.S(52), emptyW, Ui.S(33));

            _settingsPage.SetBounds(contentX, headerH, contentW, h - headerH);
            _settingsHost.SetBounds(Ui.S(ContentPad), Ui.S(8), Math.Max(0, contentW - Ui.S(ContentPad) * 2),
                Math.Max(0, h - headerH - Ui.S(16)));
            LayoutSettings();

            _aboutPage.SetBounds(contentX, headerH, contentW, h - headerH);
            LayoutAbout();

            int tw = Math.Min(Ui.S(420), Math.Max(Ui.S(240), contentW - Ui.S(64)));
            _toast.SetBounds(contentX + (contentW - tw) / 2, h - Ui.S(60), tw, Ui.S(44));

            ResumeLayout(false);
            Invalidate();
        }

        private void LayoutSettings()
        {
            if (_settingsColumn == null) return;
            int hostW = Math.Max(Ui.S(320), _settingsHost.Width - Ui.S(10));
            int colW = Math.Min(Ui.S(760), hostW);
            int x = Math.Max(0, (hostW - colW) / 2);

            _settingsColumn.SetBounds(0, 0, Math.Max(colW, hostW), _settingsColumn.Height);

            int y = Ui.S(4);
            int gap = Ui.S(16);
            PlaceCard(_cardBrowser, x, y, colW, 146);
            LayoutBrowserCard(colW);
            y += Ui.S(146) + gap;
            PlaceCard(_cardStart, x, y, colW, 120);
            LayoutStartCard(colW);
            y += Ui.S(120) + gap;
            PlaceCard(_cardStorage, x, y, colW, 144);
            LayoutStorageCard(colW);
            y += Ui.S(144) + Ui.S(24);

            _settingsColumn.Height = y;
            _settingsColumn.Width = Math.Max(colW + Ui.S(20), hostW);
            _settingsHost.SyncExtent();
        }

        private void PlaceCard(JBPanel card, int x, int y, int w, int h)
        {
            card.SetBounds(x, y, w, Ui.S(h));
        }

        private void LayoutBrowserCard(int colW)
        {
            int inner = Ui.S(18);
            int bwBrowse = _setBrowse.PreferredWidth(Ui.S(76));
            int bwDetect = _setDetect.PreferredWidth(Ui.S(96));
            _setBrowserInfo.SetBounds(inner, Ui.S(40), colW - inner * 2, Ui.S(18));
            int fieldW = Math.Max(Ui.S(100), colW - inner * 2 - bwBrowse - bwDetect - Ui.S(16));
            _setBrowserPath.SetBounds(inner, Ui.S(64), fieldW, Ui.S(33));
            _setBrowse.SetBounds(inner + fieldW + Ui.S(8), Ui.S(64), bwBrowse, Ui.S(33));
            _setDetect.SetBounds(_setBrowse.Right + Ui.S(8), Ui.S(64), bwDetect, Ui.S(33));
            _setBrowserHint.SetBounds(inner, Ui.S(106), colW - inner * 2, Ui.S(18));
        }

        private void LayoutStartCard(int colW)
        {
            int inner = Ui.S(18);
            _setStartUrl.SetBounds(inner, Ui.S(42), colW - inner * 2, Ui.S(33));
            _setStartHint.SetBounds(inner, Ui.S(84), colW - inner * 2, Ui.S(18));
        }

        private void LayoutStorageCard(int colW)
        {
            int inner = Ui.S(18);
            _setStoragePath.SetBounds(inner, Ui.S(40), colW - inner * 2, Ui.S(18));
            int w1 = _setOpenData.PreferredWidth(Ui.S(150)), w2 = _setDownload.PreferredWidth(Ui.S(150));
            _setOpenData.SetBounds(inner, Ui.S(66), w1, Ui.S(33));
            _setDownload.SetBounds(inner + w1 + Ui.S(8), Ui.S(66), w2, Ui.S(33));
            _setStorageHint.SetBounds(inner, Ui.S(108), colW - inner * 2, Ui.S(18));
        }

        private void LayoutAbout()
        {
            int w = _aboutPage.Width;
            int colW = Math.Min(Ui.S(760), Math.Max(Ui.S(300), w - Ui.S(ContentPad) * 2));
            int x = Math.Max(Ui.S(ContentPad), (w - Ui.S(ContentPad) - colW) / 2);
            int tw = _aboutTelegram.PreferredWidth(Ui.S(190));
            _aboutTelegram.SetBounds(x + colW - tw - Ui.S(20), Ui.S(20) + Ui.S(20), tw, Ui.S(33));
            _aboutPage.Invalidate();
        }

        private void SetPage(int page)
        {
            _page = page;
            _navProfiles.Selected = page == 0;
            _navSettings.Selected = page == 1;
            _navAbout.Selected = page == 2;
            _navProfiles.Invalidate();
            _navSettings.Invalidate();
            _navAbout.Invalidate();

            bool profiles = page == 0;
            _host.Visible = profiles && _cards.Count > 0;
            if (!profiles) _picks.Clear();
            _bulk.Visible = profiles && _picks.Count > 1;
            _emptyPanel.Visible = profiles && ShownCount() == 0;
            _settingsPage.Visible = page == 1;
            _aboutPage.Visible = page == 2;
            _search.Visible = profiles;
            _sortBtn.Visible = profiles;
            _newBtn.Visible = profiles;
            _saveSettings.Visible = page == 1;
            _themeSwitch.Visible = true;

            if (_emptyPanel.Visible) _emptyPanel.BringToFront();
            if (_settingsPage.Visible) _settingsPage.BringToFront();
            if (_aboutPage.Visible) _aboutPage.BringToFront();
            _toast.BringToFront();

            if (page == 1)
            {
                _setBrowserPath.Value = _settings.BrowserPath ?? "";
                _setStartUrl.Value = _settings.DefaultStartUrl ?? "";
                _setStoragePath.Text = AppPaths.Root;
                UpdateBrowserInfo();
            }
            LayoutAll();
        }

        private void ReloadCards(bool fromDisk)
        {
            if (fromDisk || _profiles == null || _profiles.Count == 0)
                _profiles = _store.LoadAll();
            ApplySort();

            foreach (var c in _cards.Values) c.Dispose();
            _cards.Clear();
            _grid.Controls.Clear();

            foreach (var p in _profiles)
            {
                string id = p.Id;
                var card = new ProfileCard
                {
                    Model = p,
                    Running = _sessions.ContainsKey(id),
                    Launching = _launching.Contains(id),
                    AccessibleName = p.Name,
                    AccessibleRole = AccessibleRole.PushButton
                };
                card.OpenRequested += delegate { OpenProfile(id); };
                card.FocusRequested += delegate { FocusSession(id); };
                card.CloseRequested += delegate { CloseSession(id); };
                card.EditRequested += delegate { EditProfile(id); };
                card.ProxyRequested += delegate { EditProfile(id, 3); };
                card.DuplicateRequested += delegate { DuplicateProfile(id); };
                card.DeleteRequested += delegate { DeleteProfile(id); };
                card.PickToggled += delegate { TogglePick(id, (ModifierKeys & Keys.Shift) != 0); };
                _grid.Controls.Add(card);
                _cards[id] = card;
            }
            ApplyFilter();
            Invalidate();
        }

        private void ApplyFilter()
        {
            string q = _filter.ToLowerInvariant();
            int shown = 0;
            foreach (var kv in _cards)
            {
                var p = kv.Value.Model;
                bool hit = q.Length == 0
                    || (p.Name ?? "").ToLowerInvariant().Contains(q)
                    || (p.Fp != null && (p.Fp.Os ?? "").ToLowerInvariant().Contains(q))
                    || (p.Fp != null && (p.Fp.Timezone ?? "").ToLowerInvariant().Contains(q))
                    || (p.Fp != null && (p.Fp.Language ?? "").ToLowerInvariant().Contains(q))
                    || (p.Proxy != null && (p.Proxy.Host ?? "").ToLowerInvariant().Contains(q));
                kv.Value.Visible = hit;
                if (hit) shown++;
            }
            for (int i = _picks.Count - 1; i >= 0; i--)
                if (!_cards[_picks[i]].Visible) _picks.RemoveAt(i);
            if (_picks.Count == 0) _pickAnchor = null;
            _grid.LayoutGrid();
            _host.SyncExtent();
            _emptyPanel.Visible = _page == 0 && shown == 0;
            SyncPicks();
            if (_emptyPanel.Visible) _emptyPanel.BringToFront();
            else if (shown > 0 && _page == 0) _host.BringToFront();
            _toast.BringToFront();
            _emptyPanel.Invalidate();
            Invalidate();
        }

        private void EmptyPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            bool searching = _filter.Length > 0;
            float w = _emptyPanel.Width;
            float cy = _emptyPanel.Height / 2f;

            float badge = Ui.S(64);
            var br = new RectangleF((w - badge) / 2f, cy - Ui.S(112), badge, badge);
            Ui.Fill(g, br, Ui.S(8), Theme.AccentOnBg);
            float ic = Ui.S(28);
            Icons.Draw(g, searching ? "search" : "layers",
                new RectangleF(br.X + (badge - ic) / 2f, br.Y + (badge - ic) / 2f, ic, ic), Theme.Accent);

            string title = searching ? "No matching profiles" : "No profiles yet";
            string sub = searching
                ? "Try another name, operating system, language or proxy host."
                : "Create your first identity. Each one is a separate, private browser.";

            Ui.Text(g, title, Theme.H1, Theme.Text, 0, br.Bottom + Ui.S(16), w, "center");
            Ui.Text(g, sub, Theme.Small, Theme.TextDim, 0, br.Bottom + Ui.S(44), w, "center", 2);
        }

        private void NewProfile()
        {
            var p = new Profile();
            FingerprintFactory.Regenerate(p.Fp, "System default", null);
            using (var dlg = new EditorForm(p, true, _settings))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _store.Save(dlg.Result);
                ReloadCards(true);
                Toast("Profile created", ToastKind.Success);
            }
        }

        private async void EditProfile(string id, int tab = 0)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            using (var dlg = new EditorForm(p, false, _settings, tab))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var updated = dlg.Result;
                _store.Save(updated);
                ReloadCards(true);

                BrowserSession s;
                if (_sessions.TryGetValue(id, out s) && s.IsRunning)
                {
                    s.UpdateProfile(updated);
                    bool live = false;
                    try { live = await s.ApplyLiveAsync(); } catch (Exception ex) { Log.Error("Live apply failed", ex); }
                    Toast(live ? "Applied to the running browser" : "Saved. Reopen the browser to apply",
                        live ? ToastKind.Success : ToastKind.Info);
                }
                else Toast("Profile updated", ToastKind.Success);
            }
        }

        private void DuplicateProfile(string id) { DuplicateProfile(id, true); }

        private void DuplicateProfile(string id, bool announce)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            var copy = p.Copy(true);
            copy.Name = p.Name + " copy";
            copy.Proxy.Password = p.Proxy.Password;
            FingerprintFactory.Regenerate(copy.Fp, copy.Fp.Os, copy.Proxy);
            try
            {
                _store.Save(copy);
                if (!announce) return;
                ReloadCards(true);
                Toast("Profile duplicated", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("Duplicate failed", ex);
                Toast("Could not duplicate profile", ToastKind.Error);
            }
        }

        /* ---- Sorting ---- */
        private static readonly string[] SortNames = { "recent", "name", "created", "running", "os" };

        private int SortIndex { get { int i = _settings.SortBy; return (i < 0 || i >= SortNames.Length) ? 0 : i; } }

        private void ApplySort()
        {
            switch (SortIndex)
            {
                case 1: _profiles = _profiles.OrderBy(p => p.Name ?? "", StringComparer.OrdinalIgnoreCase).ToList(); break;
                case 2: _profiles = _profiles.OrderByDescending(p => p.Created).ToList(); break;
                case 3: _profiles = _profiles
                    .OrderByDescending(p => _sessions.ContainsKey(p.Id))
                    .ThenBy(p => p.Name ?? "", StringComparer.OrdinalIgnoreCase).ToList(); break;
                case 4: _profiles = _profiles
                    .OrderBy(p => p.Fp == null ? "" : p.Fp.Os ?? "")
                    .ThenBy(p => p.Name ?? "", StringComparer.OrdinalIgnoreCase).ToList(); break;
            }
            if (_sortBtn != null) _sortBtn.Text = "Sort: " + SortNames[SortIndex];
        }

        private void CycleSort()
        {
            _settings.SortBy = (SortIndex + 1) % SortNames.Length;
            try { _store.SaveSettings(_settings); } catch (Exception ex) { Log.Error("Sort preference not saved", ex); }
            ReloadCards(false);
        }

        /* ---- Multi-select ---- */
        private List<ProfileCard> VisibleCards()
        {
            var order = new List<ProfileCard>();
            foreach (var kv in _cards) if (kv.Value.Visible) order.Add(kv.Value);
            order.Sort(delegate (ProfileCard a, ProfileCard b)
            {
                int c = a.Top.CompareTo(b.Top);
                return c != 0 ? c : a.Left.CompareTo(b.Left);
            });
            return order;
        }

        /// <summary>Neighbour in a direction, scored so the nearest card in the same row or column wins.</summary>
        private static ProfileCard NextInDir(List<ProfileCard> cards, ProfileCard from, Keys key)
        {
            int dx = key == Keys.Right ? 1 : key == Keys.Left ? -1 : 0;
            int dy = key == Keys.Down ? 1 : key == Keys.Up ? -1 : 0;
            float cx = from.Left + from.Width / 2f, cy = from.Top + from.Height / 2f;
            ProfileCard best = null;
            float bestScore = float.MaxValue;
            foreach (var c in cards)
            {
                if (c == from) continue;
                float tx = c.Left + c.Width / 2f - cx;
                float ty = c.Top + c.Height / 2f - cy;
                float along = tx * dx + ty * dy;
                if (along <= 0) continue;
                float score = along + (dx != 0 ? Math.Abs(ty) : Math.Abs(tx)) * 4f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        private void TogglePick(string id, bool extend)
        {
            var order = VisibleCards();
            int idx = order.FindIndex(c => c.Model.Id == id);
            if (idx < 0) return;

            int anchor = _pickAnchor == null ? -1 : order.FindIndex(c => c.Model.Id == _pickAnchor);
            if (extend && anchor >= 0)
            {
                int a = Math.Min(anchor, idx), b = Math.Max(anchor, idx);
                for (int i = a; i <= b; i++) AddPick(order[i].Model.Id);
            }
            else
            {
                if (_picks.Contains(id)) _picks.Remove(id); else _picks.Add(id);
                _pickAnchor = id;
            }
            SyncPicks();
        }

        private void AddPick(string id) { if (!_picks.Contains(id)) _picks.Add(id); }

        private void SelectAllVisible()
        {
            _picks.Clear();
            foreach (var c in VisibleCards()) AddPick(c.Model.Id);
            _pickAnchor = null;
            SyncPicks();
        }

        private void ClearPicks()
        {
            _picks.Clear();
            _pickAnchor = null;
            SyncPicks();
        }

        private void SyncPicks()
        {
            foreach (var kv in _cards) kv.Value.Picked = _picks.Contains(kv.Key);
            LayoutBulk();
            _grid.Invalidate();
        }

        private void LayoutBulk()
        {
            if (_bulk == null || _host == null) return;
            if (_picks.Count < 2 || _page != 0) { _bulk.Visible = false; return; }

            _bulkCount.Text = _picks.Count + " selected";
            int x = Ui.S(16);
            _bulkCount.SetBounds(x, Ui.S(19), _bulkCount.PreferredSize.Width, Ui.S(20));
            x = _bulkCount.Right + Ui.S(14);
            foreach (var b in new[] { _bulkOpen, _bulkStop, _bulkDup, _bulkDel, _bulkCancel })
            {
                int bw = b.PreferredWidth(Ui.S(40));
                b.SetBounds(x, Ui.S(13), bw, Ui.S(31));
                x += bw + Ui.S(8);
            }
            int width = x - Ui.S(8) + Ui.S(16);
            int height = Ui.S(57);
            _bulk.SetBounds(_host.Left + Math.Max(Ui.S(6), (_host.Width - width) / 2),
                Math.Max(_host.Top + Ui.S(6), _host.Bottom - height - Ui.S(16)), width, height);
            _bulk.BringToFront();
            _bulk.Visible = true;
        }

        private void BulkOpen()
        {
            var ids = _picks.ToArray();
            int started = 0;
            foreach (string id in ids)
            {
                if (_sessions.ContainsKey(id) || _launching.Contains(id)) continue;
                OpenProfile(id);
                started++;
            }
            if (started > 5) Toast(started + " browsers are starting; give this machine a moment", ToastKind.Info);
            ClearPicks();
        }

        private void BulkStop()
        {
            foreach (string id in _picks.ToArray()) CloseSession(id);
            ClearPicks();
        }

        private void BulkDuplicate()
        {
            foreach (string id in _picks.ToArray()) DuplicateProfile(id, false);
            ReloadCards(true);
            ClearPicks();
            Toast("Profiles duplicated", ToastKind.Success);
        }

        private void BulkDelete()
        {
            var list = _profiles.Where(p => _picks.Contains(p.Id)).ToList();
            if (list.Count == 0) { ClearPicks(); return; }
            if (!ConfirmForm.Ask(this, "Delete profiles",
                "Delete " + list.Count + " profiles and all of their browsing data? This cannot be undone.",
                "Proxy settings stored in them are removed too."))
                return;
            foreach (var p in list) DeleteProfile(p.Id, false);
            ReloadCards(true);
            ClearPicks();
            Toast(list.Count + " profiles deleted", ToastKind.Success);
        }

        private void DeleteProfile(string id) { DeleteProfile(id, true); }

        private void DeleteProfile(string id, bool ask)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            if (ask && !ConfirmForm.Ask(this, "Delete profile",
                "Delete \"" + p.Name + "\" and all of its browsing data? This cannot be undone.",
                p.Proxy.Enabled ? "The linked proxy settings are removed too." : null))
                return;

            BrowserSession s;
            if (_sessions.TryGetValue(id, out s))
            {
                _sessions.Remove(id);
                try { s.Close(); } catch { }
            }
            try
            {
                _store.Delete(p);
                _profiles = _store.LoadAll();
                if (!ask) return;
                ReloadCards(false);
                Toast("Profile deleted", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("Delete failed", ex);
                Toast("Could not delete the profile", ToastKind.Error);
            }
        }

        private void FocusSession(string id)
        {
            BrowserSession s;
            if (_sessions.TryGetValue(id, out s)) { try { s.Focus(); } catch { } }
        }

        private void CloseSession(string id)
        {
            if (!_sessions.ContainsKey(id)) return;
            BrowserSession s = _sessions[id];
            _sessions.Remove(id);
            try { s.Close(); } catch (Exception ex) { Log.Error("Close session failed", ex); }
            RefreshCardState(id);
            Toast("Browser closed", ToastKind.Info);
        }

        private async void OpenProfile(string id)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;

            BrowserSession existing;
            if (_sessions.TryGetValue(id, out existing)) { existing.Focus(); return; }
            if (_launching.Contains(id)) return;

            _launching.Add(id);
            RefreshCardState(id);
            if (!_pulse.Enabled) _pulse.Start();
            try
            {
                if (!BrowserFetcher.IsInstalled(p.BrowserKind) && BrowserFetcher.Supports(p.BrowserKind))
                {
                    string label = BrowserFetcher.Label(p.BrowserKind);
                    bool go = ConfirmForm.Ask(this, "Get " + label, label + " is not installed on this PC.",
                        "Download and install it now? The profile opens with it as soon as it is ready.",
                        "Download", BtnKind.Primary);
                    if (!go) return;
                    if (!await DownloadBrowserAsync(p.BrowserKind)) return;
                }

                var session = await BrowserSession.LaunchAsync(p, _settings.DefaultStartUrl, _settings.BrowserPath);
                _sessions[id] = session;
                session.Closed += delegate { SessionClosed(id); };
                _store.Touch(p);
                if (_cards.ContainsKey(id))
                {
                    _cards[id].Model = p;
                    _cards[id].Invalidate();
                }
                Toast("\u201c" + p.Name + "\u201d is ready", ToastKind.Success);
                Invalidate();
            }
            catch (Exception ex)
            {
                Log.Error("Open profile failed", ex);
                if (!string.IsNullOrEmpty(ex.Message) && ex.Message.StartsWith("No browser found", StringComparison.Ordinal)
                    && ConfirmForm.Ask(this, "Get Chromium",
                        "No browser is installed on this PC.",
                        "Download the official Chromium browser now? (~180 MB, one-time)", "Download", BtnKind.Primary))
                {
                    bool ok = await DownloadBrowserAsync("chromium");
                    if (ok)
                    {
                        _launching.Remove(id);
                        RefreshCardState(id);
                        OpenProfile(id);
                        return;
                    }
                }
                else
                {
                    Toast(ShortError(ex), ToastKind.Error);
                }
            }
            finally
            {
                _launching.Remove(id);
                RefreshCardState(id);
            }
        }

        private async Task<bool> DownloadBrowserAsync(string engine)
        {
            string label = BrowserFetcher.Label(engine);
            if (BrowserFetcher.IsInstalled(engine))
            {
                Toast(label + " is already installed", ToastKind.Info);
                return true;
            }

            var dlg = new DownloadDialog("Get " + label);
            dlg.Show(this);
            dlg.Activate();
            try
            {
                string exe = await BrowserFetcher.InstallAsync(engine,
                    (pct, msg) => dlg.UpdateProgress(pct, msg), dlg.Token).ConfigureAwait(true);
                dlg.Close();
                UpdateBrowserInfo();
                Toast(label + " is ready", ToastKind.Success);
                return true;
            }
            catch (OperationCanceledException)
            {
                dlg.Close();
                Toast("Download cancelled", ToastKind.Info);
            }
            catch (Exception ex)
            {
                dlg.Close();
                Log.Error(label + " download failed", ex);
                Toast(ShortError(ex), ToastKind.Error);
            }
            return false;
        }

        private static string ShortError(Exception ex)
        {
            string m = ex.Message ?? "Could not open the profile";
            int i = m.IndexOf("\r\n", StringComparison.Ordinal);
            if (i < 0) i = m.IndexOf('\n');
            if (i > 0) m = m.Substring(0, i);
            if (m.Length > 140) m = m.Substring(0, 137) + "…";
            return m;
        }

        private void RefreshCardState(string id)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action<string>(RefreshCardState), id); return; }
                ProfileCard c;
                if (_cards.TryGetValue(id, out c))
                {
                    c.Running = _sessions.ContainsKey(id);
                    c.Launching = _launching.Contains(id);
                    c.Invalidate();
                }
                Invalidate();
            }
            catch { }
        }

        private void SessionClosed(string id)
        {
            try
            {
                if (IsDisposed || Disposing) return;
                if (InvokeRequired) { BeginInvoke(new Action<string>(SessionClosed), id); return; }
                _sessions.Remove(id);
                RefreshCardState(id);
                Invalidate();
            }
            catch { }
        }

        private void Toast(string message, ToastKind kind)
        {
            try { _toast.Show(message, kind); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_pulse != null) _pulse.Stop();
                foreach (var s in _sessions.Values.ToList())
                {
                    try { s.Close(); } catch { }
                }
                _sessions.Clear();
            }
            catch { }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { NewProfile(); return true; }
            if (keyData == (Keys.Control | Keys.F) || keyData == (Keys.Control | Keys.K))
            {
                SetPage(0);
                _search.Box.Focus();
                _search.Box.SelectAll();
                return true;
            }
            if (keyData == Keys.F5 && _page == 0) { ReloadCards(true); return true; }
            if (keyData == (Keys.Control | Keys.O) && _page == 0) { CycleSort(); return true; }
            if (keyData == (Keys.Control | Keys.A) && _page == 0) { SelectAllVisible(); return true; }
            if (keyData == Keys.Escape && _page == 0 && _picks.Count > 0) { ClearPicks(); return true; }
            if (keyData == Keys.Escape && _page == 0 && _filter.Length > 0)
            {
                _search.Value = "";
                _filter = "";
                ApplyFilter();
                return true;
            }
            if (_page == 0 && (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down))
            {
                var order = VisibleCards();
                if (order.Count > 0)
                {
                    var current = ActiveControl as ProfileCard;
                    var next = current == null ? order[0] : NextInDir(order, current, keyData);
                    if (next != null) { next.Focus(); return true; }
                }
            }
            if (keyData == Keys.F2 && _page == 0)
            {
                var focused = ActiveControl as ProfileCard;
                if (focused != null && focused.Model != null) { EditProfile(focused.Model.Id); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AboutPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            int w = _aboutPage.Width;
            int colW = Math.Min(Ui.S(760), Math.Max(Ui.S(300), w - Ui.S(ContentPad) * 2));
            int x = Math.Max(Ui.S(ContentPad), (w - Ui.S(ContentPad) - colW) / 2);

            // identity card
            var card = new RectangleF(x, Ui.S(8), colW, Ui.S(112));
            Ui.Fill(g, card, Ui.S(8), Theme.Panel);
            Ui.Stroke(g, card, Ui.S(8), Theme.Line, 1f);

            var mark = new RectangleF(card.X + Ui.S(20), card.Y + Ui.S(24), Ui.S(56), Ui.S(56));
            Logo.Draw(g, mark);

            float tx = mark.Right + Ui.S(18);
            Ui.Text(g, "Joe Browser", Theme.Display, Theme.Text, tx, card.Y + Ui.S(20), colW - (tx - card.X) - Ui.S(24));
            Ui.Text(g, "Version 1.0.0 · MIT License", Theme.Small, Theme.TextDim, tx, card.Y + Ui.S(50),
                colW - (tx - card.X) - Ui.S(24));
            Ui.Text(g, AppPaths.Root, Theme.Caption, Theme.TextFaint, tx, card.Y + Ui.S(72),
                colW - (tx - card.X) - Ui.S(24));

            // feature cards
            float sectY = card.Bottom + Ui.S(24);
            Ui.Text(g, "FEATURES", Theme.Micro, Theme.TextFaint, x + Ui.S(2), sectY, colW - Ui.S(4));

            string[][] features =
            {
                new[] { "user", "Unlimited profiles", "Separate cookies, storage and sessions." },
                new[] { "fingerprint", "Coherent fingerprints", "Canvas, WebGL, timezone and locale aligned." },
                new[] { "lock", "Local and encrypted", "No account, no cloud. Every byte stays local." }
            };

            float cardY = sectY + Ui.S(26);
            float gap = Ui.S(16);
            int cols = colW >= Ui.S(560) ? 3 : 1;
            float cardH = cols == 3 ? Ui.S(92) : Ui.S(64);
            float cardW = (colW - gap * (cols - 1)) / cols;
            for (int i = 0; i < features.Length; i++)
            {
                int col = cols == 3 ? i : 0;
                int row = cols == 3 ? 0 : i;
                var r = new RectangleF(x + col * (cardW + gap), cardY + row * (cardH + gap), cardW, cardH);
                Ui.Fill(g, r, Ui.S(8), Theme.Card);
                Ui.Stroke(g, r, Ui.S(8), Theme.Line, 1f);

                float chip = Ui.S(26);
                var cr = new RectangleF(r.X + Ui.S(14), r.Y + Ui.S(14), chip, chip);
                Ui.Fill(g, cr, Ui.S(6), Theme.AccentOnBg);
                float ic = Ui.S(16);
                Icons.Draw(g, features[i][0], new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

                Ui.Text(g, features[i][1], Theme.H3, Theme.Text, cr.Right + Ui.S(10),
                    cr.Y + (chip - Theme.H3.GetHeight(g)) / 2f, r.Width - (cr.Right - r.X) - Ui.S(22));

                if (cols == 3)
                    Ui.Text(g, features[i][2], Theme.Caption, Theme.TextFaint, r.X + Ui.S(14), r.Y + Ui.S(54), r.Width - Ui.S(28), "left", 2);
                else
                    Ui.Text(g, features[i][2], Theme.Caption, Theme.TextFaint, cr.Right + Ui.S(10),
                        cr.Y + chip + Ui.S(4), r.Width - (cr.Right - r.X) - Ui.S(22));
            }

        }
    }
}
