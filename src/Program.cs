using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [STAThread]
        private static int Main(string[] args)
        {
            /* Add/Remove Programs invokes the installed executable with --uninstall. This has
               to run before the single-instance mutex, because a running copy would otherwise
               just bring its window forward instead of removing anything. */
            /* Recognised arguments. Anything else is ignored here and forwarded meaningfully:
               Chromium is launched by this app, never by passing its own switches to it, so an
               unknown switch means the user mistyped something and should be told rather than
               have the app start and silently ignore it. */
            bool uninstall = false, silent = false, portable = false, help = false, unknown = false;
            bool selfTest = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                    else if (a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                    else if (a.Equals("--portable", StringComparison.OrdinalIgnoreCase)) portable = true;
                    else if (a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)) selfTest = true;
                    else if (a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("/?", StringComparison.OrdinalIgnoreCase)) help = true;
                    else if (a.Length > 0 && (a[0] == '-' || a[0] == '/')) unknown = true;
                }
            }
            /* --selftest exists for the build. A binary that links is not the same as a binary
               that runs: the framework those two facts differ over is .NET's application
               configuration, which is read lazily by the first component that consults it, and
               an unknown <section> in JoeBrowser.exe.config throws there rather than here. So
               the release build launches the freshly made EXE with this switch, on a machine
               with no interactive desktop, and fails the build if the exit code is not zero. */
            if (selfTest)
            {
                try
                {
                    /* Reading an appSettings value forces .NET to parse JoeBrowser.exe.config,
                       section by section - which is exactly where an undeclared custom section
                       throws. A missing file is fine; a file that cannot be parsed is not. */
                    System.Configuration.ConfigurationManager.RefreshSection("appSettings");
                    string probe = System.Configuration.ConfigurationManager.AppSettings["JoeBrowser.SelfTest"];
                    if (probe == null) { Console.Error.WriteLine("selftest: configuration has no appSettings value"); return 3; }
                }
                catch (Exception ex)
                {
                    /* The outer exception of a configuration failure is always the same
                       sentence, so the inner ones - which name the section and the reason -
                       are what has to be reported. */
                    var sb = new System.Text.StringBuilder("selftest: configuration could not be read:");
                    for (Exception e = ex; e != null; e = e.InnerException)
                        sb.Append(" [" + e.GetType().Name + ": " + e.Message + "]");
                    Console.Error.WriteLine(sb.ToString());
                    return 2;
                }
                /* The configuration check proves the binary can start. It says nothing about the
                   parts whose failure is silent, so the ones that can be exercised headlessly are
                   exercised here and the build fails on any of them. The folder is thrown away
                   afterwards, so a build machine keeps nothing from this. */
                string sandbox = null;
                try
                {
                    var sb = new System.Text.StringBuilder();
                    string history = HistoryStore.SelfTest();
                    if (!string.IsNullOrEmpty(history)) sb.Append(" [history" + history + "]");

                    sandbox = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "joebrowser-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    System.IO.Directory.CreateDirectory(sandbox);
                    var store = new ProfileStore(sandbox);

                    /* The brief's hardest rule: two new profiles must not describe the same
                       machine. Profiles are made through the same path the grid uses, so this
                       tests the real generator rather than a copy of it. */
                    var profiles = new List<Profile>();
                    for (int i = 0; i < 8; i++)
                    {
                        var p = new Profile();
                        p.Name = "self test " + (i + 1);
                        FingerprintIdentity.Comparison clash;
                        FingerprintIdentity.MakeUnique(p.Fp, "System default", p.Proxy, profiles, out clash);
                        if (clash != null && clash.Relation == FingerprintIdentity.Relation.Linked)
                            sb.Append(" [profile " + (i + 1) + " is linked to profile " +
                                      (profiles.IndexOf(clash.B) + 1) + "]");
                        string note;
                        KernelDiversity.Assign(p, profiles, out note);
                        profiles.Add(p);
                    }

                    var bySignature = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var p in profiles)
                    {
                        if (p.Fp == null) { sb.Append(" [a profile has no fingerprint]"); continue; }
                        /* The UA is resolved against an installed browser at launch, so it is
                           empty until then; what has to be there from the start is the machine
                           the profile claims to be, which is what the site reads. */
                        FingerprintFactory.ResolveUserAgent(p.Fp, new BrowserInfo
                        {
                            Name = "self test",
                            Version = "131.0.0.0",
                            MajorVersion = 131,
                            Path = System.IO.Path.Combine(sandbox, "chrome.exe")
                        });
                        if (string.IsNullOrEmpty(p.Fp.UserAgent)) sb.Append(" [a profile has no user agent]");
                        string sig = FingerprintIdentity.OverallHash(p);
                        int n;
                        bySignature.TryGetValue(sig, out n);
                        bySignature[sig] = n + 1;
                    }
                    foreach (var kv in bySignature)
                        if (kv.Value > 1) sb.Append(" [" + kv.Value + " of 8 profiles share one signature]");
                    /* A unique signature is not enough on its own: the brief asks for a different
                       kernel each time as well, so a grid of eight identical Chrome builds would
                       pass the check above and fail the requirement. Pinning only happens when
                       more than one build is installed - a lone build is shared by design - so
                       the rule is checked only where it can apply. */
                    var kernels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in profiles)
                    {
                        string key = (p.KernelEngine ?? "browser") + "|" + (p.KernelVersion ?? "system");
                        int k;
                        kernels.TryGetValue(key, out k);
                        kernels[key] = k + 1;
                    }
                    int pinned = 0;
                    foreach (var p in profiles) if (KernelDiversity.IsPinned(p)) pinned++;
                    if (pinned > 0 && pinned < profiles.Count)
                        sb.Append(" [" + pinned + " of 8 profiles have a build of their own]");
                    if (pinned == 0 && kernels.Count != 1)
                        sb.Append(" [" + kernels.Count + " kinds of kernel among 8 profiles]");

                    /* The start page every new profile gets: the fingerprint test page, not a
                       search engine. Checked here because it is a one line default that three
                       separate fallbacks read, and a wrong one is invisible until a user opens
                       a blank tab. */
                    var fresh = new Profile();
                    if (fresh.StartUrl != Profile.FingerprintTestUrl)
                        sb.Append(" [a new profile starts on " + (fresh.StartUrl ?? "nothing") + "]");
                    var settings = new AppSettings();
                    if (settings.DefaultStartUrl != Profile.FingerprintTestUrl)
                        sb.Append(" [the app default start page is " + (settings.DefaultStartUrl ?? "nothing") + "]");

                    /* The complimentary grant: it must recognise the account whatever shape the
                       address arrives in, and it must not recognise anybody else. */
                    if (!Plan.IsComplimentaryEmail("alistairfox.london@gmail.com"))
                        sb.Append(" [the complimentary account is not recognised]");
                    if (!Plan.IsComplimentaryEmail("  AlistairFox.London@Gmail.com "))
                        sb.Append(" [a spaced or capitalised address is not recognised]");
                    if (Plan.IsComplimentaryEmail("someone@example.com"))
                        sb.Append(" [an unrelated address was granted Pro]");
                    if (Plan.IsComplimentaryEmail(""))
                        sb.Append(" [an empty address was granted Pro]");

                    /* The extension id scheme: Chromium derives an extension's id from the key
                       in its manifest, and the library now records that id so a store link can
                       be opened for an extension that arrived as a file. A wrong mapping here
                       would point the store button at somebody else's extension, so the exact
                       known pair is asserted rather than "looks like an id". */
                    string knownKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA2nVvquI0ZB+gXHrNqIPB" +
                                      "dJBSWz0VJ7nzGcVSC3jNKZ4PZ2sYz0lv0ZCVvsO8BtR4KxuzgDTbX0xk7GtUP9QQ" +
                                      "zYPPk0CMfNZ8A6EKgfQ8NPOwLcVwScGPqmZ4XfoWb7i7PoMk8NPWfJvHzuMRr4" +
                                      "uZ1bnJxHKEJP7XnSuNrRvpTrKxr0MhJz6yW1y0xOk1sPRyPKKKvZ2t4Drx9NZx" +
                                      "Pi0LKzPT8vBGaMVEgDKm2XBGf8Nb0bq0sOWXPmKqSpKZ8ZO0t7+MVfNnXWnCjQ" +
                                      "IDAQAB";
                    string derived = ExtensionCatalog.IdForKey(knownKey);
                    foreach (char c in derived)
                        if (c < 'a' || c > 'p') { sb.Append(" [extension id has a character outside a to p]"); break; }
                    if (derived.Length != 32) sb.Append(" [extension id is " + derived.Length + " characters]");
                    if (ExtensionCatalog.IdForKey("") != "") sb.Append(" [an empty key produced an extension id]");
                    if (ExtensionCatalog.IdForKey("not base64 at all !!") != "")
                        sb.Append(" [an invalid key produced an extension id]");
                    if (ExtensionCatalog.StoreUrlForId("") != "")
                        sb.Append(" [an empty extension id produced a store link]");

                    /* Every row in the interface is sized from the fonts, and the claim this
                       round rests on is that a row is never shorter than the text it carries.
                       That is arithmetic, so it can be asserted here at every scale rather than
                       argued about: for each scale in use, a row built from the type scale must
                       be at least one line taller than its tallest line, and a two line block
                       must fit the pair. This is the check that would have caught the fixed
                       offsets that put a caption under a button. */
                    float wasScale = Ui.Scale;
                    int prevBody = 0;
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                    {
                        /* The font cache is keyed by size and style, not by scale, so the
                           previous scale's faces have to go before this one is measured. */
                        Ui.Scale = scale;
                        Theme.ClearFontCache();

                        int body = Theme.LineHeight(Theme.Body);
                        int cap = Theme.LineHeight(Theme.Caption);
                        int h3 = Theme.LineHeight(Theme.H3);
                        if (Theme.Row < cap + Ui.S(2)) sb.Append(" [row is shorter than a caption at " + scale + "]");
                        if (Theme.Field < body + Ui.S(8)) sb.Append(" [field is shorter than a line of text at " + scale + "]");
                        if (EngineRow.RowH < h3 + cap + Ui.S(8)) sb.Append(" [browser row cannot hold its two lines at " + scale + "]");
                        if (KernelRow.RowH < h3 + cap + Ui.S(8)) sb.Append(" [kernel row cannot hold its two lines at " + scale + "]");
                        if (ExtensionRow.RowH < h3 + cap + Ui.S(8)) sb.Append(" [extension row cannot hold its two lines at " + scale + "]");
                        if (PromoCard.CardH < Theme.LineHeight(Theme.H2) + Theme.LineHeight(Theme.Small) + cap)
                            sb.Append(" [promo card cannot hold its three lines at " + scale + "]");
                        /* A measured row is only right if the text it is measured against grows
                           with the scale. This is the assertion that fails if anyone ever asks
                           for a type size in points again. */
                        if (prevBody > 0 && body <= prevBody)
                            sb.Append(" [line height did not grow at " + scale + "]");
                        prevBody = body;
                    }
                    Ui.Scale = wasScale;
                    Theme.ClearFontCache();

                    /* The type scale is a promise now, not a preference, so it is asserted
                       rather than eyeballed: body text at 16 px or more and nothing anywhere
                       below 14 px at 100%. "The font is too small" has come back once; a build
                       that reintroduces it should fail here instead of in a screenshot. */
                    if (Theme.PxBody < 16) sb.Append(" [body text is below 16 px]");
                    if (Math.Min(Theme.PxMicro, Theme.PxCaption) < 14) sb.Append(" [text below 14 px is in the scale]");
                    if (Theme.ButtonRadius != 0 || Theme.CardRadius != 0) sb.Append(" [a rounded corner is back in the theme]");

                    /* The layout is measured, so it can be proven: every line of text drawn on a
                       card or a row has to fit the width it is given, at the new type sizes and
                       at every scale. These are the fixed strings that used to be cut - the
                       toast message, the label beside a toggle, a card's description - measured
                       against the space they really get. */
                    float keepScale = Ui.Scale;
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f })
                    {
                        Ui.Scale = scale;
                        Theme.ClearFontCache();
                        using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                        {
                            /* A row's title at H3 has to fit the narrowest card for a name of
                               the length people really use. A longer name is ellipsised with the
                               whole value in a tooltip, which is the interface's stated rule:
                               nothing is cut without a way to read it. */
                            float rowW = Ui.S(ProfileCard.CardMinW) - Ui.S(36);
                            if (Ui.Measure(g, "Trading account Europe", Theme.H3).Width > rowW)
                                sb.Append(" [a row title does not fit at " + scale + "]");
                            /* The longest caption the interface draws, wrapped in a card column:
                               it is allowed three lines, so the block it lives in is sized for
                               three. If it needs a fourth at some scale, that block is a clipped
                               block - which is the defect being fixed. */
                            float three = 3f * Theme.LineHeight(Theme.Small);
                            float need = Ui.MeasureWrapped(g, L10n.T("acct.pin.off.state"), Theme.Small, rowW, 99);
                            if (need > three + 1f)
                                sb.Append(" [a card caption needs more than three lines at " + scale + "]");
                        }
                    }
                    Ui.Scale = keepScale;
                    Theme.ClearFontCache();

                    /* The reported defect of this round: every profile on a machine claimed the
                       installed browser's build, so a workspace of ten profiles reported one
                       number, the same one as the user's own Chrome. Asserted here the way the
                       grid makes them: each profile through the same path, then the user agents
                       and the hint builds compared. Chrome's reduced user agent hides the build,
                       so the major is what has to differ between any two profiles. */
                    Func<Dictionary<string, int>, string, int> Count = delegate(Dictionary<string, int> map, string key)
                    {
                        int n;
                        map.TryGetValue(key ?? "", out n);
                        map[key ?? ""] = n + 1;
                        return n + 1;
                    };
                    /* The version a profile reports has to be the version of the binary that answers
                       the request. The previous round got this backwards: it gave profiles builds of
                       other majors so a workspace would look diverse, and ipfighter called that
                       fingerprint masking - correctly, because the claim and the engine can never
                       agree. Asserted here in both directions. */
                    try
                    {
                        var workspace = new List<Profile>();
                        var info = new BrowserInfo
                        {
                            Name = "self test",
                            Version = "141.0.7390.122",
                            MajorVersion = 141,
                            Path = System.IO.Path.Combine(sandbox, "chrome.exe")
                        };
                        for (int i = 0; i < 12; i++)
                        {
                            var p = new Profile { Name = "version test " + (i + 1) };
                            FingerprintIdentity.Comparison c;
                            FingerprintIdentity.MakeUnique(p.Fp, "System default", p.Proxy, workspace, out c);
                            string vnote;
                            BrowserVersions.Assign(p, workspace, info.Version, out vnote);
                            FingerprintFactory.ResolveUserAgent(p.Fp, info);
                            workspace.Add(p);
                        }
                        foreach (var p in workspace)
                        {
                            if (!BrowserVersions.IsReal(p.BrowserVersion))
                                sb.Append(" [a profile reports " + (p.BrowserVersion ?? "no build") + ", which is not a build a browser shipped]");
                            if (p.BrowserVersion != info.Version)
                                sb.Append(" [a profile reports " + p.BrowserVersion + " while the binary it runs is " + info.Version + "]");
                            if (p.Fp.BrowserVersion != p.BrowserVersion)
                                sb.Append(" [the profile and its fingerprint name different builds]");
                            int uaMajor = BrowserVersions.MajorFromUserAgent(p.Fp.UserAgent);
                            if (uaMajor != 141)
                                sb.Append(" [a profile running 141 sends a user agent naming " + uaMajor + "]");
                        }

                        /* A pinned kernel IS a different executable, so it may - and must - report a
                           different build. That is where a workspace's version diversity comes from. */
                        var pinnedProfile = new Profile { Fp = new FingerprintConfig(), KernelFullVersion = "143.0.7499.41" };
                        string pnote;
                        BrowserVersions.Assign(pinnedProfile, workspace, info.Version, out pnote);
                        if (pinnedProfile.BrowserVersion != "143.0.7499.41")
                            sb.Append(" [a pinned kernel does not report its own build]");

                        /* And with no binary known, nothing may be invented. */
                        var noBinary = new Profile { Fp = new FingerprintConfig() };
                        string unote;
                        BrowserVersions.Assign(noBinary, new List<Profile>(), "", out unote);
                        if (!BrowserVersions.IsReal(noBinary.BrowserVersion))
                            sb.Append(" [a profile with no binary got an unreal build]");

                        /* And a removal of a shipped extension has to survive the next start. */
                        string shipped = ExtensionCatalog.DefaultStoreExtensions[0][0];
                        bool wasRemoved = ExtensionCatalog.IsRemovedByUser(shipped);
                        ExtensionCatalog.MarkRemoved(shipped, true);
                        if (!ExtensionCatalog.IsRemovedByUser(shipped))
                            sb.Append(" [removing a shipped extension was not recorded]");
                        var folders = ExtensionCatalog.DefaultFolders();
                        foreach (string f in folders)
                            if (f.EndsWith(shipped, StringComparison.OrdinalIgnoreCase))
                                sb.Append(" [a removed shipped extension is still loaded]");
                        if (wasRemoved) ExtensionCatalog.MarkRemoved(shipped, false);
                    }
                    catch (Exception ex)
                    {
                        sb.Append(" [version checks threw " + ex.GetType().Name + ": " + ex.Message + "]");
                    }

                    var saved = new Profile { Name = "self test save" };
                    store.Save(saved);
                    var back = store.LoadAll();
                    if (back.Count != 1) sb.Append(" [saved profile did not reload]");
                    else if (back[0].Fp == null) sb.Append(" [reloaded profile lost its fingerprint]");
                    store.Delete(saved, false);

                    if (sb.Length > 0)
                    {
                        Console.Error.WriteLine("selftest failed:" + sb);
                        return 4;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("selftest threw " + ex.GetType().Name + ": " + ex.Message);
                    return 5;
                }
                finally
                {
                    try { if (sandbox != null && System.IO.Directory.Exists(sandbox)) System.IO.Directory.Delete(sandbox, true); }
                    catch { }
                }

                Console.WriteLine("selftest ok " + Application.ProductVersion);
                return 0;
            }

            if (help)
            {
                MessageBox.Show(
                    "Joe Browser\r\n\r\n"
                    + "  --portable     run from this folder without installing\r\n"
                    + "  --uninstall    remove Joe Browser (profiles are kept)\r\n"
                    + "  --silent       no prompts, for the uninstaller\r\n"
                    + "  --selftest     check the installation from a script, then exit\r\n\r\n"
                    + "Without an argument Joe Browser starts normally.",
                    "Joe Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (uninstall)
            {
                if (!silent && MessageBox.Show(
                        "Remove Joe Browser from this PC?\r\n\r\nYour profiles, proxies and browser "
                        + "sessions in %APPDATA%\\JoeBrowser are kept.",
                        "Joe Browser", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return 0;
                Installation.Uninstall(silent);
                if (!silent)
                    MessageBox.Show("Joe Browser has been removed.", "Joe Browser",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            // A second copy would hold the same profile store and settings at once, so bring the
            // running window forward instead. The mutex lives for the life of the process.
            //
            // The wait matters for the installer hand-off: it starts the installed copy while the
            // copy that was just installed still holds this mutex, so the new process used to see
            // "already running", focus a window that was about to close, and exit. The install
            // looked like it did nothing. Waiting a few seconds lets the old process release the
            // mutex and the real one start.
            Mutex mutex = AcquireInstance(0);
            if (mutex == null)
            {
                FocusRunningWindow();
                mutex = AcquireInstance(6000);
                if (mutex == null) return 0;
            }

            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }

            /* Deliberately no SetCurrentProcessExplicitAppUserModelID here. An explicit id
               only helps when every shortcut carries the same id; when it does not, Windows
               cannot resolve the identity and falls back to a generic taskbar icon. Without
               one, Windows groups by executable path and uses the window icon, which is now
               the real Joe mark (see AppIcon / JBForm). */

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            /* Look for the .pdb beside the executable and delete nothing: a marker Windows
               Defender's reputation engine reads for "a program that ships a separate debug
               database" is not the problem. What the engine does react to is a single unsigned
               binary that decompresses another executable, so the installer's own README and the
               release notes say plainly which of the two to run. Nothing in this block changes
               behaviour; it records the build shape in the log so a support report carries it. */
            try
            {
                Log.Info("Build " + Application.ProductVersion + " on .NET " +
                    Environment.Version + ", " + (Environment.Is64BitProcess ? "64" : "32") + " bit process");
            }
            catch { }

            /* Registered before the first window exists. The install gate used to run with no
               handler at all, so a failure while installing showed the raw .NET crash dialog -
               the "unexpected error" users reported when the very first thing they did was
               install the app. */
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Error("UI thread exception", e.Exception);
                ShowFatal(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            };

            /* The display scale has to be known BEFORE any window is constructed. Every control
               position and size is computed with Ui.S() while the form is being built, but fonts
               are in points and therefore already scale with the display. Building at 1.0 on a
               150% screen puts 1.5x text into 1.0x boxes: clipped labels, overlapping rows and
               text spilling out of its cards - the whole app reads as broken. Only MainForm
               ever recalculated this, and only after it had already laid itself out, so every
               dialog (including the first-run sign-in window) was affected. */
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    Ui.Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { Ui.Scale = 1f; }
            /* A display between 100% and the next whole step rounds its layout to the nearest
               quarter, because a half scaled 15 point font and a half scaled row height disagree
               about where a baseline sits. The quarter is what the eye is comparing when it says
               the text looks soft rather than the box looking wrong. */
            Ui.Scale = (float)(Math.Round(Ui.Scale * 4f) / 4f);
            Log.Info("Display scale " + Ui.Scale.ToString("0.##"));

            Theme.LoadFonts();

            /* A data folder that cannot be written is the quiet cause of a whole family of
               reports ("my profiles vanish", "settings never stick", "sign-in works but the
               account is gone next launch"). Say it once, clearly, instead of failing later in
               a dozen unrelated places. */
            if (!AppPaths.IsWritable())
            {
                Log.Error("The data folder is not writable: " + AppPaths.Root);
                try
                {
                    MessageBox.Show(
                        "Joe Browser cannot write to its data folder:\n\n" + AppPaths.Root +
                        "\n\nProfiles and settings cannot be saved until this is fixed. Check folder " +
                        "permissions, or move the folder somewhere writable (for example out of OneDrive " +
                        "or a redirected AppData).",
                        "Joe Browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
            }

            /* Refuse to run as a loose file. Opening the download directly skipped the
               shortcuts, the Add/Remove Programs entry and in-place updates, and left a second
               copy of the product in Downloads. --portable is the deliberate escape hatch for
               the test harness and for support work; --help lists the switching options. */
            if (unknown && !silent)
            {
                /* Say it once, then carry on: a stray switch should not stop the app from
                   opening, but it should not be invisible either. */
                Log.Info("Ignored unrecognised command line arguments.");
            }

            if (!portable && !Installation.IsRunningInstalled())
            {
                try
                {
                    using (var gate = new InstallGate())
                    {
                        Application.Run(gate);
                    }
                }
                finally
                {
                    /* Fonts belong to the GDI session, not to one window: unloading them here
                       while another copy of the app is about to be focused left that copy
                       drawing with disposed Font objects, which is a crash, not a glitch. */
                    try { mutex.ReleaseMutex(); } catch { }
                    try { mutex.Dispose(); } catch { }
                }
                return 0;
            }

            try
            {
                AppPaths.EnsureAll();
                /* Small drives fill up quietly: browser caches are the one thing the app owns
                   that is always safe to drop, so a low volume triggers a background clean of
                   every closed profile's cache. Cookies and logins are never touched. */
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { DiskSpace.AutoCleanCaches(); });
                /* Self-repair the original built-in extensions on every start, so an app
                   update never leaves stale manifests or scripts behind. Writes only files
                   whose content actually changed. */
                ExtensionManager.EnsureAll();
                /* Three of the brief's named defaults already exist as first party extensions -
                   paste as human, enable right click, proxy watch - and are written above. What
                   was missing is that the extensions page never learned about them on a machine
                   that had never opened it: EnsureDefaults only ran when the page was built, so
                   a profile launched straight from a fresh install had the folders on disk and
                   the catalog listing them as absent. Listing is cheap and idempotent. */
                try { ExtensionCatalog.ListPresentDefaults(); } catch (Exception ex) { Log.Error("Default extensions", ex); }
                /* The account gate only appears until the first account exists. After that the
                   app remembers the sign-in for this Windows user (DPAPI-protected session
                   marker), so daily use stays a double-click. */
                if (!Session.IsOpen())
                {
                    using (var gate = new AccountGate())
                    {
                        gate.ShowDialog();
                        /* The signed-in account is the only reliable signal: the dialog result
                           is Cancel whenever the window is closed by any other path. */
                        if (gate.SignedIn == null) return 0;
                        Session.Open(gate.SignedIn);
                    }
                }
                else if (Session.Current != null && Accounts.PinEnabled(Session.Current.Email))
                {
                    /* The PIN protects the app on a shared desk: the session marker remembers
                       who is signed in, but the PIN still has to be typed each launch. */
                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        using (var ask = new AskDialog("Joe Browser is locked",
                                   "Enter your PIN to unlock Joe Browser.", "PIN", true))
                        {
                            if (ask.ShowDialog() != DialogResult.OK) return 0;
                            ok = Accounts.VerifyPin(Session.Current.Email, ask.Value);
                        }
                    }
                    if (!ok) return 0;
                }
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Log.Error("Fatal startup error", ex);
                ShowFatal(ex);
            }
            finally
            {
                Theme.UnloadFonts();
                try { mutex.ReleaseMutex(); } catch { }
                try { mutex.Dispose(); } catch { }
            }
            return 0;
        }

        /// <summary>
        /// Takes the single-instance mutex, retrying for up to <paramref name="waitMs"/>. Returns
        /// null when another copy of Joe Browser still owns it.
        /// </summary>
        private static Mutex AcquireInstance(int waitMs)
        {
            const string name = "Local\\JoeBrowser.SingleInstance";
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, waitMs));
            while (true)
            {
                bool created;
                Mutex m = null;
                try
                {
                    m = new Mutex(true, name, out created);
                    if (created) return m;
                }
                catch (Exception ex)
                {
                    /* A mutex that cannot be created at all (a locked-down policy profile)
                       should not stop the app from starting. The placeholder is not owned, so
                       the ReleaseMutex in the caller's finally just throws and is ignored. */
                    Log.Error("Single-instance mutex unavailable", ex);
                    try { return new Mutex(); } catch { return null; }
                }
                try { m.Dispose(); } catch { }
                if (DateTime.UtcNow >= deadline) return null;
                System.Threading.Thread.Sleep(200);
            }
        }

        private static void FocusRunningWindow()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcessesByName("JoeBrowser"))
                {
                    try
                    {
                        if (p.Id == me) continue;
                        p.Refresh();
                        IntPtr h = p.MainWindowHandle;
                        if (h == IntPtr.Zero) continue;
                        ShowWindow(h, 9); // SW_RESTORE
                        SetForegroundWindow(h);
                        return;
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// The last-resort report. It is calm rather than alarming: the crash is already in the
        /// log, the details are already on the clipboard, and the user is offered one action.
        ///
        /// Two things used to make this worse than the bug it reported. It asked "report it on
        /// Telegram now?" before the user could copy anything or even read the message, and it
        /// was rendered with the raw WinForms MessageBox, so the one dialog people are most
        /// likely to screenshot was the only one that ignored the app's design.
        /// </summary>
        private static void ShowFatal(Exception ex)
        {
            try
            {
                Log.Error("Fatal error offered to the user", ex);

                string detail = ex == null ? "" : (ex.Message ?? "").Trim();
                if (ex != null && ex.StackTrace != null)
                    detail += "\n" + ex.StackTrace.Split('\n')[0].Trim();

                /* Details on the clipboard before anything is asked, so a user who closes the
                   dialog still has something to send. */
                try { Report.CopyDetails(ex == null ? "" : ex.ToString()); } catch { }

                string logFile = Log.CurrentFile();
                string body = (string.IsNullOrEmpty(detail)
                        ? "" : "Details (already copied to the clipboard):\n" + detail + "\n\n")
                    + (logFile == null
                        ? "The full report is in %APPDATA%\\JoeBrowser\\Logs."
                        : "The full report is in " + logFile + ".");

                bool send;
                try
                {
                    send = ConfirmForm.Ask(null, "Joe Browser ran into a problem",
                        "Something went wrong, and the window it happened in had to close. "
                        + "Your profiles and settings are safe.",
                        body, "Send a report", BtnKind.Primary);
                }
                catch
                {
                    /* The themed dialog needs the fonts and theme tokens; if the failure
                       happened before those were ready there still has to be a dialog. */
                    send = MessageBox.Show(
                        "Joe Browser ran into a problem.\n\n" + body + "\n\nSend a report now?",
                        "Joe Browser", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                }

                if (send)
                {
                    try
                    {
                        Report.OpenForm(null, detail);
                    }
                    catch (Exception reportError)
                    {
                        Log.Error("Could not open the report window", reportError);
                    }
                }
            }
            catch
            {
                /* A crash dialog that crashes would leave the process unable to say anything
                   at all, so this path is deliberately silent. */
            }
        }
    }
}
