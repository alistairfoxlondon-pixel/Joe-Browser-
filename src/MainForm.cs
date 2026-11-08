using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
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
        private List<ProxyEntry> _proxies = new List<ProxyEntry>();
        private List<ExtensionItem> _extensions = new List<ExtensionItem>();

        // Sidebar
        private JBButton _navProfiles, _navHistory, _navAccount, _navProxies, _navExtensions, _navBrowsers, _navSettings, _navTools, _navAbout, _navPlan;
        private JBLabel _navGroupWorkspace, _navGroupSystem;
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
        private HistoryPage _historyPage;
        private AccountPage _accountPage;
        private Panel _aboutColumn;
        private JBScrollHost _aboutHost;
        private ToolsPage _toolsPage;
        private ProxyPage _proxyPage;
        private ExtensionPage _extensionPage;
        private BrowsersPage _browsersPage;
        private UpgradePage _upgradePage;
        private Timer _planTimer;
        private int _payTicks;
        private bool _payBusy;
        private const int PayPollSeconds = 8;
        private JBButton _emptyBtn, _clearFilter, _aboutTelegram;
        private JBToast _toast;

        // Settings controls
        private JBLabel _setBrowserInfo, _setStoragePath;
        private JBTextBox _setBrowserPath, _setStartUrl, _setDefaultGroup;
        private JBButton _setBrowse, _setDetect, _setOpenData, _setDownload, _setClearCache, _setReport;
        private JBLabel _setKernelInfo;
        private JBLabel _setAccountWho;
        private JBTextBox _setGoogleId;
        private JBTextBox _setGoogleSecret;
        private JBButton _setSecurity, _setSignOut, _setGoogleGuide, _setGoogleTest;
        private JBLangPicker _setLang;
        private JBToggle _setExtensions, _setCustomExt, _setTrash, _setHistory;
        private JBButton _setClearHistory;
        private Panel _settingsColumn;
        private JBScrollHost _settingsHost;
        private readonly List<SetCard> _setCards = new List<SetCard>();

        /// <summary>A full-width settings section: a title plus rows separated by hairlines.</summary>
        private sealed class SetCard
        {
            public JBPanel Panel;
            public string Key;
            public readonly List<SetRow> Rows = new List<SetRow>();
        }

        /// <summary>One settings row: label on the left, optional value text and controls flush right.</summary>
        private sealed class SetRow
        {
            public JBLabel Title;
            public string Key;
            public JBLabel Value;
            public Control[] Controls;
            public int[] Widths;
        }

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
            /* The store asks this whenever a page navigates; a null provider would silently
               record nothing. Bound to the live settings object so the toggle in Settings takes
               effect on the next page without a restart. */
            HistoryStore.EnabledProvider = delegate { return _settings == null || _settings.HistoryEnabled; };
            L10n.Set(_settings.UiLanguage);
            Theme.SetMode(_settings.ThemeMode != 2);
            _proxies = ProxyVault.Load();
            _extensions = ExtensionCatalog.Load();
            Build();
            L10n.Changed += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(OnLanguageChanged)); return; }
            ApplyLanguage();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyScale();
            ResolveAutoBrowserKind();
            ReloadCards(true);
            SetPage(0);
            LayoutAll();
            ShowInTaskbar = true;
            _search.Box.AccessibleName = "Search profiles";

            /* Running elevated is the single most common cause of "it behaves strangely"
               reports: Windows Defender prompts for the elevated process, the bundled
               Chromium cannot start its sandbox, and browsers launched from an elevated app
               inherit the administrator token. Tell the user once, in plain words. */
            if (SystemProbe.IsElevated())
            {
                Log.Info("Started elevated; recommending a normal (non-administrator) launch.");
                Toast("Running as administrator. Close Joe Browser and open it normally - "
                      + "elevated browsers can trigger security prompts and behave worse.",
                      ToastKind.Warn);
            }
            else
            {
                /* A full drive is the second most common cause of "the browser will not open":
                   Chromium needs room for its cache and profile data and fails obscurely
                   without it. Warn while there is still time to act. */
                long free = DiskSpace.Free(AppPaths.Root);
                if (free >= 0 && free < 500L * 1024 * 1024)
                {
                    Log.Info("Low disk space: " + DiskSpace.Human(free) + " free.");
                    Toast("Only " + DiskSpace.Human(free) + " free on this drive. Browsers need room "
                          + "for their cache. Free some space to avoid failed launches.",
                          ToastKind.Warn);
                }
            }
        }

        /// <summary>
        /// Profiles created with "auto" detection have no engine recorded, so the card would show
        /// a placeholder. Resolve the engine once and let the marks draw the real browser logo.
        /// </summary>
        private void ResolveAutoBrowserKind()
        {
            try
            {
                var info = BrowserLocator.Detect("auto", _settings.BrowserPath);
                BrowserMark.AutoKind = info.Found ? info.EngineKey : "";
            }
            catch (Exception ex) { Log.Error("Auto engine lookup failed", ex); }
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

            // The app is a workspace, not a dialog: it opens across the whole working area and
            // remembers the size it was closed at. Nothing is capped at a fixed 1280 box.
            var area = Screen.FromControl(this).WorkingArea;
            int savedW = _settings.WinWidth > 0 ? Ui.S(_settings.WinWidth) : 0;
            int savedH = _settings.WinHeight > 0 ? Ui.S(_settings.WinHeight) : 0;
            int wantW = Math.Max(MinimumSize.Width, savedW > 0 ? savedW : Ui.S(1440));
            int wantH = Math.Max(MinimumSize.Height, savedH > 0 ? savedH : Ui.S(900));
            wantW = Math.Min(wantW, area.Width - Ui.S(16));
            wantH = Math.Min(wantH, area.Height - Ui.S(16));

            SuppressSurfaceEvents(() => ClientSize = new Size(wantW, wantH));
            CenterToScreen();
            if (_settings.WinMaximized) Maximize();
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

        /// <summary>Small caption above a cluster of navigation items.</summary>
        private JBLabel NavGroup(string text)
        {
            var l = new JBLabel(LabelRole.Faint, text.ToUpperInvariant());
            l.AutoEllipsis = false;
            return l;
        }

        private void Build()
        {
            /* History and Account sit straight after Profiles: they are things about the
               workspace the user is looking at, not settings for how the app behaves. */
            _navProfiles = Nav(L10n.T("nav.profiles"), "layers", delegate { SetPage(0); });
            _navHistory = Nav(L10n.T("nav.history"), "clock", delegate { SetPage(8); });
            _navAccount = Nav(L10n.T("nav.account"), "user", delegate { SetPage(9); });
            _navProxies = Nav(L10n.T("nav.proxies"), "proxy", delegate { SetPage(1); });
            _navExtensions = Nav(L10n.T("nav.extensions"), "apps", delegate { SetPage(2); });
            _navBrowsers = Nav(L10n.T("nav.browsers"), "window", delegate { SetPage(3); });
            _navSettings = Nav(L10n.T("nav.settings"), "sliders", delegate { SetPage(4); });
            _navTools = Nav(L10n.T("header.tools"), "command", delegate { SetPage(5); });
            _navAbout = Nav(L10n.T("nav.about"), "info", delegate { SetPage(6); });
            _navPlan = Nav(L10n.T("nav.plan"), "crown", delegate { SetPage(7); });

            _navGroupWorkspace = NavGroup(L10n.T("nav.workspace"));
            _navGroupSystem = NavGroup(L10n.T("nav.system"));

            _themeSwitch = new JBThemeSwitch { Checked = Theme.Dark };
            _themeSwitch.NightText = L10n.T("theme.night");
            _themeSwitch.DayText = L10n.T("theme.day");
            _themeSwitch.CheckedChanged += delegate { ToggleTheme(); };

            _search = new JBTextBox { Icon = "search", Placeholder = L10n.T("btn.search"), Height = Ui.S(33) };
            _search.Box.TextChanged += delegate
            {
                _filter = (_search.Value ?? "").Trim();
                ApplyFilter();
            };

            _newBtn = new JBButton { Kind = BtnKind.Primary, Icon = "plus", Text = L10n.T("btn.new"), Radius = 4, Height = Ui.S(33) };
            _newBtn.Click += delegate { NewProfile(); };

            _sortBtn = new JBButton { Text = "Sort: recent", Radius = 4, Height = Ui.S(33), Tip = "Change the order of the list (Ctrl+O)" };
            _sortBtn.Click += delegate { CycleSort(); };

            _bulk = new JBPanel { Visible = false };
            _bulkCount = new Label { AutoSize = true, Font = Theme.H3, ForeColor = Theme.Text, BackColor = Color.Transparent };
            _bulkOpen = new JBButton { Kind = BtnKind.Primary, Icon = "play", Text = L10n.T("btn.openall"), Radius = 4, Height = Ui.S(31) };
            _bulkStop = new JBButton { Kind = BtnKind.Soft, Icon = "power", Text = L10n.T("btn.stop"), Radius = 4, Height = Ui.S(31) };
            _bulkDup = new JBButton { Kind = BtnKind.Soft, Icon = "copy", Text = L10n.T("btn.duplicate"), Radius = 4, Height = Ui.S(31) };
            _bulkDel = new JBButton { Kind = BtnKind.Danger, Icon = "trash", Text = L10n.T("btn.delete"), Radius = 4, Height = Ui.S(31) };
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

            _saveSettings = new JBButton { Kind = BtnKind.Primary, Icon = "check", Text = L10n.T("btn.save"), Radius = 4, Height = Ui.S(33) };
            _saveSettings.Click += delegate { SaveSettings(); };

            _minBtn = new JBIconButton { Icon = "min", IconSize = 11, Caption = true, Tip = "Minimize" };
            _maxBtn = new JBIconButton { Icon = "max", IconSize = 11, Caption = true, Tip = "Maximize" };
            _closeBtn = new JBIconButton { Icon = "close", IconSize = 12, Caption = true, Danger = true, Tip = "Close" };
            _minBtn.Click += delegate { WindowState = FormWindowState.Minimized; };
            _maxBtn.Click += delegate { ToggleMaximize(); };
            _closeBtn.Click += delegate { Close(); };

            _grid = new JBGrid { MinCell = 360, CellHeight = 200, Gap = 16 };
            _host = new JBScrollHost(_grid);

            _emptyPanel = new Panel { BackColor = Theme.Bg, Visible = false };
            _emptyPanel.Paint += EmptyPaint;
            _emptyBtn = new JBButton { Kind = BtnKind.Primary, Icon = "plus", Text = L10n.T("empty.action"), Radius = 4, Height = Ui.S(33) };
            _emptyBtn.Click += delegate
            {
                if (_filter.Length > 0) { ClearSearch(); }
                else NewProfile();
            };
            _clearFilter = new JBButton { Kind = BtnKind.Soft, Icon = "close", Text = "Clear search", Radius = 4, Height = Ui.S(33), Visible = false };
            _clearFilter.Click += delegate { ClearSearch(); };
            _emptyPanel.Controls.Add(_emptyBtn);
            _emptyPanel.Controls.Add(_clearFilter);

            _toast = new JBToast();

            _settingsColumn = new Panel { BackColor = Theme.Bg };
            BuildSettingsPage(_settingsColumn);
            _settingsHost = new JBScrollHost(_settingsColumn);
            _settingsPage = new Panel { BackColor = Theme.Bg, Visible = false };
            _settingsPage.Controls.Add(_settingsHost);

            _toolsPage = new ToolsPage { Visible = false };
            _toolsPage.ActionRequested += delegate(object sender, ToolsActionEventArgs e) { ToolsAction(e.Key); };

            _proxyPage = new ProxyPage { Visible = false };
            _proxyPage.ActionRequested += delegate(object sender, ProxyActionEventArgs e) { ProxyAction(e.Key, e.Index); };
            _proxyPage.SetItems(_proxies);

            _extensionPage = new ExtensionPage { Visible = false };
            _extensionPage.ActionRequested += delegate(object sender, ExtensionActionEventArgs e) { ExtensionAction(e.Key, e.Index); };
            _extensionPage.SetItems(_extensions);

            _browsersPage = new BrowsersPage { Visible = false };
            _browsersPage.ActionRequested += delegate(object sender, BrowserActionEventArgs e) { BrowserAction(e.Key, e.Engine); };

            /* History opens on the last profile that was opened, which is the one a user is
               most likely to have forgotten a URL in. */
            _accountPage = new AccountPage { Visible = false };
            _accountPage.SignOutRequested += delegate { SignOut(); };
            _accountPage.SwitchRequested += delegate { SwitchAccount(); };
            _accountPage.PinRequested += delegate { ShowSecurity(); };
            _accountPage.TwoFactorRequested += delegate { ShowSecurity(); };
            _accountPage.ManagePlanRequested += delegate { SetPage(7); };
            _accountPage.DeleteAccountRequested += delegate(object sender, string email) { RemoveAccount(email); };

            _historyPage = new HistoryPage { Visible = false };
            _historyPage.Back += delegate { SetPage(0); };
            _historyPage.ClearRequested += delegate { ClearHistory(); };
            _historyPage.OpenRequested += delegate(object sender, string url) { OpenHistoryUrl(url); };

            _upgradePage = new UpgradePage { Visible = false };
            _upgradePage.ActionRequested += delegate(object sender, UpgradeActionEventArgs e) { PlanAction(e.Key); };
            _planTimer = new Timer { Interval = 1000 };
            _planTimer.Tick += delegate
            {
                if (_page != 7) return;
                _upgradePage.TickTimer();
                /* The chain is watched while the page is open, so a payment confirms on its own.
                   The manual button stays for someone who wants an answer immediately. */
                if (!Plan.IsPro && _upgradePage.Current != null && _upgradePage.Current.Left > TimeSpan.Zero
                    && !_payBusy && ++_payTicks >= PayPollSeconds)
                {
                    _payTicks = 0;
                    var _ = CheckPaymentAsync(true);
                }
            };

            // The About page grows past a short window, so it scrolls like the settings page.
            _aboutPage = new Panel { BackColor = Theme.Bg, Visible = false };
            _aboutColumn = new Panel { BackColor = Theme.Bg };
            _aboutColumn.Paint += AboutPaint;
            _aboutHost = new JBScrollHost(_aboutColumn);
            _aboutTelegram = new JBButton { Kind = BtnKind.Soft, BrandIcon = "telegram", Text = "@joegoldberg2025", Radius = 4, Height = Ui.S(33) };
            _aboutTelegram.Click += delegate { OpenUrl("https://t.me/joegoldberg2025"); };
            _aboutColumn.Controls.Add(_aboutTelegram);
            _aboutPage.Controls.Add(_aboutHost);

            Controls.Add(_navGroupWorkspace);
            Controls.Add(_navGroupSystem);
            Controls.Add(_navProfiles);
            Controls.Add(_navHistory);
            Controls.Add(_navAccount);
            Controls.Add(_navProxies);
            Controls.Add(_navExtensions);
            Controls.Add(_navBrowsers);
            Controls.Add(_navSettings);
            Controls.Add(_navTools);
            Controls.Add(_navAbout);
            Controls.Add(_navPlan);
            Controls.Add(_themeSwitch);
            Controls.Add(_search);
            Controls.Add(_sortBtn);
            Controls.Add(_newBtn);
            Controls.Add(_saveSettings);
            Controls.Add(_bulk);
            Controls.Add(_host);
            Controls.Add(_emptyPanel);
            Controls.Add(_settingsPage);
            Controls.Add(_toolsPage);
            Controls.Add(_proxyPage);
            Controls.Add(_extensionPage);
            Controls.Add(_browsersPage);
            Controls.Add(_upgradePage);
            Controls.Add(_historyPage);
            Controls.Add(_accountPage);
            Controls.Add(_aboutPage);
            Controls.Add(_toast);
            Controls.Add(_minBtn);
            Controls.Add(_maxBtn);
            Controls.Add(_closeBtn);

            _pulse = new Timer { Interval = 90 };
            _pulse.Tick += delegate
            {
                /* The running-card animation is cosmetic, so it stops costing anything the moment
                   the window cannot be seen: hidden, minimised or behind another app. */
                if (_sessions.Count == 0) { _pulse.Stop(); return; }
                if (!Visible || WindowState == FormWindowState.Minimized) return;
                foreach (var c in _cards.Values) if (c.Running) c.Invalidate();
            };

            _built = true;
            ApplyLanguage();
            RefreshPlanState();
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Open url failed", ex); Toast("Could not open the link", ToastKind.Error); }
        }

        /* ------------------------------------------------------------------ *
         * Interface language. Codes are storage keys only - the picker always
         * shows a circular flag and the full language name.
         * ------------------------------------------------------------------ */

        private static IEnumerable<LangItem> UiLangItems()
        {
            var list = new List<LangItem>();
            foreach (var l in L10n.Languages)
            {
                /* Only languages with a real translation table are offered: the rest would save
                   a preference and change nothing on screen. */
                if (!L10n.IsTranslated(l.Key)) continue;
                string subtitle = (l.Native.Equals(l.English, StringComparison.OrdinalIgnoreCase)
                    ? "" : l.Native);
                /* Say how complete the translation is: a half-translated interface without a
                   word of warning reads as a broken app rather than a work in progress. */
                int coverage = L10n.Coverage(l.Key);
                if (l.Key != "en" && coverage < 100)
                    subtitle = (subtitle.Length == 0 ? "" : subtitle + "  ·  ") + coverage + "% translated";
                list.Add(new LangItem(l.Key, l.Flag, l.English, subtitle));
            }
            return list;
        }

        private void ApplyLanguage()
        {
            if (!_built) return;

            _navProfiles.Text = L10n.T("nav.profiles");
            _navProxies.Text = L10n.T("nav.proxies");
            _navExtensions.Text = L10n.T("nav.extensions");
            _navBrowsers.Text = L10n.T("nav.browsers");
            _navSettings.Text = L10n.T("nav.settings");
            _navTools.Text = L10n.T("header.tools");
            _navAbout.Text = L10n.T("nav.about");
            _navPlan.Text = L10n.T("nav.plan");
            _navHistory.Text = L10n.T("nav.history");
            _navAccount.Text = L10n.T("nav.account");
            _themeSwitch.NightText = L10n.T("theme.night");
            _themeSwitch.DayText = L10n.T("theme.day");
            _themeSwitch.Invalidate();

            _search.Placeholder = L10n.T("btn.search");
            _newBtn.Text = L10n.T("btn.new");
            _saveSettings.Text = L10n.T("btn.save");
            _bulkOpen.Text = L10n.T("btn.openall");
            _bulkStop.Text = L10n.T("btn.stop");
            _bulkDup.Text = L10n.T("btn.duplicate");
            _bulkDel.Text = L10n.T("btn.delete");
            _emptyBtn.Text = L10n.T("empty.action");

            foreach (var card in _setCards)
            {
                var lbl = card.Panel.Tag as JBLabel;
                if (lbl != null && lbl.Text != L10n.T(card.Key)) lbl.Text = L10n.T(card.Key);
                foreach (var r in card.Rows) r.Title.Text = L10n.T(r.Key);
            }
            _setBrowse.Text = L10n.T("btn.browse");
            _setDetect.Text = L10n.T("btn.detect");
            _setOpenData.Text = L10n.T("btn.opendata");
            _setDownload.Text = L10n.T("btn.download");
            _setDefaultGroup.Placeholder = L10n.T("set.group.hint");

            _toolsPage.ApplyText();
            _emptyPanel.Invalidate();
            _aboutColumn.Invalidate();
            /* The empty state carries text too, and it is the first thing a new user sees: it
               was the one place the new language did not reach until the window was resized. */
            if (_clearFilter != null) _clearFilter.Text = "Clear search";
            FreezeRedraw(LayoutAll);
        }

        /* ------------------------------------------------------------------ *
         * Tools & data - batch / maintenance operations, all local.
         * ------------------------------------------------------------------ */

        private void ToolsAction(string key)
        {
            try
            {
                switch (key)
                {
                    case "batch": BatchCreate(); break;
                    case "import": ImportProfiles(); break;
                    case "export": ExportProfiles(); break;
                    case "proxy": ProxyBatchTest(); break;
                    case "net": NetworkDiagnostics(); break;
                    case "cookies": CookieManager(); break;
                    case "ext": OpenExtensionFolder(); break;
                    case "trash": RecycleBin(); break;
                    case "shortcuts": ShortcutHelp(); break;
                    case "clean": ClearCaches(); break;
                    case "audit": AccountAudit(); break;
                    case "report": Report.OpenForm(this, ""); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Tools action failed: " + key, ex);
                Toast(ShortError(ex), ToastKind.Error);
            }
        }

        /// <summary>
        /// Finds profiles that belong to one account or one browser, and fixes the half of it
        /// that is fixable. Opens without requiring Pro: an audit that says "you are linked" but
        /// sits behind a paywall is worse than no audit, because the user cannot act on it.
        /// </summary>
        private void AccountAudit() { Guard("AccountAudit", delegate { return AccountAuditCore(); }); }

        private async System.Threading.Tasks.Task AccountAuditCore()
        {
            using (var dlg = new FingerprintAuditForm(_profiles, _store))
            {
                dlg.ShowDialog(this);
                if (dlg.Fixed <= 0) return;

                ReloadCards(true);
                Toast(dlg.Fixed + (dlg.Fixed == 1 ? " fingerprint re-rolled" : " fingerprints re-rolled"),
                    ToastKind.Success);

                /* A re-rolled profile may be open, and its browser is still running the identity it
                   launched with - so the card would claim one thing while the tab answers another,
                   and the leak check would disagree with the profile it is testing. Push the new
                   configuration into the live tabs and say whether it took: a re-roll nobody can
                   see land is a re-roll nobody can trust. */
                int live = 0, applied = 0;
                foreach (var p in _profiles)
                {
                    BrowserSession s;
                    if (!_sessions.TryGetValue(p.Id, out s) || s == null || !s.IsRunning) continue;
                    s.UpdateProfile(p);
                    live++;
                    try { if (await s.ApplyLiveAsync()) applied++; }
                    catch (Exception ex) { Log.Error("Live re-apply after audit failed", ex); }
                }
                if (live > 0)
                    Toast(applied == live
                        ? "New identity applied to " + live + (live == 1 ? " open profile" : " open profiles")
                        : applied + " of " + live + " open profiles took the new identity \u00b7 reopen the rest",
                        applied == live ? ToastKind.Success : ToastKind.Warn);
            }
        }

        /// <summary>Exit IP, resolver and clock for this PC or any saved proxy.</summary>
        private void NetworkDiagnostics()
        {
            using (var dlg = new NetworkForm(_proxies)) dlg.ShowDialog(this);
        }

        private void BatchCreate()
        {            string prefix, group, proxyText;
            int count;

            /* Checked before the dialog opens, so nobody fills in a form that cannot be used. */
            int slots = Entitlements.ProfileSlotsLeft(_profiles.Count);
            if (slots <= 0)
            {
                OfferUpgrade(Entitlements.ProfileReason(_profiles.Count));
                return;
            }

            using (var dlg = new ToolDialog("Batch create", "Create", "Cancel", 520, 660))
            {
                dlg.AddNote("Every profile gets its own fingerprint, cookies and browser data.");
                var prefixBox = dlg.AddField("NAME PREFIX", "Profile", "", "pencil", false, 33);
                var countBox = dlg.AddSelect(Entitlements.IsPro ? "HOW MANY" : "HOW MANY  \u00b7  " + slots + " LEFT",
                    new[] { "5", "10", "25", "50", "100" }, 1, "layers");
                var groupBox = dlg.AddField("GROUP / FOLDER", _settings.DefaultGroup ?? "",
                    L10n.T("set.group.hint"), "folder", false, 33);
                var proxyBox = dlg.AddField("PROXIES · ONE PER LINE (OPTIONAL)", "",
                    "http://user:pass@host:port", "proxy", true, 120);
                dlg.AddNote("Also accepts socks5:// and host:port:user:pass.");

                dlg.Validator = delegate
                {
                    if (!string.IsNullOrWhiteSpace(prefixBox.Value)) return true;
                    Toast("Give the profiles a name prefix", ToastKind.Info);
                    return false;
                };

                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                prefix = (prefixBox.Value ?? "Profile").Trim();
                group = (groupBox.Value ?? "").Trim();
                proxyText = proxyBox.Value ?? "";
                if (!int.TryParse(countBox.Value, out count) || count < 1) count = 10;
                if (count > 200) count = 200;
            }

            /* The plan decides how many can actually be made. Clamping here rather than
               refusing outright keeps the dialog honest: the user still gets what they can. */
            count = AllowProfiles(count);
            if (count <= 0) return;
            string[] lines = proxyText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

            int made = 0, sharedFingerprints = 0, failedSaves = 0;
            /* `taken` is the workspace as it exists, plus each profile this loop has just built.
               Deduplicating only against _profiles would let batch profile 3 collide with batch
               profile 2 - neither is in _profiles until the reload at the end. */
            var taken = new List<Profile>(_profiles);
            for (int i = 0; i < count; i++)
            {
                var p = new Profile();
                p.Name = prefix + " " + (made + 1).ToString("00");
                p.Group = group;
                if (lines.Length > 0)
                {
                    var proxy = ParseProxyLine(lines[made % lines.Length]);
                    if (proxy.Enabled) p.Proxy = proxy;
                }
                p.Tools.ProxyWatch = p.Proxy.Enabled;
                /* Every batch profile is made unique against everything already made: a batch of
                   twenty used to be twenty independent random rolls, which reliably produced
                   duplicate renderers. */
                FingerprintIdentity.Comparison clash;
                FingerprintIdentity.MakeUnique(p.Fp, "System default", p.Proxy, taken, out clash);
                string kernelNote;
                KernelDiversity.Assign(p, taken, out kernelNote);
                try
                {
                    _store.Save(p);
                    taken.Add(p);
                    made++;
                }
                catch (Exception ex)
                {
                    /* Keep going: one profile that cannot be written (a full disk, a locked file)
                       must not discard the nineteen that already succeeded. */
                    Log.Error("Batch create could not save a profile", ex);
                    failedSaves++;
                    continue;
                }
                if (clash != null && clash.Relation == FingerprintIdentity.Relation.Linked)
                    sharedFingerprints++;
            }

            ReloadCards(true);
            if (failedSaves > 0)
                Toast(made + " of " + count + " profiles created \u00b7 " + failedSaves +
                      " could not be saved", ToastKind.Warn);
            else if (sharedFingerprints > 0)
                Toast(made + " profiles created \u00b7 " + sharedFingerprints +
                      " share a strong signal with another profile", ToastKind.Warn);
            else
                Toast(made + " profiles created", ToastKind.Success);
        }

        /// <summary>Accepts scheme://user:pass@host:port, host:port and host:port:user:pass.</summary>
        private static ProxyConfig ParseProxyLine(string line)
        {
            var p = new ProxyConfig();
            string s = (line ?? "").Trim();
            if (s.Length == 0) return p;

            string type = "http";
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0)
            {
                string head = s.Substring(0, scheme).ToLowerInvariant();
                if (head == "socks5" || head == "socks5h") type = "socks5";
                else if (head == "https") type = "https";
                s = s.Substring(scheme + 3);
            }
            int slash = s.IndexOf('/');
            if (slash > 0) s = s.Substring(0, slash);

            string user = "", pass = "";
            int at = s.LastIndexOf('@');
            if (at > 0)
            {
                string cred = s.Substring(0, at);
                s = s.Substring(at + 1);
                int colon = cred.IndexOf(':');
                if (colon >= 0) { user = cred.Substring(0, colon); pass = cred.Substring(colon + 1); }
                else user = cred;
            }

            string[] bits = s.Split(':');
            if (bits.Length < 2 || string.IsNullOrWhiteSpace(bits[0])) return p;
            int port;
            if (!int.TryParse(bits[1], out port) || port <= 0 || port > 65535) return p;
            if (bits.Length >= 4) { user = bits[2]; pass = bits[3]; }

            p.Type = type;
            p.Host = bits[0].Trim();
            p.Port = port;
            p.User = user;
            p.Password = pass;
            return p;
        }

        private void ImportProfiles()
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "Import profiles",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                var list = _store.ImportFrom(ofd.FileName);
                if (list.Count == 0) { Toast("No profiles found in that file", ToastKind.Error); return; }

                int allowed = AllowProfiles(list.Count);
                if (allowed <= 0) return;
                if (allowed < list.Count)
                {
                    list = list.Take(allowed).ToList();
                    Toast("Imported the first " + allowed + ". The plan is full", ToastKind.Info);
                }

                /* An imported file carries whatever fingerprints the exporting machine had, which
                   is frequently a duplicate of the ones already here - the same JSON imported
                   twice, or two files from one seller. Re-rolling the colliding ones keeps the
                   "every profile is distinct" guarantee true after an import as well. */
                int rerolled = 0;
                foreach (var p in list)
                {
                    if (p.Fp == null) p.Fp = new FingerprintConfig();
                    var worst = FingerprintIdentity.WorstMatch(p, _profiles);
                    if (worst != null && worst.Relation != FingerprintIdentity.Relation.Distinct &&
                        worst.Relation != FingerprintIdentity.Relation.Remote)
                    {
                        FingerprintIdentity.Comparison clash;
                        FingerprintIdentity.MakeUnique(p.Fp, p.Fp.Os, p.Proxy, _profiles, out clash);
                        rerolled++;
                    }
                    /* Imported profiles carry whatever fingerprint the exporting machine had,
                       which is frequently an impossible machine - see Coerce. Repair it here so the
                       file the user just imported is coherent from the first launch. */
                    FingerprintFactory.Coerce(p.Fp);
                    string kernelNote;
                    if (!KernelDiversity.IsPinned(p)) KernelDiversity.Assign(p, _profiles, out kernelNote);
                    _store.Save(p);
                    _profiles.Add(p);
                }
                ReloadCards(true);
                Toast(rerolled > 0
                    ? list.Count + " profiles imported · " + rerolled + " fingerprints re-rolled · re-enter proxy passwords"
                    : list.Count + " profiles imported · re-enter proxy passwords", ToastKind.Success);
            }
        }

        private void ExportProfiles()
        {
            Directory.CreateDirectory(AppPaths.Exports);
            using (var sfd = new SaveFileDialog
            {
                Title = "Export profiles",
                Filter = "JSON files (*.json)|*.json",
                InitialDirectory = AppPaths.Exports,
                FileName = "joebrowser-profiles-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json",
                OverwritePrompt = true
            })
            {
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                int n = _store.ExportTo(sfd.FileName, _profiles);
                Toast(n + " profiles exported · proxy secrets excluded", ToastKind.Success);
            }
        }

        private void ProxyBatchTest()
        {
            if (!RequirePro(Entitlements.BatchProxyTest)) return;
            using (var dlg = new ToolDialog("Proxy batch test", "Close", "", 620, 640))
            {
                dlg.Secondary.Visible = false;
                var input = dlg.AddField("PROXIES · ONE PER LINE", "",
                    "http://user:pass@host:port", "proxy", true, 150);
                var output = dlg.AddReadOnlyText("Results appear here.", 210);
                dlg.AddAction("Test every proxy", "bolt", async delegate
                {
                    string[] lines = (input.Value ?? "")
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                    if (lines.Length == 0)
                    {
                        output.Text = "Paste at least one proxy above.";
                        return;
                    }

                    output.Text = "Checking " + lines.Length + " proxies…";
                    var sb = new StringBuilder();
                    foreach (string line in lines)
                    {
                        if (output.IsDisposed) return;
                        var cfg = ParseProxyLine(line);
                        if (!cfg.Enabled)
                        {
                            sb.Append("SKIP   ").Append(line).Append("   (could not parse)").AppendLine();
                            continue;
                        }
                        try
                        {
                            var r = await ProxyTester.TestAsync(cfg);
                            sb.Append(r.Ok ? "OK     " : "FAIL   ")
                              .Append(cfg.Host).Append(':').Append(cfg.Port)
                              .Append("   ").Append(r.Ok ? r.Ip : r.Error)
                              .Append(r.Ok ? "   " + r.Country + "   " + r.LatencyMs + " ms" : "")
                              .AppendLine();
                        }
                        catch (Exception ex)
                        {
                            sb.Append("FAIL   ").Append(cfg.Host).Append("   ").Append(ex.Message).AppendLine();
                        }
                    }
                    if (!output.IsDisposed) output.Text = sb.ToString();
                });
                dlg.ShowDialog(this);
            }
        }

        private void CookieManager()
        {
            var running = _sessions.Values.Where(s => s != null && s.IsRunning && s.Profile != null).ToList();
            using (var dlg = new ToolDialog("Cookie manager", "Close", "", 560, 560))
            {
                dlg.Secondary.Visible = false;
                dlg.AddNote(running.Count == 0
                    ? "Open a profile first. Cookies are read from a running browser."
                    : "Choose a running browser, then import or export its cookies.");
                var names = running.Select(s => s.Profile.Name ?? s.Profile.Id).ToArray();
                var sel = dlg.AddSelect("RUNNING BROWSER", names, names.Length > 0 ? 0 : -1, "window");
                var status = dlg.AddReadOnlyText("", 90);
                dlg.AddAction("Export cookies…", "download", async delegate
                {
                    if (sel.SelectedIndex < 0 || status.IsDisposed) return;
                    if (running.Count <= sel.SelectedIndex) { status.Text = "Open a profile first."; return; }
                    try
                    {
                        string json = await running[sel.SelectedIndex].ExportCookiesAsync();
                        if (status.IsDisposed) return;
                        using (var sfd = new SaveFileDialog
                        {
                            Title = "Export cookies",
                            Filter = "JSON files (*.json)|*.json",
                            FileName = "cookies-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json"
                        })
                        {
                            if (sfd.ShowDialog(dlg) != DialogResult.OK) return;
                            File.WriteAllText(sfd.FileName, json, new UTF8Encoding(false));
                        }
                        status.Text = "Cookies exported.";
                    }
                    catch (Exception ex) { if (!status.IsDisposed) status.Text = ex.Message; }
                });
                dlg.AddAction("Import cookies…", "external", async delegate
                {
                    if (sel.SelectedIndex < 0 || status.IsDisposed) return;
                    if (running.Count <= sel.SelectedIndex) { status.Text = "Open a profile first."; return; }
                    try
                    {
                        using (var ofd = new OpenFileDialog
                        {
                            Title = "Import cookies",
                            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                            CheckFileExists = true
                        })
                        {
                            if (ofd.ShowDialog(dlg) != DialogResult.OK) return;
                            string json = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                            int n = await running[sel.SelectedIndex].ImportCookiesAsync(json);
                            if (!status.IsDisposed) status.Text = n + " cookies imported.";
                        }
                    }
                    catch (Exception ex) { if (!status.IsDisposed) status.Text = ex.Message; }
                });
                dlg.ShowDialog(this);
            }
        }

        private void OpenExtensionFolder()
        {
            ExtensionManager.EnsureAll();
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.Extensions + "\"")
                { UseShellExecute = true });
            }
            catch (Exception ex) { Toast(ShortError(ex), ToastKind.Error); }
        }

        private void RecycleBin()
        {
            ProfileStore.TrashItem chosen = null;
            using (var dlg = new ToolDialog("Recycle bin", "Restore", "Close", 560, 540))
            {
                var items = _store.ListTrash();
                var list = dlg.AddList(240);
                list.SetItems(items.ConvertAll(delegate(ProfileStore.TrashItem t)
                {
                    return new JBList.Item(string.IsNullOrWhiteSpace(t.Name) ? t.Id : t.Name,
                        t.Deleted.ToString("yyyy MMM d, HH:mm"), t);
                }));
                dlg.AddNote(items.Count == 0
                    ? "The recycle bin is empty."
                    : "Select a profile, then Restore.");
                dlg.Validator = delegate
                {
                    chosen = list.SelectedItem == null ? null : (ProfileStore.TrashItem)list.SelectedItem.Tag;
                    if (chosen != null) return true;
                    Toast("Select a profile first", ToastKind.Info);
                    return false;
                };
                dlg.AddAction("Empty recycle bin", "trash", delegate
                {
                    if (!ConfirmForm.Ask(dlg, "Empty recycle bin",
                        "Erase every deleted profile from the recycle bin?",
                        "This cannot be undone. Browser data folders stay on disk.", "Empty", BtnKind.Danger)) return;
                    _store.EmptyTrash();
                    list.SetItems(null);
                    dlg.Primary.Enabled = false;
                });
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }

            if (chosen == null) return;
            /* Restoring puts a profile back in the live list, so it needs a slot like any
               other creation. */
            if (!AllowAnotherProfile()) return;
            if (_store.Restore(chosen.File))
            {
                ReloadCards(true);
                Toast("\"" + (chosen.Name ?? chosen.Id) + "\" restored", ToastKind.Success);
            }
            else Toast("Could not restore that profile", ToastKind.Error);
        }

        /// <summary>
        /// Shortcut table shared by the help dialog and the About page. A non-empty third column
        /// starts a new group; the renderer prints it as a heading. Two of the old entries were
        /// not shortcuts at all ("Right click", "On-page alert") and belonged nowhere near this
        /// list, so they were removed rather than dressed up as keyboard commands.
        /// </summary>
        private static readonly string[][] ShortcutRows =
        {
            new[] { "Ctrl + N", "New profile", "PROFILE LIST" },
            new[] { "Ctrl + F", "Search profiles", "" },
            new[] { "Ctrl + O", "Change sort order", "" },
            new[] { "Ctrl + A", "Select every visible profile", "" },
            new[] { "F2", "Edit the focused profile", "" },
            new[] { "F5", "Reload profiles from disk", "" },
            new[] { "Esc", "Clear the search, then the selection", "" },
            new[] { "Shift + click", "Select a range of profiles", "" },
            new[] { "Arrows", "Move between profile cards", "" },
            new[] { "Enter", "Open the focused profile", "" },
            new[] { "Shift + F10", "Open the card menu", "" },
            new[] { "Tab", "Move through every control on the page", "EVERYWHERE" },
            new[] { "Page Up/Down", "Scroll the page", "" },
            new[] { "F4", "Open a dropdown list", "" },
            new[] { "Enter", "Submit the dialog you are in", "" }
        };

        /// <summary>
        /// The same table with group headings folded in, so both renderers walk one list and
        /// neither can silently drop the grouping.
        /// </summary>
        private static List<string[]> ShortcutDisplay()
        {
            var rows = new List<string[]>();
            foreach (var r in ShortcutRows)
            {
                if (!string.IsNullOrEmpty(r[2])) rows.Add(new[] { "", r[2] });
                rows.Add(new[] { r[0], r[1] });
            }
            return rows;
        }

        private void ShortcutHelp()
        {
            using (var dlg = new ToolDialog("Keyboard shortcuts", "Close", "", 560, 560))
            {
                dlg.Secondary.Visible = false;
                string text = "";
                foreach (var s in ShortcutDisplay())
                {
                    // A row with no key is a group heading.
                    if (s[0].Length == 0) text += (text.Length > 0 ? "\r\n" : "") + s[1] + "\r\n";
                    else text += s[0].PadRight(18) + s[1] + "\r\n";
                }
                dlg.AddReadOnlyText(text, 360);
                dlg.ShowDialog(this);
            }
        }

        private void ClearCaches()
        {
            if (!ConfirmForm.Ask(this, "Clear browser caches",
                "Empty Chromium cache folders for every profile?",
                "Cookies, logins and local storage are kept. Close running browsers first for the best result.",
                "Clean", BtnKind.Primary)) return;
            long bytes = ProfileStore.ClearCaches();
            Toast("Freed " + (bytes / 1048576.0).ToString("0.0") + " MB", ToastKind.Success);
        }

        /* ------------------------------------------------------------------ *
         * Proxy Library - save once, check, pick when creating a profile, and
         * re-point a running browser without closing its tab or losing cookies.
         * ------------------------------------------------------------------ */

        private void ProxyAction(string key, int index)
        {
            try
            {
                switch (key)
                {
                    case "add": EditProxy(-1); break;
                    case "edit": EditProxy(index); break;
                    case "delete": DeleteProxy(index); break;
                    case "import": ImportProxyList(); break;
                    case "checkall": CheckAllProxies(); break;
                    case "test": CheckProxy(index); break;
                    case "use": CreateProfileWithProxy(index); break;
                    case "live": ApplyProxyLive(index); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Proxy action failed: " + key, ex);
                Toast(ShortError(ex), ToastKind.Error);
            }
        }

        private ProxyEntry ProxyAt(int index)
        {
            return index >= 0 && index < _proxies.Count ? _proxies[index] : null;
        }

        private void SaveProxies()
        {
            ProxyVault.Save(_proxies);
            _proxyPage.SetItems(_proxies);
        }

        private void EditProxy(int index)
        {
            ProxyEntry entry = ProxyAt(index);
            bool isNew = entry == null;
            var cfg = isNew ? new ProxyConfig() : ProxyVault.Copy(entry.Config);
            string name, note;
            int typeIdx;

            using (var dlg = new ToolDialog(isNew ? "Add proxy" : "Edit proxy", "Save", "Cancel", 540, 560))
            {
                dlg.AddNote(isNew
                    ? "Available when creating a profile."
                    : "Applies next time a profile uses it.");
                var nameBox = dlg.AddField("NAME", isNew ? "" : entry.Name, "Home IP · Frankfurt", "pencil", false, 33);
                var typeBox = dlg.AddSelect("TYPE", new[] { "HTTP", "HTTPS", "SOCKS5" },
                    cfg.Type == "socks5" ? 2 : cfg.Type == "https" ? 1 : 0, "proxy");
                var lineBox = dlg.AddField("HOST : PORT",
                    isNew || !cfg.Enabled ? "" : cfg.Host + ":" + cfg.Port, "proxy.example.com:8080", "globe", false, 33);
                var userBox = dlg.AddField("USERNAME (OPTIONAL)", cfg.User, "Optional", "user", false, 33);
                var passBox = dlg.AddField("PASSWORD (OPTIONAL)", cfg.Password, "Optional", "key", false, 33);
                var noteBox = dlg.AddField("NOTE (OPTIONAL)", isNew ? "" : entry.Note, "Optional", "file", false, 33);
                dlg.AddNote("Passwords are encrypted on this device.");

                dlg.Validator = delegate
                {
                    string rawLine = (lineBox.Value ?? "").Trim();
                    int colon = rawLine.LastIndexOf(':');
                    int port;
                    if (colon < 1 || !int.TryParse(rawLine.Substring(colon + 1), out port) || port < 1 || port > 65535)
                    {
                        Toast("Enter the host and port, for example proxy.example.com:8080", ToastKind.Info);
                        return false;
                    }
                    return true;
                };

                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                name = (nameBox.Value ?? "").Trim();
                note = (noteBox.Value ?? "").Trim();
                typeIdx = typeBox.SelectedIndex;
                string line = (lineBox.Value ?? "").Trim();
                int c = line.LastIndexOf(':');
                cfg.Type = typeIdx == 2 ? "socks5" : typeIdx == 1 ? "https" : "http";
                cfg.Host = line.Substring(0, c).Trim();
                cfg.Port = int.Parse(line.Substring(c + 1));
                cfg.User = (userBox.Value ?? "").Trim();
                cfg.Password = passBox.Value ?? "";
            }

            if (isNew)
            {
                entry = new ProxyEntry { Name = name, Note = note, Config = cfg };
                entry.Config.LastIp = "";
                _proxies.Add(entry);
            }
            else
            {
                entry.Name = name;
                entry.Note = note;
                entry.Config = cfg;
                entry.LastStatus = 0;
            }
            SaveProxies();
            Toast(isNew ? "Proxy saved" : "Proxy updated", ToastKind.Success);
        }

        private void DeleteProxy(int index)
        {
            var entry = ProxyAt(index);
            if (entry == null) return;
            if (!ConfirmForm.Ask(this, "Delete proxy", "Remove \"" + (string.IsNullOrWhiteSpace(entry.Name) ? entry.Connection : entry.Name) + "\" from the library?",
                "Profiles already using it keep their own copy.", "Delete", BtnKind.Danger)) return;
            _proxies.RemoveAt(index);
            SaveProxies();
            Toast("Proxy removed", ToastKind.Success);
        }

        private void ImportProxyList()
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "Import proxy list",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string[] lines = File.ReadAllLines(ofd.FileName);
                int added = 0;
                foreach (string raw in lines)
                {
                    var cfg = ProxyVault.ParseLine(raw);
                    if (!cfg.Enabled) continue;
                    _proxies.Add(new ProxyEntry { Config = cfg, Name = cfg.Host });
                    added++;
                }
                SaveProxies();
                Toast(added + (added == 1 ? " proxy" : " proxies") + " imported", added > 0 ? ToastKind.Success : ToastKind.Info);
            }
        }

        private async System.Threading.Tasks.Task CheckAllProxiesCore()
        {
            if (_proxies.Count == 0) { Toast("Add a proxy first", ToastKind.Info); return; }
            _proxyPage.SetBusy(true, "Checking…");
            try
            {
                for (int i = 0; i < _proxies.Count; i++)
                {
                    var e = _proxies[i];
                    var r = await ProxyTester.TestAsync(e.Config);
                    ApplyProxyResult(e, r);
                    e.LastChecked = DateTime.Now;
                    _proxyPage.RefreshRows();
                }
                ProxyVault.Save(_proxies);
                int ok = _proxies.Count(x => x.LastStatus == 1);
                Toast(ok + " of " + _proxies.Count + " proxies working", ok > 0 ? ToastKind.Success : ToastKind.Error);
            }
            catch (Exception ex) { Log.Error("Proxy check failed", ex); Toast(ShortError(ex), ToastKind.Error); }
            finally { _proxyPage.SetBusy(false, ""); }
        }

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void CheckAllProxies() { Guard("CheckAllProxies", delegate { return CheckAllProxiesCore(); }); }

        private async System.Threading.Tasks.Task CheckProxyCore(int index)
        {
            var e = ProxyAt(index);
            if (e == null) return;
            try
            {
                var r = await ProxyTester.TestAsync(e.Config);
                ApplyProxyResult(e, r);
                e.LastChecked = DateTime.Now;
                ProxyVault.Save(_proxies);
                _proxyPage.RefreshRows();
                Toast(r.Ok ? "Working · " + r.Ip + " · " + r.LatencyMs + " ms" : "Offline · " + r.Error,
                    r.Ok ? ToastKind.Success : ToastKind.Error);
            }
            catch (Exception ex) { Toast(ShortError(ex), ToastKind.Error); }
        }

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void CheckProxy(int index) { Guard("CheckProxy", delegate { return CheckProxyCore(index); }); }

        private static void ApplyProxyResult(ProxyEntry e, ProxyCheckResult r)
        {
            if (e == null || r == null) return;
            e.LastStatus = r.Ok ? 1 : -1;
            if (!r.Ok) return;
            e.LastIp = r.Ip ?? "";
            e.LastCountry = r.Country ?? "";
            e.LastCity = r.City ?? "";
            e.LastLatencyMs = r.LatencyMs;
            if (e.Config == null) e.Config = new ProxyConfig();
            e.Config.LastIp = r.Ip ?? "";
            e.Config.LastCountry = r.Country ?? "";
            e.Config.LastCountryCode = r.CountryCode ?? "";
            e.Config.LastCity = r.City ?? "";
            e.Config.LastTimezone = r.Timezone ?? "";
        }

        private void CreateProfileWithProxy(int index)
        {
            var e = ProxyAt(index);
            if (e == null) return;
            // Creating a profile from a proxy row obeys the same plan limit as the New button.
            if (!AllowAnotherProfile()) return;
            /* Opened with the proxy already set, so a proxy that has been checked once also
               carries its country into the new fingerprint instead of generating a mismatch the
               user has to notice and fix by hand. */
            var p = SeedNewProfile("Profile " + (_profiles.Count + 1).ToString("00"), ProxyVault.Copy(e.Config));
            using (var dlg = new EditorForm(p, true, _settings, 0, new List<Profile>(_profiles)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _store.Save(dlg.Result);
                ReloadCards(true);
                SetPage(0);
                Toast("Profile created with the saved proxy", ToastKind.Success);
            }
        }

        /// <summary>
        /// The relay reads its upstream from the live Profile reference on every new
        /// connection, so replacing Proxy here reroutes traffic inside the SAME browser
        /// process: tabs, cookies, logins and local storage are never closed or reset.
        /// </summary>
        private void ApplyProxyLive(int index)
        {
            var e = ProxyAt(index);
            if (e == null) return;
            var running = _sessions
                .Where(kv => kv.Value != null && kv.Value.IsRunning && kv.Value.Profile != null)
                .ToList();
            if (running.Count == 0)
            {
                Toast("Open a profile first, then click a proxy row to change it live", ToastKind.Info);
                return;
            }

            string[] names = running.Select(kv => kv.Value.Profile.Name ?? kv.Key).ToArray();
            int picked;
            using (var dlg = new ToolDialog("Change proxy, keep the tab open", "Apply now", "Cancel", 540, 400))
            {
                dlg.AddNote("Cookies and logins stay. Only new connections use the new proxy.");
                var sel = dlg.AddSelect("RUNNING PROFILE", names, 0, "window");
                dlg.AddNote("New proxy: " + e.Connection);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                picked = sel.SelectedIndex < 0 ? 0 : sel.SelectedIndex;
            }
            if (picked >= running.Count) return;

            var session = running[picked].Value;
            var p = session.Profile;
            p.Proxy = ProxyVault.Copy(e.Config);
            p.Tools.ProxyWatch = true;
            _store.Save(p);
            session.UpdateProfile(p);
            ReloadCards(true);
            Toast("Proxy changed live · your tab and session are still open", ToastKind.Success);
        }

        /* ------------------------------------------------------------------ *
         * Extensions Library
         * ------------------------------------------------------------------ */

        private void ExtensionAction(string key, int index)
        {
            try
            {
                switch (key)
                {
                    case "url": AddExtensionByUrl(); break;
                    case "folder": AddExtensionFolder(); break;
                    case "file": AddExtensionFile(); break;
                    case "openfolder": OpenExtensionFolder(); break;
                    case "toggle": ToggleExtension(index); break;
                    case "delete": DeleteExtension(index); break;
                    case "store": OpenExtensionStore(index); break;
                    case "recommend": OpenRecommendation(index); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Extension action failed: " + key, ex);
                Toast(ShortError(ex), ToastKind.Error);
            }
        }

        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }

        private async System.Threading.Tasks.Task BrowserActionCore(string key, string engine)
        {
            try
            {
                if (key == "rescan")
                {
                    _browsersPage.Rescan();
                    int n = 0;
                    foreach (var e in BrowsersPage.EngineCatalog())
                        if (BrowsersPage.FindInstalled(e.Key) != null) n++;
                    Toast(n + " of 5 engines available on this PC", ToastKind.Info);
                    return;
                }

                if (key == "openfolder")
                {
                    Directory.CreateDirectory(AppPaths.Browsers);
                    Process.Start("explorer.exe", "\"" + AppPaths.Browsers + "\"");
                    return;
                }

                if (key == "kernels")
                {
                    using (var dlg = new KernelForm()) dlg.ShowDialog(this);
                    _browsersPage.Rescan();
                    return;
                }

                if (string.IsNullOrEmpty(engine)) return;

                if (key == "reveal")
                {
                    var info = BrowsersPage.FindInstalled(engine);
                    if (info == null || !info.Found)
                    {
                        _browsersPage.Rescan();
                        Toast(BrowserFetcher.Label(engine) + " is no longer installed", ToastKind.Warn);
                        return;
                    }
                    Process.Start("explorer.exe", "/select,\"" + info.Path + "\"");
                    return;
                }

                if (key != "install") return;

                /* Edge ships with Windows; a "download" button there would only open a page. */
                if (!BrowserFetcher.Supports(engine))
                {
                    OpenUrl(BrowserFetcher.HomePage(engine));
                    return;
                }

                string label = BrowserFetcher.Label(engine);
                /* Chromium is unpacked into this app's folder; the other three run the vendor's
                   own installer, which writes outside the app. That deserves a yes first. */
                if (engine != "chromium")
                {
                    if (!ConfirmForm.Ask(this, "Install " + label,
                        "Download and run " + label + "'s official installer now?",
                        "It is fetched from " + HostOf(BrowserFetcher.HomePage(engine)) +
                        " and installed the normal way. Windows may ask for permission, and " + label +
                        " will be available to every app on this PC, not only Joe Browser.",
                        "Install", BtnKind.Primary)) return;
                }

                _browsersPage.SetBusy(engine, true, "Downloading " + label + "\u2026");
                bool ok = await DownloadBrowserAsync(engine);
                _browsersPage.SetBusy(null, false, null);
                _browsersPage.Rescan();
                if (ok) Invalidate();
            }
            catch (Exception ex)
            {
                _browsersPage.SetBusy(null, false, null);
                Log.Error("Browser action failed: " + key, ex);
                Toast(ShortError(ex), ToastKind.Error);
            }
        }

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void BrowserAction(string key, string engine) { Guard("BrowserAction", delegate { return BrowserActionCore(key, engine); }); }

        private void SaveExtensions()
        {
            ExtensionCatalog.Save(_extensions);
            _extensionPage.SetItems(_extensions);
        }

        private async System.Threading.Tasks.Task AddExtensionByUrlCore()
        {
            string url;
            using (var dlg = new ToolDialog("Add extension by store link", "Import", "Cancel", 560, 340))
            {
                dlg.AddNote("Paste a Chrome Web Store link or its 32-character id.");
                var field = dlg.AddField("CHROME WEB STORE LINK", "", "https://chromewebstore.google.com/detail/...", "link", false, 33);
                dlg.AddNote("Downloaded into Joe Browser's own extensions folder. No store account needed.");
                dlg.Validator = delegate
                {
                    if (!string.IsNullOrWhiteSpace(field.Value)) return true;
                    Toast("Paste the extension link first", ToastKind.Info);
                    return false;
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                url = (field.Value ?? "").Trim();
            }

            _extensionPage.SetBusy(true, "Importing…");
            try
            {
                var item = await Task.Run(new Func<ExtensionItem>(delegate { return ExtensionCatalog.ImportFromUrl(url); }));
                _extensions.Add(item);
                SaveExtensions();
                Toast("\"" + item.Name + "\" added to all Chromium profiles", ToastKind.Success);
            }
            catch (Exception ex) { Toast(ShortError(ex), ToastKind.Error); }
            finally { _extensionPage.SetBusy(false, ""); }
        }

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void AddExtensionByUrl() { Guard("AddExtensionByUrl", delegate { return AddExtensionByUrlCore(); }); }

        private void AddExtensionFile()
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "Import an extension",
                Filter = "Packed extension (*.crx;*.zip)|*.crx;*.zip|All files (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var item = ExtensionCatalog.ImportFromFile(ofd.FileName);
                    _extensions.Add(item);
                    SaveExtensions();
                    Toast("\"" + item.Name + "\" added to all Chromium profiles", ToastKind.Success);
                }
                catch (Exception ex) { Toast(ShortError(ex), ToastKind.Error); }
            }
        }

        private void AddExtensionFolder()
        {
            using (var fbd = new FolderBrowserDialog { Description = "Select an unpacked extension folder (contains manifest.json)" })
            {
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var item = ExtensionCatalog.ImportFromFolder(fbd.SelectedPath);
                    _extensions.Add(item);
                    SaveExtensions();
                    Toast("\"" + item.Name + "\" added", ToastKind.Success);
                }
                catch (Exception ex) { Toast(ShortError(ex), ToastKind.Error); }
            }
        }

        private void ToggleExtension(int index)
        {
            if (index < 0 || index >= _extensions.Count) return;
            _extensions[index].Enabled = !_extensions[index].Enabled;
            SaveExtensions();
            Toast(_extensions[index].Enabled ? "Enabled for the next browser launch" : "Disabled", ToastKind.Success);
        }

        private void DeleteExtension(int index)
        {
            if (index < 0 || index >= _extensions.Count) return;
            var item = _extensions[index];
            if (!ConfirmForm.Ask(this, "Remove extension", "Remove \"" + item.Name + "\" from Joe Browser?",
                "The files stay on disk.", "Remove", BtnKind.Danger)) return;
            _extensions.RemoveAt(index);
            SaveExtensions();
            Toast("Extension removed", ToastKind.Success);
        }

        private void OpenExtensionStore(int index)
        {
            if (index < 0 || index >= _extensions.Count) return;
            string url = _extensions[index].StoreUrl;
            if (string.IsNullOrWhiteSpace(url)) { Toast("This extension was added from a local folder", ToastKind.Info); return; }
            OpenUrl(url);
        }

        private void OpenRecommendation(int index)
        {
            var rec = ExtensionCatalog.Recommended();
            if (index < 0 || index >= rec.Length) return;
            OpenUrl(ExtensionCatalog.StoreSearchUrl(rec[index][2]));
        }

        private void BuildSettingsPage(Panel page)
        {
            _setBrowserInfo = Label(LabelRole.Strong, "Detecting…");
            _setBrowserInfo.TextAlign = ContentAlignment.MiddleRight;
            _setStoragePath = Label(LabelRole.Body, AppPaths.Root);
            _setStoragePath.TextAlign = ContentAlignment.MiddleRight;
            /* A path is the one thing here a user regularly needs out of the window. */
            _setStoragePath.Selectable = true;
            /* The folder path is longer than the row on a normal window, so the label elides
               it. The tooltip is what makes the elision harmless: the full path is one hover
               away and the Open button sits right beside it. */
            ToolTips.Attach(_setStoragePath, AppPaths.Root);

            _setBrowserPath = Field("Automatic (recommended)");
            _setBrowserPath.Value = _settings.BrowserPath ?? "";
            _setBrowse = new JBButton { Kind = BtnKind.Soft, Text = L10n.T("btn.browse"), Radius = 4, Height = Ui.S(33) };
            _setDetect = new JBButton { Kind = BtnKind.Soft, Icon = "refresh", Text = L10n.T("btn.detect"), Radius = 4, Height = Ui.S(33) };
            _setStartUrl = Field("https://www.google.com");
            _setDefaultGroup = Field(L10n.T("set.group.hint"));
            _setDefaultGroup.Value = _settings.DefaultGroup ?? "";
            _setOpenData = new JBButton { Kind = BtnKind.Soft, Text = L10n.T("btn.opendata"), Radius = 4, Height = Ui.S(33) };
            _setDownload = new JBButton { Kind = BtnKind.Soft, Text = L10n.T("btn.download"), Radius = 4, Height = Ui.S(33) };
            _setClearCache = new JBButton { Kind = BtnKind.Soft, Icon = "sync", Text = L10n.T("btn.clean"), Radius = 4, Height = Ui.S(33) };
            _setClearCache.Click += delegate { ClearCaches(); };

            /* Interface language: circular flag + full language name, never a bare code. */
            _setLang = new JBLangPicker();
            _setLang.SetItems(UiLangItems(), _settings.UiLanguage);
            _setLang.SelectedIndexChanged += delegate
            {
                if (string.IsNullOrEmpty(_setLang.Value)) return;
                _settings.UiLanguage = _setLang.Value;
                L10n.Set(_setLang.Value);
                try { _store.SaveSettings(_settings); }
                catch (Exception ex) { Log.Error("Language preference not saved", ex); }
                /* Say what the change means when the table is not finished, so the English
                   words that remain do not look like a bug. */
                int coverage = L10n.Coverage(_setLang.Value);
                if (_setLang.Value != "en" && coverage < 100)
                    Toast("Interface language set to " + L10n.NameOf(_setLang.Value) + " · "
                        + coverage + "% translated, the rest stays in English", ToastKind.Info);
            };

            // Switches carry no caption of their own; the row title to the left is the label.
            _setExtensions = new JBToggle { Checked = _settings.EnableExtensions };
            _setCustomExt = new JBToggle { Checked = _settings.EnableCustomExtensions };
            _setTrash = new JBToggle { Checked = _settings.UseTrash };
            _setHistory = new JBToggle { Checked = _settings.HistoryEnabled };
            _setExtensions.CheckedChanged += delegate { _settings.EnableExtensions = _setExtensions.Checked; };
            _setCustomExt.CheckedChanged += delegate { _settings.EnableCustomExtensions = _setCustomExt.Checked; };
            _setTrash.CheckedChanged += delegate { _settings.UseTrash = _setTrash.Checked; };
            _setHistory.CheckedChanged += delegate { _settings.HistoryEnabled = _setHistory.Checked; };

            _setBrowse.Click += delegate { BrowseBrowser(); };
            _setDetect.Click += delegate { _setBrowserPath.Value = ""; UpdateBrowserInfo(); };
            _setOpenData.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.Root + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { Toast(ex.Message, ToastKind.Error); }
            };
            _setDownload.Click += delegate { var _ = DownloadBrowserAsync("chromium"); };

            var engine = Card("set.browser");
            Row(engine, "set.row.detected", null, _setBrowserInfo);
            Row(engine, "set.row.path", new[] { Ui.S(520), Ui.S(76), Ui.S(86) }, _setBrowserPath, _setBrowse, _setDetect);

            /* Kernel spread: how many different browser builds this workspace actually runs on.
               A number that stays at 1 is the honest signal that every profile is on one binary,
               and it is the one fact a user needs before trusting the fingerprint features. */
            _setKernelInfo = new JBLabel(LabelRole.Body, "");
            _setKernelInfo.Wrap = true;
            _setKernelInfo.TextAlign = ContentAlignment.MiddleLeft;
            /* Kernel names and versions are the other thing a user copies into a report. */
            _setKernelInfo.Selectable = true;

            var workspace = Card("set.workspace");
            Row(workspace, "set.row.kernels", null, _setKernelInfo);
            Row(workspace, "set.row.start", new[] { Ui.S(560) }, _setStartUrl);
            Row(workspace, "set.row.group", new[] { Ui.S(300) }, _setDefaultGroup);
            Row(workspace, "set.row.lang", new[] { Ui.S(280) }, _setLang);

            var defaults = Card("set.tools");
            Row(defaults, "set.ext", new[] { Ui.S(46) }, _setExtensions);
            Row(defaults, "set.cext", new[] { Ui.S(46) }, _setCustomExt);
            Row(defaults, "set.trash", new[] { Ui.S(46) }, _setTrash);

            var storage = Card("set.storage");
            Row(storage, "set.row.data", new[] { Ui.S(96) }, _setOpenData, _setStoragePath);
            Row(storage, "set.row.cache", new[] { Ui.S(120) }, _setClearCache);
            Row(storage, "set.row.build", new[] { Ui.S(110) }, _setDownload);
            /* History off means nothing new is recorded; what is already there stays until the
               user clears it, because turning recording off should not quietly delete evidence. */
            _setClearHistory = new JBButton { Kind = BtnKind.Soft, Icon = "trash", Text = L10n.T("set.clearhistory"), Radius = 4, Height = Ui.S(33) };
            _setClearHistory.Click += delegate { ClearAllHistory(); };
            Row(storage, "set.row.history", new[] { Ui.S(46), Ui.S(120) }, _setHistory, _setClearHistory);

            /* The one line to the developer when anything looks wrong. It opens Telegram with
               the user's own words; the technical details ride on the clipboard. */
            _setReport = new JBButton { Kind = BtnKind.Soft, Icon = "error", Text = L10n.T("btn.report"), Radius = 4, Height = Ui.S(33) };
            _setReport.Click += delegate { Report.OpenForm(this, ""); };
            Row(storage, "set.row.report", new[] { Ui.S(96) }, _setReport);

            /* Account and security. Everything here is optional for daily use: the email
               account only exists so the sign-in gate can remember who is using the app, and
               the Google client id is needed only for the Continue with Google button. */
            _setAccountWho = Label(LabelRole.Body, Session.Current != null ? Session.Current.Email : L10n.T("acct.signedout"));
            _setAccountWho.TextAlign = ContentAlignment.MiddleRight;
            _setGoogleId = Field("1234567890-abc.apps.googleusercontent.com");
            _setGoogleId.Value = GoogleSignIn.ClientId();
            /* A "Desktop app" client comes with a secret. Google accepts the code exchange with
               PKCE alone, but sending the secret too is what Google's own sample does, so it is
               worth a field - it removes a whole class of "invalid_request" failures. */
            _setGoogleSecret = Field("GOCSPX-…  (optional)");
            _setGoogleSecret.Value = string.IsNullOrWhiteSpace(_settings.GoogleClientSecret)
                ? GoogleSignIn.DefaultClientSecret : _settings.GoogleClientSecret;
            _setSecurity = new JBButton { Kind = BtnKind.Soft, Text = L10n.T("btn.security"), Radius = 4, Height = Ui.S(33) };
            /* The Google client id is the only setting that cannot be pre-filled by the app,
               so it gets a guided setup and a button that actually runs the flow and reports
               what Google said. Without those two, a wrong id just looks like a dead button. */
            _setGoogleGuide = new JBButton { Kind = BtnKind.Soft, Icon = "help", Text = "Set up", Radius = 4, Height = Ui.S(33), Tip = L10n.T("set.google.hint") };
            _setGoogleTest = new JBButton { Kind = BtnKind.Soft, Icon = "check", Text = "Test", Radius = 4, Height = Ui.S(33), Tip = "Run the real Google sign-in now" };
            _setGoogleGuide.Click += delegate { GoogleSetupHelp(); };
            _setGoogleTest.Click += delegate { TestGoogleSignIn(); };
            _setSignOut = new JBButton { Kind = BtnKind.Soft, Text = L10n.T("set.row.signout"), Radius = 4, Height = Ui.S(33) };
            _setSecurity.Click += delegate
            {
                if (Session.Current == null) { Toast("Sign in first.", ToastKind.Error); return; }
                using (var dlg = new SecurityDialog(Session.Current.Email)) dlg.ShowDialog(this);
            };
            _setSignOut.Click += delegate
            {
                if (!ConfirmForm.Ask(this, "Sign out",
                        "Sign out of this device?",
                        "Your profiles, proxies and sessions stay where they are and are available the next time you sign in.",
                        "Sign out", BtnKind.Primary)) return;
                Session.SignOut();
                Application.Restart();
                Environment.Exit(0);
            };

            var account = Card("set.account");
            Row(account, "set.row.signed", null, _setAccountWho);
            /* The id is long (72+ characters) and the row also carries two buttons, so the
               field asks for more room than the other rows and shrinks first when the window is
               narrow - RowWidths already handles that for every JBTextBox. */
            Row(account, "set.row.google", new[] { Ui.S(420), Ui.S(92), Ui.S(80) }, _setGoogleId, _setGoogleGuide, _setGoogleTest);
            Row(account, "set.row.gsecret", new[] { Ui.S(360) }, _setGoogleSecret);
            Row(account, "set.row.security", new[] { Ui.S(104), Ui.S(88) }, _setSecurity, _setSignOut);

            foreach (var c in _setCards) page.Controls.Add(c.Panel);
        }

        /// <summary>A full-bleed settings section; rows are added by Row() and divided by hairlines.</summary>
        private SetCard Card(string key)
        {
            var card = new SetCard { Panel = new JBPanel { Hoverable = false }, Key = key };
            var lbl = Label(LabelRole.Accent, L10n.T(key));
            lbl.SetBounds(Ui.S(18), Ui.S(14), Ui.S(400), Ui.S(18));
            card.Panel.Controls.Add(lbl);
            card.Panel.Tag = lbl;
            _setCards.Add(card);
            return card;
        }

        /// <summary>
        /// Adds a row. Controls sit flush against the right edge in the order given, each at its
        /// resting width; buttons re-measure for the current language and text fields shrink first
        /// when the window is narrow, so the label always keeps room on the left.
        /// A trailing JBLabel argument is drawn as the row's right-aligned value instead.
        /// </summary>
        private SetRow Row(SetCard card, string key, int[] widths, params Control[] right)
        {
            var row = new SetRow { Key = key };
            row.Title = Label(LabelRole.Strong, L10n.T(key));
            row.Title.TextAlign = ContentAlignment.MiddleLeft;
            card.Panel.Controls.Add(row.Title);

            var list = new List<Control>();
            var ws = new List<int>();
            foreach (var c in right)
            {
                card.Panel.Controls.Add(c);
                var asValue = c as JBLabel;
                if (asValue != null) { row.Value = asValue; continue; }
                list.Add(c);
                int w = widths != null && ws.Count < widths.Length ? widths[ws.Count] : Ui.S(180);
                ws.Add(w);
            }
            row.Controls = list.ToArray();
            row.Widths = ws.ToArray();
            card.Rows.Add(row);
            return row;
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

        /* ------------------------------------------------------------------ *
         * Google sign-in setup. The OAuth client id can only be created by the
         * owner of the Google account, so the app explains the four console
         * steps and then proves the result by running the real flow.
         * ------------------------------------------------------------------ */

        private void GoogleSetupHelp()
        {
            /* Same dialog as the first-run button, so the instructions can never drift apart
               from what the code actually does. */
            if (GoogleSetupDialog.Ensure(this, _setGoogleId.Value))
            {
                _setGoogleId.Value = GoogleSignIn.ClientId();
                _setGoogleSecret.Value = GoogleSignIn.SavedClientSecret();
            }
        }

        private async System.Threading.Tasks.Task TestGoogleSignInCore()
        {
            /* Normalised, not trimmed: a paste from the console commonly carries a trailing
               space or the "id:secret" pair, and the un-cleaned value used to reach Google as
               an invalid client. */
            string id = GoogleSignIn.NormalizeClientId(_setGoogleId.Value);
            if (!GoogleSignIn.LooksValid(id))
            {
                ConfirmForm.Tell(this, "Test Google sign-in",
                    "Paste a Google OAuth client id first. It looks like",
                    "123456789012-abcdefghijklmnopqrstuvwxyz012345.apps.googleusercontent.com"
                    + "\n\nOr use \"Set up\" and Joe Browser will walk through creating one.");
                _setGoogleId.Box.Focus();
                return;
            }

            /* The test has to run against exactly what the sign-in gate will read, so the id is
               saved before the flow starts rather than after it succeeds. */
            _settings.GoogleClientId = id;
            _settings.GoogleClientSecret = (_setGoogleSecret.Value ?? "").Trim();
            try { _store.SaveSettings(_settings); }
            catch (Exception ex) { Log.Error("Google client id not saved", ex); }

            _setGoogleTest.Enabled = false;
            _setGoogleTest.Text = "Testing\u2026";
            _setGoogleTest.Invalidate();
            GoogleResult result;
            try
            {
                result = await Task.Run(new Func<GoogleResult>(delegate { return GoogleSignIn.SignIn(id); }));
            }
            finally
            {
                _setGoogleTest.Enabled = true;
                _setGoogleTest.Text = "Test";
                _setGoogleTest.Invalidate();
            }

            if (result.Ok)
            {
                Toast("Google sign-in works, " + result.Identity.Email, ToastKind.Success);
                ConfirmForm.Tell(this, "Test Google sign-in",
                    "Google sign-in works. Signed in as " + result.Identity.Email + ".",
                    string.IsNullOrEmpty(result.Identity.Name) ? null : "Name: " + result.Identity.Name);
            }
            else
            {
                ConfirmForm.Tell(this, "Test Google sign-in", result.Message,
                    "Error code: " + result.Code);
            }
        }

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void TestGoogleSignIn() { Guard("TestGoogleSignIn", delegate { return TestGoogleSignInCore(); }); }

        /* ------------------------------------------------------------------ *
         * Plan, limits and crypto payment.
         * ------------------------------------------------------------------ */

        /// <summary>Free plan caps the number of profiles; Pro does not.</summary>
        private bool AllowAnotherProfile()
        {
            if (Entitlements.ProfileSlotsLeft(_profiles.Count) >= 1) return true;
            OfferUpgrade(Entitlements.ProfileReason(_profiles.Count));
            return false;
        }

        /// <summary>
        /// Asks for <paramref name="adding"/> new profiles at once. Returns how many are allowed
        /// and says so when the plan cannot take them all.
        /// </summary>
        private int AllowProfiles(int adding)
        {
            int left = Entitlements.ProfileSlotsLeft(_profiles.Count);
            if (left >= adding) return adding;
            if (left <= 0)
            {
                OfferUpgrade(Entitlements.ProfileReason(_profiles.Count));
                return 0;
            }
            Toast("Free plan: " + left + " more profile" + (left == 1 ? "" : "s") + " available", ToastKind.Info);
            return left;
        }

        /// <summary>Free plan runs one browser at a time.</summary>
        private bool AllowAnotherBrowser()
        {
            int running = 0;
            foreach (var s in _sessions.Values) if (s.IsRunning) running++;
            if (Entitlements.ConcurrentSlotsLeft(running) >= 1) return true;
            OfferUpgrade(Entitlements.ConcurrentReason());
            return false;
        }

        /// <summary>Refuses a Pro-only tool on the free plan and offers the upgrade.</summary>
        private bool RequirePro(string feature)
        {
            if (Entitlements.Allows(feature)) return true;
            OfferUpgrade(Entitlements.Reason(feature));
            return false;
        }

        /// <summary>
        /// Re-reads the plan and updates everything that depends on it. One method rather than
        /// scattered calls: a lock that is set in one place and not another is a bug that only
        /// shows up on the machine of the person who just paid.
        /// </summary>
        private void RefreshPlanState()
        {
            _upgradePage.RefreshPlan();
            if (_toolsPage != null) _toolsPage.SetLocked("proxy", !Entitlements.IsPro);
            Invalidate();
        }

        private void OfferUpgrade(string why)
        {
            if (ConfirmForm.Ask(this, "Free plan", why,
                    "Everything else in the app stays available on the free plan.",
                    "See the plan", BtnKind.Primary))
                SetPage(7);
            else
                Toast("Free plan: " + Entitlements.FreeProfiles + " profiles, one open at a time", ToastKind.Info);
        }

        private void PlanAction(string key)
        {
            if (key == "copy" || key == "copyamount")
            {
                var inv = _upgradePage.Current;
                if (inv == null) return;
                /* Send exactly this amount: the number the user would otherwise retype from the
                   screen, rounded to the coin's own precision so the clipboard value is the one
                   the invoice is actually matched against. */
                string text = key == "copyamount"
                    ? Invoice.Fmt(inv.Amount)
                    : inv.Coin.Address;
                try
                {
                    Clipboard.SetText(text);
                    Toast(key == "copyamount" ? "Amount copied  ·  " + text + " " + inv.Coin.Symbol
                                              : "Address copied", ToastKind.Success);
                }
                catch (Exception ex) { Log.Error("Clipboard", ex); Toast("Could not copy", ToastKind.Error); }
                return;
            }
            if (key == "restore")
            {
                RestorePurchase();
                return;
            }
            if (key == "terms")
            {
                ShowTerms();
                return;
            }
            if (key == "invite")
            {
                try
                {
                    Clipboard.SetText(Referral.Invite());
                    Toast("Invite copied. Your code is " + Referral.MyCode, ToastKind.Success);
                }
                catch (Exception ex) { Log.Error("Clipboard", ex); Toast("Could not copy", ToastKind.Error); }
                return;
            }
            if (key == "claim")
            {
                ClaimReferral();
                return;
            }
            if (key == "check")
            {
                /* An expired session cannot be paid into, so the button becomes "New session". */
                if (_upgradePage.Current == null || _upgradePage.Current.Left <= TimeSpan.Zero)
                {
                    if (_upgradePage.Selected == null) return;
                    _upgradePage.NewInvoice(_upgradePage.Selected);
                    _payTicks = 0;
                    Toast("New 30-minute session at the current rate", ToastKind.Info);
                    return;
                }
                var _ = CheckPaymentAsync();
            }
        }

        /// <summary>
        /// The payment terms, shown before the first payment. The same text is reachable later
        /// from the Terms link, so nobody has to accept something they cannot re-read.
        /// </summary>
        /// <summary>
        /// Redeems a receipt a referred friend sent. The transaction is checked on the chain
        /// before any days are added, so a receipt cannot simply be typed out.
        /// </summary>
        private void ClaimReferral()
        {
            using (var dlg = new ToolDialog("Claim a referral", "Claim", "Cancel", 560, 400))
            {
                dlg.AddNote("Paste the receipt your friend sent after their payment. It is checked "
                          + "on the chain before the days are added.");
                var box = dlg.AddField("RECEIPT", "", "JBR1.JB7K2M4Q.bsc.0x\u2026", "copy", true, 74);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                bool credited;
                string message = Referral.Claim(box.Value, out credited);
                RefreshPlanState();
                ConfirmForm.Tell(this, credited ? "Referral claimed" : "Referral",
                    credited ? "Referral credit added." : "That receipt could not be used.", message);
            }
        }

        /// <summary>
        /// After a payment, a referred user gets the receipt to send on. It is only offered when
        /// they actually entered somebody's code, so nobody is shown a receipt that means nothing.
        /// </summary>
        private void OfferReferralReceipt(Invoice inv, string txId)
        {
            string receipt = Referral.BuildReceipt(inv == null ? "" : inv.Coin.Chain, txId);
            if (string.IsNullOrEmpty(receipt)) return;

            using (var dlg = new ToolDialog("Your referral receipt", "Copy", "Close", 600, 380))
            {
                dlg.AddNote("Send this to " + Referral.MyReferrer() + " so they receive "
                          + Referral.Days + " days. One payment, one receipt.");
                var box = dlg.AddField("RECEIPT", receipt, "", "copy", true, 74);
                box.Box.ReadOnly = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try { Clipboard.SetText(receipt); Toast("Receipt copied", ToastKind.Success); }
                    catch (Exception ex) { Log.Error("Clipboard", ex); }
                }
            }
        }

        private void ShowTerms()
        {
            using (var dlg = new ToolDialog("Payment terms", "I accept", "Cancel", 660, 580))
            {
                dlg.AddNote("Version " + Terms.Version + "  \u00b7  crypto payments are final.");
                foreach (var s in Terms.Sections())
                {
                    dlg.AddSection(s.Title.ToUpperInvariant());
                    var body = new JBLabel(LabelRole.Body, s.Body);
                    body.Wrap = true;
                    body.TextAlign = ContentAlignment.TopLeft;
                    dlg.AddRow(null, body, Ui.S(74));
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _upgradePage.AcceptTerms();
                    _payTicks = 0;
                }
            }
        }

        /// <summary>
        /// Reads the chain for the open invoice. Deliberately not a "trust the button" flow:
        /// the amount, the block time and the transaction id are all checked, and a hash that
        /// has already paid once is refused.
        /// </summary>
        private async Task CheckPaymentAsync(bool quiet = false)
        {
            var inv = _upgradePage.Current;
            if (inv == null || _payBusy) return;
            if (inv.Left <= TimeSpan.Zero)
            {
                _upgradePage.SetStatus("That session expired. Pick a coin to start a new one.", Theme.Danger);
                return;
            }

            _payBusy = true;
            _upgradePage.SetBusy(true);
            if (!quiet) _upgradePage.SetStatus("Reading the " + inv.Coin.Network + " network\u2026", Theme.TextDim);
            PaymentCheck result;
            try
            {
                result = await Task.Run(new Func<PaymentCheck>(delegate { return Payments.Check(inv); }));
            }
            catch (Exception ex)
            {
                Log.Error("Payment check", ex);
                result = PaymentCheck.No("Could not reach the network. Try again in a moment.");
            }
            finally
            {
                _payBusy = false;
                _upgradePage.SetBusy(false);
            }

            if (result.Found)
            {
                if (Plan.HasSpent(result.TxId))
                {
                    _upgradePage.SetStatus("That transaction has already been used.", Theme.Danger);
                    return;
                }
                Plan.Activate(result.TxId, inv.Coin.Key + " " + Invoice.Fmt(result.Amount) + " " +
                    inv.Coin.Symbol + "  tx " + result.TxId);
                Plan.ClearPending();
                RefreshPlanState();
                _upgradePage.SetStatus("Payment confirmed. Pro is active until " +
                    (Plan.Until.HasValue ? Plan.Until.Value.ToLocalTime().ToString("d MMMM") : "next month") + ".",
                    Theme.Success);
                Toast("Payment confirmed. Pro unlocked", ToastKind.Success);
                Invalidate();
                OfferReferralReceipt(inv, result.TxId);
                return;
            }

            /* A background poll only speaks when it has something to say. */
            if (!quiet || result.Pending) _upgradePage.SetStatus(result.Message, result.Pending ? Theme.Accent : Theme.TextDim);
        }

        private void RestorePurchase()
        {
            var history = Plan.History();
            if (Plan.IsPro)
            {
                ConfirmForm.Tell(this, "Plan", "Pro is active on this Windows user.",
                    Plan.Summary() + (history.Count > 0 ? "\n\nLast payment: " + history[history.Count - 1] : ""));
                return;
            }
            ConfirmForm.Tell(this, "Restore a purchase",
                "Pro is tied to this Windows user.",
                "Reinstall and sign in to the same account to get it back. A transaction can never unlock twice.");
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
            if (url.Length > 0 && !url.StartsWith("about:", StringComparison.Ordinal))
            {
                /* "https://" with nothing after it, or a space in the host, would be saved and
                   then silently open a blank tab every launch. Reject it where it was typed. */
                Uri parsed;
                if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || string.IsNullOrEmpty(parsed.Host))
                {
                    Toast("That start page is not a usable address. Try https://example.com", ToastKind.Warn);
                    _setStartUrl.Box.Focus();
                    _setStartUrl.Box.SelectAll();
                    return;
                }
                url = parsed.AbsoluteUri;
            }
            /* The field shows what was stored, not what was typed: without this a save that
               normalised "google.com" left the box looking unsaved. */
            if (_setStartUrl.Value != url) _setStartUrl.Value = url;
            _settings.DefaultStartUrl = string.IsNullOrEmpty(url) ? "https://www.google.com" : url;
            _settings.ThemeMode = Theme.Dark ? 1 : 2;
            _settings.DefaultGroup = (_setDefaultGroup.Value ?? "").Trim();
            /* Normalised rather than trimmed: a paste from the console usually carries quotes,
               a trailing space or the "id:secret" pair, and the raw value reached Google as an
               invalid client. */
            string clientId = GoogleSignIn.NormalizeClientId(_setGoogleId.Value);
            /* A client id that is not a client id produces an opaque "invalid_client" from
               Google later. Catch it at the point where it was typed instead. */
            if (clientId.Length > 0 && !GoogleSignIn.LooksValid(clientId))
            {
                Toast("That does not look like a Google OAuth client id: the part before .apps.googleusercontent.com "
                    + "starts with the project number, e.g. 123456789012-abc123.", ToastKind.Warn);
                _setGoogleId.Box.Focus();
                _setGoogleId.Box.SelectAll();
                return;
            }
            _settings.GoogleClientId = clientId;
            _settings.GoogleClientSecret = (_setGoogleSecret.Value ?? "").Trim();
            _settings.EnableExtensions = _setExtensions.Checked;
            _settings.EnableCustomExtensions = _setCustomExt.Checked;
            _settings.UseTrash = _setTrash.Checked;
            _settings.HistoryEnabled = _setHistory.Checked;
            if (!string.IsNullOrEmpty(_setLang.Value)) _settings.UiLanguage = _setLang.Value;
            try
            {
                _store.SaveSettings(_settings);
                Toast("Settings saved", ToastKind.Success);
            }
            catch (Exception ex)
            {
                /* A green "Settings saved" toast over a disk that refused the write is how a
                   user loses their configuration and never finds out. */
                Log.Error("Settings not saved", ex);
                Toast("Settings could not be saved: " + ex.Message, ToastKind.Error);
            }
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
            /* Pages paint from Theme.* live, but their BackColor and a few cached brushes are
               captured at construction, so a theme flip has to walk the tree once. */
            Theme.Restyle(this);
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
                g.DrawLine(p, Ui.S(20), Ui.S(114), sb - Ui.S(20), Ui.S(114));

            // ---- page header band ----
            string title, sub;
            switch (_page)
            {
                case 0:
                    title = L10n.T("header.profiles");
                    int shown = ShownCount();
                    sub = _filter.Length > 0
                        ? shown + " of " + _profiles.Count + " profiles"
                        : _profiles.Count + (_profiles.Count == 1 ? " profile" : " profiles");
                    /* The header is the one place a user always sees, so it carries the state of
                       the workspace and not just its size: how many profiles share an identity and
                       how many browser builds they run on. Without this the two facts that decide
                       whether the workspace is safe are only visible by opening every card. */
                    if (_collisionGroups > 0 && _filter.Length == 0)
                        sub += "  \u00b7  " + _collisionProfiles + " share a fingerprint";
                    if (_filter.Length == 0 && _profiles.Count > 1)
                    {
                        int builds = KernelDiversity.DistinctCount(_profiles);
                        if (builds <= 1) sub += "  \u00b7  one browser build";
                    }
                    break;
                case 1: title = L10n.T("header.proxies"); sub = L10n.T("sub.proxies"); break;
                case 2: title = L10n.T("header.extensions"); sub = L10n.T("sub.extensions"); break;
                case 3: title = L10n.T("header.browsers"); sub = L10n.T("sub.browsers"); break;
                case 4: title = L10n.T("header.settings"); sub = L10n.T("sub.settings"); break;
                case 5: title = L10n.T("header.tools"); sub = L10n.T("sub.tools"); break;
                case 7: title = L10n.T("header.plan"); sub = L10n.T("sub.plan"); break;
                default: title = L10n.T("header.about"); sub = L10n.T("sub.about"); break;
            }

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

            // navbar rows, grouped the way a workspace actually reads: day-to-day work first,
            // configuration second.
            int navX = Ui.S(10), navW = sb - Ui.S(20);
            /* The theme switch belongs with the app chrome, not with navigation: at the top of
               the rail it competed with the primary destinations for attention. */
            _themeSwitch.SetBounds(navX, h - Ui.S(54), navW, Ui.S(38));
            int navY = Ui.S(130), navStep = Ui.S(44);
            _navGroupWorkspace.SetBounds(navX + Ui.S(8), Ui.S(112), navW, Ui.S(16));
            _navProfiles.SetBounds(navX, navY, navW, Ui.S(40));
            _navHistory.SetBounds(navX, navY + navStep, navW, Ui.S(40));
            _navAccount.SetBounds(navX, navY + navStep * 2, navW, Ui.S(40));
            _navProxies.SetBounds(navX, navY + navStep * 3, navW, Ui.S(40));
            _navExtensions.SetBounds(navX, navY + navStep * 4, navW, Ui.S(40));
            _navBrowsers.SetBounds(navX, navY + navStep * 5, navW, Ui.S(40));
            _navGroupSystem.SetBounds(navX + Ui.S(8), navY + navStep * 6 + Ui.S(2), navW, Ui.S(16));
            _navSettings.SetBounds(navX, navY + navStep * 6 + Ui.S(22), navW, Ui.S(40));
            _navTools.SetBounds(navX, navY + navStep * 7 + Ui.S(22), navW, Ui.S(40));
            _navAbout.SetBounds(navX, navY + navStep * 8 + Ui.S(22), navW, Ui.S(40));
            _navPlan.SetBounds(navX, navY + navStep * 9 + Ui.S(22), navW, Ui.S(40));

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
            int emptyW;
            /* When a search caused the empty list the panel offers "Clear search" next to the
               create action instead of hiding the way back. */
            bool searching = _filter.Length > 0;
            _emptyBtn.Text = searching ? "New profile" : L10n.T("empty.action");
            _emptyBtn.Kind = searching ? BtnKind.Soft : BtnKind.Primary;
            emptyW = _emptyBtn.PreferredWidth(Ui.S(160));
            if (emptyW < Ui.S(40)) emptyW = Ui.S(160);
            int extraW = searching ? _clearFilter.PreferredWidth(Ui.S(140)) + Ui.S(10) : 0;
            int startX = (_emptyPanel.Width - (emptyW + extraW)) / 2;
            int btnY = _emptyPanel.Height / 2 + Ui.S(52);
            if (searching)
            {
                _clearFilter.Kind = BtnKind.Primary;
                _clearFilter.SetBounds(startX, btnY, extraW - Ui.S(10), Ui.S(33));
                _clearFilter.Visible = true;
                _emptyBtn.SetBounds(startX + extraW, btnY, emptyW, Ui.S(33));
            }
            else
            {
                _clearFilter.Visible = false;
                _emptyBtn.SetBounds(startX, btnY, emptyW, Ui.S(33));
            }
            _emptyBtn.Invalidate();

            int pageH = Math.Max(1, h - headerH);
            _settingsPage.SetBounds(contentX, headerH, contentW, pageH);
            _settingsHost.SetBounds(Ui.S(ContentPad), Ui.S(8), Math.Max(0, contentW - Ui.S(ContentPad) * 2),
                Math.Max(0, pageH - Ui.S(16)));
            LayoutSettings();

            _toolsPage.SetBounds(contentX, headerH, contentW, pageH);
            _proxyPage.SetBounds(contentX, headerH, contentW, pageH);
            _extensionPage.SetBounds(contentX, headerH, contentW, pageH);
            _browsersPage.SetBounds(contentX, headerH, contentW, pageH);
            _upgradePage.SetBounds(contentX, headerH, contentW, pageH);
            _aboutPage.SetBounds(contentX, headerH, contentW, pageH);
            LayoutAbout();

            int tw = Math.Min(Ui.S(560), Math.Max(Ui.S(240), contentW - Ui.S(64)));
            _toast.Place(contentX + (contentW - tw) / 2, h - Ui.S(16), tw);

            ResumeLayout(false);
            Invalidate();
        }

        /// <summary>
        /// Puts the layers back in their fixed order, bottom to top: page content, then the
        /// toast, then the caption buttons. Called after anything that changes what is on top -
        /// a page switch, a toast, the bulk bar - so a transient message can never end up under
        /// a control it is supposed to float above.
        /// </summary>
        private void Restack()
        {
            try
            {
                if (_toast != null) _toast.BringToFront();
                if (_minBtn != null) _minBtn.BringToFront();
                if (_maxBtn != null) _maxBtn.BringToFront();
                if (_closeBtn != null) _closeBtn.BringToFront();
            }
            catch { }
        }

        /// <summary>
        /// The one line that says whether this workspace is spread across builds or sitting on a
        /// single binary. Recomputed on every visit because installing a kernel changes it.
        /// </summary>
        private void RefreshKernelInfo()
        {
            if (_setKernelInfo == null) return;
            int builds = KernelDiversity.Available().Count;
            int used = KernelDiversity.DistinctCount(_profiles);
            if (builds <= 1)
                _setKernelInfo.Text = "1 build installed \u00b7 every profile shares it. Add another kernel on the Browsers page.";
            else
                _setKernelInfo.Text = builds + " builds installed \u00b7 " + used +
                    (used == 1 ? " in use" : " in use") + ". New profiles take a build the workspace has not used.";
        }

        private void LayoutSettings()
        {
            if (_settingsColumn == null || _setCards.Count == 0) return;
            int hostW = Math.Max(Ui.S(320), _settingsHost.Width - Ui.S(10));
            // Windows UI pages run edge to edge; a centered column only wasted the viewport.
            int colW = Math.Max(Ui.S(320), hostW - Ui.S(8));
            int headH = Ui.S(44), rowH = Ui.S(54), pad = Ui.S(18), gap = Ui.S(16);
            /* One row runs to two lines of text - the browser-build summary - and a wrapped label
               in a 54px row would be clipped by the divider under it. */
            int tallRowH = Ui.S(68);

            _settingsColumn.SetBounds(0, 0, Math.Max(colW, hostW), _settingsColumn.Height);

            int y = Ui.S(4);
            foreach (var card in _setCards)
            {
                int rowsH = 0;
                foreach (var r in card.Rows) rowsH += RowHeight(r, rowH, tallRowH);
                int h = headH + rowsH + Ui.S(10);
                card.Panel.SetBounds(0, y, colW, h);
                var lbl = card.Panel.Tag as JBLabel;
                if (lbl != null) lbl.SetBounds(pad, Ui.S(14), colW - pad * 2, Ui.S(18));

                var divs = new float[Math.Max(0, card.Rows.Count - 1)];
                int rowY = headH;
                for (int i = 0; i < card.Rows.Count; i++)
                {
                    if (i > 0) divs[i - 1] = rowY;
                    int rh = RowHeight(card.Rows[i], rowH, tallRowH);
                    LayoutSetRow(card.Rows[i], colW, rowY, rh, pad);
                    rowY += rh;
                }
                card.Panel.Dividers = divs;
                card.Panel.Invalidate();
                y += h + gap;
            }

            _settingsColumn.Height = y + Ui.S(8);
            _settingsColumn.Width = Math.Max(colW + Ui.S(20), hostW);
            _settingsHost.SyncExtent();
        }

        /// <summary>Rows that carry a wrapped explanatory value need the extra height.</summary>
        private int RowHeight(SetRow row, int normal, int tall)
        {
            return row.Value != null && Wraps(row.Value) ? tall : normal;
        }

        private static bool Wraps(Control c)
        {
            var l = c as JBLabel;
            return l != null && !l.AutoEllipsis;
        }

        private void LayoutSetRow(SetRow row, int colW, int rowY, int rowH, int pad)
        {
            int[] w = RowWidths(row, colW, pad);
            int right = colW - pad;
            int edge = right;
            for (int i = row.Controls.Length - 1; i >= 0; i--)
            {
                var c = row.Controls[i];
                edge -= w[i];
                c.SetBounds(edge, rowY + (rowH - c.Height) / 2, w[i], c.Height);
                if (i > 0) edge -= Ui.S(8);
            }

            int valRight = row.Controls.Length > 0 ? edge - Ui.S(16) : right;
            int usable = Math.Max(Ui.S(120), valRight - pad);
            // A row with a value splits the card between the label and its current setting.
            int titleW = row.Value == null ? usable : Math.Min(Ui.S(320), (int)(usable * 0.45f));
            // Labels stop a pixel short of the boundary: an opaque label would paint over the hairline.
            int ly = rowY + 1, lh = rowH - 2;
            row.Title.SetBounds(pad, ly, titleW, lh);
            if (row.Value == null) return;
            int vx = pad + titleW + Ui.S(16);
            row.Value.SetBounds(vx, ly, Math.Max(Ui.S(60), valRight - vx), lh);
        }

        /// <summary>
        /// Resting widths for a row's controls, re-measured for buttons so a longer translation
        /// cannot clip them. Text fields absorb any overflow first; the label keeps its space.
        /// </summary>
        private int[] RowWidths(SetRow row, int colW, int pad)
        {
            int n = row.Controls.Length;
            var w = new int[n];
            int total = n > 1 ? Ui.S(8) * (n - 1) : 0;
            int flexCount = 0;
            for (int i = 0; i < n; i++)
            {
                int wi = row.Widths[i];
                var b = row.Controls[i] as JBButton;
                if (b != null) wi = b.PreferredWidth(wi);
                else if (row.Controls[i] is JBTextBox) flexCount++;
                w[i] = wi;
                total += wi;
            }

            int limit = colW - pad * 2 - Ui.S(300);
            if (total > limit && flexCount > 0)
            {
                int over = total - limit;
                int shrunk = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!(row.Controls[i] is JBTextBox)) continue;
                    int share = over / flexCount;
                    int nw = Math.Max(Ui.S(140), w[i] - share);
                    shrunk += w[i] - nw;
                    w[i] = nw;
                }
                if (shrunk < over)
                {
                    // Even at their floor the fields overflow; let the label give ground instead.
                    int still = over - shrunk;
                    for (int i = 0; i < n; i++)
                        if (row.Controls[i] is JBTextBox) { w[i] = Math.Max(Ui.S(100), w[i] - still / flexCount); break; }
                }
            }
            return w;
        }

        private void LayoutAbout()
        {
            if (_aboutHost == null) return;
            int w = _aboutPage.Width, h = _aboutPage.Height;
            _aboutHost.SetBounds(Ui.S(ContentPad), Ui.S(8),
                Math.Max(Ui.S(160), w - Ui.S(ContentPad) * 2), Math.Max(Ui.S(120), h - Ui.S(16)));
            int tw = _aboutTelegram.PreferredWidth(Ui.S(190));
            _aboutTelegram.SetBounds(Math.Max(Ui.S(300), _aboutColumn.Width) - tw - Ui.S(20), Ui.S(47), tw, Ui.S(33));
            _aboutColumn.Invalidate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Switching pages used to repaint once per Visible change, which showed up as a flash
        /// of the previous page. WM_SETREDRAW freezes the whole window until the new page is
        /// laid out, then a single paint draws the final state.
        /// </summary>
        private void FreezeRedraw(Action work)
        {
            bool frozen = IsHandleCreated;
            if (frozen) SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            try { work(); }
            finally
            {
                if (frozen && IsHandleCreated)
                {
                    SendMessage(Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
                    Invalidate(true);
                }
            }
        }

        private void SignOut()
        {
            if (!ConfirmForm.Ask(this, L10n.T("acct.signout"), L10n.T("acct.signout.confirm"),
                L10n.T("acct.signout.detail"), L10n.T("acct.signout"), BtnKind.Primary)) return;
            Session.SignOut();
            Application.Restart();
            Environment.Exit(0);
        }

        private void SwitchAccount()
        {
            if (ConfirmForm.Ask(this, L10n.T("acct.switch.title"), L10n.T("acct.switch.body"), null,
                L10n.T("acct.switch"), BtnKind.Primary))
                SignOut();
        }

        private void ShowSecurity()
        {
            if (Session.Current == null) { Toast(L10n.T("acct.signedout"), ToastKind.Error); return; }
            using (var dlg = new SecurityDialog(Session.Current.Email)) dlg.ShowDialog(this);
            _accountPage.Reload();
        }

        private void RemoveAccount(string email)
        {
            if (string.Equals(email, Session.Current == null ? null : Session.Current.Email, StringComparison.OrdinalIgnoreCase))
            {
                Toast(L10n.T("acct.switch.body"), ToastKind.Error);
                return;
            }
            if (!ConfirmForm.Ask(this, L10n.T("acct.remove.title"), L10n.T("acct.remove.body"),
                L10n.T("acct.remove.detail"), L10n.T("acct.remove"), BtnKind.Danger)) return;
            Accounts.Delete(email);
            _accountPage.Reload();
            Toast(email + " " + L10n.T("acct.switched"), ToastKind.Info);
        }

        /// <summary>The profile whose history is showing, or the most recently used one.</summary>
        private string _historyProfileId;

        private Profile HistoryTarget()
        {
            if (!string.IsNullOrEmpty(_historyProfileId))
                foreach (var p in _profiles) if (p.Id == _historyProfileId) return p;
            /* Newest first, so an unset target is the profile the user last actually browsed. */
            Profile best = null;
            foreach (var p in _profiles)
                if (best == null || p.LastOpened > best.LastOpened) best = p;
            return best;
        }

        /// <summary>Opens the History page focused on one profile rather than the last used.</summary>
        private void ShowProfileHistory(string id)
        {
            _historyProfileId = id;
            SetPage(8);
        }

        private void RefreshHistory()
        {
            if (_historyPage == null) return;
            _historyPage.Show(HistoryTarget());
        }

        /// <summary>Wipes history for every profile of the signed-in account.</summary>
        private void ClearAllHistory()
        {
            if (!ConfirmForm.Ask(this, L10n.T("history.confirmall.title"), L10n.T("history.confirmall.body"),
                L10n.T("history.confirmall.detail"), L10n.T("set.clearhistory"), BtnKind.Danger)) return;
            int n = HistoryStore.ClearAll();
            if (_historyPage != null) _historyPage.Rebuild();
            Toast(n + " " + L10n.T("history.cleared"), ToastKind.Success);
        }

        private void ClearHistory()
        {
            var p = HistoryTarget();
            if (p == null) { Toast(L10n.T("history.none"), ToastKind.Info); return; }
            if (!ConfirmForm.Ask(this, L10n.T("history.confirm.title"), L10n.T("history.confirm.body"),
                L10n.T("history.confirm.detail"), "Clear", BtnKind.Danger)) return;
            int n = HistoryStore.Clear(p.Id);
            _historyPage.Rebuild();
            Toast(n + " " + L10n.T("history.cleared"), ToastKind.Success);
        }

        /// <summary>Opens a recorded URL in that profile's browser, launching it if needed.</summary>
        private void OpenHistoryUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var p = HistoryTarget();
            if (p == null) return;
            _historyProfileId = p.Id;
            /* Re-opening a profile that is already running just focuses it: the URL belongs to
               the tab the user already has, not to a second window. */
            OpenProfile(p.Id, url);
        }

        private void SetPage(int page)
        {
            FreezeRedraw(delegate { SetPageCore(page); });
        }

        private void SetPageCore(int page)
        {
            _page = page;
            SuspendLayout();
            /* Pages 0-7 are the long-standing destinations; 8 and 9 are History and Account.
               Selected state is per button, so the subscript mismatch between "page number" and
               "slot in this array" has to be spelled out rather than assumed. */
            JBButton[] navs = { _navProfiles, _navProxies, _navExtensions, _navBrowsers, _navSettings, _navTools, _navAbout, _navPlan, _navHistory, _navAccount };
            int[] pageOf = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            for (int i = 0; i < navs.Length; i++)
            {
                navs[i].Selected = pageOf[i] == page;
                navs[i].Invalidate();
            }

            bool profiles = page == 0;
            _host.Visible = profiles && _cards.Count > 0;
            if (!profiles) _picks.Clear();
            _bulk.Visible = profiles && _picks.Count > 1;
            _emptyPanel.Visible = profiles && ShownCount() == 0;
            _proxyPage.Visible = page == 1;
            _extensionPage.Visible = page == 2;
            _browsersPage.Visible = page == 3;
            _settingsPage.Visible = page == 4;
            // Refreshed on arrival, not only on resize: installing a kernel on the Browsers page
            // changes this line, and a stale count would quietly contradict the page next door.
            if (page == 4) RefreshKernelInfo();
            _toolsPage.Visible = page == 5;
            _aboutPage.Visible = page == 6;
            _upgradePage.Visible = page == 7;
            _historyPage.Visible = page == 8;
            if (page == 8) RefreshHistory();
            _accountPage.Visible = page == 9;
            if (page == 9) _accountPage.Reload();
            _search.Visible = profiles;
            _sortBtn.Visible = profiles;
            _newBtn.Visible = profiles;
            _saveSettings.Visible = page == 4;
            _themeSwitch.Visible = true;

            /* The newly visible page has to come forward over the other page containers...
               and then the fixed chrome has to be put back on top of everything. Every page
               that is not showing stays Visible=false but keeps its z-order slot above the
               toast, so without the second step the answer to a save, a proxy test or a Google
               sign-in was hidden behind an invisible page on every screen but Profiles, and the
               user saw nothing happen at all. */
            Control[] pages = { _host, _emptyPanel, _proxyPage, _extensionPage, _browsersPage, _settingsPage, _toolsPage, _aboutPage, _upgradePage, _historyPage, _accountPage };
            foreach (var p in pages) if (p.Visible) p.BringToFront();
            Restack();

            if (page == 1)
            {
                _proxies = ProxyVault.Load();
                _proxyPage.SetItems(_proxies);
            }
            else if (page == 2)
            {
                _extensions = ExtensionCatalog.Load();
                _extensionPage.SetItems(_extensions);
            }
            else if (page == 3)
            {
                _browsersPage.Rescan();
            }
            else if (page == 7)
            {
                RefreshPlanState();
                /* Re-open the session that was live when the app last closed, so a payment made
                   just before closing is still watched instead of being treated as too old. */
                _upgradePage.RestorePending();
                _payTicks = 0;
                if (!_planTimer.Enabled) _planTimer.Start();
            }
            else if (page == 4)
            {
                _setBrowserPath.Value = _settings.BrowserPath ?? "";
                _setGoogleId.Value = GoogleSignIn.ClientId();
                _setGoogleSecret.Value = string.IsNullOrWhiteSpace(_settings.GoogleClientSecret)
                    ? GoogleSignIn.DefaultClientSecret : _settings.GoogleClientSecret;
                _setStartUrl.Value = _settings.DefaultStartUrl ?? "";
                _setStoragePath.Text = AppPaths.Root;
                _setDefaultGroup.Value = _settings.DefaultGroup ?? "";
                _setExtensions.Checked = _settings.EnableExtensions;
                _setCustomExt.Checked = _settings.EnableCustomExtensions;
                _setTrash.Checked = _settings.UseTrash;
                _setHistory.Checked = _settings.HistoryEnabled;
                _setLang.Value = string.IsNullOrEmpty(_settings.UiLanguage) ? "en" : _settings.UiLanguage;
                UpdateBrowserInfo();
            }
            ResumeLayout(false);
            LayoutAll();
        }

        private void ReloadCards(bool fromDisk)
        {
            /* A zero-count reload used to re-read the store on every call, so deleting the
               last profile hit the disk twice in a row for no reason. From-disk is the explicit
               signal for "the store may have changed". */
            if (fromDisk || _profiles == null)
                _profiles = _store.LoadAll();
            ApplySort();

            foreach (var c in _cards.Values) c.Dispose();
            _cards.Clear();
            _grid.Controls.Clear();

            /* Worked out once per rebuild rather than per card: the ordering that decides which
               profiles a lapsed plan still covers is a sort of the whole list. */
            var locked = Entitlements.LockedProfiles(_profiles);
            foreach (var p in _profiles)
            {
                string id = p.Id;
                var card = new ProfileCard
                {
                    Model = p,
                    Running = _sessions.ContainsKey(id),
                    Launching = _launching.Contains(id),
                    Locked = locked.Contains(id),
                    AccessibleName = p.Name,
                    AccessibleRole = AccessibleRole.PushButton
                };
                card.OpenRequested += delegate { OpenProfile(id); };
                card.FocusRequested += delegate { FocusSession(id); };
                card.CloseRequested += delegate { CloseSession(id); };
                card.EditRequested += delegate { EditProfile(id); };
                card.ProxyRequested += delegate { EditProfile(id, 3); };
                card.RenameRequested += delegate { RenameProfile(id); };
                card.ClearCacheRequested += delegate { ClearProfileData(id); };
                card.LeakTestRequested += delegate { RunLeakTest(id); };
                card.HistoryRequested += delegate { ShowProfileHistory(id); };
                card.DuplicateRequested += delegate { DuplicateProfile(id); };
                card.DeleteRequested += delegate { DeleteProfile(id); };
                card.PickToggled += delegate { TogglePick(id, (ModifierKeys & Keys.Shift) != 0); };
                _grid.Controls.Add(card);
                _cards[id] = card;
            }

            ApplySignatures();

            /* Cards are rebuilt from scratch, so any selection that pointed at a profile which
               no longer exists has to go before the filter walks it. This was the crash: a
               picked profile was deleted, ReloadCards dropped its card, and ApplyFilter then
               indexed _cards with the stale id - KeyNotFoundException, reported to the user as
               "Joe Browser hit an unexpected error" every time a picked profile was deleted. */
            PrunePicks();
            ApplyFilter();
            Invalidate();
        }

        /// <summary>Collision counters from the last reload, for the header subtitle.</summary>
        private int _collisionGroups, _collisionProfiles;

        /// <summary>
        /// Puts a fingerprint signature on every card, and marks the ones that collide.
        ///
        /// Computed here rather than per card because a signature only means something in
        /// company: the same ten characters on two cards is the visual proof that two profiles
        /// are one browser, and a card cannot see its siblings. Hashing twelve surfaces per
        /// profile is a few microseconds, and this runs only when the list is rebuilt.
        /// </summary>
        private void ApplySignatures()
        {
            _collisionGroups = 0;
            _collisionProfiles = 0;
            if (_cards.Count == 0) return;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var sigs = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var kv in _cards)
            {
                var p = kv.Value.Model;
                if (p == null || p.Fp == null) continue;
                string sig = FingerprintIdentity.OverallHash(p);
                sigs[kv.Key] = sig;
                int n;
                seen.TryGetValue(sig, out n);
                seen[sig] = n + 1;
            }

            foreach (var kv in _cards)
            {
                string sig;
                if (!sigs.TryGetValue(kv.Key, out sig) || sig.Length == 0) continue;
                int n;
                seen.TryGetValue(sig, out n);
                /* A clash is worth a pointer, but the audit lives in Tools and a person reading a
                   tooltip is not going to navigate there from a hint alone - so the text names the
                   action and the numbers behind it. */
                kv.Value.SetSignature(sig, n > 1
                    ? "Fingerprint " + sig + " is shared by " + n +
                      " profiles. Tools \u203a Account audit finds every group and can separate them."
                    : "Fingerprint " + sig + " \u00b7 unique in this workspace");
                kv.Value.Invalidate();
            }

            foreach (var kv in seen) if (kv.Value > 1) { _collisionGroups++; _collisionProfiles += kv.Value; }
        }

        /// <summary>
        /// Drops selection entries whose card no longer exists, and an anchor that points at
        /// nothing. Every read of _picks goes through this first, because a delete, a filter
        /// change and a bulk action can each remove a card under it.
        /// </summary>
        private void PrunePicks()
        {
            for (int i = _picks.Count - 1; i >= 0; i--)
                if (!_cards.ContainsKey(_picks[i])) _picks.RemoveAt(i);
            if (_pickAnchor != null && !_cards.ContainsKey(_pickAnchor)) _pickAnchor = null;
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
                    || (p.Fp != null && ((p.Fp.Language ?? "").ToLowerInvariant().Contains(q)
                        || FingerprintFactory.LanguageName(p.Fp.Language).ToLowerInvariant().Contains(q)))
                    || (p.Proxy != null && (p.Proxy.Host ?? "").ToLowerInvariant().Contains(q));
                kv.Value.Visible = hit;
                if (hit) shown++;
            }
            /* Only selections that are still on screen survive a filter change. A pick whose
               card has been unloaded is dropped too, rather than trusted - see PrunePicks. */
            for (int i = _picks.Count - 1; i >= 0; i--)
            {
                ProfileCard c;
                if (!_cards.TryGetValue(_picks[i], out c) || !c.Visible) _picks.RemoveAt(i);
            }
            if (_picks.Count == 0) _pickAnchor = null;
            _emptyPanel.Visible = _page == 0 && shown == 0;
            SyncPicks();
            /* Z-order first, then layout: raising the host after the grid was measured but
               before the page that owns it was raised left the stacked "no profiles" panel on
               top of the first card whenever a filter emptied and then refilled the list. */
            if (_emptyPanel.Visible) _emptyPanel.BringToFront();
            else if (shown > 0 && _page == 0) _host.BringToFront();
            _grid.LayoutGrid();
            _host.SyncExtent();
            Restack();
            _emptyPanel.Invalidate();
            Invalidate();
        }

        /// <summary>One place that undoes a search, used by Esc, the empty panel and the filter chip.</summary>
        private void ClearSearch()
        {
            if (_search != null) _search.Value = "";
            _filter = "";
            ApplyFilter();
            if (_search != null && _search.Visible) { try { _search.Box.Focus(); } catch { } }
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

            string title = searching ? L10n.T("empty.nomatch") : L10n.T("empty.title");
            string sub = searching ? L10n.T("empty.nomatch.sub") : L10n.T("empty.sub");

            Ui.Text(g, title, Theme.H1, Theme.Text, 0, br.Bottom + Ui.S(16), w, "center");
            Ui.Text(g, sub, Theme.Small, Theme.TextDim, 0, br.Bottom + Ui.S(44), w, "center", 2);
        }

        /// <summary>
        /// The single way a profile is born: a fresh identity that is provably unlike everything
        /// already in the workspace, on a browser build the workspace has not used yet.
        ///
        /// Every creation path goes through this - the New button, batch create, duplicating with
        /// a proxy, importing a list - because the alternative is what the app used to do: four
        /// different call sites each rolling their own random fingerprint, three of which forgot
        /// to check anything.
        /// </summary>
        private Profile SeedNewProfile(string name, ProxyConfig proxy)
        {
            var p = new Profile();
            if (!string.IsNullOrEmpty(name)) p.Name = name;
            if (proxy != null && proxy.Enabled) p.Proxy = proxy;
            p.Tools.ProxyWatch = p.Proxy.Enabled;
            FingerprintIdentity.Comparison clash;
            FingerprintIdentity.MakeUnique(p.Fp, "System default", p.Proxy, _profiles, out clash);
            string note;
            KernelDiversity.Assign(p, _profiles, out note);
            return p;
        }

        private void NewProfile()
        {
            if (!AllowAnotherProfile()) return;
            /* A new profile is generated unique against the workspace before the editor opens,
               so the chips the user sees are the truth: rolling here and de-duplicating on save
               would show one fingerprint and store another. */
            var p = SeedNewProfile(null, null);
            using (var dlg = new EditorForm(p, true, _settings, 0, new List<Profile>(_profiles)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _store.Save(dlg.Result);
                ReloadCards(true);
                Toast("Profile created", ToastKind.Success);
            }
            if (KernelDiversity.Available().Count == 0)
                OfferKernelForNewProfiles();
        }

        /// <summary>
        /// Asked once, when the workspace has no second build. Every new profile must carry a
        /// browser version of its own, and on a machine with one installed browser that is
        /// impossible however the identity is rolled, so the app offers the thing that makes it
        /// possible instead of quietly producing profiles that share a version - which is what
        /// the fingerprint check reports as a match with the host browser.
        /// </summary>
        private void OfferKernelForNewProfiles()
        {
            if (_kernelAsked) return;
            _kernelAsked = true;
            bool go = ConfirmForm.Ask(this, "Give every profile its own browser version",
                "Only one browser build is installed on this PC.",
                "New profiles then share its version, which is the first thing a fingerprint test " +
                "compares against your own browser. Joe Browser can download another build and " +
                "spread new profiles across them.",
                "Download a second build", BtnKind.Primary);
            if (go) SetPage(3);
        }

        private bool _kernelAsked;

        private async System.Threading.Tasks.Task EditProfileCore(string id, int tab = 0)
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

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void EditProfile(string id, int tab = 0) { Guard("EditProfile", delegate { return EditProfileCore(id, tab); }); }

        /// <summary>Quick rename, so changing the label never means opening the whole editor.</summary>
        private void RenameProfile(string id)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            using (var dlg = new ToolDialog("Rename profile", "Rename", "Cancel", 460, 260))
            {
                var field = dlg.AddField("PROFILE NAME", p.Name, "Name this profile", "user", false, 40);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string name = (field.Value ?? "").Trim();
                if (name.Length == 0) return;
                p.Name = name;
                try { _store.Save(p); }
                catch (Exception ex)
                {
                    Log.Error("Rename failed", ex);
                    Toast("Could not rename the profile", ToastKind.Error);
                    return;
                }
                BrowserSession s;
                if (_sessions.TryGetValue(id, out s) && s.IsRunning) s.UpdateProfile(p);
                ReloadCards(true);
                Toast("Renamed to " + name, ToastKind.Success);
            }
        }

        /// <summary>
        /// Clears this profile's cache - or its cache and cookies when it is closed. An open
        /// profile is cleaned through the browser itself, so the tab survives.
        /// </summary>
        private async System.Threading.Tasks.Task ClearProfileDataCore(string id)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            BrowserSession s;
            _sessions.TryGetValue(id, out s);
            bool live = s != null && s.IsRunning;
            long size = live ? ProfileData.CacheSize(p) : ProfileData.Size(p);

            string title = live ? "Clear cache" : "Clear cache and cookies";
            string body = live
                ? "Clear the cached files of this open profile?\r\nThe tab stays open and you stay signed in."
                : "Clear cached files and cookies for this profile?\r\n" + ProfileData.Human(size) +
                  " will be removed and its sites will sign you out.";
            if (!ConfirmForm.Ask(this, title, body, null, "Clear", BtnKind.Primary)) return;

            try
            {
                long freed = await ProfileData.ClearAsync(p, live ? ClearScope.Cache : ClearScope.SiteData, s);
                Toast(live ? "Cache cleared" : "Cleared " + ProfileData.Human(freed), ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("Clear profile data failed", ex);
                Toast(ex.Message, ToastKind.Error);
            }
        }

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void ClearProfileData(string id) { Guard("ClearProfileData", delegate { return ClearProfileDataCore(id); }); }

        /// <summary>Reads the live tab back and reports what a fingerprint page would see.</summary>
        private async System.Threading.Tasks.Task RunLeakTestCore(string id)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            BrowserSession s;
            if (!_sessions.TryGetValue(id, out s) || s == null || !s.IsRunning)
            {
                Toast("Open the profile first, then run the check.", ToastKind.Info);
                return;
            }
            Toast("Reading the live session\u2026", ToastKind.Info);
            LeakResult result;
            try { result = await LeakTest.RunAsync(s, p.Fp); }
            catch (Exception ex)
            {
                Log.Error("Leak test failed", ex);
                Toast("The check could not run: " + ex.Message, ToastKind.Error);
                return;
            }
            using (var dlg = new LeakForm(result, p.Name, s)) dlg.ShowDialog(this);
            if (!string.IsNullOrEmpty(result.Error)) Toast(result.Error, ToastKind.Warn);
            else if (result.Clean) Toast("No leak found in " + result.Total + " checks", ToastKind.Success);
            else Toast(result.Verdict, ToastKind.Warn);
        }

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void RunLeakTest(string id) { Guard("RunLeakTest", delegate { return RunLeakTestCore(id); }); }

        /// <summary>Reason the last duplicate could not be given its own identity, if any.</summary>
        private string _duplicateWarning;

        private void DuplicateProfile(string id) { DuplicateProfile(id, true); }
        private bool DuplicateProfile(string id, bool announce)
        {
            _duplicateWarning = null;
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return false;
            /* The bulk path is clamped once, up front: _profiles does not grow inside the loop,
               so re-checking here would let a batch overshoot the cap. */
            if (announce && !AllowAnotherProfile()) return false;
            var copy = p.Copy(true);
            copy.Name = p.Name + " copy";
            copy.Proxy.Password = p.Proxy.Password;
            /* The copy starts from the source's fingerprint settings and then re-rolls, so the
               OS and locale family still read as the same kind of machine while no surface is
               shared. Duplicating used to leave every surface identical. */
            FingerprintIdentity.Comparison clash;
            FingerprintIdentity.MakeUnique(copy.Fp, copy.Fp.Os, copy.Proxy, _profiles, out clash);
            string kernelNote;
            bool pinned = KernelDiversity.AssignDifferent(copy, p, _profiles, out kernelNote);
            string warning = null;
            if (!pinned) warning = kernelNote;
            else if (clash != null && clash.Relation == FingerprintIdentity.Relation.Linked)
                warning = clash.Summary;
            _duplicateWarning = warning;
            try
            {
                _store.Save(copy);
                if (!announce) return true;
                ReloadCards(true);
                Toast(warning == null ? "Profile duplicated \u00b7 new fingerprint and kernel"
                    : "Profile duplicated \u00b7 " + warning,
                    warning == null ? ToastKind.Success : ToastKind.Warn);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Duplicate failed", ex);
                Toast("Could not duplicate profile", ToastKind.Error);
                return false;
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
            PrunePicks();
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
            PrunePicks();
            foreach (var kv in _cards) kv.Value.Picked = _picks.Contains(kv.Key);
            LayoutBulk();
            _grid.Invalidate();
        }

        private void LayoutBulk()
        {
            if (_bulk == null || _host == null) return;
            PrunePicks();
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
            Restack();   // the toast and the caption buttons stay above the bulk action bar
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
            var ids = _picks.ToArray();
            int allowed = AllowProfiles(ids.Length);
            if (allowed <= 0) { ClearPicks(); return; }

            int made = 0, failed = 0;
            string lastWarning = null;
            foreach (string id in ids)
            {
                if (made >= allowed) break;
                /* Counted from what actually succeeded. The old loop incremented made whether or
                   not the save worked, so a full disk produced a cheerful "5 profiles duplicated"
                   and five missing cards. */
                if (DuplicateProfile(id, false)) { made++; lastWarning = _duplicateWarning; }
                else failed++;
            }
            ReloadCards(true);
            ClearPicks();
            if (failed > 0)
                Toast(made + " duplicated, " + failed + " could not be saved", ToastKind.Warn);
            else if (lastWarning != null)
                Toast(made + (made == 1 ? " profile duplicated" : " profiles duplicated") +
                      " \u00b7 " + lastWarning, ToastKind.Warn);
            else
                Toast(made + (made == 1 ? " profile duplicated" : " profiles duplicated"), ToastKind.Success);
        }

        private void BulkDelete()
        {
            var list = _profiles.Where(p => _picks.Contains(p.Id)).ToList();
            if (list.Count == 0) { ClearPicks(); return; }
            bool trash = _settings.UseTrash;
            if (!ConfirmForm.Ask(this, "Delete profiles",
                trash
                    ? "Move " + list.Count + " profiles to the recycle bin?"
                    : "Delete " + list.Count + " profiles and all of their browsing data? This cannot be undone.",
                trash ? "You can restore them from Tools & data → Recycle bin."
                      : "Proxy settings stored in them are removed too."))
                return;
            /* Each DeleteProfile(..., false) takes its card out of the grid, so the grid is
               never rebuilt once per item. Profiles loaded from disk still need one reload to
               pick up anything the store rewrote. */
            foreach (var p in list) DeleteProfile(p.Id, false);
            ReloadCards(true);
            ClearPicks();
            Toast(trash ? list.Count + " profiles moved to the recycle bin" : list.Count + " profiles deleted", ToastKind.Success);
        }

        private void DeleteProfile(string id) { DeleteProfile(id, true); }

        private void DeleteProfile(string id, bool ask)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            bool useTrash = _settings.UseTrash;
            if (ask && !ConfirmForm.Ask(this, "Delete profile",
                useTrash
                    ? "Move \"" + p.Name + "\" to the recycle bin?"
                    : "Delete \"" + p.Name + "\" and all of its browsing data? This cannot be undone.",
                useTrash ? "Browsing data and cookies are kept so a restore is complete."
                         : (p.Proxy != null && p.Proxy.Enabled) ? "The linked proxy settings are removed too." : null))
                return;

            BrowserSession s;
            if (_sessions.TryGetValue(id, out s))
            {
                _sessions.Remove(id);
                try { s.Close(); } catch { }
            }
            /* The selection must not keep pointing at the profile that is going away; the card
               it refers to disappears on the next reload. */
            _picks.Remove(id);
            if (_pickAnchor == id) _pickAnchor = null;
            _launching.Remove(id);

            try
            {
                _store.Delete(p, useTrash);
                /* The deleted profile may have been the only one holding a build, so what counts
                   as "used" changes here: without this the next new profile could be offered a
                   build the workspace just freed, or keep avoiding one it no longer uses. */
                KernelDiversity.Invalidate();
                _profiles = _store.LoadAll();
                PrunePicks();
                if (!ask)
                {
                    /* Called from the bulk bar: the caller reloads and clears the selection
                       once, so a per-item reload here would rebuild every card N times. */
                    RemoveCard(id);
                    SyncPicks();
                    return;
                }
                ReloadCards(false);
                Toast(useTrash ? "Moved to the recycle bin" : "Profile deleted", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("Delete failed", ex);
                Toast("Could not delete the profile", ToastKind.Error);
            }
        }

        /// <summary>Takes one card out of the grid without rebuilding the rest.</summary>
        private void RemoveCard(string id)
        {
            ProfileCard card;
            if (!_cards.TryGetValue(id, out card)) return;
            _cards.Remove(id);
            _grid.Controls.Remove(card);
            try { card.Dispose(); } catch { }
            _grid.LayoutGrid();
            _host.SyncExtent();
            _emptyPanel.Visible = _page == 0 && _cards.Count == 0;
            if (_emptyPanel.Visible) _emptyPanel.BringToFront();
            _grid.Invalidate();
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

        private async System.Threading.Tasks.Task OpenProfileCore(string id) { await OpenProfileCore(id, null); }

        private async System.Threading.Tasks.Task OpenProfileCore(string id, string startUrl)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return;

            /* A lapsed plan leaves the profiles on disk but refuses to open the ones it no
               longer covers. The offer is the upgrade, and the data is still there afterwards. */
            if (Entitlements.LockedProfiles(_profiles).Contains(id))
            {
                OfferUpgrade("This profile is outside the free plan. Renew to open it again.");
                return;
            }

            BrowserSession existing;
            if (_sessions.TryGetValue(id, out existing))
            {
                existing.Focus();
                /* The profile is already open: honour the URL in the window that exists rather
                   than refusing or opening a second one. */
                if (!string.IsNullOrEmpty(startUrl))
                {
                    var nav = existing.NavigateAsync(startUrl);
                    nav.ContinueWith(delegate(System.Threading.Tasks.Task<bool> t) { var ignored = t.Exception; },
                        System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                }
                return;
            }
            if (_launching.Contains(id)) return;
            if (!AllowAnotherBrowser()) return;

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

                var session = await BrowserSession.LaunchAsync(p,
                    string.IsNullOrEmpty(startUrl) ? _settings.DefaultStartUrl : startUrl,
                    _settings.BrowserPath, _settings);
                _sessions[id] = session;
                session.Closed += delegate { SessionClosed(id); };
                /* A proxy that dies mid-session no longer freezes the tabs: the relay moves to a
                   direct connection and says so, loudly, because that means the real IP is out. */
                session.ProxyFallback += delegate(string endpoint)
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            Toast("Proxy " + endpoint + " stopped responding. \u201c" + p.Name +
                                  "\u201d is now on a direct connection, so the real IP is visible.", ToastKind.Error);
                        }));
                    }
                    catch { }
                };
                _store.Touch(p);
                if (_cards.ContainsKey(id))
                {
                    _cards[id].Model = p;
                    _cards[id].Invalidate();
                }
                Toast("\u201c" + p.Name + "\u201d is ready", ToastKind.Success);
                /* A phone profile is the device the page sees, not a phone. Everything the page
                   can read is emulated and consistent; what cannot be emulated on a Windows PC is
                   the hardware underneath, so a user testing one should be told once rather than
                   left to assume a phone is being faked completely. */
                if (p.Fp != null && FingerprintFactory.IsMobileOs(p.Fp.Os))
                    Toast("The page sees " + p.Fp.Os + ". The window runs on this PC, so hardware " +
                          "level checks still belong to it.", ToastKind.Info);
                if (session.IsFirefox && p.Fp != null && !FingerprintFactory.IsWindowsTarget(p.Fp.Os))
                    Toast("Firefox cannot spoof " + p.Fp.Os + ". WebGL, timezone and screen stay real.", ToastKind.Error);
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
                else if (DiskSpace.IsOutOfSpace(ex))
                {
                    /* The full disk has a one-click answer: the profile caches are the app's
                       own data and always safe to drop. Clean now, say so, and the user can
                       simply press Open again. */
                    long freed = DiskSpace.AutoCleanCaches();
                    Toast(freed > 0
                        ? "The disk was full. " + DiskSpace.Human(freed) + " of cache was cleared. Try opening the profile again."
                        : "The disk is full. Free some space or clear cached files in Settings.", ToastKind.Error);
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

        /// <summary>Guarded entry point: an exception lands in the log and a message, not a crash dialog.</summary>
        private void OpenProfile(string id) { Guard("OpenProfile", delegate { return OpenProfileCore(id, null); }); }

        /// <summary>Opens a profile and, when a URL is given, sends the running browser there.</summary>
        private void OpenProfile(string id, string url) { Guard("OpenProfile", delegate { return OpenProfileCore(id, url); }); }

        private async Task<bool> DownloadBrowserAsync(string engine)
        {
            string label = BrowserFetcher.Label(engine);
            if (BrowserFetcher.IsInstalled(engine))
            {
                Toast(label + " is already installed", ToastKind.Info);
                return true;
            }

            var dlg = new DownloadDialog("Get " + label);
            dlg.CenterOn(this);
            dlg.Show(this);
            dlg.Activate();
            try
            {
                string exe = await BrowserFetcher.InstallAsync(engine,
                    (pct, msg) => dlg.UpdateProgress(pct, msg), dlg.Token).ConfigureAwait(true);
                /* Every close below is guarded: the user can close the progress window while the
                   fetch is still running, and disposing it twice threw out of an awaited
                   continuation - a hard crash for pressing the window's own X. */
                CloseQuietly(dlg);
                UpdateBrowserInfo();
                Toast(label + " is ready", ToastKind.Success);
                return true;
            }
            catch (OperationCanceledException)
            {
                CloseQuietly(dlg);
                Toast("Download cancelled", ToastKind.Info);
            }
            catch (Exception ex)
            {
                CloseQuietly(dlg);
                Log.Error(label + " download failed", ex);
                Toast(ShortError(ex), ToastKind.Error);
            }
            return false;
        }

        /// <summary>Closes a window the user may already have closed themselves.</summary>
        private static void CloseQuietly(Form form)
        {
            if (form == null) return;
            try { if (!form.IsDisposed) form.Close(); } catch { }
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
                    c.Locked = Entitlements.LockedProfiles(_profiles).Contains(id);
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
            /* A profile just released its cache handles - the moment a low disk can take the
               space back. Fire-and-forget: the clean skips running profiles and never logs out. */
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { DiskSpace.AutoCleanCaches(); });
        }

        private void Toast(string message, ToastKind kind)
        {
            try
            {
                _toast.Show(message, kind);
                Restack();
            }
            catch { }
        }

        /// <summary>
        /// Runs an async handler so a failed await cannot reach the global "unexpected error"
        /// dialog. Before this every one of these was <c>async void</c>: a network hiccup in a
        /// proxy check or a missing kernel folder during launch threw away the user's whole
        /// session and showed a crash box with no context.
        /// </summary>
        private void Guard(string action, Func<System.Threading.Tasks.Task> work)
        {
            System.Threading.Tasks.Task task;
            try { task = work(); }
            catch (Exception ex) { Fail(action, ex); return; }
            if (task == null) return;

            task.ContinueWith(delegate(System.Threading.Tasks.Task t)
            {
                Exception ex = t.Exception == null ? null : t.Exception.GetBaseException();
                if (ex == null) return;
                if (IsDisposed || Disposing) { Log.Error(action, ex); return; }
                try
                {
                    BeginInvoke(new Action(delegate { if (!IsDisposed) Fail(action, ex); }));
                }
                catch { Log.Error(action, ex); }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        private void Fail(string action, Exception ex)
        {
            Log.Error(action, ex);
            string hint = ex is UnauthorizedAccessException
                ? " A file or folder is locked. Close any Joe Browser browsers and try again."
                : ex is System.IO.IOException
                    ? " A file could not be read or written."
                    : "";
            Toast("That did not work: " + ex.Message + hint, ToastKind.Error);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                L10n.Changed -= OnLanguageChanged;
                if (_pulse != null) _pulse.Stop();

                // Remember how the window was left so the next launch opens the same width.
                var restore = Maximized ? LastNormalBounds : new Rectangle(Location, Size);
                _settings.WinMaximized = Maximized;
                if (!restore.IsEmpty && restore.Width > 0 && restore.Height > 0)
                {
                    _settings.WinWidth = Math.Max(1000, (int)Math.Round(restore.Width / (double)Ui.Scale));
                    _settings.WinHeight = Math.Max(660, (int)Math.Round(restore.Height / (double)Ui.Scale));
                }
                /* Closing the window is not the moment to lose the geometry and the
                   preferences with it: a locked or read-only settings.json used to throw out of
                   OnFormClosing, which can stop the window from closing at all. */
                try { _store.SaveSettings(_settings); }
                catch (Exception ex) { Log.Error("Settings not saved on exit", ex); }

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
            /* Keyboard-cue tracking lives in JBForm.ProcessCmdKey and covers this window too;
               it is deliberately not duplicated here. */
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
            if (keyData == Keys.Escape && _page == 0 && _filter.Length > 0) { ClearSearch(); return true; }
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
            /* Shift+F10 is the second standard context-menu key, after the Apps key the card
               already handles. Without it the menu was unreachable on keyboards that have no
               Apps key - which is most laptops. */
            if (keyData == (Keys.Shift | Keys.F10) && _page == 0)
            {
                var focused = ActiveControl as ProfileCard;
                if (focused != null)
                {
                    focused.ShowMenuKey();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AboutPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            int colW = Math.Max(Ui.S(300), _aboutColumn.Width);
            int x = 0;
            int gap = Ui.S(16);

            // identity card
            var card = new RectangleF(x, Ui.S(8), colW, Ui.S(112));
            Ui.Fill(g, card, Ui.S(8), Theme.Panel);
            Ui.Stroke(g, card, Ui.S(8), Theme.Line, 1f);

            var mark = new RectangleF(card.X + Ui.S(20), card.Y + Ui.S(28), Ui.S(56), Ui.S(56));
            Logo.Draw(g, mark);

            float tx = mark.Right + Ui.S(18);
            float textW = colW - (tx - card.X) - Ui.S(220);
            Ui.Text(g, "Joe Browser", Theme.Display, Theme.Text, tx, card.Y + Ui.S(20), textW);
            Ui.Text(g, L10n.T("sub.about"), Theme.Small, Theme.TextDim, tx, card.Y + Ui.S(52), textW);
            Ui.Text(g, AppPaths.Root, Theme.Caption, Theme.TextFaint, tx, card.Y + Ui.S(76), textW);

            // capability tiles
            float sectY = card.Bottom + Ui.S(26);
            Ui.Text(g, L10n.T("about.features"), Theme.Micro, Theme.TextFaint, x + Ui.S(2), sectY, colW - Ui.S(4));

            string[][] features =
            {
                new[] { "user", L10n.T("about.f1"), L10n.T("about.f1d") },
                new[] { "fingerprint", L10n.T("about.f2"), L10n.T("about.f2d") },
                new[] { "lock", L10n.T("about.f3"), L10n.T("about.f3d") },
                new[] { "proxy", L10n.T("about.f4"), L10n.T("about.f4d") },
                new[] { "laptop", L10n.T("about.f5"), L10n.T("about.f5d") },
                new[] { "route", L10n.T("about.f6"), L10n.T("about.f6d") },
                new[] { "extension", L10n.T("about.f7"), L10n.T("about.f7d") },
                new[] { "shield", L10n.T("about.f8"), L10n.T("about.f8d") }
            };

            float tileY = sectY + Ui.S(26);
            int cols = colW >= Ui.S(1420) ? 4 : colW >= Ui.S(1060) ? 3 : colW >= Ui.S(680) ? 2 : 1;
            float tileW = (colW - gap * (cols - 1)) / cols;
            float tileH = Ui.S(104);
            for (int i = 0; i < features.Length; i++)
            {
                var r = new RectangleF(x + (i % cols) * (tileW + gap), tileY + (i / cols) * (tileH + gap), tileW, tileH);
                Ui.Fill(g, r, Ui.S(8), Theme.Card);
                Ui.Stroke(g, r, Ui.S(8), Theme.Line, 1f);

                float chip = Ui.S(28);
                /* The block is centred in the tile: pinning the description to a fixed offset left
                   a gap under the title and a different amount of padding on every row. */
                float titleRow = Math.Max(chip, Theme.H3.GetHeight(g));
                float descW = r.Width - Ui.S(32);
                float descH = Ui.MeasureWrapped(g, features[i][2], Theme.Caption, descW, 2);
                float blockH = titleRow + Ui.S(8) + descH;
                float top = r.Y + Math.Max(Ui.S(12), (tileH - blockH) / 2f);

                var cr = new RectangleF(r.X + Ui.S(16), top, chip, chip);
                Ui.Fill(g, cr, Ui.S(6), Theme.AccentOnBg);
                float ic = Ui.S(17);
                Icons.Draw(g, features[i][0], new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), Theme.Accent);

                Ui.Text(g, features[i][1], Theme.H3, Theme.Text, cr.Right + Ui.S(10),
                    cr.Y + (chip - Theme.H3.GetHeight(g)) / 2f, r.Width - (cr.Right - r.X) - Ui.S(18));
                Ui.Text(g, features[i][2], Theme.Caption, Theme.TextDim, r.X + Ui.S(16), top + titleRow + Ui.S(8),
                    descW, "left", 2);
            }

            float afterTiles = tileY + (float)Math.Ceiling(features.Length / (double)cols) * (tileH + gap);

            // keyboard shortcuts, two columns of key chips
            float scSect = afterTiles + Ui.S(10);
            Ui.Text(g, L10n.T("about.shortcuts"), Theme.Micro, Theme.TextFaint, x + Ui.S(2), scSect, colW - Ui.S(4));

            var shortcuts = ShortcutDisplay();
            int scCols = colW >= Ui.S(900) ? 2 : 1;
            int perCol = (int)Math.Ceiling(shortcuts.Count / (double)scCols);
            float rowH = Ui.S(38);
            var sc = new RectangleF(x, scSect + Ui.S(26), colW, perCol * rowH + Ui.S(14));
            Ui.Fill(g, sc, Ui.S(8), Theme.Panel);
            Ui.Stroke(g, sc, Ui.S(8), Theme.Line, 1f);

            float halfW = colW / scCols;
            for (int i = 0; i < shortcuts.Count; i++)
            {
                int col = i / perCol, line = i % perCol;
                float ry = sc.Y + Ui.S(7) + line * rowH;
                float kx = sc.X + col * halfW + Ui.S(18);
                float cy = ry + (rowH - Ui.S(24)) / 2f;

                // A row with no key is a heading, not a shortcut: no chip, no divider above it.
                if (shortcuts[i][0].Length == 0)
                {
                    Ui.Text(g, shortcuts[i][1], Theme.Micro, Theme.TextFaint, kx, cy, halfW - Ui.S(36));
                    continue;
                }

                if (line > 0)
                    using (var p = new Pen(Theme.Line))
                    {
                        float dy = ry + 0.5f;
                        g.DrawLine(p, x + col * halfW + Ui.S(16), dy, x + (col + 1) * halfW - Ui.S(16), dy);
                    }

                int keyW = (int)Math.Ceiling(Ui.Measure(g, shortcuts[i][0], Theme.Caption).Width) + Ui.S(18);
                var chipR = new RectangleF(kx, cy, keyW, Ui.S(24));
                Ui.Fill(g, chipR, Ui.S(4), Theme.Card);
                Ui.Stroke(g, chipR, Ui.S(4), Theme.Line, 1f);
                Ui.Text(g, shortcuts[i][0], Theme.Caption, Theme.Text, chipR.X, chipR.Y, chipR.Width, "center");

                Ui.Text(g, shortcuts[i][1], Theme.Small, Theme.TextDim, chipR.Right + Ui.S(12),
                    ry + (rowH - Theme.Small.GetHeight(g)) / 2f,
                    sc.X + (col + 1) * halfW - (chipR.Right + Ui.S(30)));
            }

            // this workspace: live counts of what the app holds on this machine
            float wsSect = sc.Bottom + Ui.S(26);
            Ui.Text(g, L10n.T("about.workspace"), Theme.Micro, Theme.TextFaint, x + Ui.S(2), wsSect, colW - Ui.S(4));

            string[][] stats =
            {
                new[] { L10n.T("about.w1"), (_profiles == null ? 0 : _profiles.Count).ToString() },
                new[] { L10n.T("about.w2"), (_proxies == null ? 0 : _proxies.Count).ToString() },
                new[] { L10n.T("about.w3"), (_extensions == null ? 0 : _extensions.Count).ToString() }
            };
            float wsRowH = Ui.S(44);
            var ws = new RectangleF(x, wsSect + Ui.S(26), colW, stats.Length * wsRowH + Ui.S(12));
            Ui.Fill(g, ws, Ui.S(8), Theme.Panel);
            Ui.Stroke(g, ws, Ui.S(8), Theme.Line, 1f);
            for (int i = 0; i < stats.Length; i++)
            {
                float ry = ws.Y + Ui.S(6) + i * wsRowH;
                if (i > 0)
                    using (var p = new Pen(Theme.Line))
                    {
                        float dy = ry + 0.5f;
                        g.DrawLine(p, ws.X + Ui.S(18), dy, ws.Right - Ui.S(18), dy);
                    }
                Ui.Text(g, stats[i][0], Theme.Body, Theme.Text, ws.X + Ui.S(18),
                    ry + (wsRowH - Theme.Body.GetHeight(g)) / 2f, ws.Width - Ui.S(120));
                Ui.Text(g, stats[i][1], Theme.H3, Theme.Accent, ws.Right - Ui.S(90),
                    ry + (wsRowH - Theme.H3.GetHeight(g)) / 2f, Ui.S(72), "right");
            }

            int need = (int)Math.Ceiling(ws.Bottom) + Ui.S(20);
            if (_aboutColumn.Height != need)
            {
                _aboutColumn.Height = need;
                _aboutHost.SyncExtent();
            }
        }
    }
}
