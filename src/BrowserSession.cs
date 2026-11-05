using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    internal sealed class BrowserSession
    {
        public Profile Profile { get; private set; }
        public BrowserInfo Browser { get; private set; }
        public int DebugPort { get; private set; }
        public string DataDir { get; set; }

        private Process _proc;
        private ProxyRelay _relay;
        private FileStream _dirLock;
        private CdpConnection _cdp;
        private int _closedRaised;
        private int _closing;
        private string _startUrl = "about:blank";

        /// <summary>Targets already configured, by sessionId, so a target is stamped exactly once.</summary>
        private readonly HashSet<string> _stamped = new HashSet<string>();
        private string _primarySession;
        private bool _navigated;

        public event Action<BrowserSession> Closed;

        public bool IsRunning
        {
            get
            {
                try { return _proc != null && !_proc.HasExited; }
                catch { return false; }
            }
        }

        public bool IsFirefox
        {
            get { return Browser != null && Browser.IsFirefox; }
        }

        public string ProxyEndpoint
        {
            get { return _relay == null ? "Direct" : _relay.Port.ToString(); }
        }

        /// <summary>
        /// Swaps in the edited profile. The relay reads its upstream through this reference,
        /// so a new proxy takes effect on the next request without touching the browser.
        /// </summary>
        public void UpdateProfile(Profile updated)
        {
            if (updated == null) return;
            Profile = updated;
            if (updated.Fp != null) FingerprintFactory.ResolveUserAgent(updated.Fp, Browser);
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static async Task<BrowserSession> LaunchAsync(Profile profile, string defaultStartUrl, string defaultBrowserPath)
        {
            var s = new BrowserSession { Profile = profile };

            string pref = profile.BrowserKind ?? "auto";
            string custom = "";
            if (pref == "custom") custom = profile.BrowserPath;
            else if (pref == "auto") custom = defaultBrowserPath;

            var browser = BrowserLocator.Detect(pref, custom);
            if (!browser.Found)
                throw new InvalidOperationException("No browser found.\r\n\r\nInstall Google Chrome, Microsoft Edge, Brave or Firefox, or set a browser path in Settings.");
            s.Browser = browser;

            var fp = profile.Fp ?? new FingerprintConfig();
            FingerprintFactory.ResolveUserAgent(fp, browser);

            string startUrl = profile.StartUrl;
            if (string.IsNullOrWhiteSpace(startUrl)) startUrl = defaultStartUrl;
            Uri parsed;
            if (string.IsNullOrWhiteSpace(startUrl) || !Uri.TryCreate(startUrl, UriKind.Absolute, out parsed))
                startUrl = "https://www.google.com";
            s._startUrl = startUrl;

            string dataDir = Path.Combine(AppPaths.Data, profile.Id);
            Directory.CreateDirectory(dataDir);
            s.DataDir = dataDir;

            /* Windows gives Chrome no profile lock of its own, so a second window pointed at the
               same directory corrupts it. An exclusive handle is released if the app crashes. */
            try
            {
                s._dirLock = new FileStream(Path.Combine(dataDir, "joe.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                throw new InvalidOperationException("This profile is already open in another window.\r\n\r\nClose it first.");
            }

            // The relay always runs, even with no proxy, so a proxy can be swapped later without
            // restarting the browser: the flag stays pointed at 127.0.0.1 and only the upstream moves.
            s._relay = new ProxyRelay(new Func<ProxyConfig>(delegate { return s.Profile != null ? s.Profile.Proxy : null; }));

            Log.Info("Launching " + browser.Name + " " + browser.Version + " for profile " + profile.Name);

            if (browser.IsFirefox)
            {
                s.LaunchFirefox(fp, dataDir, startUrl);
                return s;
            }

            int port = GetFreePort();
            s.DebugPort = port;

            int winW = Math.Min(fp.ScreenW > 0 ? fp.ScreenW - 120 : 1280, 1600);
            int winH = Math.Min(fp.ScreenH > 0 ? fp.ScreenH - 140 : 900, 1000);
            if (winW < 900) winW = 900;
            if (winH < 620) winH = 620;

            var args = new List<string>();
            args.Add("--user-data-dir=\"" + dataDir + "\"");
            args.Add("--remote-debugging-port=" + port);
            args.Add("--no-first-run");
            args.Add("--no-default-browser-check");
            args.Add("--hide-crash-restore-bubble");
            args.Add("--disable-session-crashed-bubble");
            args.Add("--disable-default-apps");
            args.Add("--disable-sync");
            args.Add("--disable-background-networking");
            args.Add("--metrics-recording-only");
            args.Add("--no-service-autorun");
            args.Add("--password-store=basic");
            args.Add("--use-mock-keychain");
            args.Add("--lang=" + (fp.Language ?? "en-US"));
            args.Add("--window-size=" + winW + "," + winH);
            args.Add("--window-position=80,60");
            args.Add("--proxy-server=http://127.0.0.1:" + s._relay.Port);
            args.Add("--proxy-bypass-list=localhost;127.0.0.1");
            // Chrome 137+ refuses a WebGL context entirely when there is no hardware GPU. Without
            // the software fallback a machine on a VM, RemoteApp or RDP session launches with
            // getContext('webgl') === null, which is a louder tell than any renderer string.
            args.Add("--enable-unsafe-swiftshader");
            if (!SystemProbe.HasHardwareGl())
            {
                args.Add("--use-gl=angle");
                args.Add("--use-angle=swiftshader");
                // The GPU blocklist still marks a software adapter as unusable and returns a null
                // context, which defeats the whole fallback.
                args.Add("--ignore-gpu-blocklist");
            }
            ApplyWebRtcFlags(args, fp);
            args.Add("about:blank");

            var psi = new ProcessStartInfo
            {
                FileName = browser.Path,
                Arguments = string.Join(" ", args.ToArray()),
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(browser.Path)
            };

            s._proc = Process.Start(psi);
            if (s._proc == null) throw new InvalidOperationException("Could not start the browser.");

            s._proc.EnableRaisingEvents = true;
            s._proc.Exited += delegate { s.Cleanup(); };

            var ready = Stopwatch.StartNew();
            string wsUrl = await WaitForEndpointAsync(port, s._proc, 30000).ConfigureAwait(false);
            if (wsUrl == null)
            {
                s.Cleanup();
                throw new InvalidOperationException("The browser started but its debugging endpoint was not reachable.\r\nTry updating Chrome or Edge.");
            }
            Log.Info("Browser debug endpoint ready in " + ready.ElapsedMilliseconds + " ms");

            s._cdp = await CdpConnection.ConnectAsync(wsUrl, 10000).ConfigureAwait(false);
            s._cdp.Closed += delegate { s.Cleanup(); };

            // Attach at the browser level so tabs, windows and popups opened later are stamped too;
            // a page target connection only ever covers the single tab that existed at launch.
            s._cdp.Event += s.OnCdpEvent;
            await s.TrySend("Target.setDiscoverTargets", new Dictionary<string, object> { { "discover", true } }, null).ConfigureAwait(false);
            await s.TrySend("Target.setAutoAttach", new Dictionary<string, object>
            {
                { "autoAttach", true },
                { "waitForDebuggerOnStart", false },
                { "flatten", true }
            }, null).ConfigureAwait(false);

            await s.AttachExistingTargetsAsync().ConfigureAwait(false);
            Log.Info("Fingerprint applied in " + ready.ElapsedMilliseconds + " ms; profile ready");
            return s;
        }

        private void OnCdpEvent(string method, Dictionary<string, object> args)
        {
            if (method == "Target.attachedToTarget")
            {
                string sessionId = Str(args, "sessionId");
                if (string.IsNullOrEmpty(sessionId)) return;
                var info = Get<Dictionary<string, object>>(args, "targetInfo");
                string type = info != null ? Str(info, "type") : "";
                if (type != "page") return;
                var ignored = StampSessionAsync(sessionId, null);
                return;
            }
            if (method == "Target.detachedFromTarget" || method == "Target.targetDestroyed")
            {
                string sessionId = Str(args, "sessionId");
                string targetId = Str(args, "targetId");
                lock (_stamped)
                {
                    if (!string.IsNullOrEmpty(sessionId)) _stamped.Remove(sessionId);
                    if (!string.IsNullOrEmpty(targetId)) _stamped.Remove(targetId);
                }
            }
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static T Get<T>(Dictionary<string, object> d, string key) where T : class
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as T : null;
        }

        /// <summary>Attaches to the tabs that were already open when the debugging port came up.</summary>
        private async Task AttachExistingTargetsAsync()
        {
            try
            {
                var res = await _cdp.SendAsync("Target.getTargets", null, 8000).ConfigureAwait(false);
                object raw;
                var arr = res != null && res.TryGetValue("targetInfos", out raw) ? raw as object[] : null;
                if (arr == null) return;
                foreach (object o in arr)
                {
                    var info = o as Dictionary<string, object>;
                    if (info == null) continue;
                    if (Str(info, "type") != "page") continue;
                    await AttachAndStampAsync(Str(info, "targetId")).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { Log.Error("Target enumeration failed", ex); }
        }

        private async Task AttachAndStampAsync(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            try
            {
                var res = await _cdp.SendAsync("Target.attachToTarget", new Dictionary<string, object>
                {
                    { "targetId", targetId },
                    { "flatten", true }
                }, 8000).ConfigureAwait(false);
                string sessionId = Str(res, "sessionId");
                if (!string.IsNullOrEmpty(sessionId)) await StampSessionAsync(sessionId, null).ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Error("Attaching to target failed", ex); }
        }

        /// <summary>
        /// Stamps one target with the profile. Safe to call repeatedly: a session is only
        /// configured once, so a popup Chrome reports twice does not get two script copies.
        /// The first target that gets stamped also loads the start page.
        /// </summary>
        private async Task StampSessionAsync(string sessionId, string navigateTo)
        {
            lock (_stamped)
            {
                if (_stamped.Contains(sessionId)) return;
                _stamped.Add(sessionId);
            }
            if (_primarySession == null) _primarySession = sessionId;
            if (!_navigated)
            {
                _navigated = true;
                navigateTo = _startUrl;
            }
            else
            {
                navigateTo = null;
            }
            var fp = Profile != null ? Profile.Fp : null;
            if (fp == null) return;
            try
            {
                FingerprintFactory.ResolveUserAgent(fp, Browser);
                await ApplyFingerprintAsync(fp, navigateTo, sessionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error("Fingerprint stamp failed", ex);
                lock (_stamped) { _stamped.Remove(sessionId); }
            }
        }

        private static void ApplyWebRtcFlags(List<string> args, FingerprintConfig fp)
        {
            string mode = fp.WebRtcMode ?? (fp.WebRtcProtect ? "proxy" : "off");
            // "Disabled" is enforced in the page: the injected script replaces RTCPeerConnection
            // outright, which a command-line policy cannot do.
            if (mode.Equals("off", StringComparison.OrdinalIgnoreCase)) return;
            string policy = mode.Equals("public", StringComparison.OrdinalIgnoreCase)
                ? "default_public_interface_only" : "disable_non_proxied_udp";
            args.Add("--force-webrtc-ip-handling-policy=" + policy);
            args.Add("--webrtc-ip-handling-policy=" + policy);
        }

        /// <summary>
        /// Firefox has no debugging pipe, so everything it can honour lives in prefs.js.
        /// Proxy, locale, language and user agent are covered; WebGL and timezone are not.
        /// </summary>
        private void LaunchFirefox(FingerprintConfig fp, string dataDir, string startUrl)
        {
            string firefoxDir = Path.Combine(dataDir, "firefox");
            WriteFirefoxPrefs(firefoxDir, fp);

            var args = new List<string>();
            args.Add("-no-remote");
            args.Add("-new-instance");
            args.Add("-profile \"" + firefoxDir + "\"");
            args.Add("\"" + startUrl + "\"");

            var psi = new ProcessStartInfo
            {
                FileName = Browser.Path,
                Arguments = string.Join(" ", args.ToArray()),
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(Browser.Path)
            };

            _proc = Process.Start(psi);
            if (_proc == null) throw new InvalidOperationException("Could not start Firefox.");
            _proc.EnableRaisingEvents = true;
            _proc.Exited += delegate { Cleanup(); };
        }

        private void WriteFirefoxPrefs(string dir, FingerprintConfig fp)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("user_pref(\"browser.startup.homepage\", \"" + Escape(startUrlSafe()) + "\");");
                sb.AppendLine("user_pref(\"browser.startup.page\", 1);");
                sb.AppendLine("user_pref(\"datareporting.healthreport.uploadEnabled\", false);");
                sb.AppendLine("user_pref(\"datareporting.policy.dataSubmissionEnabled\", false);");
                sb.AppendLine("user_pref(\"app.shield.optoutstudies.enabled\", false);");
                sb.AppendLine("user_pref(\"browser.aboutConfig.showWarning\", false);");
                sb.AppendLine("user_pref(\"signon.rememberSignons\", false);");
                // RFP would pin the clock to UTC and shrink the viewport, which fights the profile.
                sb.AppendLine("user_pref(\"privacy.resistFingerprinting\", false);");
                sb.AppendLine("user_pref(\"privacy.trackingprotection.enabled\", false);");
                sb.AppendLine("user_pref(\"network.proxy.type\", 1);");
                sb.AppendLine("user_pref(\"network.proxy.http\", \"127.0.0.1\");");
                sb.AppendLine("user_pref(\"network.proxy.http_port\", " + _relay.Port + ");");
                sb.AppendLine("user_pref(\"network.proxy.ssl\", \"127.0.0.1\");");
                sb.AppendLine("user_pref(\"network.proxy.ssl_port\", " + _relay.Port + ");");
                sb.AppendLine("user_pref(\"network.proxy.socks\", \"127.0.0.1\");");
                sb.AppendLine("user_pref(\"network.proxy.socks_port\", " + _relay.Port + ");");
                sb.AppendLine("user_pref(\"network.proxy.socks_version\", 5);");
                sb.AppendLine("user_pref(\"network.proxy.no_proxies_on\", \"localhost, 127.0.0.1\");");
                sb.AppendLine("user_pref(\"network.proxy.autoconfig_url\", \"\");");
                sb.AppendLine("user_pref(\"media.peerconnection.enabled\", " + (fp.WebRtcProtect ? "false" : "true") + ");");

                string lang = string.IsNullOrEmpty(fp.Language) ? "en-US" : fp.Language;
                sb.AppendLine("user_pref(\"intl.locale.requested\", \"" + Escape(lang) + "\");");
                sb.AppendLine("user_pref(\"intl.accept_languages\", \"" + Escape(fp.AcceptLanguage ?? lang) + "\");");

                if (!string.IsNullOrWhiteSpace(fp.UserAgent) && !IsSamePlatform(fp))
                    sb.AppendLine("user_pref(\"general.useragent.override\", \"" + Escape(fp.UserAgent) + "\");");

                File.WriteAllText(Path.Combine(dir, "prefs.js"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("Firefox prefs write failed", ex);
            }
        }

        private string startUrlSafe()
        {
            return string.IsNullOrEmpty(_startUrl) ? "about:blank" : _startUrl;
        }

        private static bool IsSamePlatform(FingerprintConfig fp)
        {
            return string.IsNullOrEmpty(fp.Os) || fp.Os.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Pushes the current profile into a live browser: the relay picks up the new upstream on
        /// the next connection and CDP restamps timezone, locale, headers and the noise script.
        /// </summary>
        public async Task<bool> ApplyLiveAsync()
        {
            if (!IsRunning) return false;
            var fp = Profile != null ? Profile.Fp : null;
            if (fp == null) return false;

            if (IsFirefox)
            {
                // prefs.js is read at start only, so a Firefox session needs a restart to change.
                return false;
            }
            if (_cdp == null) return false;

            string[] sessions;
            lock (_stamped) sessions = new List<string>(_stamped).ToArray();
            if (sessions.Length == 0 && !string.IsNullOrEmpty(_primarySession))
                sessions = new[] { _primarySession };
            foreach (string id in sessions)
                await ApplyFingerprintAsync(fp, null, id).ConfigureAwait(false);
            return true;
        }

        private async Task ApplyFingerprintAsync(FingerprintConfig fp, string navigateTo, string session)
        {
            string lang = string.IsNullOrEmpty(fp.Language) ? "en-US" : fp.Language;
            string baseLang = lang.Split('-')[0];

            // Legacy profiles can store an Accept-Language that contradicts their locale, which is
            // an easy mismatch for a fingerprint script to spot. Keep the two coherent at launch.
            if (string.IsNullOrWhiteSpace(fp.AcceptLanguage) ||
                fp.AcceptLanguage.IndexOf(lang, StringComparison.OrdinalIgnoreCase) < 0)
                fp.AcceptLanguage = FingerprintFactory.AcceptLanguageFor(lang);
            string acceptLanguage = fp.AcceptLanguage;

            await TrySend("Page.enable", null, session);
            await TrySend("Network.enable", null, session);

            if (!string.IsNullOrEmpty(fp.Timezone))
            {
                await TrySend("Emulation.setTimezoneOverride", new Dictionary<string, object> { { "timezoneId", fp.Timezone } }, session);
            }
            if (!string.IsNullOrEmpty(lang))
            {
                await TrySend("Emulation.setLocaleOverride", new Dictionary<string, object> { { "locale", lang } }, session);
                var headers = new Dictionary<string, object>
                {
                    { "Accept-Language", acceptLanguage }
                };
                await TrySend("Network.setExtraHTTPHeaders", new Dictionary<string, object> { { "headers", headers } }, session);
            }

            if (fp.GeoMode != null && !fp.GeoMode.Equals("proxy", StringComparison.OrdinalIgnoreCase))
            {
                if (fp.GeoMode.Equals("block", StringComparison.OrdinalIgnoreCase))
                    await TrySend("Emulation.setGeolocationOverride", new Dictionary<string, object> { { "latitude", null }, { "longitude", null }, { "accuracy", 1 } }, session);
                else if (fp.GeoMode.Equals("custom", StringComparison.OrdinalIgnoreCase))
                {
                    double lat, lng;
                    if (double.TryParse(fp.GeoLat, NumberStyles.Any, CultureInfo.InvariantCulture, out lat) &&
                        double.TryParse(fp.GeoLng, NumberStyles.Any, CultureInfo.InvariantCulture, out lng))
                    {
                        await TrySend("Emulation.setGeolocationOverride", new Dictionary<string, object>
                        {
                            { "latitude", lat }, { "longitude", lng }, { "accuracy", 20 }
                        }, session);
                    }
                }
            }

            bool mobile = fp.DeviceType != null && fp.DeviceType != "desktop";
            if (!string.IsNullOrEmpty(fp.UserAgent))
            {
                // The header has to move with the scripted navigator even when the profile claims
                // this machine's own platform: a matching JS UA next to a real header UA is a
                // contradiction any remote script can read in one request.
                // Real iOS browsers expose no UA-Client-Hints at all, so the override carries none.
                object meta = FingerprintFactory.IsIos(fp.Os) ? null : (object)await BuildUaMetadataAsync(fp, session).ConfigureAwait(false);
                var overrideParams = new Dictionary<string, object>
                {
                    { "userAgent", fp.UserAgent },
                    { "platform", fp.Platform },
                    { "acceptLanguage", acceptLanguage }
                };
                if (meta != null) overrideParams["userAgentMetadata"] = meta;
                await TrySend("Network.setUserAgentOverride", overrideParams, session);
            }

            if (mobile)
            {
                await TrySend("Emulation.setDeviceMetricsOverride", new Dictionary<string, object>
                {
                    { "width", fp.ScreenW > 0 ? fp.ScreenW : 390 },
                    { "height", fp.ScreenH > 0 ? fp.ScreenH : 844 },
                    { "deviceScaleFactor", fp.PixelRatio > 0 ? fp.PixelRatio : 3 },
                    { "mobile", true }
                }, session);
                await TrySend("Emulation.setTouchEmulationEnabled", new Dictionary<string, object>
                {
                    { "enabled", true },
                    { "maxTouchPoints", fp.TouchPoints > 0 ? fp.TouchPoints : 5 }
                }, session);
            }
            else
            {
                await TrySend("Emulation.clearDeviceMetricsOverride", null, session);
                await TrySend("Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { { "enabled", false } }, session);
            }

            string[] languages = lang.IndexOf('-') > 0
                ? new[] { lang, baseLang }
                : new[] { lang };

            string script = FingerprintScript.Build(fp, fp.UserAgent, fp.Platform, lang, languages);
            await TrySend("Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object>
            {
                { "source", script },
                { "runImmediately", true }
            }, session);

            if (!string.IsNullOrEmpty(navigateTo))
                await TrySend("Page.navigate", new Dictionary<string, object> { { "url", navigateTo } }, session);
        }

        /// <summary>
        /// Client hints read from the running browser, so the GREASE brand entry and the build
        /// numbers are the ones Chrome actually emits rather than guesses. Null when unavailable.
        /// </summary>
        private async Task<Dictionary<string, object>> ReadRealClientHintsAsync(string session)
        {
            if (string.IsNullOrEmpty(session) || _cdp == null) return null;
            const string expr =
                "(function(){var u=navigator.userAgentData;if(!u)return null;" +
                "return u.getHighEntropyValues(['fullVersionList','platform','platformVersion','architecture','bitness','model','mobile','wow64'])" +
                ".then(function(h){var o={};for(var k in h)o[k]=h[k];o.brands=u.brands;return JSON.stringify(o);},function(){return null;});})()";
            try
            {
                var res = await _cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object>
                {
                    { "expression", expr },
                    { "returnByValue", true },
                    { "awaitPromise", true }
                }, session, 6000).ConfigureAwait(false);
                object inner;
                var d = res != null && res.TryGetValue("result", out inner) ? inner as Dictionary<string, object> : null;
                object val;
                string json = d != null && d.TryGetValue("value", out val) && val != null ? Convert.ToString(val) : null;
                if (string.IsNullOrEmpty(json)) return null;
                return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                Log.Error("Reading client hints failed", ex);
                return null;
            }
        }

        private async Task<Dictionary<string, object>> BuildUaMetadataAsync(FingerprintConfig fp, string session)
        {
            return BuildUaMetadata(fp, await ReadRealClientHintsAsync(session).ConfigureAwait(false));
        }

        private static Dictionary<string, object> BuildUaMetadata(FingerprintConfig fp, Dictionary<string, object> real)
        {
            int major = 131;
            string full = null;
            string ua = fp.UserAgent ?? "";
            int ci = ua.IndexOf("Chrome/", StringComparison.Ordinal);
            if (ci >= 0)
            {
                string rest = ua.Substring(ci + 7);
                int sp = rest.IndexOf(' ');
                if (sp > 0) full = rest.Substring(0, sp);
                int dot = rest.IndexOf('.');
                int parsed;
                if (dot > 0 && int.TryParse(rest.Substring(0, dot), out parsed)) major = parsed;
            }
            if (string.IsNullOrEmpty(full)) full = major + ".0.0.0";

            bool edge = (fp.Brand ?? "").Equals("Edge", StringComparison.OrdinalIgnoreCase);
            bool android = FingerprintFactory.IsAndroid(fp.Os);
            string brandName = edge ? "Microsoft Edge" : "Google Chrome";

            // The GREASE brand ("Not_A Brand", "Not;A=Brand", …) and its version rotate with the
            // Chromium release, so copying the live value beats hard-coding a stale one.
            string greaseBrand = null, greaseVersion = null;
            var realFull = new List<Dictionary<string, object>>();
            if (real != null)
            {
                object rawBrands;
                var brands = real.TryGetValue("brands", out rawBrands) ? rawBrands as object[] : null;
                if (brands != null)
                {
                    foreach (object o in brands)
                    {
                        var b = o as Dictionary<string, object>;
                        if (b == null) continue;
                        string name = Str(b, "brand");
                        if (name.IndexOf("Chromium", StringComparison.Ordinal) >= 0 ||
                            name.IndexOf("Chrome", StringComparison.Ordinal) >= 0 ||
                            name.IndexOf("Edge", StringComparison.Ordinal) >= 0) continue;
                        greaseBrand = name;
                        greaseVersion = Str(b, "version");
                    }
                }
                object rawFull;
                var fullList = real.TryGetValue("fullVersionList", out rawFull) ? rawFull as object[] : null;
                if (fullList != null)
                    foreach (object o in fullList)
                    {
                        var b = o as Dictionary<string, object>;
                        if (b == null) continue;
                        realFull.Add(new Dictionary<string, object> { { "brand", Str(b, "brand") }, { "version", Str(b, "version") } });
                    }
            }

            var reduced = new List<object>();
            var fullListOut = new List<object>();
            if (greaseBrand != null)
            {
                reduced.Add(new Dictionary<string, object> { { "brand", greaseBrand }, { "version", greaseVersion ?? "8" } });
                foreach (var b in realFull)
                    if (Str(b, "brand") == greaseBrand) fullListOut.Add(b);
            }
            else
            {
                reduced.Add(new Dictionary<string, object> { { "brand", "Not_A Brand" }, { "version", "8" } });
            }
            reduced.Add(new Dictionary<string, object> { { "brand", "Chromium" }, { "version", major.ToString() } });
            reduced.Add(new Dictionary<string, object> { { "brand", brandName }, { "version", major.ToString() } });

            if (fullListOut.Count == 0)
            {
                if (greaseBrand != null)
                    fullListOut.Add(new Dictionary<string, object> { { "brand", greaseBrand }, { "version", greaseVersion ?? "8" } });
                fullListOut.Add(new Dictionary<string, object> { { "brand", "Chromium" }, { "version", full } });
                fullListOut.Add(new Dictionary<string, object> { { "brand", brandName }, { "version", full } });
            }

            string platform = FingerprintFactory.IsMac(fp.Os) ? "macOS"
                : (FingerprintFactory.IsLinux(fp.Os) ? "Linux" : (android ? "Android" : "Windows"));

            // Only keep the live platform fields when the profile claims this machine's OS;
            // otherwise fall back to values that are plausible for the target.
            string realPlatform = real != null ? Str(real, "platform") : null;
            bool samePlatform = string.Equals(realPlatform, platform, StringComparison.OrdinalIgnoreCase);

            string platformVersion = samePlatform ? Str(real, "platformVersion") : "";
            if (string.IsNullOrEmpty(platformVersion))
            {
                if (platform == "macOS") platformVersion = "14.5.0";
                else if (android) platformVersion = "14.0.0";
                else if (platform == "Linux") platformVersion = "6.5.0";
                else platformVersion = "10.0.0";
            }

            string architecture = samePlatform ? Str(real, "architecture") : (android ? "arm" : "x86");
            if (string.IsNullOrEmpty(architecture)) architecture = android ? "arm" : "x86";
            string bitness = samePlatform ? Str(real, "bitness") : (android ? "" : "64");
            if (string.IsNullOrEmpty(bitness)) bitness = android ? "" : "64";
            string model = samePlatform ? Str(real, "model") : "";
            if (string.IsNullOrEmpty(model) && android)
                model = (fp.DeviceType ?? "").Equals("tablet", StringComparison.OrdinalIgnoreCase) ? "SM-X710" : "SM-S918B";

            object wow64 = samePlatform && real != null && HasTrue(real, "wow64");

            return new Dictionary<string, object>
            {
                { "brands", reduced },
                { "fullVersionList", fullListOut },
                { "fullVersion", full },
                { "platform", platform },
                { "platformVersion", platformVersion },
                { "architecture", architecture },
                { "bitness", bitness },
                { "model", model ?? "" },
                { "mobile", fp.DeviceType != null && fp.DeviceType != "desktop" },
                { "wow64", wow64 }
            };
        }

        private static bool HasTrue(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            bool b;
            return bool.TryParse(Convert.ToString(v), out b) && b;
        }

        private async Task TrySend(string method, Dictionary<string, object> parameters, string session)
        {
            try { await _cdp.SendAsync(method, parameters, session, 15000).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error("CDP " + method + " failed", ex); }
        }

        private static async Task<string> WaitForEndpointAsync(int port, Process proc, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (proc != null)
                {
                    try { if (proc.HasExited) return null; } catch { }
                }
                try
                {
                    string json = HttpGet("http://127.0.0.1:" + port + "/json/version", 1500);
                    if (json != null)
                    {
                        var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                        string ws = d != null ? Str(d, "webSocketDebuggerUrl") : null;
                        if (!string.IsNullOrEmpty(ws)) return ws;
                    }
                }
                catch { }
                await Task.Delay(300).ConfigureAwait(false);
            }
            return null;
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Proxy = null;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.KeepAlive = false;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// Reserves a loopback port. The window between releasing it and Chrome binding it is
        /// unavoidable, so the launch side retries; what is avoided here is handing back a port
        /// something is already listening on.
        /// </summary>
        private static int GetFreePort()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int port;
                var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                try { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; }
                catch { continue; }
                finally { l.Stop(); }
                if (port <= 0) continue;
                if (!IsListening(port)) return port;
            }
            var again = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            again.Start();
            int fallback = ((IPEndPoint)again.LocalEndpoint).Port;
            again.Stop();
            return fallback;
        }

        private static bool IsListening(int port)
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var ar = c.BeginConnect(IPAddress.Loopback, port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(150);
                    if (ok) c.EndConnect(ar);
                    return ok;
                }
            }
            catch { return false; }
        }

        public async Task<string> EvaluateAsync(string expression)
        {
            if (_cdp == null) return null;
            var result = await _cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", expression },
                { "returnByValue", true },
                { "awaitPromise", true }
            }, _primarySession, 15000).ConfigureAwait(false);
            object res;
            if (result != null && result.TryGetValue("result", out res))
            {
                var d = res as Dictionary<string, object>;
                object v;
                if (d != null && d.TryGetValue("value", out v) && v != null) return Convert.ToString(v);
            }
            return null;
        }

        public void Focus()
        {            try
            {
                if (_proc == null) return;
                _proc.Refresh();
                IntPtr h = _proc.MainWindowHandle;
                if (h == IntPtr.Zero) return;
                ShowWindow(h, 9);
                SetForegroundWindow(h);
            }
            catch { }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closing, 1) == 1) return;
            var cdp = _cdp;
            var proc = _proc;

            // Shutdown runs away from the caller: waiting on the browser to exit here froze the
            // profile grid for up to five seconds whenever a tab had an open beforeunload prompt.
            var ignored = Task.Run(async delegate
            {
                try
                {
                    if (cdp != null) await cdp.SendAsync("Browser.close", null, null, 2000).ConfigureAwait(false);
                }
                catch { }
                try
                {
                    if (proc != null)
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            try { if (proc.HasExited) break; } catch { break; }
                            await Task.Delay(250).ConfigureAwait(false);
                        }
                        try { if (!proc.HasExited) proc.Kill(); } catch { }
                    }
                }
                catch { }
                Cleanup();
            });
        }

        private void Cleanup()
        {
            if (Interlocked.Exchange(ref _closedRaised, 1) == 1) return;
            try { if (_cdp != null) _cdp.Dispose(); } catch { }
            try { if (_relay != null) _relay.Dispose(); } catch { }
            try { if (_proc != null) _proc.Dispose(); } catch { }
            try { if (_dirLock != null) { _dirLock.Close(); _dirLock = null; } } catch { }
            var handler = Closed;
            if (handler != null) { try { handler(this); } catch { } }
        }
    }
}
