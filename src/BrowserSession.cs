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
using System.Linq;

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
        /// <summary>Worker targets already stamped. Kept apart: a worker has no Page domain.</summary>
        private readonly HashSet<string> _workerStamped = new HashSet<string>();
        /// <summary>Installed browser build, e.g. "156.0.4314.58". The UA string cannot carry it.</summary>
        private string _browserFullVersion;
        private bool _browserIsBrave;
        private bool _browserIsEdge;
        /// <summary>False on the engines that refuse extensions (Edge branded builds, Firefox):
        /// the CDP tool injection is skipped too, so the app never pretends a tool is running
        /// where the engine will not carry one.</summary>
        private bool _toolsAllowed = true;
        /// <summary>The Settings master switch for extensions. The command line used to honour
        /// it while the injected tool script ignored it, which made the switch a half-truth.</summary>
        private bool _masterExtensions = true;
        private string _primarySession;
        private int _navCount;
        private readonly object _scriptState = new object();
        /// <summary>Identifiers of injected documents, so a live re-apply replaces instead of stacking.</summary>
        private readonly Dictionary<string, string> _fpScripts = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _toolScripts = new Dictionary<string, string>();
        /// <summary>Chromium refuses a second locale override; remembered so we can clear it first.</summary>
        private readonly Dictionary<string, string> _locales = new Dictionary<string, string>();
        private bool _extensionsLoaded;

        public event Action<BrowserSession> Closed;

        /// <summary>Fires when the configured proxy stopped answering and the session moved
        /// itself onto a direct connection. The argument is the proxy that failed.</summary>
        public event Action<string> ProxyFallback;

        public bool ProxyFellBack { get; private set; }

        /// <summary>True when this window's traffic goes through a relay that can be re-pointed
        /// at a different upstream without closing the window. A profile that opened with no
        /// proxy has none, and Chrome cannot be handed one after it starts: its proxy comes from
        /// the command line it was launched with.</summary>
        public bool HasRelay { get { return _relay != null; } }

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

        public static async Task<BrowserSession> LaunchAsync(Profile profile, string defaultStartUrl, string defaultBrowserPath, AppSettings settings = null)
        {
            var s = new BrowserSession { Profile = profile };

            string pref = profile.BrowserKind ?? "auto";
            string custom = "";
            if (pref == "custom") custom = profile.BrowserPath;
            else if (pref == "auto") custom = defaultBrowserPath;

            /* A profile can be pinned to an exact build, the way AdsPower hands out kernels.
               The pinned executable wins over whatever happens to be installed; if the build
               was removed the profile falls back to the normal lookup rather than refusing
               to open. */
            BrowserInfo browser = null;
            string pinnedExe = null;
            if (!string.IsNullOrEmpty(profile.KernelEngine) && !string.IsNullOrEmpty(profile.KernelVersion))
            {
                string kernelExe = KernelStore.ResolveExe(profile.KernelEngine, profile.KernelVersion);
                if (kernelExe != null)
                {
                    browser = BrowserLocator.FromPath(kernelExe, profile.KernelEngine);
                    pinnedExe = kernelExe;
                    Log.Info("Using pinned kernel " + profile.KernelEngine + " " + profile.KernelVersion);
                }
                else
                {
                    Log.Info("Pinned kernel " + profile.KernelEngine + " " + profile.KernelVersion +
                             " is not installed; using the installed browser");
                }
            }
            if (browser == null || !browser.Found)
                browser = BrowserLocator.Detect(pref, custom);
            if (!browser.Found)
                throw new InvalidOperationException("No browser found.\r\n\r\nInstall Google Chrome, Microsoft Edge, Brave or Firefox, or set a browser path in Settings.");
            s.Browser = browser;
            s._browserFullVersion = browser.Version;
            s._browserIsBrave = browser.IsBrave;
            s._browserIsEdge = browser.IsEdge;

            var fp = profile.Fp ?? new FingerprintConfig();
            /* Last line of defence before the script is injected. A profile edited by hand, or
               restored from a file written by an older build, can still describe an impossible
               machine; coercing here means the browser never launches with one, whatever state the
               store was in. Coherence only: the seed and the canvas payload are untouched. */
            try
            {
                int repaired = FingerprintFactory.Coerce(fp);
                if (repaired > 0)
                    Log.Info("Repaired " + repaired + " incoherent fingerprint field(s) for " + profile.Name);
            }
            catch (Exception ex) { Log.Error("Fingerprint coherence check failed", ex); }
            FingerprintFactory.ResolveUserAgent(fp, browser);

            string startUrl = profile.StartUrl;
            if (string.IsNullOrWhiteSpace(startUrl)) startUrl = defaultStartUrl;
            if (string.IsNullOrWhiteSpace(startUrl)) startUrl = Profile.FingerprintTestUrl;
            /* A stored address that is no longer a URL must not become a Google tab: the profile
               asks for one thing and gets another, which is worse than falling back to the
               default everybody else gets. */
            Uri parsed;
            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out parsed))
                startUrl = Profile.FingerprintTestUrl;
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

            /* The relay is only needed when a proxy is actually configured. Routing a direct
               connection through it adds a local hop to every request - measurably slow on
               busy pages - and buys nothing. When a proxy is set the relay always runs, so the
               upstream can be swapped later without restarting the browser: the flag stays
               pointed at 127.0.0.1 and only the upstream moves. */
            bool proxyConfigured = profile.Proxy != null && profile.Proxy.Enabled;

            /* A profile is edited long before it is launched, and the proxy's country can be
               learned in between (or the proxy replaced entirely). Re-deriving the locale from
               the proxy's last known geography here is what keeps the clock, the language and
               the exit IP in the same place. Without it a New York proxy could open with a
               London clock - a one-line giveaway for any detector. */
            if (proxyConfigured)
                FingerprintFactory.ApplyProxyLocale(profile.Fp, profile.Proxy);

            if (proxyConfigured)
                s._relay = new ProxyRelay(new Func<ProxyConfig>(delegate { return s.Profile != null ? s.Profile.Proxy : null; }));

            if (s._relay != null)
            {
                s._relay.Fallback += delegate(string endpoint)
                {
                    s.ProxyFellBack = true;
                    var h = s.ProxyFallback;
                    if (h != null) { try { h(endpoint); } catch { } }
                };
            }

            Log.Info("Launching " + browser.Name + " " + browser.Version + " for profile " + profile.Name);

            if (browser.IsFirefox)
            {
                s.LaunchFirefox(fp, dataDir, startUrl);
                return s;
            }

            int port = GetFreePort();
            s.DebugPort = port;

            int winW, winH;
            int viewH = FingerprintFactory.MobileViewHeight(fp);
            /* Where the window lands matters as much as its size: a 393 px phone window pinned at
               x=80 reads as "pushed to the left of the screen", which is exactly what a phone
               profile looked like. Mobile windows are centred in the work area; desktop windows
               keep the old top-left offset, where a second window is expected to cascade. */
            int posX = 80, posY = 60;
            if (viewH > 0)
            {
                // A phone profile has to open like a phone. A desktop-sized window next to a
                // 393x852 emulation leaves a dead gutter and contradicts screen.width.
                var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                /* Emulation.setDeviceMetricsOverride scales the emulated viewport to fit the
                   window and pins it to the TOP LEFT when the two do not have the same shape.
                   A tall device (932 px plus chrome) does not fit a 1080 px screen, and the old
                   code clamped width and height independently - which broke the ratio and drew
                   the phone screen in the corner of a larger window with dead space beside it.
                   Clamping both by the same factor keeps the window's content area the same
                   shape as the device screen, so the viewport fills the window exactly. */
                double vw = Math.Max(320, fp.ScreenW > 0 ? fp.ScreenW : 393);
                double aspect = vw / Math.Max(1, viewH);
                const int chromeH = 128;
                double wantW = vw;
                double wantH = wantW / aspect + chromeH;
                double maxW = Math.Max(320, wa.Width - 80);
                double maxH = Math.Max(480, wa.Height - 40);
                if (wantW > maxW || wantH > maxH)
                {
                    double scale = Math.Min(maxW / wantW, maxH / wantH);
                    wantW *= scale;
                    wantH *= scale;
                }
                winW = (int)Math.Round(wantW);
                winH = (int)Math.Round(wantH);
                posX = wa.Left + Math.Max(0, (wa.Width - winW) / 2);
                posY = wa.Top + Math.Max(0, (wa.Height - winH) / 2);
            }
            else
            {
                winW = Math.Min(fp.ScreenW > 0 ? fp.ScreenW - 120 : 1280, 1600);
                winH = Math.Min(fp.ScreenH > 0 ? fp.ScreenH - 140 : 900, 1000);
                if (winW < 900) winW = 900;
                if (winH < 620) winH = 620;
            }

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
            /* Sets the Accept-Language header in the network service, with no CDP domain
               enabled. Previously the header was stamped through Network.setExtraHTTPHeaders,
               which required Network.enable - the single clearest indication to a page that the
               browser is under remote control. */
            if (!string.IsNullOrEmpty(fp.AcceptLanguage)) args.Add("--accept-lang=\"" + fp.AcceptLanguage + "\"");
            args.Add("--window-size=" + winW + "," + winH);
            /* Chrome features this app turns off everywhere. DnsOverHttps so every name is
               resolved at the proxy rather than by a public resolver the browser chose itself,
               which is exactly what a DNS-leak page reports; InsecureDownloadWarnings and
               TriggeredPopupDefault because the infobars they draw are visible leaks that
               --test-type used to hide at the cost of putting the word "test" on every child
               process command line. */
            var disableFeatures = new List<string> { "DnsOverHttps", "InsecureDownloadWarnings", "TriggeredPopupDefault" };
            args.Add("--window-position=" + posX + "," + posY);
            /* Only point the browser at the local relay when an upstream proxy is actually
               configured. Routing a direct connection through the relay adds a local hop to
               every single request (measured: 14-25 s per resource on busy pages) and buys
               nothing. When a proxy IS set the relay always runs, so the upstream can be
               swapped later without restarting the browser: the flag stays pointed at
               127.0.0.1 and only the upstream moves. */
            bool proxyOn = profile.Proxy != null && profile.Proxy.Enabled;
            if (proxyOn)
            {
                args.Add("--proxy-server=http://127.0.0.1:" + s._relay.Port);
                args.Add("--proxy-bypass-list=localhost;127.0.0.1");
            }
            /* DNS is the classic proxy leak: Chrome must not resolve a hostname locally for a
               speculative connection or prefetch when the request itself is proxied. Prefetch is
               disabled below, and both supported upstreams receive the hostname unresolved
               (SOCKS5 ATYP=domain / HTTP CONNECT), so the lookup happens at the proxy.
               host-resolver-rules makes that a hard guarantee: with every name mapped to
               ~NOTFOUND, no code path inside Chromium can fall back to the local resolver, and
               the relay is reached by IP so it still resolves. */
            if (proxyOn) args.Add("--host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE 127.0.0.1\"");
            args.Add("--dns-prefetch-disable");
            /* QUIC is UDP and is never carried by an HTTP proxy; disabling it keeps the whole
               session on the proxied TCP path instead of a parallel direct connection. */
            if (proxyOn) args.Add("--disable-quic");
            /* Chromium shows a yellow "unsupported command-line flag" infobar for some of the
               switches above (host-resolver-rules among them). That bar is itself a detection
               signal, and a screenshot-visible one, so the infobars are suppressed - but NOT
               with --test-type. That switch is a lie with a cost: its name sits on the command
               line of every child process, and "test" in an argument list is read by bot checks
               the way a watermark is read. The feature flags in disableFeatures remove the same
               bars without putting a word like that anywhere a page can find it. */
            /* A portable build cannot start its sandbox in an elevated (administrator) session:
               the sandboxed child processes need a restricted token that an elevated parent is
               not allowed to create. Chromium then logs "Sandbox cannot access executable",
               the network service crashes and restarts, and page loads become slow and flaky.
               Measured on a pinned Chrome for Testing build as well, not just the bundled
               Chromium: an installed Chrome lives under Program Files and gets away with it,
               a copy in the user's AppData does not. */
            if (SystemProbe.IsElevated() && (browser.EngineKey == "chromium" || pinnedExe != null))
            {
                args.Add("--no-sandbox");
                Log.Info("Elevated session detected: sandbox disabled for the portable build.");
            }

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

            /* Original unpacked extensions. Chrome 137+ hides --load-extension behind a
               feature flag on branded builds, so disable that flag and always inject the
               same first-party tools through CDP as a guaranteed fallback.

               Store extensions only behave on Chrome, Brave and Chromium: Microsoft Edge and
               branded Chrome both drop the switch, Firefox is a different engine entirely,
               and the owner's requirement is explicit - the shipped pair and the tools are
               a Chrome/Brave/Chromium feature. On Edge and Firefox the folders are skipped
               here and the CDP tool script is skipped below, so the app never claims a
               capability the engine will not honour. */
            bool engineTakesExtensions = string.Equals(browser.EngineKey, "chrome", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(browser.EngineKey, "brave", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(browser.EngineKey, "chromium", StringComparison.OrdinalIgnoreCase);
            s._toolsAllowed = engineTakesExtensions;
            s._masterExtensions = settings == null || settings.EnableExtensions;
            bool masterExtensions = s._masterExtensions && engineTakesExtensions;
            bool customExtensions = settings == null || settings.EnableCustomExtensions;
            var folders = !engineTakesExtensions || DiagnosticNoExtensions
                ? new List<string>()
                : ExtensionManager.EnabledFolders(profile.Tools, masterExtensions, customExtensions, proxyOn);
            if (folders.Count > 0)
            {
                /* Every folder is on the command line twice, once for each switch. Duplicates
                   across the built-in tools and the user's library make Chromium drop the whole
                   list, and a command line past 32k characters is not passed at all; keep the
                   list unique and inside the limit rather than losing it silently. */
                var unique = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string folder in folders)
                {
                    if (string.IsNullOrEmpty(folder)) continue;
                    if (seen.Add(folder.TrimEnd('\\'))) unique.Add(folder);
                }
                var paths = new StringBuilder();
                int dropped = 0;
                for (int i = 0; i < unique.Count; i++)
                {
                    string part = (paths.Length > 0 ? "," : "") + unique[i].Replace("\\", "/");
                    // Two switches of the same length, so the budget is spent at twice the size.
                    if ((paths.Length + part.Length) * 2 > 28000) { dropped = unique.Count - i; break; }
                    paths.Append(part);
                }
                if (dropped > 0)
                    Log.Info("Left out " + dropped + " extensions: the command line would be too long.");
                if (paths.Length > 0)
                {
                    s._extensionsLoaded = true;
                    disableFeatures.Add("DisableLoadExtensionCommandLineSwitch");
                    args.Add("--disable-extensions-except=\"" + paths + "\"");
                    args.Add("--load-extension=\"" + paths + "\"");
                }
            }
            if (disableFeatures.Count > 0)
                args.Add("--disable-features=" + string.Join(",", disableFeatures.ToArray()));
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

        /// <summary>
        /// Optional sink for raw CDP events. The diagnostic harness uses it to see console
        /// errors and failed requests; production code never sets it.
        /// </summary>
        /// <summary>
        /// Turns on the Runtime domain for the live tab so console output and exceptions are
        /// reported to the CDP event sink. Used by the diagnostic harness only.
        /// </summary>
        public async Task EnableDiagnosticsAsync()
        {
            if (_cdp == null) return;
            await TrySend("Runtime.enable", null, _primarySession).ConfigureAwait(false);
        }

        public Action<string, Dictionary<string, object>> DebugEvents;

        /// <summary>Diagnostic only: launch without any extension loaded.</summary>
        public static bool DiagnosticNoExtensions;

        /// <summary>Diagnostic only: launch without the injected fingerprint script.</summary>
        public static bool DiagnosticNoInjection;

        private void OnCdpEvent(string method, Dictionary<string, object> args)
        {
            var dbg = DebugEvents;
            if (dbg != null)
            {
                try { dbg(method, args ?? new Dictionary<string, object>()); } catch { }
            }
            if (method == "Target.attachedToTarget")
            {
                string sessionId = Str(args, "sessionId");
                if (string.IsNullOrEmpty(sessionId)) return;
                var info = Get<Dictionary<string, object>>(args, "targetInfo");
                string type = info != null ? Str(info, "type") : "";
                if (type == "worker" || type == "service_worker" || type == "shared_worker")
                {
                    string wurl = info != null ? Str(info, "url") : "";
                    /* Extension and devtools workers are not part of the page's fingerprint
                       surface, and a background extension worker does not answer Runtime
                       commands at all - attaching to them only burns the timeout. */
                    if (wurl.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                        wurl.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase))
                        return;
                    /* A worker has its own realm and never runs Page.addScriptToEvaluateOnNewDocument.
                       Leaving it unstamped meant navigator/canvas/WebGL read back the real values
                       there, which is a direct main-thread-vs-worker mismatch any fingerprint
                       library can spot with one postMessage. */
                    var worker = StampWorkerAsync(sessionId);
                    Log.Info("Worker target attached: " + wurl + " [" + sessionId + "]");
                    return;
                }
                if (type != "page") return;
                Log.Info("Page target attached: " + (info != null ? Str(info, "url") : "") + " [" + sessionId + "]");
                var ignored = StampSessionAsync(sessionId, null, info != null ? Str(info, "url") : "");
                return;
            }
            /* ---- Browsing history ----
               FrameNavigated is the page's own statement that it now shows a different document,
               and it fires for every hop in a redirect chain, not only for the URL that ends up
               in the address bar. FrameStartedNavigating fires before the request, which is the
               only way to see a hop that is answered from cache or aborted mid-chain. Neither
               is a request-level event, so no sub-resource is recorded: the unit is "the page
               became this document". */
            if (method == "Page.frameNavigated" || method == "Page.frameStartedNavigating")
            {
                /* The two methods describe the frame differently: frameNavigated nests it under
                   "frame" with the url and the type, while frameStartedNavigating puts url,
                   loaderId and navigationType at the top level next to frameId. Reading only the
                   nested shape meant the earlier event recorded an empty url on every real
                   navigation, which the store then discarded as uninteresting - so the hop that
                   only that event can see would never have been kept. */
                var frame = Get<Dictionary<string, object>>(args, "frame");
                bool started = method == "Page.frameStartedNavigating";
                bool top = started ? string.IsNullOrEmpty(Str(args, "parentId"))
                    : (frame != null && string.IsNullOrEmpty(Str(frame, "parentId")));
                if (!top) return;
                string url = !started && frame != null ? Str(frame, "url") : Str(args, "url");
                if (string.IsNullOrEmpty(url))
                {
                    var nested = frame != null ? Str(frame, "url") : "";
                    url = nested;
                }
                string navType = started ? Str(args, "navigationType") : (frame != null ? Str(frame, "type") : "");
                /* Back and forward are the user asking to see something they already saw; a
                   reload is the same document again. Recording those would triple every entry
                   the first time the user moves through their own history. */
                if (navType.Equals("BackForwardCacheRestore", StringComparison.OrdinalIgnoreCase)) return;
                if (method == "Page.frameNavigated" && navType.Equals("SameDocument", StringComparison.OrdinalIgnoreCase)) return;
                HistoryStore.Add(Profile != null ? Profile.Id : null,
                    Profile != null ? Profile.Name : null,
                    url, "", started);
                return;
            }
            if (method == "Page.titleUpdated" || method == "Page.frameNavigated")
            {
                /* Best effort: the title usually arrives after the document starts, so the
                   history entry is rewritten with it rather than waiting to record the visit. */
                string t = Str(args, "title");
                if (!string.IsNullOrEmpty(t) && Profile != null) HistoryStore.SetLastTitle(Profile.Id, t);
            }
            if (method == "Page.navigatedWithinDocument")
            {
                /* A single-page app changes the document it "is" without a navigation event. */
                string frag = Str(args, "url");
                if (Profile != null) HistoryStore.Add(Profile.Id, Profile.Name, frag, "", true);
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
                if (!string.IsNullOrEmpty(sessionId))
                {
                    lock (_scriptState)
                    {
                        _fpScripts.Remove(sessionId);
                        _toolScripts.Remove(sessionId);
                        _locales.Remove(sessionId);
                    }
                }
            }
        }

        /// <summary>
        /// Applies the profile to a worker realm. Workers expose no Page domain, so the same
        /// script is evaluated once in the worker's execution context instead.
        /// </summary>
        private async Task StampWorkerAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || _cdp == null) return;
            lock (_workerStamped)
            {
                if (_workerStamped.Contains(sessionId)) return;
                _workerStamped.Add(sessionId);
            }
            var fp = Profile != null ? Profile.Fp : null;
            if (fp == null) return;
            try
            {
                string lang = string.IsNullOrEmpty(fp.Language) ? "en-US" : fp.Language;
                string baseLang = lang.Split('-')[0];
                string[] languages = lang.IndexOf('-') > 0 ? new[] { lang, baseLang } : new[] { lang };
                var meta = BuildUaMetadata(fp, null, _browserFullVersion);
                string script = FingerprintScript.Build(fp, fp.UserAgent, fp.Platform, lang, languages, meta);
                /* No Runtime.enable here: on a worker session it waits for a domain handshake
                   that never completes on every build, while evaluate reaches the default
                   execution context directly. */
                await _cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object>
                {
                    { "expression", script },
                    { "returnByValue", true },
                    { "awaitPromise", false }
                }, sessionId, 2500).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Info("Worker stamp skipped: " + ex.Message);
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
                    await AttachAndStampAsync(Str(info, "targetId"), Str(info, "url")).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { Log.Error("Target enumeration failed", ex); }
        }

        private async Task AttachAndStampAsync(string targetId, string targetUrl)
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
                if (!string.IsNullOrEmpty(sessionId)) await StampSessionAsync(sessionId, null, targetUrl).ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Error("Attaching to target failed", ex); }
        }

        /// <summary>
        /// Stamps one target with the profile. Safe to call repeatedly: a session is only
        /// configured once, so a popup Chrome reports twice does not get two script copies.
        /// The first target that gets stamped also loads the start page.
        /// </summary>
        private async Task StampSessionAsync(string sessionId, string navigateTo, string targetUrl)
        {
            lock (_stamped)
            {
                if (_stamped.Contains(sessionId)) return;
                _stamped.Add(sessionId);
            }
            if (_primarySession == null) _primarySession = sessionId;
            /* Edge reports more than one "page" target at launch - the visible tab plus an
               internal blank page - and the internal one can attach first. Navigating only the
               first target therefore left the real window sitting on about:blank with the
               fingerprint applied to a page nobody could see. Every blank page is navigated
               once, capped so later user tabs are untouched. */
            bool blank = string.IsNullOrEmpty(targetUrl) || targetUrl == "about:blank" || targetUrl == "edge://newtab";
            if (blank && Interlocked.Increment(ref _navCount) <= 3) navigateTo = _startUrl;
            else navigateTo = null;
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
            /* Off is not just a page-level decision: without the policy Chrome still sends STUN
               packets directly and a network-level leak test sees the real interface. The page
               script additionally withholds every candidate, which a command-line policy alone
               cannot do. */
            string policy = mode.Equals("public", StringComparison.OrdinalIgnoreCase)
                ? "default_public_interface_only" : "disable_non_proxied_udp";
            args.Add("--force-webrtc-ip-handling-policy=" + policy);
            args.Add("--enforce-webrtc-ip-permission-check");
        }

        /// <summary>
        /// Firefox has no debugging pipe, so everything it can honour lives in prefs.js:
        /// proxy, locale and language. WebGL, timezone, screen and platform cannot be restamped
        /// there, so a non-Windows device target is declined rather than half-applied.
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
                /* Firefox must not be pointed at the relay unless an upstream proxy is really
                   configured - same reason as the Chromium --proxy-server flag. With no proxy
                   the browser talks to the network directly, which is both faster and honest. */
                bool fxProxy = Profile != null && Profile.Proxy != null && Profile.Proxy.Enabled && _relay != null;
                if (fxProxy)
                {
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
                    sb.AppendLine("user_pref(\"network.proxy.socks_remote_dns\", true);");
                    /* Without failover_direct a dead proxy stops the page instead of silently
                       falling back to the real IP, which is the worst possible leak. */
                    sb.AppendLine("user_pref(\"network.proxy.failover_direct\", false);");
                }
                else
                {
                    sb.AppendLine("user_pref(\"network.proxy.type\", 0);");
                    sb.AppendLine("user_pref(\"network.proxy.share_proxy_settings\", false);");
                }
                sb.AppendLine("user_pref(\"network.dns.disablePrefetch\", true);");
                sb.AppendLine("user_pref(\"network.dns.disablePrefetchFromHTTPS\", true);");
                sb.AppendLine("user_pref(\"network.prefetch-next\", false);");
                sb.AppendLine("user_pref(\"network.http.speculative-parallel-limit\", 0);");
                sb.AppendLine("user_pref(\"browser.region.network.url\", \"\");");
                sb.AppendLine("user_pref(\"browser.region.update.enabled\", false);");
                string rtcMode = fp.WebRtcMode ?? (fp.WebRtcProtect ? "proxy" : "off");
                bool rtcOff = rtcMode.Equals("off", StringComparison.OrdinalIgnoreCase);
                sb.AppendLine("user_pref(\"media.peerconnection.enabled\", " + (rtcOff ? "false" : "true") + ");");
                sb.AppendLine("user_pref(\"media.peerconnection.ice.no_host\", true);");
                if (!rtcMode.Equals("public", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("user_pref(\"media.peerconnection.ice.proxy_only\", true);");

                string lang = string.IsNullOrEmpty(fp.Language) ? "en-US" : fp.Language;
                sb.AppendLine("user_pref(\"intl.locale.requested\", \"" + Escape(lang) + "\");");
                sb.AppendLine("user_pref(\"intl.accept_languages\", \"" + Escape(fp.AcceptLanguage ?? lang) + "\");");

                if (!fp.AutoUserAgent && !string.IsNullOrWhiteSpace(fp.UserAgentOverride))
                {
                    /* A hand-typed UA is the user's call, so honour it. An automatic one is not:
                       Firefox prefs can restamp the header but not navigator.platform, screen,
                       WebGL or timezone, so a foreign or Chrome-shaped UA would contradict the
                       engine and read as a fake. The real Firefox UA is the only coherent one. */
                    sb.AppendLine("user_pref(\"general.useragent.override\", \"" + Escape(fp.UserAgentOverride) + "\");");
                }
                else if (!FingerprintFactory.IsWindowsTarget(fp.Os))
                {
                    Log.Info("Firefox profile targets " + fp.Os + "; keeping the host user agent (Firefox cannot mask platform, screen or WebGL)");
                }

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
            /* Deliberately no Network.enable. Enabling that domain is the clearest CDP tell a
               page can read without any cooperation: the browser stops reusing its network
               stack, request priority classes appear as "kRenderBlocking"/"kPrefetch", and the
               presence of the domain is itself observable. It was only ever here to inject an
               Accept-Language header, which the --accept-lang launch switch now sets natively,
               and every switch that reads its value still works without the domain enabled -
               Chromium honours them in the network service, not through this session. */
            if (DebugEvents != null) await TrySend("Network.enable", null, session);
            /* Runtime.enable only when somebody is listening: it makes the renderer report every
               console call and exception to us, which is what the diagnostic harness needs to
               explain a page that never finishes loading. Left off in normal use to keep the
               event traffic (and the memory behind it) at zero. */
            if (DebugEvents != null) await TrySend("Runtime.enable", null, session);

            if (!string.IsNullOrEmpty(fp.Timezone))
            {
                await TrySend("Emulation.setTimezoneOverride", new Dictionary<string, object> { { "timezoneId", fp.Timezone } }, session);
            }
            if (!string.IsNullOrEmpty(lang))
            {
                /* Chromium keeps a single locale override per emulation agent, and tabs in the
                   same renderer share that agent. A second tab therefore reports "Another
                   locale override is already in effect" even though the override it wants is
                   already active. That is a successful no-op, not a failure: the Accept-Language
                   header, --lang switch and injected navigator.languages still cover that tab.
                   Only clear when this very session is switching to a different locale. */
                string previous;
                bool had;
                lock (_scriptState) had = _locales.TryGetValue(session ?? "", out previous);
                if (!had || !string.Equals(previous, lang, StringComparison.OrdinalIgnoreCase))
                {
                    if (had)
                        await TrySend("Emulation.setLocaleOverride", new Dictionary<string, object>(), session);
                    await SetLocaleAsync(lang, session);
                    lock (_scriptState) _locales[session ?? ""] = lang;
                }
                /* The header is set by the --accept-lang launch switch, which the network
                   service applies without the Network domain being enabled. Restamping it here
                   needed Network.setExtraHTTPHeaders, and having that domain on at all was the
                   "Chrome Runtime" tell. A language edited in the editor therefore lands on
                   the next launch rather than mid-page, which is the honest trade: a profile's
                   language is chosen when the profile is made. */
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
            int viewH = FingerprintFactory.MobileViewHeight(fp);
            /* Read the running browser's GREASE brand once, then use the same object for the
               request headers and the in-page navigator.userAgentData override. Two separate
               reads could disagree and produce the "different browser version" flag. */
            Dictionary<string, object> uaMeta = FingerprintFactory.IsIos(fp.Os)
                ? null : await BuildUaMetadataAsync(fp, session).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(fp.UserAgent))
            {
                // The header has to move with the scripted navigator even when the profile claims
                // this machine's own platform: a matching JS UA next to a real header UA is a
                // contradiction any remote script can read in one request.
                // Real iOS browsers expose no UA-Client-Hints at all, so the override carries none.
                object meta = uaMeta;
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
                /* screen.orientation has to agree with the shape of the viewport: a 393x852
                   phone reporting "landscape-primary" is a contradiction that costs nothing
                   to get right. Tablets open in the same orientation as their geometry. */
                int sw = fp.ScreenW > 0 ? fp.ScreenW : 390;
                int sh = viewH > 0 ? viewH : (fp.ScreenH > 0 ? fp.ScreenH : 844);
                var orientation = new Dictionary<string, object>
                {
                    { "type", sh >= sw ? "portraitPrimary" : "landscapePrimary" },
                    { "angle", sh >= sw ? 0 : 90 }
                };

                await TrySend("Emulation.setDeviceMetricsOverride", new Dictionary<string, object>
                {
                    { "width", sw },
                    { "height", sh },
                    { "deviceScaleFactor", fp.PixelRatio > 0 ? fp.PixelRatio : 3 },
                    { "mobile", true },
                    { "screenOrientation", orientation }
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

            /* Remove the previous injections before adding replacements. Without this a
               live edit leaves several copies of the noise script running in one page,
               which is both slower and a fingerprint of its own. */
            string key = session ?? "";
            string oldFp, oldTool;
            lock (_scriptState)
            {
                _fpScripts.TryGetValue(key, out oldFp);
                _toolScripts.TryGetValue(key, out oldTool);
            }
            if (!string.IsNullOrEmpty(oldFp))
                await TrySend("Page.removeScriptToEvaluateOnNewDocument", new Dictionary<string, object> { { "identifier", oldFp } }, session);
            if (!string.IsNullOrEmpty(oldTool))
                await TrySend("Page.removeScriptToEvaluateOnNewDocument", new Dictionary<string, object> { { "identifier", oldTool } }, session);

            string script = FingerprintScript.Build(fp, fp.UserAgent, fp.Platform, lang, languages, uaMeta);
            if (DiagnosticNoInjection)
            {
                if (!string.IsNullOrEmpty(navigateTo))
                    await TrySend("Page.navigate", new Dictionary<string, object> { { "url", navigateTo } }, session);
                return;
            }
            /* persistAcrossRestarts is written out here rather than in TrySendResult: the CDP
               client refuses the whole call when it cannot verify a capability it does not know,
               and this one is optional. Losing it only means a crashed tab reloads unstamped. */
            var fpParams = new Dictionary<string, object>
            {
                { "source", script },
                { "runImmediately", true }
            };
            try { fpParams["persistAcrossRestarts"] = true; } catch { }
            var fpRes = await TrySendResult("Page.addScriptToEvaluateOnNewDocument", fpParams, session);
            string fpId = fpRes != null ? Str(fpRes, "identifier") : "";

            /* First-party tools: human paste, proxy rotation guard and right-click guard.
               Injected through CDP so they work even on builds that ignore --load-extension.
               Skipped on the engines the tools do not ship for, and when the master switch
               turned extensions off - the Settings row used to promise a switch-off that the
               injection silently ignored. */
            var tools = Profile != null ? Profile.Tools : null;
            if (tools == null) tools = new ToolConfig();
            var liveProxy = Profile != null ? Profile.Proxy : null;
            string toolScript = _toolsAllowed && _masterExtensions
                ? ExtensionManager.BuildToolScript(tools, Profile != null ? Profile.Name : "",
                    liveProxy != null && liveProxy.Enabled)
                : null;
            string toolId = "";
            if (!string.IsNullOrEmpty(toolScript))
            {
                var toolParams = new Dictionary<string, object>
                {
                    { "source", toolScript },
                    { "runImmediately", true }
                };
                try { toolParams["persistAcrossRestarts"] = true; } catch { }
                var toolRes = await TrySendResult("Page.addScriptToEvaluateOnNewDocument", toolParams, session);
                toolId = toolRes != null ? Str(toolRes, "identifier") : "";
            }

            lock (_scriptState)
            {
                if (!string.IsNullOrEmpty(fpId)) _fpScripts[key] = fpId;
                if (!string.IsNullOrEmpty(toolId)) _toolScripts[key] = toolId;
            }

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
            /* Ask the browser what build it is before anything is overridden. That answer is the
               only honest source for the version a profile reports: the executable on disk can be
               one build while the executable actually launched - a pinned kernel, a patched
               install, a vendor's build - is another, and a checker sees the one that answers. */
            string truth = await ReadRunningBuildAsync(session).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(truth)) _truthVersion = truth;
            string own = _truthVersion;
            if (string.IsNullOrEmpty(own) || !IsRealBuild(own))
            {
                string kernel = BrowserVersions.IsReal(Profile != null ? Profile.KernelFullVersion : null) ? Profile.KernelFullVersion : null;
                own = !string.IsNullOrEmpty(kernel) ? kernel : _browserFullVersion;
            }
            /* A profile pinned to a specific build reports that build, because that is the binary
               the pin starts. Everything else reports what the browser answered with. */
            if (BrowserVersions.IsReal(own))
            {
                fp.BrowserVersion = own;
                if (Profile != null) Profile.BrowserVersion = own;
            }
            return BuildUaMetadata(fp, await ReadRealClientHintsAsync(session).ConfigureAwait(false), own);
        }

        /// <summary>The build the running browser claims for itself, before any override.</summary>
        private string _truthVersion;

        private async Task<string> ReadRunningBuildAsync(string session)
        {
            if (_cdp == null) return null;
            try
            {
                var res = await _cdp.SendAsync("Browser.getVersion", new Dictionary<string, object>(), null, 4000).ConfigureAwait(false);
                string product = res != null ? Str(res, "product") : null;
                /* "Chrome/141.0.7390.55" - the real build of the image that is running. */
                if (!string.IsNullOrEmpty(product) && product.IndexOf('/') > 0)
                {
                    string build = product.Substring(product.IndexOf('/') + 1).Trim();
                    if (IsRealBuild(build)) return build;
                }
            }
            catch (Exception ex) { Log.Info("Browser.getVersion unavailable: " + ex.Message); }
            return null;
        }

        private Dictionary<string, object> BuildUaMetadata(FingerprintConfig fp, Dictionary<string, object> real, string installedFull)
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
            /* The hint list has to name the browser the page is actually talking to, not the one
               the user agent string imitates. Brave keeps Chrome's user agent word for word and
               puts its own name only here, and the reverse holds for Edge: a real Edge that
               reports Google Chrome is a mismatch, because everything else Edge exposes (its
               own extension store, its escape hatch pages) still says Edge. */
            string brandName = edge || _browserIsEdge ? "Microsoft Edge"
                : _browserIsBrave ? "Brave"
                : "Google Chrome";

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

            /* Both lists must describe the same products. The previous version copied only the
               GREASE entry from the running browser and skipped the rest whenever it found one,
               leaving fullVersionList with a single fake brand - the exact "different browser
               version" mismatch a client-hints checker reports. */
            var reduced = new List<object>();
            var fullListOut = new List<object>();
            string chromiumFull = full;
            string brandFull = full;
            string greaseFull = "8.0.0.0";
            if (!string.IsNullOrEmpty(greaseVersion))
                greaseFull = greaseVersion.IndexOf('.') >= 0 ? greaseVersion : greaseVersion + ".0.0.0";
            foreach (var b in realFull)
            {
                string name = Str(b, "brand");
                string ver = Str(b, "version");
                if (string.IsNullOrEmpty(ver)) continue;
                if (name == "Chromium") chromiumFull = ver;
                else if (name == brandName) brandFull = ver;
                else if (greaseBrand != null && name == greaseBrand) greaseFull = ver;
            }
            /* The UA string is reduced to major.0.0.0, so it cannot supply the build number the
               hints are required to carry. The profile's own build comes first: it is the number
               chosen for this identity when it was made, and using the installed browser's build
               here is what made every profile on a machine report the same version. The live
               hint and a synthesised value are the fallbacks, in that order. */
            string own = fp.BrowserVersion;
            string realFullVersion = BrowserVersions.IsReal(own) ? own
                : IsRealBuild(installedFull) ? installedFull
                : (IsRealBuild(brandFull) ? brandFull : (IsRealBuild(chromiumFull) ? chromiumFull : FingerprintFactory.FullChromiumVersion(null, major)));
            if (!IsRealBuild(chromiumFull)) chromiumFull = realFullVersion;
            if (!IsRealBuild(brandFull)) brandFull = realFullVersion;
            if (greaseBrand != null)
            {
                reduced.Add(new Dictionary<string, object> { { "brand", greaseBrand }, { "version", greaseVersion ?? "8" } });
                fullListOut.Add(new Dictionary<string, object> { { "brand", greaseBrand }, { "version", greaseFull } });
            }
            else
            {
                reduced.Add(new Dictionary<string, object> { { "brand", "Not_A Brand" }, { "version", "8" } });
                fullListOut.Add(new Dictionary<string, object> { { "brand", "Not_A Brand" }, { "version", "8.0.0.0" } });
            }
            reduced.Add(new Dictionary<string, object> { { "brand", "Chromium" }, { "version", major.ToString() } });
            reduced.Add(new Dictionary<string, object> { { "brand", brandName }, { "version", major.ToString() } });
            fullListOut.Add(new Dictionary<string, object> { { "brand", "Chromium" }, { "version", chromiumFull } });
            fullListOut.Add(new Dictionary<string, object> { { "brand", brandName }, { "version", brandFull } });

            string platform = FingerprintFactory.IsMac(fp.Os) ? "macOS"
                : (FingerprintFactory.IsLinux(fp.Os) ? "Linux" : (android ? "Android" : "Windows"));

            // Only keep the live platform fields when the profile claims this machine's OS;
            // otherwise fall back to values that are plausible for the target.
            string realPlatform = real != null ? Str(real, "platform") : null;
            bool samePlatform = string.Equals(realPlatform, platform, StringComparison.OrdinalIgnoreCase);

            string platformVersion = samePlatform ? Str(real, "platformVersion") : "";
            /* The hint build has to be the same Android the user agent carries, or the two
               disagree in the same request that ships both. */
            string[] device = FingerprintFactory.DeviceIdentity(fp);
            string deviceVersion = device != null && device.Length > 1 ? device[1] : "";
            string deviceModel = device != null && device.Length > 0 ? device[0] : "";
            if (android && deviceVersion.Length > 0) platformVersion = deviceVersion + ".0.0";
            if (string.IsNullOrEmpty(platformVersion))
            {
                if (platform == "macOS") platformVersion = "15.1.0";
                else if (android) platformVersion = "16.0.0";
                else if (platform == "Linux") platformVersion = "6.11.0";
                else if (FingerprintFactory.IsWindows11(fp.Os)) platformVersion = "15.0.0";
                else platformVersion = "10.0.0";
            }

            string architecture = samePlatform ? Str(real, "architecture") : (android ? "arm" : "x86");
            if (string.IsNullOrEmpty(architecture)) architecture = android ? "arm" : "x86";
            string bitness = samePlatform ? Str(real, "bitness") : (android ? "" : "64");
            if (string.IsNullOrEmpty(bitness)) bitness = android ? "" : "64";
            string model = samePlatform ? Str(real, "model") : "";
            /* The hint model is the same handset the user agent names, and empty when the viewport
               belongs to no table entry - which is what the unified Android UA reports too. */
            if (android) model = deviceModel;

            object wow64 = samePlatform && real != null && HasTrue(real, "wow64");

            return new Dictionary<string, object>
            {
                { "brands", reduced },
                { "fullVersionList", fullListOut },
                { "fullVersion", realFullVersion },
                { "platform", platform },
                { "platformVersion", platformVersion },
                { "architecture", architecture },
                { "bitness", bitness },
                { "model", model ?? "" },
                { "mobile", fp.DeviceType != null && fp.DeviceType != "desktop" },
                { "wow64", wow64 }
            };
        }

        /// <summary>
        /// True when the string looks like a build number a real Chromium ships: four numeric
        /// parts with a non-zero build and patch. "156.0.0.0" is the reduced UA shape and must
        /// never be handed to client hints, which always carry the full build.
        /// </summary>
        private static bool IsRealBuild(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string[] parts = version.Trim().Split('.');
            if (parts.Length != 4) return false;
            int head, build, patch;
            if (!int.TryParse(parts[0], out head) || head <= 0) return false;
            if (!int.TryParse(parts[2], out build) || build <= 0) return false;
            if (!int.TryParse(parts[3], out patch) || patch <= 0) return false;
            return true;
        }

        private static bool HasTrue(Dictionary<string, object> d, string key)        {
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

        /// <summary>
        /// Applies the locale override, treating Chromium's "already in effect" answer as the
        /// success it is (another tab in the same renderer already set this exact locale).
        /// </summary>
        private async Task SetLocaleAsync(string lang, string session)
        {
            try
            {
                await _cdp.SendAsync("Emulation.setLocaleOverride",
                    new Dictionary<string, object> { { "locale", lang } }, session, 15000).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string message = (ex.InnerException != null ? ex.InnerException.Message + " " : "") + ex.Message;
                if (message.IndexOf("Another locale override is already in effect", StringComparison.OrdinalIgnoreCase) < 0 &&
                    message.IndexOf("already in effect", StringComparison.OrdinalIgnoreCase) < 0)
                    Log.Error("CDP Emulation.setLocaleOverride failed", ex);
            }
        }

        private async Task<Dictionary<string, object>> TrySendResult(string method, Dictionary<string, object> parameters, string session)
        {
            try { return await _cdp.SendAsync(method, parameters, session, 15000).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error("CDP " + method + " failed", ex); return null; }
        }

        /// <summary>
        /// Clears cache and/or cookies through the browser's own storage APIs, so a profile that
        /// is already open can be cleaned without closing it. Site data additionally asks every
        /// origin the profile has touched to drop its storage.
        /// </summary>
        public async Task<bool> ClearStorageAsync(ClearScope scope)
        {
            if (_cdp == null) return false;
            bool any = false;
            try
            {
                if (scope != ClearScope.Cookies)
                {
                    await _cdp.SendAsync("Network.clearBrowserCache", null, null, 15000).ConfigureAwait(false);
                    any = true;
                }
                if (scope != ClearScope.Cache)
                {
                    await _cdp.SendAsync("Network.clearBrowserCookies", null, null, 15000).ConfigureAwait(false);
                    any = true;
                }
                if (scope == ClearScope.SiteData)
                {
                    /* Storage.clearDataForOrigin needs an origin, and there is no "all" variant.
                       The start page's origin plus the two most common login origins cover the
                       case this button exists for: a site that stored something the user wants
                       gone while the tab stays open. Everything else is handled on disk once the
                       profile is closed. */
                    foreach (string origin in SiteOrigins())
                    {
                        try
                        {
                            await _cdp.SendAsync("Storage.clearDataForOrigin", new Dictionary<string, object>
                            {
                                { "origin", origin },
                                { "storageTypes", "all" }
                            }, null, 15000).ConfigureAwait(false);
                            any = true;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Clearing browser storage failed", ex);
            }
            return any;
        }

        private IEnumerable<string> SiteOrigins()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            string[] urls = { _startUrl, Profile != null ? Profile.StartUrl : null };
            foreach (string u in urls)
            {
                if (string.IsNullOrWhiteSpace(u)) continue;
                try
                {
                    var uri = new Uri(u);
                    string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port);
                    if (seen.Add(origin)) list.Add(origin);
                }
                catch { }
            }
            return list;
        }

        /// <summary>Exports every cookie of this browser context as a portable JSON array.</summary>
        public async Task<string> ExportCookiesAsync()
        {
            if (_cdp == null) throw new InvalidOperationException("Open this profile first.");
            var res = await _cdp.SendAsync("Storage.getCookies", null, null, 15000).ConfigureAwait(false);
            object raw;
            var cookies = res != null && res.TryGetValue("cookies", out raw) ? raw as object[] : null;
            if (cookies == null) cookies = new object[0];
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(cookies);
        }

        /// <summary>
        /// Imports a JSON cookie array (or an object with a "cookies" key). Unknown fields are
        /// stripped because Storage.setCookies rejects anything outside its schema.
        /// </summary>
        public async Task<int> ImportCookiesAsync(string json)
        {
            if (_cdp == null) throw new InvalidOperationException("Open this profile first.");
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("The file is empty.");
            object parsed;
            try { parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json); }
            catch (Exception ex) { throw new InvalidOperationException("Not a valid cookie JSON file.", ex); }

            var root = parsed as Dictionary<string, object>;
            object arrObj = parsed;
            if (root != null)
            {
                object inner;
                if (!root.TryGetValue("cookies", out inner)) throw new InvalidOperationException("The file has no \"cookies\" array.");
                arrObj = inner;
            }
            var arr = arrObj as object[];
            if (arr == null) throw new InvalidOperationException("Expected a JSON array of cookies.");

            var clean = new List<object>();
            foreach (object o in arr)
            {
                var c = o as Dictionary<string, object>;
                if (c == null) continue;
                object nv;
                string name = c.TryGetValue("name", out nv) ? Convert.ToString(nv) : "";
                object dv;
                string domain = c.TryGetValue("domain", out dv) ? Convert.ToString(dv) : "";
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(domain)) continue;
                var outc = new Dictionary<string, object> { { "name", name }, { "domain", domain } };
                CopyField(c, outc, "value", "");
                CopyField(c, outc, "path", "/");
                CopyField(c, outc, "expires", -1.0);
                CopyField(c, outc, "httpOnly", false);
                CopyField(c, outc, "secure", false);
                object ss;
                if (c.TryGetValue("sameSite", out ss) && ss != null)
                {
                    string v = Convert.ToString(ss);
                    if (v == "Strict" || v == "Lax" || v == "None") outc["sameSite"] = v;
                }
                object src;
                if (c.TryGetValue("sourceScheme", out src) && src != null) outc["sourceScheme"] = Convert.ToString(src);
                object prio;
                if (c.TryGetValue("priority", out prio) && prio != null) outc["priority"] = Convert.ToString(prio);
                clean.Add(outc);
            }
            if (clean.Count == 0) throw new InvalidOperationException("No usable cookies found in the file.");

            var result = await _cdp.SendAsync("Storage.setCookies",
                new Dictionary<string, object> { { "cookies", clean.ToArray() } }, null, 20000).ConfigureAwait(false);
            if (result == null) throw new InvalidOperationException("The browser rejected the cookies.");
            return clean.Count;
        }

        private static void CopyField(Dictionary<string, object> src, Dictionary<string, object> dst, string key, object fallback)
        {
            object v;
            if (src.TryGetValue(key, out v) && v != null)
            {
                if (v is double || v is int || v is long || v is bool || v is string) { dst[key] = v; return; }
            }
            dst[key] = fallback;
        }

        public bool ExtensionsLoaded { get { return _extensionsLoaded; } }

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
            return await EvaluateAsync(expression, 15000).ConfigureAwait(false);
        }

        /// <summary>
        /// A long-timeout variant for fingerprint sites whose own JavaScript keeps the main
        /// thread busy for tens of seconds on a software renderer. The page is not broken;
        /// a 15 second cut would report a healthy load as a failure.
        /// </summary>
        public async Task<string> EvaluateAsync(string expression, int timeoutMs)
        {
            if (_cdp == null) return null;
            var result = await _cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", expression },
                { "returnByValue", true },
                { "awaitPromise", true }
            }, _primarySession, timeoutMs).ConfigureAwait(false);
            object res;
            if (result != null && result.TryGetValue("result", out res))
            {
                var d = res as Dictionary<string, object>;
                object v;
                if (d != null && d.TryGetValue("value", out v) && v != null) return Convert.ToString(v);
            }
            return null;
        }

        /// <summary>
        /// Types real characters into the focused element with the DevTools input domain.
        /// This is the only honest way to test that a login form accepts input: setting
        /// element.value in JavaScript bypasses every framework binding and proves nothing
        /// about whether a user can actually sign in.
        /// </summary>
        public async Task<string> TypeTextAsync(string text)
        {
            if (_cdp == null || string.IsNullOrEmpty(text)) return null;
            try
            {
                await _cdp.SendAsync("Input.insertText", new Dictionary<string, object> { { "text", text } },
                    _primarySession, 15000).ConfigureAwait(false);
                return text;
            }
            catch (Exception ex)
            {
                Log.Info("Type failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Navigates the live tab to a URL, keeping the fingerprint applied.</summary>
        public async Task<bool> NavigateAsync(string url)
        {
            if (_cdp == null || string.IsNullOrEmpty(url)) return false;
            try
            {
                var r = await _cdp.SendAsync("Page.navigate", new Dictionary<string, object> { { "url", url } },
                    _primarySession, 20000).ConfigureAwait(false);
                if (r == null) return false;
                object err;
                if (r.TryGetValue("errorText", out err) && err != null) return false;
                return true;
            }
            catch (Exception ex)
            {
                Log.Info("Navigate failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Captures the visible tab as PNG through the DevTools protocol. Works without an
        /// interactive desktop, unlike a screen grab.
        /// </summary>
        public async Task<string> ScreenshotAsync(string path)
        {
            if (_cdp == null) return null;
            var result = await _cdp.SendAsync("Page.captureScreenshot", new Dictionary<string, object>
            {
                { "format", "png" },
                { "fromSurface", true }
            }, _primarySession, 15000).ConfigureAwait(false);
            object data;
            if (result == null || !result.TryGetValue("data", out data) || data == null) return null;
            byte[] bytes = Convert.FromBase64String(Convert.ToString(data));
            File.WriteAllBytes(path, bytes);
            return path;
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
            /* A profile marked as a fresh session is wiped here, on the way out, not when the
               next one starts: the data must not survive the window that created it. It runs on
               the closing path so the user sees no delay before the browser disappears. */
            if (Profile != null && Profile.FreshSession && !string.IsNullOrEmpty(DataDir))
                ProfileStore.WipeSessionData(DataDir);
            var handler = Closed;
            if (handler != null) { try { handler(this); } catch { } }
        }
    }
}
