using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal sealed class EditorForm : JBForm
    {
        private readonly Profile _profile;
        private readonly FingerprintConfig _fp;
        private readonly ProxyConfig _proxy;
        private readonly ToolConfig _tools;
        private readonly bool _isNew;
        /// <summary>Profiles already in the workspace, so a new one can be proven distinct
        /// from all of them rather than merely random. Null in an edit, where the point is to
        /// keep the identity the user already has.</summary>
        private readonly List<Profile> _siblings;
        private readonly int _initialTab;
        private bool _systemTarget;
        private readonly AppSettings _settings;
        private List<double[]> _screens;
        private List<string[]> _gpus;
        private List<int> _coreVals = new List<int>();
        private List<int> _memVals = new List<int>();
        private bool _suppress;
        private bool _built;

        public Profile Result { get; private set; }

        private JBLabel _title, _err;
        private JBSegment _tabs, _proxySeg;
        private Panel _tabGeneral, _tabFp, _tabPriv, _tabProxy, _tabTools;
        private JBScrollHost _hostGeneral, _hostFp, _hostPriv, _hostProxy, _hostTools;
        private JBTextBox _name, _startUrl, _notes, _browserPath, _host, _port, _user, _pass, _ua, _lat, _lng;
        private JBTextBox _group, _tags, _status, _customExt;
        private JBSelect _screen, _tz, _gpu, _os, _cores, _ram, _browserKind, _webrtcSel, _geoSel, _savedProxy;
        private JBSelect _kernel, _osVer, _fontSet;
        private JBLabel _kernelHint;
        private JBButton _kernelManage;
        private JBToggle _hwNoise;
        private List<string> _kernelKeys = new List<string>();
        private List<string> _osVerKeys = new List<string>();
        private JBLangPicker _lang;
        private JBToggle _canvas, _audio, _fonts, _dnt, _battery, _autoMatch, _autoUa;
        private JBButton _regen, _browse, _testBtn, _save, _cancel, _getBrowser, _openExtFolder, _random;
        private JBToggle _toolHumanPaste, _toolProxyWatch, _toolRightClick, _toolAllowShift;
        private JBSelect _toolSpeed, _toolInterval;
        private JBLabel _browserNote;
        private JBLabel _fpWarn;
        private JBIconButton _close, _reveal;
        private JBPanel _proxyCard;
        private JBLabel _proxyResult;
        private int _proxyState; // -1 error, 0 idle, 1 ok
        private int _footerY;
        private List<string> _kindKeys = new List<string>();

        private JBLabel _lblName, _lblStart, _lblKind, _lblPath, _lblNotes, _lblKernel;
        private JBLabel _lblGroup, _lblTags, _lblStatus;
        private JBLabel _lblOs, _lblScreen, _lblTz, _lblLang, _lblGpu, _lblCores, _lblRam;
        private JBLabel _lblOsVer, _lblFontSet;
        private JBLabel _lblUa, _lblWebrtc, _lblGeo, _lblLat, _lblLng;
        private JBLabel _lblType, _lblHost, _lblPort, _lblUser, _lblPass, _lblSaved;
        private JBLabel _lblExt, _lblSpeed, _lblInterval, _lblCustomExt, _lblExtHint;

        public EditorForm(Profile profile, bool isNew, AppSettings settings, int initialTab = 0)
            : this(profile, isNew, settings, initialTab, null) { }

        /// <param name="siblings">Profiles already stored. A new profile is re-rolled against
        /// them, so what the editor shows is already known to be unlike the rest of the
        /// workspace; an edit passes null and keeps its identity.</param>
        public EditorForm(Profile profile, bool isNew, AppSettings settings, int initialTab, List<Profile> siblings)
        {
            _isNew = isNew;
            _siblings = siblings;
            _systemTarget = isNew;
            _settings = settings;
            _profile = profile.Copy(false);
            _fp = _profile.Fp.Copy();
            _proxy = _profile.Proxy.Copy();
            _tools = _profile.Tools == null ? new ToolConfig() : _profile.Tools.Copy();
            _initialTab = (initialTab < 0 || initialTab > 4) ? 0 : initialTab;

            if (_fp.Seed == 0)
            {
                // A new identity mirrors this machine: it is the hardest one to detect.
                FingerprintFactory.Regenerate(_fp, _isNew ? "System default" : _fp.Os, _proxy);
                FingerprintFactory.ApplyProxyLocale(_fp, _proxy);
            }

            Text = isNew ? "New profile" : "Edit profile";
            Resizable = true;
            DraggableSidebar = false;
            HeaderHit = 64;
            KeyPreview = true;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            MinimumSize = new Size(Ui.S(640), Ui.S(520));
            Size want = Ui.FitToScreen(this, Ui.S(900), Ui.S(620));
            Size = new Size(Math.Max(Ui.S(640), want.Width), Math.Max(Ui.S(520), want.Height));

            Build();
            ApplyScale();
            SwitchTab(_initialTab);
            /* SwitchTab already focuses the first field of the page it shows; focusing a
               hard-coded field here would override that for the proxy tab, whose first field is
               not the host box. */
        }

        private void ApplyScale()
        {
            try { using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f; }
            catch { Ui.Scale = 1f; }
            if (Ui.Scale < 1f) Ui.Scale = 1f;
            MinimumSize = new Size(Ui.S(640), Ui.S(520));
            FitToWorkArea();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterToScreen();
            LayoutAll();
            /* The caret belongs in the first field of the page that is actually showing, and
               only once the form is on screen - focusing during construction is lost when the
               window is activated. */
            Control first = FirstField(_tabs == null ? 0 : _tabs.SelectedIndex);
            if (first != null) { try { first.Focus(); } catch { } }
            DetectLocaleAsync();
        }

        /// <summary>
        /// Without a proxy the connection this machine already uses sets the clock and the language,
        /// so a fresh profile matches the public IP the sites will see.
        /// </summary>
        private async System.Threading.Tasks.Task DetectLocaleAsyncCore()
        {
            if (_proxy != null && _proxy.Enabled) return;
            if (!_fp.AutoMatchProxy) return;
            try
            {
                var res = await ProxyTester.SystemAsync();
                if (res == null || !res.Ok || IsDisposed) return;
                _proxy.LastIp = res.Ip;
                _proxy.LastCountry = res.Country;
                _proxy.LastCountryCode = res.CountryCode;
                _proxy.LastTimezone = res.Timezone;
                _proxy.LastCity = res.City;
                if (_autoMatch == null || !_autoMatch.Checked) return;
                ApplyDetectedLocale();
            }
            catch (Exception ex) { Log.Error("Location lookup failed", ex); }
        }

        /// <summary>Guarded entry point so a failed await never reaches the crash dialog.</summary>
        private void DetectLocaleAsync() { Guarded.Run("DetectLocaleAsync", this, delegate { return DetectLocaleAsyncCore(); }); }

        private void ApplyDetectedLocale()
        {
            var probe = _fp.Copy();
            probe.AutoMatchProxy = true;
            FingerprintFactory.ApplyProxyLocale(probe, _proxy);
            _fp.Timezone = probe.Timezone;
            _fp.Language = probe.Language;
            _fp.AcceptLanguage = probe.AcceptLanguage;
            RefreshFingerprintUi();
        }

        protected override void OnSurfaceChanged()
        {
            if (_built) LayoutAll();
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            _title = new JBLabel(LabelRole.Title, _isNew ? "New profile" : "Edit profile");
            Controls.Add(_title);

            _close = new JBIconButton { Icon = "close", Danger = true, IconSize = 14, Tip = "Close" };
            _close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_close);

            _tabs = new JBSegment();
            _tabs.SetItems(new[] { "General", "Fingerprint", "Privacy", "Proxy", "Tools" }, _initialTab);
            _tabs.SelectedIndexChanged += delegate { SwitchTab(_tabs.SelectedIndex); };
            Controls.Add(_tabs);

            _tabGeneral = MakeTab();
            _tabFp = MakeTab();
            _tabPriv = MakeTab();
            _tabProxy = MakeTab();
            _tabTools = MakeTab();
            _hostGeneral = new JBScrollHost(_tabGeneral);
            _hostFp = new JBScrollHost(_tabFp);
            _hostPriv = new JBScrollHost(_tabPriv);
            _hostProxy = new JBScrollHost(_tabProxy);
            _hostTools = new JBScrollHost(_tabTools);
            Controls.Add(_hostGeneral);
            Controls.Add(_hostFp);
            Controls.Add(_hostPriv);
            Controls.Add(_hostProxy);
            Controls.Add(_hostTools);

            BuildGeneral();
            BuildFingerprint();
            BuildPrivacy();
            BuildProxy();
            BuildTools();

            RefreshFingerprintUi();

            _save = new JBButton { Kind = BtnKind.Primary, Icon = "check", Text = _isNew ? "Create profile" : "Save changes" };
            _save.Click += delegate { Save(); };
            _cancel = new JBButton { Kind = BtnKind.Ghost, Text = "Cancel" };
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_save);
            Controls.Add(_cancel);

            _err = new JBLabel(LabelRole.Strong, "");
            _err.TextAlign = ContentAlignment.MiddleLeft;
            _err.Visible = false;
            Controls.Add(_err);

            _built = true;
        }

        private Panel MakeTab()
        {
            return new Panel { BackColor = Theme.Bg };
        }

        private JBLabel Lbl(Panel parent, string text)
        {
            var l = new JBLabel(LabelRole.Accent, text);
            parent.Controls.Add(l);
            return l;
        }

        private JBTextBox Txt(Panel parent, string value, string placeholder, string icon, bool multiline, int height)
        {
            var t = new JBTextBox(multiline);
            t.Value = value ?? "";
            t.Placeholder = placeholder;
            t.Icon = icon;
            if (multiline) t.PadTop = Ui.S(10);
            t.Height = Ui.S(height);
            parent.Controls.Add(t);
            return t;
        }

        private JBSelect Pick(Panel parent, string icon)
        {
            var s = new JBSelect { Icon = icon };
            parent.Controls.Add(s);
            return s;
        }

        private void BuildGeneral()
        {
            _lblName = Lbl(_tabGeneral, "PROFILE NAME");
            _name = Txt(_tabGeneral, _profile.Name, "Name this profile", "user", false, 40);

            _lblStart = Lbl(_tabGeneral, "START PAGE");
            /* The hint is the value that will be used when the field is left empty, so it has to
               be the fingerprint test page and not a search engine. */
            _startUrl = Txt(_tabGeneral, _profile.StartUrl, Profile.FingerprintTestUrl, "globe", false, 40);

            _lblKind = Lbl(_tabGeneral, "BROWSER");
            _browserKind = Pick(_tabGeneral, "window");
            BuildBrowserOptions();
            _browserKind.SetBounds(0, 0, Ui.S(210), Ui.S(33));

            _lblPath = Lbl(_tabGeneral, "CUSTOM PATH (OPTIONAL)");
            _browserPath = Txt(_tabGeneral, _profile.BrowserKind == "custom" ? _profile.BrowserPath : "",
                "Leave empty for automatic", "folder", false, 40);
            _browse = new JBButton { Kind = BtnKind.Soft, Text = "Browse" };
            _browse.Click += delegate { BrowseBrowser(); };
            _tabGeneral.Controls.Add(_browse);

            _lblGroup = Lbl(_tabGeneral, "GROUP / FOLDER");            _group = Txt(_tabGeneral, _profile.Group, "e.g. Clients / EU store", "folder", false, 40);
            _lblTags = Lbl(_tabGeneral, "TAGS (COMMA SEPARATED)");
            _tags = Txt(_tabGeneral, _profile.Tags, "work, ads, verified", "flag", false, 40);
            _lblStatus = Lbl(_tabGeneral, "STATUS");
            _status = Txt(_tabGeneral, _profile.Status, "Account status or owner", "info", false, 40);

            _lblNotes = Lbl(_tabGeneral, "NOTES");
            _notes = Txt(_tabGeneral, _profile.Notes, "Notes", null, true, 64);

            _browserNote = new JBLabel(LabelRole.Faint, "");
            _browserNote.TextAlign = ContentAlignment.MiddleLeft;
            _tabGeneral.Controls.Add(_browserNote);
            _getBrowser = new JBButton { Kind = BtnKind.Soft, Icon = "download", Text = "Download" };
            _getBrowser.Click += delegate { FetchBrowser(); };
            _tabGeneral.Controls.Add(_getBrowser);
            _browserKind.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                UpdateBrowserFetchUi();
                LayoutAll();
            };
            UpdateBrowserFetchUi();

            /* Pinned build. Empty means "whatever is installed"; a pinned build keeps the
               profile on one exact version even after the everyday browser updates. */
            _lblKernel = Lbl(_tabGeneral, "ENGINE BUILD");
            _kernel = Pick(_tabGeneral, "layers");
            BuildKernelOptions();
            _kernel.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                int i = _kernel.SelectedIndex;
                string key = i >= 0 && i < _kernelKeys.Count ? _kernelKeys[i] : "";
                /* Say out loud what the choice does. "Installed browser" and a pinned build look
                   like two equal options in a dropdown, but one tracks the user's Chrome and the
                   other freezes an exact version - a difference that only shows up months later. */
                if (key.Length == 0)
                {
                    _kernelHint.Text = _kernelKeys.Count <= 1
                        ? "Uses whatever browser is installed. Install another kernel to give each profile its own build."
                        : "Uses whatever browser is installed, and follows its updates.";
                }
                else
                {
                    int bar = key.IndexOf('|');
                    string engine = bar > 0 ? key.Substring(0, bar) : key;
                    string ver = bar > 0 ? key.Substring(bar + 1) : "";
                    _kernelHint.Text = "Pinned to " + KernelDiversity.PrettyEngine(engine) + " " + ver +
                        ". This profile keeps that build even after the installed browser updates.";
                }
                LayoutAll();
            };
            _kernelManage = new JBButton { Kind = BtnKind.Soft, Icon = "download", Text = "Kernels" };
            _kernelManage.Click += delegate
            {
                using (var dlg = new KernelForm()) dlg.ShowDialog(this);
                /* Installing a build inside that window changes the choice this dropdown offers,
                   so the inventory is re-read and the list rebuilt - otherwise the build the user
                   just downloaded is only visible after restarting the app. */
                KernelDiversity.Invalidate();
                _suppress = true;
                BuildKernelOptions();
                _suppress = false;
                LayoutAll();
            };
            _tabGeneral.Controls.Add(_kernelManage);
            _kernelHint = new JBLabel(LabelRole.Faint, "");
            _kernelHint.TextAlign = ContentAlignment.MiddleLeft;
            // Wrapped, not ellipsised: this is the only place that explains what pinning a build
            // costs, so a truncated sentence would be worse than no sentence.
            _kernelHint.Wrap = true;
            _tabGeneral.Controls.Add(_kernelHint);

            _name.EnterPressed += OnEnterKey;
            _startUrl.EnterPressed += OnEnterKey;
            _browserPath.EnterPressed += OnEnterKey;
            _group.EnterPressed += OnEnterKey;
            _tags.EnterPressed += OnEnterKey;
            _status.EnterPressed += OnEnterKey;
        }

        private void OnEnterKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Save();
        }

        private void BuildBrowserOptions() { BuildBrowserOptions(null); }

        private void BuildBrowserOptions(string selectKey)
        {
            var rows = BrowserLocator.Options();
            var labels = new List<string>();
            _kindKeys.Clear();
            int sel = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                labels.Add(rows[i][1]);
                _kindKeys.Add(rows[i][0]);
                if (rows[i][0] == (_profile.BrowserKind ?? "auto")) sel = i;
            }
            labels.Add("Custom executable");
            _kindKeys.Add("custom");
            if (_profile.BrowserKind == "custom") sel = labels.Count - 1;
            if (!string.IsNullOrEmpty(selectKey))
            {
                int forced = _kindKeys.IndexOf(selectKey);
                if (forced >= 0) sel = forced;
            }
            _browserKind.SetItems(labels, sel);
        }

        /// <summary>Installed builds plus the "whatever is installed" default.</summary>
        private void BuildKernelOptions()
        {
            var labels = new List<string> { "Installed browser" };
            _kernelKeys = new List<string> { "" };
            foreach (var k in KernelStore.Installed())
            {
                string name = k.Engine == "chrome" ? "Chrome"
                    : k.Engine == "brave" ? "Brave" : "Chromium";
                labels.Add(name + " " + k.Version);
                _kernelKeys.Add(k.Engine + "|" + k.Version);
            }
            string want = (_profile.KernelEngine ?? "") + "|" + (_profile.KernelVersion ?? "");
            int sel = _kernelKeys.IndexOf(want);
            if (sel < 0) sel = 0;
            _suppress = true;
            _kernel.SetItems(labels, sel);
            _suppress = false;

            if (_kernelHint != null)
            {
                _kernelHint.Text = sel == 0
                    ? (_kernelKeys.Count <= 1
                        ? "Uses whatever browser is installed. Install another kernel to give each profile its own build."
                        : "Uses whatever browser is installed, and follows its updates.")
                    : "Pinned to " + labels[sel] + ". This profile keeps that build even after the installed browser updates.";
            }
        }

        private void BrowseBrowser()        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select a browser executable";
                dlg.Filter = "Executables (*.exe)|*.exe";
                if (dlg.ShowDialog(this) == DialogResult.OK) _browserPath.Value = dlg.FileName;
            }
        }

        private string CurrentKindKey()
        {
            int i = _browserKind.SelectedIndex;
            return i >= 0 && i < _kindKeys.Count ? _kindKeys[i] : "auto";
        }

        /// <summary>Offers to fetch the engine the profile wants when this PC does not have it.</summary>
        private void UpdateBrowserFetchUi()
        {
            string key = CurrentKindKey();
            bool missing = BrowserFetcher.Supports(key) && !BrowserFetcher.IsInstalled(key);
            _getBrowser.Visible = missing;
            _browserNote.Visible = missing;
            if (!missing) return;
            _browserNote.Text = BrowserFetcher.Label(key) + " is not installed on this PC";
        }

        private async System.Threading.Tasks.Task FetchBrowserCore()
        {
            string key = CurrentKindKey();
            if (!BrowserFetcher.Supports(key) || BrowserFetcher.IsInstalled(key)) return;
            string label = BrowserFetcher.Label(key);

            var dlg = new DownloadDialog("Get " + label);
            dlg.Show(this);
            dlg.Activate();
            _suppress = true;
            try
            {
                await BrowserFetcher.InstallAsync(key, dlg.UpdateProgress, dlg.Token);
                dlg.Close();
                BuildBrowserOptions(key);
            }
            catch (OperationCanceledException) { dlg.Close(); }
            catch (Exception ex)
            {
                dlg.Close();
                Fail(0, ShortLine(ex));
            }
            finally { _suppress = false; }
            UpdateBrowserFetchUi();
            LayoutAll();
        }

        /// <summary>Guarded entry point so a failed await never reaches the crash dialog.</summary>
        private void FetchBrowser() { Guarded.Run("FetchBrowser", this, delegate { return FetchBrowserCore(); }); }

        private static string ShortLine(Exception ex)
        {
            string m = ex.Message ?? "The download failed.";
            int i = m.IndexOf('\n');
            if (i > 0) m = m.Substring(0, i);
            return m.Trim();
        }

        private void BuildFingerprint()
        {
            _lblOs = Lbl(_tabFp, "DEVICE TARGET");
            _os = Pick(_tabFp, "desktop");
            _os.SetItems(FingerprintFactory.OsList, 0);
            _os.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                _systemTarget = false;
                /* Changing the target re-rolls the whole identity, and it has to stay unlike the
                   rest of the workspace while it does: switching to macOS only to land on another
                   profile's M2 is the kind of silent collision the workspace audit then reports. */
                FingerprintIdentity.Comparison clash;
                FingerprintIdentity.MakeUnique(_fp, ResolveOs(_os.Value), _proxy, Others(), out clash);
                FingerprintFactory.ApplyProxyLocale(_fp, _proxy);
                _os.Icon = FingerprintFactory.DeviceIcon(_fp.Os);
                RefreshFingerprintUi();
                LayoutAll();
            };

            /* Android and iOS version. "auto" takes the build the handset behind the viewport
               really shipped; a manual pick is honoured on every surface that mentions it. */
            _lblOsVer = Lbl(_tabFp, "OS VERSION");
            _osVer = Pick(_tabFp, "android");
            _osVer.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                int i = _osVer.SelectedIndex;
                string key = i >= 0 && i < _osVerKeys.Count ? _osVerKeys[i] : "auto";
                if (FingerprintFactory.IsAndroid(_fp.Os)) _fp.AndroidVersion = key;
                else _fp.IosVersion = key;
                FingerprintFactory.ResolveUserAgent(_fp, null);
            };

            _lblScreen = Lbl(_tabFp, "SCREEN");            _screen = Pick(_tabFp, "screen");
            _screen.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                ApplyScreenSelection();
            };

            _lblCores = Lbl(_tabFp, "CPU CORES");
            _cores = Pick(_tabFp, "cpu");
            _cores.SelectedIndexChanged += delegate
            {
                if (_suppress || _coreVals == null) return;
                int i = _cores.SelectedIndex;
                if (i >= 0 && i < _coreVals.Count) _fp.Cores = _coreVals[i];
            };

            _lblRam = Lbl(_tabFp, "MEMORY");
            _ram = Pick(_tabFp, "ram");
            _ram.SelectedIndexChanged += delegate
            {
                if (_suppress || _memVals == null) return;
                int i = _ram.SelectedIndex;
                if (i >= 0 && i < _memVals.Count) _fp.Memory = _memVals[i];
            };

            _lblTz = Lbl(_tabFp, "TIMEZONE");
            _tz = Pick(_tabFp, "clock");
            _tz.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                _fp.Timezone = _tz.Value;
            };

            _lblLang = Lbl(_tabFp, "LANGUAGE");
            /* Circular country flags + full language names. The locale code is only a storage
               key; it is never the visible label, and the popup can be searched by name. */
            _lang = new JBLangPicker();
            _tabFp.Controls.Add(_lang);
            _lang.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                _fp.Language = _lang.Value;
                _fp.AcceptLanguage = FingerprintFactory.AcceptLanguageFor(_fp.Language);
            };

            _lblGpu = Lbl(_tabFp, "WEBGL RENDERER");
            _gpu = Pick(_tabFp, "gpu");
            _gpu.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                ApplyGpuSelection();
            };

            _regen = new JBButton { Kind = BtnKind.Soft, Icon = "refresh", Text = "New fingerprint" };
            _regen.Click += delegate
            {
                bool manualUa = !_autoUa.Checked;
                /* Re-rolled against the workspace, not just re-rolled: the point of the button is
                   a fingerprint nobody else has, and a plain Regenerate would happily land on
                   another profile's renderer. */
                FingerprintIdentity.Comparison clash;
                FingerprintIdentity.MakeUnique(_fp, ResolveOs(_os.Value), _proxy, Others(), out clash);
                ClashNote(clash);
                _fp.AutoUserAgent = !manualUa;
                RefreshFingerprintUi();
                _ua.Value = _fp.UserAgentOverride;
                SetError("");
            };
            _tabFp.Controls.Add(_regen);

            /* A whole new identity, not just a re-roll inside the same device target. This is
               what makes two profiles created a minute apart share nothing at all. */
            _random = new JBButton { Kind = BtnKind.Soft, Icon = "sparkle", Text = "Random identity" };
            _random.Click += delegate
            {
                string[] pool = { "Windows 11", "Windows 10", "macOS", "Linux", "Android", "iPhone", "iPad" };
                string os = pool[new Random(Guid.NewGuid().GetHashCode()).Next(pool.Length)];
                _systemTarget = false;
                FingerprintIdentity.Comparison clash;
                FingerprintIdentity.MakeUnique(_fp, os, _proxy, Others(), out clash);
                ClashNote(clash);
                FingerprintFactory.ApplyProxyLocale(_fp, _proxy);
                RefreshFingerprintUi();
                _ua.Value = "";
                LayoutAll();
                SetError("");
            };
            _tabFp.Controls.Add(_random);

            _fpWarn = new JBLabel(LabelRole.Faint, "");
            _fpWarn.TextAlign = ContentAlignment.MiddleLeft;
            _tabFp.Controls.Add(_fpWarn);
        }

        /// <summary>
        /// Every other profile in the workspace. A new profile must differ from all of them; an
        /// edit must differ from all but itself, and _profile carries the identity being edited,
        /// so WorstMatch skips it by id.
        /// </summary>
        private List<Profile> Others()
        {
            if (_siblings != null) return _siblings;
            var self = new List<Profile>();
            if (!_isNew && _profile != null) self.Add(_profile);
            return self;
        }

        /// <summary>
        /// Tells the truth after a re-roll. A clean roll clears the note; a roll that could not
        /// escape a shared strong surface says which one, because "unique" is a claim the app
        /// should only make when it is true.
        /// </summary>
        private void ClashNote(FingerprintIdentity.Comparison clash)
        {
            if (clash == null || clash.Relation == FingerprintIdentity.Relation.Distinct ||
                clash.Relation == FingerprintIdentity.Relation.Remote)
            {
                SetError("");
                return;
            }
            SetError(clash.Summary + " with another profile.");
        }

        /// <summary>
        /// Firefox has no debugging pipe: only the UA header and locale can be restamped there, so a
        /// foreign device target would leak through navigator.platform, screen and WebGL.
        /// </summary>
        private void UpdateFirefoxWarn(int x, int y, int w)
        {
            if (_fpWarn == null) return;
            string key = CurrentKindKey();
            var info = BrowserLocator.Detect(key == "custom" ? "auto" : key, _browserPath.Value);
            bool firefox = info != null && info.IsFirefox;
            bool foreign = !FingerprintFactory.IsWindowsTarget(_fp.Os);
            bool show = firefox && foreign;
            _fpWarn.Visible = show;
            if (!show) return;
            _fpWarn.Text = "Firefox cannot mask screen, WebGL or timezone. Use Chrome, Edge or Brave for " + _fp.Os + ".";
            _fpWarn.SetBounds(x, y, w, Ui.S(26));
        }

        private void BuildPrivacy()
        {
            _lblFontSet = Lbl(_tabPriv, "FONT LIST");
            _fontSet = Pick(_tabPriv, "face");
            var fontLabels = new List<string>();
            foreach (var f in FingerprintFactory.FontSets) fontLabels.Add(f[1]);
            _fontSet.SetItems(fontLabels, 0);
            _hwNoise = new JBToggle { Text = "Hardware noise", Checked = _fp.HardwareNoise };
            _tabPriv.Controls.Add(_hwNoise);

            _lblWebrtc = Lbl(_tabPriv, "WEBRTC LEAK");            _webrtcSel = Pick(_tabPriv, "shield");
            _webrtcSel.SetItems(LabelsOf(FingerprintFactory.WebRtcOptions), 0);

            _lblGeo = Lbl(_tabPriv, "GEOLOCATION");
            _geoSel = Pick(_tabPriv, "pin");
            _geoSel.SetItems(LabelsOf(FingerprintFactory.GeoOptions), 0);
            _geoSel.SelectedIndexChanged += delegate
            {
                if (_suppress) return;
                ApplyGeoVisibility();
                LayoutAll();
            };

            _lblLat = Lbl(_tabPriv, "LATITUDE");
            _lat = Txt(_tabPriv, _fp.GeoLat, "51.5074", null, false, 40);
            _lblLng = Lbl(_tabPriv, "LONGITUDE");
            _lng = Txt(_tabPriv, _fp.GeoLng, "-0.1278", null, false, 40);

            _lblUa = Lbl(_tabPriv, "USER AGENT");
            _ua = Txt(_tabPriv, _fp.UserAgentOverride, "Built from the device target when the browser opens", null, false, 40);
            _autoUa = new JBToggle { Text = "Generate from device" };
            _autoUa.CheckedChanged += delegate
            {
                if (_suppress) return;
                _fp.AutoUserAgent = _autoUa.Checked;
                _ua.Placeholder = _autoUa.Checked
                    ? "Built from the device target when the browser opens"
                    : "Paste the exact string to use";
            };
            _tabPriv.Controls.Add(_autoUa);

            _canvas = new JBToggle { Text = "Canvas noise" };
            _audio = new JBToggle { Text = "Audio noise" };
            _fonts = new JBToggle { Text = "Font noise" };
            _battery = new JBToggle { Text = "Battery reading" };
            _dnt = new JBToggle { Text = "Do Not Track" };
            _autoMatch = new JBToggle { Text = "Match locale to connection" };
            _tabPriv.Controls.Add(_canvas);
            _tabPriv.Controls.Add(_audio);
            _tabPriv.Controls.Add(_fonts);
            _tabPriv.Controls.Add(_battery);
            _tabPriv.Controls.Add(_dnt);
            _tabPriv.Controls.Add(_autoMatch);
        }

        private static List<string> LabelsOf(string[][] options)
        {
            var list = new List<string>();
            for (int i = 0; i < options.Length; i++) list.Add(options[i][1]);
            return list;
        }

        /// <summary>
        /// Turns stored locale keys into readable rows: a full language name, the region as a
        /// quiet subtitle, and a circular country flag. No bare "en / de / ru" labels anywhere.
        /// </summary>
        private static List<LangItem> LangItems(IEnumerable<string> codes)
        {
            var list = new List<LangItem>();
            foreach (string code in codes)
            {
                if (string.IsNullOrWhiteSpace(code)) continue;
                string full = FingerprintFactory.LanguageName(code.Trim());
                int p = full.IndexOf(" (", StringComparison.Ordinal);
                string title = p > 0 ? full.Substring(0, p) : full;
                string region = p > 0 ? full.Substring(p + 2).TrimEnd(')', ' ') : "";
                list.Add(new LangItem(code.Trim(), FingerprintFactory.CountryForLanguage(code.Trim()),
                    string.IsNullOrEmpty(title) ? code.Trim() : title, region));
            }
            return list;
        }

        private static string KeyOf(string[][] options, int index, string fallback)
        {
            return index >= 0 && index < options.Length ? options[index][0] : fallback;
        }

        /// <summary>The target the profile actually pretends to be, resolved from the picker.</summary>
        private static string ResolveOs(string choice)
        {
            return choice == "System default" ? FingerprintFactory.SystemOsName() : choice;
        }

        private void ApplyGpuSelection()
        {
            int i = _gpu.SelectedIndex;
            if (_gpus == null || i < 0 || i >= _gpus.Count) return;
            _fp.WebGlVendor = _gpus[i][0];
            _fp.WebGlRenderer = _gpus[i][1];
        }

        private bool GeoIsCustom
        {
            get
            {
                return KeyOf(FingerprintFactory.GeoOptions, _geoSel.SelectedIndex, "proxy")
                    .Equals("custom", StringComparison.Ordinal);
            }
        }

        private void ApplyGeoVisibility()
        {
            if (_lblLat == null) return;
            _lblLat.Visible = GeoIsCustom;
            _lat.Visible = GeoIsCustom;
            _lblLng.Visible = GeoIsCustom;
            _lng.Visible = GeoIsCustom;
        }

        private void BuildProxy()
        {
            /* Proxies saved in the Proxy Library are pickable here, so credentials are typed once. */
            var vault = ProxyVault.Load();
            var names = new List<string>();
            names.Add(vault.Count == 0 ? "No saved proxies yet" : "Enter manually");
            foreach (var entry in vault)
                names.Add(string.IsNullOrWhiteSpace(entry.Name) ? entry.Connection : entry.Name + "  ·  " + entry.Connection);
            _lblSaved = Lbl(_tabProxy, "SAVED PROXIES");
            _savedProxy = Pick(_tabProxy, "proxy");
            _savedProxy.SetItems(names, 0);
            _savedProxy.SelectedIndexChanged += delegate
            {
                int i = _savedProxy.SelectedIndex - 1;
                if (i < 0 || i >= vault.Count) return;
                var cfg = vault[i].Config;
                if (cfg == null) return;
                _proxy.Type = cfg.Type ?? "none";
                _proxy.Host = cfg.Host ?? "";
                _proxy.Port = cfg.Port;
                _proxy.User = cfg.User ?? "";
                _proxy.Password = cfg.Password ?? "";
                _proxy.LastIp = cfg.LastIp ?? "";
                _proxy.LastCountry = cfg.LastCountry ?? "";
                _proxy.LastCountryCode = cfg.LastCountryCode ?? "";
                _proxy.LastCity = cfg.LastCity ?? "";
                _proxy.LastTimezone = cfg.LastTimezone ?? "";
                _proxySeg.SelectedIndex = _proxy.Type == "http" ? 1 : _proxy.Type == "https" ? 2 : _proxy.Type == "socks5" ? 3 : 0;
                _host.Value = _proxy.Host;
                _port.Value = _proxy.Port > 0 ? _proxy.Port.ToString() : "";
                _user.Value = _proxy.User;
                _pass.Value = _proxy.Password;
                SetProxyResult(0, "Saved proxy loaded. Test the connection if you like.");
            };

            _lblType = Lbl(_tabProxy, "PROXY TYPE");
            _proxySeg = new JBSegment();
            _proxySeg.SetItems(new[] { "None", "HTTP", "HTTPS", "SOCKS5" }, 0);
            _tabProxy.Controls.Add(_proxySeg);

            _lblHost = Lbl(_tabProxy, "HOST");
            _host = Txt(_tabProxy, _proxy.Host, "proxy.example.com", "route", false, 40);
            _lblPort = Lbl(_tabProxy, "PORT");
            _port = Txt(_tabProxy, _proxy.Port > 0 ? _proxy.Port.ToString() : "", "8080", null, false, 40);

            _lblUser = Lbl(_tabProxy, "USERNAME");
            _user = Txt(_tabProxy, _proxy.User, "Optional", "user", false, 40);
            _lblPass = Lbl(_tabProxy, "PASSWORD");
            _pass = Txt(_tabProxy, _proxy.Password, "Optional", "key", false, 40);
            _pass.UsePasswordChar = true;
            _reveal = new JBIconButton { Icon = "eye", IconSize = 15, Tip = "Show password" };
            _reveal.Click += delegate { ToggleReveal(); };
            _pass.Trailing = _reveal;

            _testBtn = new JBButton { Kind = BtnKind.Soft, Icon = "globe", Text = "Test connection" };
            _testBtn.Click += delegate { TestProxy(); };
            _tabProxy.Controls.Add(_testBtn);

            _proxyCard = new JBPanel();
            _proxyCard.Paint += ProxyCardPaint;
            _tabProxy.Controls.Add(_proxyCard);
            _proxyResult = new JBLabel(LabelRole.Body, "Not tested yet.");
            _proxyResult.TextAlign = ContentAlignment.MiddleLeft;
            _proxyCard.Controls.Add(_proxyResult);

            int type = 0;
            if (_proxy.Type == "http") type = 1;
            else if (_proxy.Type == "https") type = 2;
            else if (_proxy.Type == "socks5") type = 3;
            _proxySeg.SelectedIndex = type;

            _host.EnterPressed += OnEnterKey;
            _port.EnterPressed += OnEnterKey;
            _user.EnterPressed += OnEnterKey;
            _pass.EnterPressed += OnEnterKey;
        }

        private static readonly string[] SpeedKeys = { "relaxed", "natural", "fast", "instant" };
        private static readonly string[] SpeedLabels =
        {
            "Relaxed, 90 to 240 ms per key",
            "Natural, 38 to 135 ms per key",
            "Fast, 18 to 60 ms per key",
            "Instant, no delay"
        };
        private static readonly int[] IntervalValues = { 30, 60, 120, 300, 600 };
        private static readonly string[] IntervalLabels =
        {
            "Every 30 seconds", "Every minute", "Every 2 minutes", "Every 5 minutes", "Every 10 minutes"
        };

        /// <summary>
        /// The three original Joe Browser tools plus a place to add unpacked extensions the
        /// user built. Every switch is per profile, so one identity can be strict and another lax.
        /// </summary>
        private void BuildTools()
        {
            _lblExt = Lbl(_tabTools, "BUILT-IN APP TOOLS");

            _toolHumanPaste = new JBToggle { Text = "Paste as human", Checked = _tools.HumanPaste };
            _toolProxyWatch = new JBToggle { Text = "Proxy IP change & offline alerts", Checked = _tools.ProxyWatch };
            _toolRightClick = new JBToggle { Text = "Enable right click & copy", Checked = _tools.EnableRightClick };
            _toolAllowShift = new JBToggle { Text = "Also unlock text selection", Checked = _tools.UnlockSelection };
            _tabTools.Controls.Add(_toolHumanPaste);
            _tabTools.Controls.Add(_toolProxyWatch);
            _tabTools.Controls.Add(_toolRightClick);
            _tabTools.Controls.Add(_toolAllowShift);

            _lblSpeed = Lbl(_tabTools, "TYPING SPEED");
            _toolSpeed = Pick(_tabTools, "speed");
            int speedIdx = Array.IndexOf(SpeedKeys, _tools.HumanPasteSpeed ?? "natural");
            _toolSpeed.SetItems(SpeedLabels, speedIdx < 0 ? 1 : speedIdx);

            _lblInterval = Lbl(_tabTools, "CHECK INTERVAL");
            _toolInterval = Pick(_tabTools, "clock");
            int intervalIdx = Array.IndexOf(IntervalValues, _tools.ProxyWatchInterval);
            _toolInterval.SetItems(IntervalLabels, intervalIdx < 0 ? 1 : intervalIdx);

            _lblCustomExt = Lbl(_tabTools, "EXTRA EXTENSION FOLDERS");
            _customExt = Txt(_tabTools, _tools.CustomExtensions, "C:\\extensions\\my-extension", null, true, 76);

            _lblExtHint = new JBLabel(LabelRole.Faint,
                "Tools are injected directly, so no extension install is needed. Extra folders are optional unpacked extensions.");
            _lblExtHint.Wrap = true;
            _lblExtHint.TextAlign = ContentAlignment.TopLeft;
            _tabTools.Controls.Add(_lblExtHint);

            _openExtFolder = new JBButton { Kind = BtnKind.Soft, Icon = "folder", Text = "Open extensions folder" };
            _openExtFolder.Click += delegate
            {
                try
                {
                    ExtensionManager.EnsureAll();
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.Extensions + "\"")
                    { UseShellExecute = true });
                }
                catch (Exception ex) { SetError(ex.Message); }
            };
            _tabTools.Controls.Add(_openExtFolder);

            _toolAllowShift.Visible = _tools.EnableRightClick;
            _toolRightClick.CheckedChanged += delegate
            {
                _toolAllowShift.Visible = _toolRightClick.Checked;
                LayoutAll();
            };
        }

        private void ToggleReveal()
        {
            bool shown = !_pass.UsePasswordChar;
            _pass.UsePasswordChar = !shown;
            _reveal.Icon = shown ? "eye" : "eye-off";
            _reveal.Tip = shown ? "Show password" : "Hide password";
            _reveal.Invalidate();
        }

        private void ProxyCardPaint(object sender, PaintEventArgs e)
        {
            if (_proxyState == 0) return;
            var card = (JBPanel)sender;
            Ui.Quality(e.Graphics);
            float ic = Ui.S(16);
            string name = _proxyState > 0 ? "check-circle" : "alert";
            Color c = _proxyState > 0 ? Theme.Accent : Theme.Text;
            Icons.Draw(e.Graphics, name,
                new RectangleF(Ui.S(14), (card.Height - ic) / 2f, ic, ic), c, 1.6f);
        }

        // ---------------------------------------------------------------- state

        private void RefreshFingerprintUi()
        {
            _suppress = true;
            try
            {
                int osIdx = 0;
                for (int i = 0; i < FingerprintFactory.OsList.Length; i++)
                    if (string.Equals(FingerprintFactory.OsList[i], _fp.Os, StringComparison.OrdinalIgnoreCase)) osIdx = i;
                if (_systemTarget) osIdx = 0;
                /* A new profile mirrors this PC. If the stored target is not this machine - a
                   duplicated profile opened as new, or a fingerprint rolled before the editor
                   existed - the whole identity is re-rolled instead of keeping a Windows screen
                   and GPU under a target the picker now calls "System default". */
                if (_systemTarget && !string.Equals(_fp.Os, FingerprintFactory.SystemOsName(), StringComparison.OrdinalIgnoreCase))
                {
                    /* A repair, not a choice: this only runs when the stored target disagrees
                       with the picker, so it re-rolls coherently and then re-checks the
                       workspace like any other roll. */
                    FingerprintIdentity.Comparison repairClash;
                    FingerprintIdentity.MakeUnique(_fp, FingerprintFactory.SystemOsName(), _proxy,
                        Others(), out repairClash);
                }
                _os.SelectedIndex = osIdx;
                _os.Icon = FingerprintFactory.DeviceIcon(_fp.Os);
                if (!string.Equals(FingerprintFactory.OsList[osIdx], _fp.Os, StringComparison.OrdinalIgnoreCase))
                    _fp.Os = ResolveOs(FingerprintFactory.OsList[osIdx]);

                _screens = FingerprintFactory.ScreensFor(_fp.Os);
                var screenItems = new List<string>();
                int selScreen = 0;
                bool matched = false;
                for (int i = 0; i < _screens.Count; i++)
                {
                    screenItems.Add((int)_screens[i][0] + " \u00d7 " + (int)_screens[i][1]);
                    if (!matched && _screens[i][0] == _fp.ScreenW && _screens[i][1] == _fp.ScreenH) { selScreen = i; matched = true; }
                }
                if (!matched)
                {
                    // A profile can carry a resolution that is not in the curated list (a custom
                    // value or an older table). Show it as its own entry instead of silently
                    // replacing it with the first item.
                    screenItems.Insert(0, _fp.ScreenW + " \u00d7 " + _fp.ScreenH);
                    _screens.Insert(0, new[] { (double)_fp.ScreenW, _fp.ScreenH, _fp.PixelRatio });
                    selScreen = 0;
                }
                _screen.SetItems(screenItems, selScreen);
                if (matched) ApplyScreenSelection();

                _coreVals = WithValue(FingerprintFactory.CoreOptions, _fp.Cores);
                var coreItems = new List<string>();
                for (int i = 0; i < _coreVals.Count; i++) coreItems.Add(FingerprintFactory.CoresLabel(_coreVals[i]));
                _cores.SetItems(coreItems, _coreVals.IndexOf(_fp.Cores));

                _memVals = WithValue(FingerprintFactory.MemoryOptions, _fp.Memory);
                var memItems = new List<string>();
                for (int i = 0; i < _memVals.Count; i++) memItems.Add(FingerprintFactory.MemoryLabel(_memVals[i]));
                _ram.SetItems(memItems, _memVals.IndexOf(_fp.Memory));

                var tzItems = new List<string>(FingerprintFactory.Timezones);
                if (!string.IsNullOrEmpty(_fp.Timezone) && !tzItems.Contains(_fp.Timezone)) tzItems.Insert(0, _fp.Timezone);
                _tz.SetItems(tzItems, tzItems.IndexOf(_fp.Timezone));

                var langItems = new List<string>(FingerprintFactory.Languages);
                if (!string.IsNullOrEmpty(_fp.Language) && !langItems.Contains(_fp.Language)) langItems.Insert(0, _fp.Language);
                _lang.SetItems(LangItems(langItems), _fp.Language);

                _gpus = FingerprintFactory.GpusFor(_fp.Os);
                var gpuItems = new List<string>();
                int selGpu = 0;
                for (int i = 0; i < _gpus.Count; i++)
                {
                    gpuItems.Add(FingerprintFactory.GpuLabel(_gpus[i]));
                    if (_gpus[i][1] == _fp.WebGlRenderer) selGpu = i;
                }
                _gpu.SetItems(gpuItems, selGpu);

                _webrtcSel.SelectedIndex = FingerprintFactory.IndexOfKey(FingerprintFactory.WebRtcOptions, _fp.WebRtcMode, 0);
                _geoSel.SelectedIndex = FingerprintFactory.IndexOfKey(FingerprintFactory.GeoOptions, _fp.GeoMode, 0);
                _lat.Value = _fp.GeoLat ?? "";
                _lng.Value = _fp.GeoLng ?? "";
                ApplyGeoVisibility();

                _canvas.Checked = _fp.CanvasNoise;
                _audio.Checked = _fp.AudioNoise;
                _fonts.Checked = _fp.FontsNoise;
                _battery.Checked = _fp.SpoofBattery;
                _dnt.Checked = _fp.DoNotTrack;
                _autoMatch.Checked = _fp.AutoMatchProxy;
                _autoUa.Checked = _fp.AutoUserAgent;
                _ua.Value = _fp.UserAgentOverride ?? "";
                _ua.Placeholder = _fp.AutoUserAgent
                    ? "Built from the device target when the browser opens"
                    : "Paste the exact string to use";
            }
            finally
            {
                _suppress = false;
                _regen.Invalidate();
            }
            RefreshOsVersionUi();
        }

        /// <summary>Android/iOS version list, shown only for a mobile target.</summary>
        private void RefreshOsVersionUi()
        {
            bool android = FingerprintFactory.IsAndroid(_fp.Os);
            bool ios = FingerprintFactory.IsIos(_fp.Os);
            if (_osVer == null) return;
            _suppress = true;
            try
            {
                _lblOsVer.Visible = android || ios;
                _osVer.Visible = android || ios;
                _fontSet.SelectedIndex = Math.Max(0,
                    FingerprintFactory.IndexOfKey(FingerprintFactory.FontSets, _fp.FontSet ?? "auto", 0));
                _hwNoise.Checked = _fp.HardwareNoise;
                if (!android && !ios) return;

                string[] values = android ? FingerprintFactory.AndroidVersions : FingerprintFactory.IosVersions;
                string cur = android ? (_fp.AndroidVersion ?? "auto") : (_fp.IosVersion ?? "auto");
                var labels = new List<string>();
                _osVerKeys = new List<string>();
                foreach (string v in values)
                {
                    _osVerKeys.Add(v);
                    labels.Add(v == "auto"
                        ? "Auto  (" + (android ? FingerprintFactory.AndroidVersionFor(_fp) : FingerprintFactory.IosVersionFor(_fp)) + ")"
                        : v);
                }
                int idx = _osVerKeys.IndexOf(cur);
                _osVer.SetItems(labels, idx < 0 ? 0 : idx);
                _lblOsVer.Text = android ? "ANDROID VERSION" : "IOS VERSION";
                _osVer.Icon = android ? "android" : "iphone";
            }
            finally { _suppress = false; }
        }

        /// <summary>The option list plus the stored value, so a legacy profile is never lost.</summary>
        private static List<int> WithValue(int[] options, int value)
        {
            var list = new List<int>(options);
            if (value > 0 && !list.Contains(value))
            {
                list.Add(value);
                list.Sort();
            }
            return list;
        }

        private void ApplyScreenSelection()
        {
            if (_screens == null) return;
            int i = _screen.SelectedIndex;
            if (i < 0 || i >= _screens.Count) return;
            FingerprintFactory.ApplyScreen(_fp, _fp.Os, _screens[i]);
        }

        private void SwitchTab(int index)
        {
            /* Whoever calls this - a click or code - the strip and the page must agree. The
               setter is a no-op when the index already matches, so this cannot loop. */
            if (_tabs != null && _tabs.SelectedIndex != index) _tabs.SelectedIndex = index;
            _hostGeneral.Visible = index == 0;
            _hostFp.Visible = index == 1;
            _hostPriv.Visible = index == 2;
            _hostProxy.Visible = index == 3;
            _hostTools.Visible = index == 4;
            /* A double-buffered host that becomes visible again can otherwise keep the
               previous tab's pixels for one frame. Force a full repaint of its children. */
            JBScrollHost[] hosts = { _hostGeneral, _hostFp, _hostPriv, _hostProxy, _hostTools };
            foreach (JBScrollHost h in hosts) h.Invalidate(true);
            /* Focus follows the tab. Without this the first control of the newly shown page is
               never focused, so Tab after switching a tab started from the old page and the
               keyboard user was dropped back where they came from. */
            /* Only once the form is on screen: focusing a control during construction is
               discarded when the window is activated, and would fight OnLoad's choice. */
            Control first = IsHandleCreated && Visible ? FirstField(index) : null;
            if (first != null) { try { first.Focus(); } catch { } }
            Invalidate();
        }

        /// <summary>The first real field of a tab, in the order it appears on screen.</summary>
        private Control FirstField(int index)
        {
            switch (index)
            {
                case 0: return _name == null ? null : _name.Box;
                /* Fingerprint, privacy, proxy and tools all open with a picker, which is a
                   real tab stop in every one of those pages. */
                case 1: return _os;
                case 2: return _webrtcSel;
                case 3: return _savedProxy;
                case 4: return _toolSpeed;
                default: return null;
            }
        }

        private void SetError(string text)
        {
            _err.Text = text ?? "";
            _err.Visible = text != null && text.Length > 0;
            /* The line elides at the end, and the end of "…is too long" is the part that says
               what is wrong. The tooltip carries the whole sentence. */
            ToolTips.Attach(_err, _err.Text);
            LayoutFooter();
            _err.Invalidate();
        }

        // ---------------------------------------------------------------- layout

        private void LayoutAll()
        {
            if (!_built) return;
            SuspendLayout();
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(28);

            /* The tabs are measured, not guessed. At 150% scale the five names needed more room
               than the cell width the old code assumed, so "Privacy" was drawn under the start of
               "Proxy" and the tab under the pointer was not the tab that was read. When the window
               is too narrow for all five names the row keeps its bounds and each name is clipped
               with an ellipsis, which reads as a shortened name and not as two names in one cell. */
            int tabNeed = _tabs.PreferredWidth(Ui.S(72));
            _close.SetBounds(w - pad - Ui.S(32), Ui.S(16), Ui.S(32), Ui.S(32));
            _title.SetBounds(pad, Ui.S(16), Math.Max(Ui.S(120), w - pad * 2 - Ui.S(48)), Ui.S(32));
            _tabs.SetBounds(pad, Ui.S(58), Math.Min(Math.Max(Ui.S(360), tabNeed), w - pad * 2), Ui.S(38));

            int top = Ui.S(108);
            int bodyH = Math.Max(Ui.S(140), h - top - Ui.S(70));
            int bodyW = Math.Max(Ui.S(200), w - pad * 2);

            _hostGeneral.SetBounds(pad, top, bodyW, bodyH);
            _hostFp.SetBounds(pad, top, bodyW, bodyH);
            _hostPriv.SetBounds(pad, top, bodyW, bodyH);
            _hostProxy.SetBounds(pad, top, bodyW, bodyH);
            _hostTools.SetBounds(pad, top, bodyW, bodyH);

            LayoutGeneral(_tabGeneral, _hostGeneral, _hostGeneral.ContentWidth);
            LayoutFingerprint(_tabFp, _hostFp, _hostFp.ContentWidth);
            LayoutPrivacy(_tabPriv, _hostPriv, _hostPriv.ContentWidth);
            LayoutProxy(_tabProxy, _hostProxy, _hostProxy.ContentWidth);
            LayoutTools(_tabTools, _hostTools, _hostTools.ContentWidth);

            LayoutFooter();
            ResumeLayout(false);
            Invalidate();
        }

        private void LayoutFooter()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(28);
            int saveW = _save.PreferredWidth(Ui.S(150)), saveH = Ui.S(33);
            int cancelW = _cancel.PreferredWidth(Ui.S(88));
            /* Both buttons and the error line have to fit in the footer. At a wide scale on a
               narrow window they did not, and the Cancel button was pushed past the left edge
               while the error line was drawn from the same place. Shrink the pair in proportion
               before anything is placed. */
            int room = Math.Max(Ui.S(160), w - pad * 2);
            if (saveW + Ui.S(8) + cancelW > room)
            {
                int spare = Math.Max(Ui.S(80), room - Ui.S(8));
                int want = Math.Max(1, saveW + cancelW);
                saveW = Math.Max(Ui.S(88), spare * saveW / want);
                cancelW = Math.Max(Ui.S(64), spare - saveW);
            }
            _save.SetBounds(w - pad - saveW, h - Ui.S(16) - saveH, saveW, saveH);
            _cancel.SetBounds(_save.Left - Ui.S(8) - cancelW, h - Ui.S(16) - saveH, cancelW, saveH);
            /* The label starts at pad+22 (right of the alert glyph), so its width has to be
               measured from that x or the last characters sit under the Cancel button. */
            /* The error line is one row high and takes whatever the two buttons leave. On a
               narrow window that used to be negative, so the message was drawn nowhere: the save
               was refused and nothing said why. It gets a minimum and is allowed to overlap the
               footer padding rather than vanish. */
            int errW = Math.Max(Ui.S(80), _cancel.Left - Ui.S(8) - (pad + Ui.S(22)) - Ui.S(4));
            _err.SetBounds(pad + Ui.S(22), h - Ui.S(16) - saveH, errW, saveH);
            ToolTips.Attach(_err, _err.Text);
            _footerY = h - Ui.S(64);
        }

        private void LayoutGeneral(Panel page, JBScrollHost host, int cw)
        {
            int y = Ui.S(4);
            int lh = Ui.S(18), fh = Ui.S(33), lg = Ui.S(6), rg = Ui.S(16);
            int browseW = _browse.PreferredWidth(Ui.S(76));

            _lblName.SetBounds(0, y, cw, lh); y += lh + lg;
            _name.SetBounds(0, y, cw, fh); y += fh + rg;

            _lblStart.SetBounds(0, y, cw, lh); y += lh + lg;
            _startUrl.SetBounds(0, y, cw, fh); y += fh + rg;

            bool wide = cw >= Ui.S(540);
            if (wide)
            {
                int kindW = Ui.S(226);
                int pathW = cw - kindW - browseW - Ui.S(16);
                _lblKind.SetBounds(0, y, kindW, lh);
                _lblPath.SetBounds(kindW + Ui.S(8), y, pathW, lh);
                y += lh + lg;
                _browserKind.SetBounds(0, y, kindW, fh);
                _browserPath.SetBounds(kindW + Ui.S(8), y, pathW, fh);
                _browse.SetBounds(cw - browseW, y, browseW, fh);
                y += fh;
            }
            else
            {
                _lblKind.SetBounds(0, y, cw, lh); y += lh + lg;
                _browserKind.SetBounds(0, y, cw, fh); y += fh + Ui.S(12);
                _lblPath.SetBounds(0, y, cw, lh); y += lh + lg;
                int pathW = cw - browseW - Ui.S(8);
                _browserPath.SetBounds(0, y, pathW, fh);
                _browse.SetBounds(cw - browseW, y, browseW, fh);
                y += fh;
            }
            y += rg;

            int manageW = _kernelManage.PreferredWidth(Ui.S(112));
            _lblKernel.SetBounds(0, y, cw - manageW - Ui.S(8), lh); y += lh + lg;
            _kernel.SetBounds(0, y, cw - manageW - Ui.S(8), fh);
            _kernelManage.SetBounds(cw - manageW, y, manageW, fh);
            y += fh + Ui.S(4);
            if (_kernelHint != null)
            {
                // Two lines of help at most: the text is a sentence, and a taller box would push
                // the rest of the page down for a note most profiles never need to read twice.
                _kernelHint.SetBounds(0, y, cw, Ui.S(34));
                y += Ui.S(34) + Ui.S(6);
            }
            y += rg - Ui.S(6);

            if (_getBrowser != null && _getBrowser.Visible)
            {
                int gw = Ui.S(118);
                _getBrowser.SetBounds(cw - gw, y - rg + Ui.S(2), gw, Ui.S(34));
                _browserNote.SetBounds(0, y - rg + Ui.S(6), cw - gw - Ui.S(12), Ui.S(26));
                y += Ui.S(34) + Ui.S(6);
            }

            y = Row(_lblGroup, _group, _lblTags, _tags, y, cw, wide, lh, lg, rg, Ui.S(12), fh, fh);
            _lblStatus.SetBounds(0, y, cw, lh); y += lh + lg;
            _status.SetBounds(0, y, cw, fh); y += fh + rg;

            _lblNotes.SetBounds(0, y, cw, lh); y += lh + lg;
            _notes.SetBounds(0, y, cw, Ui.S(72));

            FinishPage(page, host, cw, y + Ui.S(72));
        }

        /// <summary>A labelled row that pairs two fields side by side, or stacks them when narrow.</summary>
        private int Row(JBLabel l1, Control c1, JBLabel l2, Control c2, int y, int cw, bool two,
            int lh, int lg, int rg, int cg, int h1, int h2)
        {
            int leftW = two ? (cw - cg) / 2 : cw;
            int rightW = two ? cw - leftW - cg : cw;
            int rx = two ? leftW + cg : 0;

            l1.SetBounds(0, y, leftW, lh);
            if (two)
            {
                l2.SetBounds(rx, y, rightW, lh);
                y += lh + lg;
                c1.SetBounds(0, y, leftW, h1);
                c2.SetBounds(rx, y, rightW, h2);
                return y + Math.Max(h1, h2) + rg;
            }
            y += lh + lg;
            c1.SetBounds(0, y, leftW, h1);
            y += h1 + Ui.S(12);
            l2.SetBounds(0, y, rightW, lh);
            y += lh + lg;
            c2.SetBounds(0, y, rightW, h2);
            return y + h2 + rg;
        }

        private void LayoutFingerprint(Panel page, JBScrollHost host, int cw)
        {
            int lh = Ui.S(18), fh = Ui.S(33), lg = Ui.S(6), rg = Ui.S(16), cg = Ui.S(12);
            bool two = cw >= Ui.S(560);

            int y = Ui.S(4);
            y = Row(_lblOs, _os, _lblScreen, _screen, y, cw, two, lh, lg, rg, cg, fh, fh);
            /* Decide from the profile, not from Control.Visible: a tab that is not the current
               one reports every child as invisible, which would skip this row and leave the
               label stacked at 0,0 on top of the first one. */
            if (FingerprintFactory.IsMobileOs(_fp.Os))
            {
                _lblOsVer.SetBounds(0, y, cw, lh); y += lh + lg;
                _osVer.SetBounds(0, y, two ? (cw - cg) / 2 : cw, fh);
                y += fh + rg;
            }
            y = Row(_lblCores, _cores, _lblRam, _ram, y, cw, two, lh, lg, rg, cg, fh, fh);
            y = Row(_lblTz, _tz, _lblLang, _lang, y, cw, two, lh, lg, rg, cg, fh, fh);

            _lblGpu.SetBounds(0, y, cw, lh); y += lh + lg;
            _gpu.SetBounds(0, y, cw, fh); y += fh + rg;

            /* The pair shares the column: two long labels at a large scale were wider than the
               page, so the second button hung outside the content and its last word was cut. */
            int regenW = _regen.PreferredWidth(Ui.S(160));
            int randomW = _random.PreferredWidth(Ui.S(160));
            Ui.FitPair(ref regenW, ref randomW, Ui.S(8), Math.Max(Ui.S(200), cw), Ui.S(96), Ui.S(96));
            _regen.SetBounds(0, y, regenW, Ui.S(33));
            _random.SetBounds(_regen.Right + Ui.S(8), y, randomW, Ui.S(33));
            y += Ui.S(33) + Ui.S(10);
            UpdateFirefoxWarn(0, y, cw);
            if (_fpWarn.Visible) y += Ui.S(26) + Ui.S(6);

            FinishPage(page, host, cw, y);
        }

        private void LayoutPrivacy(Panel page, JBScrollHost host, int cw)
        {
            int lh = Ui.S(18), fh = Ui.S(33), lg = Ui.S(6), rg = Ui.S(16), cg = Ui.S(12);
            bool two = cw >= Ui.S(560);

            int y = Ui.S(4);
            y = Row(_lblWebrtc, _webrtcSel, _lblGeo, _geoSel, y, cw, two, lh, lg, rg, cg, fh, fh);
            if (GeoIsCustom)
                y = Row(_lblLat, _lat, _lblLng, _lng, y, cw, two, lh, lg, rg, cg, fh, fh);

            _lblFontSet.SetBounds(0, y, cw, lh); y += lh + lg;
            _fontSet.SetBounds(0, y, two ? (cw - cg) / 2 : cw, fh); y += fh + rg;

            _lblUa.SetBounds(0, y, cw, lh); y += lh + lg;
            /* The "keep it automatic" button was placed at a fixed 214 px reserve, measured
               against the English label. A longer translation needs more than that, and the two
               controls then overlapped across the middle of the row. Its real width is asked
               for instead, and the field keeps whatever is left. */
            int autoW = Math.Min(two ? cw / 2 : cw, MeasureToggleWidth(_autoUa));
            if (two)
            {
                int uaW = cw - autoW - cg;
                _ua.SetBounds(0, y, uaW, fh);
                _autoUa.SetBounds(uaW + cg, y, autoW, fh);
                y += fh + rg;
            }
            else
            {
                _ua.SetBounds(0, y, cw, fh);
                y += fh + lg;
                _autoUa.SetBounds(0, y, autoW, fh);
                y += fh + rg;
            }

            Control[] toggles = { _canvas, _audio, _fonts, _hwNoise, _battery, _dnt, _autoMatch };
            int cols = two ? 2 : 1;
            int tg = Ui.S(10), th = Ui.S(28);
            int colW = two ? (cw - tg) / 2 : cw;
            int rows = (toggles.Length + cols - 1) / cols;
            int rowY = y;
            for (int i = 0; i < toggles.Length; i++)
                toggles[i].SetBounds((i % cols) * (colW + tg), rowY + (i / cols) * (th + Ui.S(6)), colW, th);
            y = rowY + rows * (th + Ui.S(6)) + Ui.S(4);

            FinishPage(page, host, cw, y);
        }

        private void LayoutProxy(Panel page, JBScrollHost host, int cw)
        {
            int lh = Ui.S(18), fh = Ui.S(33), lg = Ui.S(6), rg = Ui.S(16), cg = Ui.S(12);
            bool two = cw >= Ui.S(520);

            int y = Ui.S(4);
            _lblSaved.SetBounds(0, y, cw, lh); y += lh + lg;
            _savedProxy.SetBounds(0, y, cw, fh); y += fh + rg;

            _lblType.SetBounds(0, y, cw, lh); y += lh + lg;
            /* Four names in 420 pixels is 105 each, which "SOCKS5" at a large scale does not
               fit, and the name ran into its neighbour. Measured, then capped at the column. */
            int segW = Math.Max(Ui.S(320), Math.Min(_proxySeg.PreferredWidth(Ui.S(96)), Ui.S(520)));
            _proxySeg.SetBounds(0, y, Math.Min(cw, segW), Ui.S(38));
            y += Ui.S(38) + rg;

            y = Row(_lblHost, _host, _lblPort, _port, y, cw, two, lh, lg, rg, cg, fh, fh);
            y = Row(_lblUser, _user, _lblPass, _pass, y, cw, two, lh, lg, rg, cg, fh, fh);

            int testW = _testBtn.PreferredWidth(Ui.S(150));
            _testBtn.SetBounds(0, y, Math.Min(cw, testW), fh);
            y += fh + Ui.S(12);

            _proxyCard.SetBounds(0, y, cw, Ui.S(52));
            _proxyResult.SetBounds(Ui.S(36), Ui.S(6), Math.Max(Ui.S(60), cw - Ui.S(48)), Ui.S(40));
            y += Ui.S(52) + Ui.S(12);

            FinishPage(page, host, cw, y);
        }

        private void LayoutTools(Panel page, JBScrollHost host, int cw)
        {
            int lh = Ui.S(18), fh = Ui.S(33), lg = Ui.S(6), rg = Ui.S(16), cg = Ui.S(12);
            bool two = cw >= Ui.S(560);
            int y = Ui.S(4);

            _lblExt.SetBounds(0, y, cw, lh); y += lh + lg;

            int cols = two ? 2 : 1;
            int gap = Ui.S(10), th = Ui.S(28);
            int colW = two ? (cw - gap) / 2 : cw;
            Control[] toggles = { _toolHumanPaste, _toolProxyWatch, _toolRightClick, _toolAllowShift };
            int placed = 0;
            foreach (Control t in toggles)
            {
                /* Do not test Control.Visible here. OnLoad lays out the editor after
                   SwitchTab has hidden every tab except General, and WinForms reports a
                   child as invisible whenever its parent is hidden. That made all four
                   toggles keep their default (0,0) bounds and paint over the section
                   label. Only the Shift override is intentionally hidden. */
                if (t == _toolAllowShift && !_toolRightClick.Checked) continue;
                t.SetBounds((placed % cols) * (colW + gap), y + (placed / cols) * (th + Ui.S(6)), colW, th);
                placed++;
            }
            y += ((placed + cols - 1) / cols) * (th + Ui.S(6)) + Ui.S(8);

            y = Row(_lblSpeed, _toolSpeed, _lblInterval, _toolInterval, y, cw, two, lh, lg, rg, cg, fh, fh);

            _lblCustomExt.SetBounds(0, y, cw, lh); y += lh + lg;
            _customExt.SetBounds(0, y, cw, Ui.S(76)); y += Ui.S(76) + Ui.S(6);
            _lblExtHint.SetBounds(0, y, cw, Ui.S(34)); y += Ui.S(34) + Ui.S(10);
            int bw = Math.Min(cw, _openExtFolder.PreferredWidth(Ui.S(200)));
            _openExtFolder.SetBounds(0, y, bw, fh); y += fh + Ui.S(12);

            FinishPage(page, host, cw, y);
        }

        /// <summary>Width a toggle needs for its label, plus the switch it draws itself.</summary>
        private static int MeasureToggleWidth(JBToggle t)
        {
            if (t == null) return Ui.S(180);
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                float text = Ui.Measure(g, t.Text ?? "", t.Font ?? Theme.Body).Width;
                return (int)Math.Ceiling(text) + Ui.S(78);
            }
        }

        private void FinishPage(Panel page, JBScrollHost host, int cw, int height)
        {
            /* The page carries its content plus room for the footer bar, which is not part of the
               scrolling area: without the allowance the last row of the last tab sat under the
               Save button and could not be scrolled clear of it. */
            int bottom = height + Ui.S(24);
            page.SetBounds(0, 0, cw, Math.Max(bottom, host.Height));
            host.SyncExtent();
        }

        // ---------------------------------------------------------------- proxy test

        private async System.Threading.Tasks.Task TestProxyCore()
        {
            var cfg = new ProxyConfig
            {
                Type = ProxyTypeFromUi(),
                Host = _host.Value.Trim(),
                Port = ParsePort(_port.Value),
                User = _user.Value.Trim(),
                Password = _pass.Value
            };

            if (!cfg.Enabled)
            {
                SetProxyResult(-1, "Pick a proxy type and add a host and port first.");
                return;
            }

            _testBtn.Enabled = false;
            SetProxyResult(0, "Testing connection\u2026");
            try
            {
                var res = await ProxyTester.TestAsync(cfg);
                if (IsDisposed) return;
                if (res.Ok)
                {
                    SetProxyResult(1, "Connected " + res.Ip + ", " + res.Country
                        + (string.IsNullOrEmpty(res.City) ? "" : ", " + res.City)
                        + ", " + res.LatencyMs + " ms");

                    _proxy.LastIp = res.Ip;
                    _proxy.LastCountry = res.Country;
                    _proxy.LastCountryCode = res.CountryCode;
                    _proxy.LastTimezone = res.Timezone;
                    _proxy.LastCity = res.City;

                    if (_autoMatch.Checked) ApplyDetectedLocale();
                }
                else SetProxyResult(-1, res.Error);
            }
            finally
            {
                _testBtn.Enabled = true;
            }
        }

        /// <summary>Guarded entry point so a failed await never reaches the crash dialog.</summary>
        private void TestProxy() { Guarded.Run("TestProxy", this, delegate { return TestProxyCore(); }); }

        private void SetProxyResult(int state, string text)
        {
            _proxyState = state;
            _proxyResult.Text = text;
            _proxyResult.ForeColor = state > 0 ? Theme.AccentText : state < 0 ? Theme.Text : Theme.TextFaint;
            _proxyCard.Invalidate();
            _proxyResult.Invalidate();
        }

        private static int ParsePort(string s)
        {
            int p;
            return int.TryParse((s ?? "").Trim(), out p) && p > 0 && p < 65536 ? p : 0;
        }

        private string ProxyTypeFromUi()
        {
            switch (_proxySeg.SelectedIndex)
            {
                case 1: return "http";
                case 2: return "https";
                case 3: return "socks5";
                default: return "none";
            }
        }

        // ---------------------------------------------------------------- save

        private void Save()
        {
            string name = _name.Value.Trim();
            if (name.Length == 0)
            {
                SetError("Give this profile a name.");
                _tabs.SelectedIndex = 0;
                _name.Box.Select();
                _name.Box.Focus();
                return;
            }

            string type = ProxyTypeFromUi();
            string host = _host.Value.Trim();
            int port = ParsePort(_port.Value);
            if (type != "none")
            {
                if (host.Length == 0) { Fail(3, "Proxy host is required."); return; }
                if (port == 0)
                {
                    /* Name the value that was typed. "Must be between 1 and 65535" told a user
                       who entered "4758q" nothing about why it was rejected, and the field is
                       numeric-looking enough that the typo is easy to miss. */
                    string typed = (_port.Value ?? "").Trim();
                    Fail(3, typed.Length == 0
                        ? "Proxy port is required."
                        : "\"" + typed + "\" is not a valid port. Use a number from 1 to 65535.");
                    _port.Box.Focus();
                    _port.Box.SelectAll();
                    return;
                }
            }

            string start = _startUrl.Value.Trim();
            if (start.Length > 0 && !start.StartsWith("http://") && !start.StartsWith("https://") && !start.StartsWith("about:"))
                start = "https://" + start;

            /* A clash accepted in the editor is a decision, not an oversight: the user was told
               what the profile shares and chose to keep it. That is exactly the case the list's
               header counter exists to make visible afterwards, so nothing is blocked here. */
            string uaText = _ua.Value.Trim();
            if (!_autoUa.Checked && uaText.Length == 0)
            {
                Fail(2, "Add a user agent string, or turn automatic generation back on.");
                return;
            }

            string geoMode = KeyOf(FingerprintFactory.GeoOptions, _geoSel.SelectedIndex, "proxy");
            string latText = _lat.Value.Trim();
            string lngText = _lng.Value.Trim();
            if (geoMode == "custom")
            {
                double latVal, lngVal;
                if (!double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out latVal) ||
                    !double.TryParse(lngText, NumberStyles.Float, CultureInfo.InvariantCulture, out lngVal) ||
                    Math.Abs(latVal) > 90 || Math.Abs(lngVal) > 180)
                {
                    Fail(2, "Custom location needs a latitude within 90 and a longitude within 180.");
                    return;
                }
            }

            SetError("");
            _profile.Name = name;
            _profile.Notes = _notes.Value.Trim();
            _profile.Group = _group.Value.Trim();
            _profile.Tags = _tags.Value.Trim();
            _profile.Status = _status.Value.Trim();
            _profile.StartUrl = start;

            string custom = _browserPath.Value.Trim();
            _profile.BrowserPath = custom;
            int ki = _browserKind.SelectedIndex;
            _profile.BrowserKind = custom.Length > 0 ? "custom"
                : (ki >= 0 && ki < _kindKeys.Count ? _kindKeys[ki] : "auto");

            int kk = _kernel.SelectedIndex;
            string kernelKey = kk > 0 && kk < _kernelKeys.Count ? _kernelKeys[kk] : "";
            int bar = kernelKey.IndexOf('|');
            _profile.KernelEngine = bar > 0 ? kernelKey.Substring(0, bar) : "";
            _profile.KernelVersion = bar > 0 ? kernelKey.Substring(bar + 1) : "";

            ApplyScreenSelection();
            _fp.Os = ResolveOs(_os.Value);
            _fp.DeviceType = FingerprintFactory.DeviceTypeFor(_fp.Os);
            _fp.Timezone = _tz.Value;
            _fp.Language = _lang.Value;
            _fp.AcceptLanguage = FingerprintFactory.AcceptLanguageFor(_fp.Language);
            ApplyGpuSelection();
            _fp.CanvasNoise = _canvas.Checked;
            _fp.AudioNoise = _audio.Checked;
            _fp.FontsNoise = _fonts.Checked;
            _fp.HardwareNoise = _hwNoise.Checked;
            _fp.FontSet = KeyOf(FingerprintFactory.FontSets, _fontSet.SelectedIndex, "auto");
            _fp.SpoofBattery = _battery.Checked;
            _fp.DoNotTrack = _dnt.Checked;
            _fp.AutoMatchProxy = _autoMatch.Checked;
            _fp.AutoUserAgent = _autoUa.Checked;
            _fp.UserAgentOverride = _autoUa.Checked ? "" : uaText;
            _fp.WebRtcMode = KeyOf(FingerprintFactory.WebRtcOptions, _webrtcSel.SelectedIndex, "proxy");
            _fp.WebRtcProtect = _fp.WebRtcMode != "off";
            _fp.GeoMode = geoMode;
            _fp.GeoLat = latText;
            _fp.GeoLng = lngText;
            _fp.UserAgent = "";
            _fp.Platform = "";
            _fp.Brand = "";
            if (_fp.Seed == 0) _fp.Seed = FingerprintFactory.NewSeed();
            _profile.Fp = _fp;

            _proxy.Type = type;
            _proxy.Host = host;
            _proxy.Port = port;
            _proxy.User = _user.Value.Trim();
            _proxy.Password = _pass.Value;
            _profile.Proxy = _proxy;

            _tools.HumanPaste = _toolHumanPaste.Checked;
            int si = _toolSpeed.SelectedIndex;
            _tools.HumanPasteSpeed = SpeedKeys[si < 0 || si >= SpeedKeys.Length ? 1 : si];
            _tools.ProxyWatch = _toolProxyWatch.Checked;
            int ii = _toolInterval.SelectedIndex;
            _tools.ProxyWatchInterval = IntervalValues[ii < 0 || ii >= IntervalValues.Length ? 1 : ii];
            _tools.EnableRightClick = _toolRightClick.Checked;
            _tools.UnlockSelection = _toolAllowShift.Checked;
            _tools.CustomExtensions = _customExt.Value.Trim();
            _profile.Tools = _tools;

            Result = _profile;
            DialogResult = DialogResult.OK;
            Close();
        }

        private DateTime? _escGuard;

        private void Fail(int tab, string message)
        {
            SetError(message);
            _tabs.SelectedIndex = tab;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Quality(g);
            using (var p = new Pen(Theme.Line))
            {
                // The pivot rule runs the full dialog width, past the last tab label.
                float tabRule = Ui.S(95);
                g.DrawLine(p, Ui.S(24), tabRule, ClientSize.Width - Ui.S(24), tabRule);
                g.DrawLine(p, Ui.S(24), _footerY, ClientSize.Width - Ui.S(24), _footerY);
            }

            if (_err != null && _err.Visible)
            {
                float ic = Ui.S(16);
                Icons.Draw(g, "alert", new RectangleF(Ui.S(28), ClientSize.Height - Ui.S(48), ic, ic), Theme.Accent, 1.7f);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                /* Esc used to close the editor outright. Five tabs of edits — name, notes,
                   fingerprint, proxy credentials, tools — were discarded silently, and Esc is
                   also the natural "undo the value I am typing" key, so this was hit by
                   accident more often than on purpose. One Esc now asks; a second Esc (or the
                   Discard button) closes. */
                if (_escGuard != null && (DateTime.UtcNow - _escGuard.Value).TotalSeconds < 4)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return true;
                }
                _escGuard = DateTime.UtcNow;
                SetError("Press Esc again to discard your changes.");
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                Save();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D))
            {
                _tabs.SelectedIndex = 3;
                TestProxy();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>Compact modal used for destructive and one-off confirmations.</summary>
    internal sealed class ConfirmForm : JBForm
    {
        private readonly JBLabel _title, _message, _detail;
        private readonly JBButton _ok, _cancel;

        private ConfirmForm(string title, string message, string detail, string okText, BtnKind okKind)
        {
            Resizable = false;
            DraggableSidebar = false;
            AllowMaximize = false;
            HeaderHit = 56;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Theme.Bg;
            MinimizeBox = false;

            _title = new JBLabel(LabelRole.Title, title);
            _title.AutoEllipsis = false;
            _message = new JBLabel(LabelRole.Body, message);
            _message.TextAlign = ContentAlignment.TopLeft;
            _message.Wrap = true;
            _detail = new JBLabel(LabelRole.Faint, detail ?? "");
            _detail.TextAlign = ContentAlignment.TopLeft;
            _detail.Wrap = true;
            _detail.Visible = !string.IsNullOrEmpty(detail);

            _ok = new JBButton { Kind = okKind, Text = okText };
            _ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            _cancel = new JBButton { Kind = BtnKind.Ghost, Text = "Cancel" };
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_title);
            Controls.Add(_message);
            Controls.Add(_detail);
            Controls.Add(_ok);
            Controls.Add(_cancel);
        }

        public static bool Ask(IWin32Window owner, string title, string message,
            string detail = null, string okText = "Delete", BtnKind okKind = BtnKind.Danger)
        {
            using (var f = new ConfirmForm(title, message, detail, okText, okKind))
                return f.ShowDialog(owner) == DialogResult.OK;
        }

        /// <summary>
        /// One-button notice with the same chrome as every other dialog. MessageBox shows the
        /// Windows system font in a rectangle that ignores the theme, which is the most obvious
        /// "this part is not finished" tell in an otherwise custom-drawn app.
        /// </summary>
        public static void Tell(IWin32Window owner, string title, string message, string detail = null)
        {
            try
            {
                using (var f = new ConfirmForm(title, message, detail, "OK", BtnKind.Primary))
                {
                    f._cancel.Visible = false;
                    f.ShowDialog(owner);
                }
            }
            catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitContent();
            /* A destructive confirmation must not be armed the moment it appears: a stray Enter
               or Space - including the one that opens it - used to choose Delete. The safe
               button takes focus, and Tab reaches the danger action in one step. */
            if (_ok.Kind == BtnKind.Danger)
            {
                _cancel.Select();
                _cancel.Focus();
            }
            else
            {
                _ok.Select();
                _ok.Focus();
            }
        }

        /* Enter and Esc are resolved here rather than through AcceptButton/CancelButton: the
           window is borderless, and a command key would never reach the buttons' own handlers. */
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return)
            {
                /* Enter always means the safe answer. Only an explicitly focused danger button
                   may be chosen with Enter, so the default action here is Cancel for a
                   destructive prompt and OK for a notice. */
                if (_ok.Kind == BtnKind.Danger && !_ok.Focused)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return true;
                }
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Sizes the dialog around its text so long messages never clip or scroll.</summary>
        private void FitContent()
        {
            int pad = Ui.S(26);
            int badge = Ui.S(40);
            int textX = pad + badge + Ui.S(16);
            int bodyTop = pad + badge + Ui.S(14);
            int saveW = Ui.S(124), cancelW = Ui.S(96);

            Size screenMax = Ui.FitToScreen(this, Ui.S(560), Ui.S(900));
            int textW = Math.Min(Ui.S(360), screenMax.Width - textX - pad);
            int width = textX + textW + pad;

            using (var g = CreateGraphics())
            {
                // Hard line breaks: a Label left to word-wrap can still trim the tail.
                string msg = WrapText(g, _message.Text, Theme.Body, textW);
                string det = _detail.Visible ? WrapText(g, _detail.Text, Theme.Caption, textW) : "";
                _message.Text = msg;
                _detail.Text = det;

                int mh = MeasureLines(g, msg, Theme.Body, textW);
                int dh = _detail.Visible ? MeasureLines(g, det, Theme.Caption, textW) : 0;
                int bottom = bodyTop + mh + (dh > 0 ? Ui.S(8) + dh : 0) + Ui.S(24);
                int height = bottom + Ui.S(33) + pad;
                int minButtons = textX + Ui.S(60) + saveW + cancelW + Ui.S(10);
                if (width < minButtons) width = Math.Min(minButtons, screenMax.Width);
                if (height > screenMax.Height) height = screenMax.Height;
                if (width < Ui.S(360)) width = Ui.S(360);

                int finalWidth = width, finalHeight = height, finalBottom = bottom;
                SuppressSurfaceEvents(() =>
                {
                    ClientSize = new Size(finalWidth, finalHeight);
                    _title.SetBounds(textX, pad + Ui.S(2), ClientSize.Width - textX - pad, Ui.S(32));
                    _message.SetBounds(textX, bodyTop, textW, mh);
                    _detail.SetBounds(textX, bodyTop + mh + Ui.S(8), textW, dh);
                    _ok.SetBounds(ClientSize.Width - pad - saveW, finalBottom, saveW, Ui.S(33));
                    _cancel.SetBounds(_ok.Left - Ui.S(8) - cancelW, finalBottom, cancelW, Ui.S(33));
                });
            }
            Invalidate();
            CenterToParent();
        }

        private static string WrapText(Graphics g, string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = new List<string>();
            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                if (para.Length == 0) { lines.Add(""); continue; }
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string test = cur.Length == 0 ? word : cur + " " + word;
                    if (Ui.Measure(g, test, font).Width <= width || cur.Length == 0) cur = test;
                    else { lines.Add(cur); cur = word; }
                }
                lines.Add(cur);
            }
            return string.Join("\n", lines.ToArray());
        }

        private static int MeasureLines(Graphics g, string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float lineH = font.GetHeight(g);
            var lines = new List<string>();
            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                if (para.Length == 0) { lines.Add(" "); continue; }
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string test = cur.Length == 0 ? word : cur + " " + word;
                    if (Ui.Measure(g, test, font).Width <= width || cur.Length == 0) cur = test;
                    else { lines.Add(cur); cur = word; }
                }
                lines.Add(cur);
            }
            return (int)Math.Ceiling(lineH * lines.Count) + Ui.S(2);
        }
    }
}
