using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>
    /// One build of one engine that Joe Browser can place on this machine. AdsPower calls these
    /// kernels; the point is the same - a profile is pinned to an exact browser build, several
    /// builds live side by side, and a profile never silently rides a different version because
    /// the user updated their everyday browser.
    /// </summary>
    internal sealed class KernelRelease
    {
        public string Engine;     // chrome | brave | chromium
        public string Version;    // folder name and the value stored on the profile
        public string Label;      // what the picker shows
        public string Channel;    // Stable / Beta / Dev / Canary / ESR / Snapshot
        public string Url;
        public string Kind;       // zip | msiexec
        public long SizeMb;

        public KernelRelease(string engine, string version, string channel, string url, string kind)
        {
            Engine = engine;
            Version = version;
            Channel = channel;
            Url = url;
            Kind = kind;
            Label = version;
        }
    }

    /// <summary>A build that is already on disk.</summary>
    internal sealed class KernelInstall
    {
        public string Engine;
        public string Version;
        public string Dir;
        public string Exe;
        public string FileVersion;

        public bool Ready { get { return !string.IsNullOrEmpty(Exe) && File.Exists(Exe); } }

        public string Label
        {
            get { return Version + (string.IsNullOrEmpty(FileVersion) ? "" : "  \u00b7  " + FileVersion); }
        }
    }

    /// <summary>
    /// Downloads and manages pinned engine builds. Everything here is a portable layout:
    /// Chrome for Testing, Brave, Firefox and the Chromium snapshot all publish a plain Windows
    /// zip, so a build is unpacked into its own folder and never registered with Windows.
    ///
    /// Edge is the exception. Microsoft ships only the enterprise MSI, whose payload is an
    /// embedded 7z that Windows Installer expands during a real install. An administrative
    /// install (msiexec /a) copies the payload out without registering Edge with Windows, so
    /// the build lands under the kernels folder the same way the others do - this is the
    /// same trick the old "Firefox is deliberately absent" comment worried about, solved with
    /// a documented Windows feature instead of a 7z decoder.
    /// </summary>
    internal static class KernelStore
    {
        public static readonly string[] Engines = { "chrome", "brave", "chromium", "firefox", "edge" };

        private const string ChromeCatalog =
            "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json";
        /* Recent releases with their per version download links. The catalog above only carries
           the newest build per channel - four builds in total - and "every profile gets its own
           browser version" cannot hold across a workspace of ten with four of them. This
           endpoint lists the last hundred known good versions, each with its own url, so a
           profile can be pinned to a build nobody else is using. */
        private const string ChromeHistory =
            "https://googlechromelabs.github.io/chrome-for-testing/known-good-versions-with-downloads.json";
        private const string BraveCatalog =
            "https://api.github.com/repos/brave/brave-browser/releases?per_page=10";
        private const string ChromiumBase =
            "https://storage.googleapis.com/chromium-browser-snapshots/Win_x64/";
        /* Mozilla publishes a portable win64 zip for every Firefox release at a stable URL.
           The product-details JSON lists the current Stable and Beta (Devel) versions, so a
           pinned kernel catalog is one round-trip the way Chrome's is. */
        private const string FirefoxVersions =
            "https://product-details.mozilla.org/1.0/firefox_versions.json";
        private const string FirefoxZipUrl =
            "https://archive.mozilla.org/pub/firefox/releases/{0}/win64/en-US/Firefox%20Setup%20{0}.zip";
        /* Edge: the enterprise MSI is the only published artifact. The Winget / Edge update
           APIs redirect here in the end. The version string is taken from the URL the redirect
           lands on, so a pinned kernel catalog needs the latest two or three versions. */
        private const string EdgeVersions =
            "https://edgeupdates.microsoft.com/api/v1/edge/business/stable?arch=x64&platform=windows&appid=Microsoft Edge";

        /* Chromium publishes a version number for every snapshot through the dashboard API,
           and the same call carries the branch position - the snapshot revision the zip is
           published under. That is what makes a real Chromium build with a real version
           number possible, instead of the single "latest snapshot" the storage bucket
           names on its own. */
        private const string ChromiumReleaseApi =
            "https://chromiumdash.appspot.com/fetch_releases?channel={0}&platform=Windows&num={1}";

        public static string Root { get { return AppPaths.Kernels; } }

        public static string DirFor(string engine, string version)
        {
            return Path.Combine(Root, Safe(engine), Safe(version));
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ installed

        /// <summary>Every build already unpacked under the kernels folder.</summary>
        public static List<KernelInstall> Installed()
        {
            var list = new List<KernelInstall>();
            try
            {
                if (!Directory.Exists(Root)) return list;
                foreach (string engineDir in Directory.GetDirectories(Root))
                {
                    string engine = Path.GetFileName(engineDir).ToLowerInvariant();
                    if (Array.IndexOf(Engines, engine) < 0) continue;
                    foreach (string versionDir in Directory.GetDirectories(engineDir))
                    {
                        string exe = FindExe(versionDir, engine);
                        if (exe == null) continue;
                        var k = new KernelInstall
                        {
                            Engine = engine,
                            Version = Path.GetFileName(versionDir),
                            Dir = versionDir,
                            Exe = exe
                        };
                        try
                        {
                            var fvi = FileVersionInfo.GetVersionInfo(exe);
                            k.FileVersion = fvi.ProductVersion ?? fvi.FileVersion ?? "";
                        }
                        catch { }
                        list.Add(k);
                    }
                }
            }
            catch (Exception ex) { Log.Error("Kernel scan failed", ex); }
            return list;
        }

        public static KernelInstall Find(string engine, string version)
        {
            if (string.IsNullOrEmpty(engine) || string.IsNullOrEmpty(version)) return null;
            foreach (var k in Installed())
                if (string.Equals(k.Engine, engine, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(k.Version, version, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        /// <summary>The executable a profile pinned to this build should launch, or null.</summary>
        public static string ResolveExe(string engine, string version)
        {
            var k = Find(engine, version);
            return k != null && k.Ready ? k.Exe : null;
        }

        /// <summary>Best installed build for an engine, newest file version first.</summary>
        public static KernelInstall Newest(string engine)
        {
            KernelInstall best = null;
            foreach (var k in Installed())
            {
                if (!string.Equals(k.Engine, engine, StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || string.CompareOrdinal(VersionKey(k), VersionKey(best)) > 0) best = k;
            }
            return best;
        }

        private static string VersionKey(KernelInstall k)
        {
            string v = string.IsNullOrEmpty(k.FileVersion) ? k.Version : k.FileVersion;
            string[] parts = v.Split('.');
            var sb = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                int n = 0;
                if (i < parts.Length) int.TryParse(parts[i], out n);
                sb.Append(n.ToString("D6"));
            }
            return sb.ToString();
        }

        private static string FindExe(string dir, string engine)
        {
            string[] want = engine == "firefox" ? new[] { "firefox.exe" }
                : engine == "edge" ? new[] { "msedge.exe" }
                : engine == "brave" ? new[] { "brave.exe" }
                : new[] { "chrome.exe" };
            // Shallow first: the published archives put the binary one or two levels down.
            for (int depth = 0; depth <= 3; depth++)
            {
                string found = Walk(dir, want, depth, 0);
                if (found != null) return found;
            }
            return null;
        }

        private static string Walk(string dir, string[] want, int maxDepth, int depth)
        {
            try
            {
                foreach (string w in want)
                {
                    string p = Path.Combine(dir, w);
                    if (File.Exists(p)) return p;
                }
                if (depth >= maxDepth) return null;
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string r = Walk(sub, want, maxDepth, depth + 1);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        public static void Delete(KernelInstall k)
        {
            if (k == null || string.IsNullOrEmpty(k.Dir)) return;
            // Only ever delete inside the kernels root, never a path a caller invented.
            string full = Path.GetFullPath(k.Dir);
            if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)) return;
            try { Directory.Delete(full, true); }
            catch (Exception ex) { Log.Error("Kernel delete failed", ex); throw; }
            /* The inventory other code reasons about just changed; a stale list would let a
               profile keep pinning a build that no longer exists. */
            KernelDiversity.Invalidate();
        }

        // ------------------------------------------------------------------ catalog

        /// <summary>
        /// The catalogue of an engine, kept for the length of the session. Fetching it costs
        /// a round-trip to the publisher, and the profile that opens a pinned build asks for
        /// the same list the browsers page just looked at.
        /// </summary>
        private static readonly Dictionary<string, List<KernelRelease>> CatalogCache =
            new Dictionary<string, List<KernelRelease>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> CatalogCacheAt =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CatalogGate = new object();

        /// <summary>Forgets the cached catalogue, so the next read goes to the publisher.</summary>
        public static void ClearCatalogCache()
        {
            lock (CatalogGate)
            {
                CatalogCache.Clear();
                CatalogCacheAt.Clear();
            }
        }

        /// <summary>
        /// The published build of one engine and one version, or null when the publisher does
        /// not offer it. The catalogue is read once per engine.
        /// </summary>
        public static KernelRelease FindRelease(string engine, string version)
        {
            if (string.IsNullOrEmpty(engine) || string.IsNullOrEmpty(version)) return null;
            List<KernelRelease> list = CatalogCached(engine);
            foreach (var r in list)
                if (r != null && string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        /// <summary>
        /// The catalogue of an engine, read once and kept for a while. Asking a publisher for
        /// the same list twice costs the user two slow round trips, and the list changes once
        /// a week at most.
        /// </summary>
        public static List<KernelRelease> CatalogCached(string engine)
        {
            List<KernelRelease> list;
            lock (CatalogGate)
            {
                DateTime when;
                if (CatalogCache.TryGetValue(engine, out list) &&
                    CatalogCacheAt.TryGetValue(engine, out when) &&
                    (DateTime.UtcNow - when).TotalMinutes < 20)
                    return list;
            }
            list = Catalog(engine);
            lock (CatalogGate)
            {
                CatalogCache[engine] = list;
                CatalogCacheAt[engine] = DateTime.UtcNow;
            }
            return list;
        }

        /// <summary>
        /// Installs the newest build an engine publishes. This is what the profile editor
        /// calls when the user asks for an engine that is not on this machine: the build goes
        /// into the application's own folder, is managed from the Browsers page, and can be
        /// pinned to a profile - rather than an installer that writes all over Windows and
        /// leaves the app dependent on the machine's browser.
        /// </summary>
        public static async Task<KernelInstall> InstallNewestAsync(string engine,
            Action<int, string> progress, CancellationToken ct)
        {
            var catalog = CatalogCached(engine);
            if (catalog == null || catalog.Count == 0)
                throw new InvalidOperationException(
                    "No " + (engine ?? "") + " build is published right now. Try again later, or pick another engine.");
            var rel = catalog[0];
            var installed = await InstallAsync(rel, progress, ct).ConfigureAwait(false);
            KernelDiversity.Invalidate();
            return installed;
        }

        /// <summary>True when the build a profile is pinned to is on this machine and ready.</summary>
        public static bool IsReady(string engine, string version)
        {
            var k = Find(engine, version);
            return k != null && k.Ready;
        }

        /// <summary>
        /// Makes sure a pinned build is on this machine, fetching it when it is not.
        ///
        /// This is the rule the whole browser manager is built around: nothing is downloaded
        /// until something needs it. A profile pinned to a build that is not here yet asks
        /// for exactly that build, and gets it - with the download visible - instead of
        /// quietly falling back to whatever browser happens to be installed.
        /// </summary>
        public static async Task<KernelInstall> EnsureAsync(string engine, string version,
            Action<int, string> progress, CancellationToken ct)
        {
            var have = Find(engine, version);
            if (have != null && have.Ready) return have;

            var rel = FindRelease(engine, version);
            if (rel == null)
            {
                throw new InvalidOperationException(
                    "The publisher no longer offers " + (engine ?? "") + " " + (version ?? "") +
                    ". Choose another build on the Browsers page, or let the profile use the installed browser.");
            }
            var installed = await InstallAsync(rel, progress, ct).ConfigureAwait(false);
            KernelDiversity.Invalidate();
            return installed;
        }

        public static Task<List<KernelRelease>> CatalogAsync(string engine)
        {
            return Task.Run(new Func<List<KernelRelease>>(() => Catalog(engine)));
        }

        /// <summary>
        /// The builds an engine publishes, newest first. A call that cannot reach the
        /// publisher returns an empty list rather than throwing: the caller shows the
        /// difference between "nothing published" and "could not ask".
        /// </summary>
        public static List<KernelRelease> Catalog(string engine)
        {
            switch (engine)
            {
                /* The channel builds first - they are the ones a user is offered at the top and
                   the ones the newest version lives in - then a run of older exact versions so a
                   workspace of profiles can each have their own. Duplicates are dropped, since
                   the newest channel build is also the newest entry in the history. */
                case "chrome":
                {
                    /* The channel list and the history are asked for at the same time. The
                       history file carries every release Chrome ever published and takes
                       seconds on its own; asking for the two one after the other doubled the
                       wait for the page. */
                    var currentTask = Task.Run(new Func<List<KernelRelease>>(ChromeReleases));
                    var olderTask = Task.Run(new Func<List<KernelRelease>>(delegate
                    {
                        return ChromeHistoryReleases(ChromeMajors, ChromePerMajor);
                    }));
                    List<KernelRelease> current = null, older = null;
                    try { current = currentTask.Result; } catch (Exception ex) { Log.Error("Chrome catalogue failed", ex); }
                    try { older = olderTask.Result; } catch (Exception ex) { Log.Error("Chrome history failed", ex); }
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var merged = new List<KernelRelease>();
                    if (current != null)
                        foreach (var r in current)
                            if (r != null && seen.Add(r.Engine + "|" + r.Version)) merged.Add(r);
                    if (older != null)
                        foreach (var r in older)
                            if (r != null && seen.Add(r.Engine + "|" + r.Version)) merged.Add(r);
                    /* Newest first, the way every browser manager lists them. */
                    merged.Sort(delegate(KernelRelease a, KernelRelease b)
                    {
                        int ma = BrowserVersions.MajorOf(a.Version);
                        int mb = BrowserVersions.MajorOf(b.Version);
                        if (ma != mb) return mb.CompareTo(ma);
                        return string.CompareOrdinal(b.Version, a.Version);
                    });
                    return merged;
                }
                case "brave": return BraveReleases();
                case "chromium": return ChromiumReleases();
                case "firefox": return FirefoxReleases();
                case "edge": return EdgeReleases();
                default: return new List<KernelRelease>();
            }
        }

        private static List<KernelRelease> ChromeReleases()
        {
            var list = new List<KernelRelease>();
            try
            {
                var root = Json(Get(ChromeCatalog)) as Dictionary<string, object>;
                var channels = root != null ? root["channels"] as Dictionary<string, object> : null;
                if (channels == null) return list;
                string[] order = { "Stable", "Beta", "Dev", "Canary" };
                foreach (string ch in order)
                {
                    object raw;
                    if (!channels.TryGetValue(ch, out raw)) continue;
                    var c = raw as Dictionary<string, object>;
                    if (c == null) continue;
                    string version = Str(c, "version");
                    var downloads = c.ContainsKey("downloads") ? c["downloads"] as Dictionary<string, object> : null;
                    if (downloads == null) continue;
                    var chrome = downloads.ContainsKey("chrome") ? downloads["chrome"] as object[] : null;
                    if (chrome == null) continue;
                    foreach (object o in chrome)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        if (!string.Equals(Str(d, "platform"), "win64", StringComparison.OrdinalIgnoreCase)) continue;
                        string url = Str(d, "url");
                        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(version)) continue;
                        list.Add(new KernelRelease("chrome", version, ch, url, "zip") { SizeMb = 170 });
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Chrome kernel catalog failed", ex); }
            return list;
        }

        /// <summary>
        /// The newest <paramref name="want"/> stable Chrome builds before the current one, so a
        /// workspace can be given distinct versions. Best effort: an offline machine gets an
        /// empty list and keeps working on what it has.
        ///
        /// The history is walked by MAJOR, not by release: the last twenty-four releases are
        /// all one major, and a user who asks for version 156 wants 156 - not four more
        /// builds of 159 with a different patch number. So the list covers the last few
        /// majors, with a few builds from each.
        /// </summary>
        private static List<KernelRelease> ChromeHistoryReleases(int majors, int perMajor)
        {
            var list = new List<KernelRelease>();
            try
            {
                var root = Json(Get(ChromeHistory)) as Dictionary<string, object>;
                var versions = root != null && root.ContainsKey("versions") ? root["versions"] as object[] : null;
                if (versions == null) return list;

                var perRelease = new Dictionary<int, int>();
                /* Walk backwards from the newest: the array is in release order, and the last
                   entries are the most recent builds. */
                for (int i = versions.Length - 1; i >= 0 && perRelease.Count < majors; i--)
                {
                    var v = versions[i] as Dictionary<string, object>;
                    if (v == null) continue;
                    string version = Str(v, "version");
                    if (string.IsNullOrEmpty(version)) continue;
                    int major = BrowserVersions.MajorOf(version);
                    if (major <= 0) continue;
                    int taken;
                    perRelease.TryGetValue(major, out taken);
                    if (taken >= perMajor) continue;
                    var downloads = v.ContainsKey("downloads") ? v["downloads"] as Dictionary<string, object> : null;
                    if (downloads == null) continue;
                    var chrome = downloads.ContainsKey("chrome") ? downloads["chrome"] as object[] : null;
                    if (chrome == null) continue;
                    foreach (object o in chrome)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        if (!string.Equals(Str(d, "platform"), "win64", StringComparison.OrdinalIgnoreCase)) continue;
                        string url = Str(d, "url");
                        if (string.IsNullOrEmpty(url)) continue;
                        /* An exact version is a whole release: the fourth part identifies the
                           build, so two profiles pinned to 131.0.6778.86 and .204 are two builds
                           that a page can tell apart. */
                        list.Add(new KernelRelease("chrome", version, "Stable", url, "zip") { SizeMb = 170 });
                        perRelease[major] = taken + 1;
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Chrome version history failed", ex); }
            return list;
        }

        /// <summary>How many majors of Chrome the catalogue reaches back to.</summary>
        private const int ChromeMajors = 8;

        /// <summary>How many builds of each of those majors are offered.</summary>
        private const int ChromePerMajor = 3;

        private static List<KernelRelease> BraveReleases()
        {
            var list = new List<KernelRelease>();
            try
            {
                var arr = Json(Get(BraveCatalog)) as object[];
                if (arr == null) return list;
                foreach (object o in arr)
                {
                    var r = o as Dictionary<string, object>;
                    if (r == null) continue;
                    string tag = Str(r, "tag_name");
                    if (string.IsNullOrEmpty(tag)) continue;
                    string version = tag.TrimStart('v', 'V');
                    /* Brave publishes its beta as a pre-release with the same file naming, so
                       the channel is taken from the tag instead of the release being dropped.
                       A user who wants the beta channel gets the beta channel. */
                    string channel = "Stable";
                    if (version.IndexOf("-beta", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        channel = "Beta";
                        int dash = version.IndexOf('-');
                        if (dash > 0) version = version.Substring(0, dash);
                    }
                    else if (version.IndexOf("-nightly", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        channel = "Dev";
                        int dash = version.IndexOf('-');
                        if (dash > 0) version = version.Substring(0, dash);
                    }
                    var assets = r.ContainsKey("assets") ? r["assets"] as object[] : null;
                    if (assets == null) continue;
                    foreach (object a in assets)
                    {
                        var asset = a as Dictionary<string, object>;
                        if (asset == null) continue;
                        string name = Str(asset, "name");
                        // Skip symbols and Origin builds; the browser itself is the plain win32-x64 zip.
                        if (!name.EndsWith("-win32-x64.zip", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.IndexOf("symbols", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (name.IndexOf("origin", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (!name.StartsWith("brave-v", StringComparison.OrdinalIgnoreCase)) continue;
                        long size = 0;
                        object sz;
                        if (asset.TryGetValue("size", out sz)) long.TryParse(Convert.ToString(sz), out size);
                        list.Add(new KernelRelease("brave", version, channel, Str(asset, "browser_download_url"), "zip")
                        {
                            SizeMb = size / 1048576
                        });
                        break;
                    }
                    if (list.Count >= 16) break;
                }
            }
            catch (Exception ex) { Log.Error("Brave kernel catalog failed", ex); }
            return list;
        }

        /// <summary>
        /// Chromium builds with their real version numbers, taken from the dashboard API.
        /// Every release carries the branch position it was cut from, and that position is
        /// the snapshot revision the archive is published under - so the catalogue can offer
        /// "159.0.8154.0" and know exactly which zip holds it.
        ///
        /// The four channels Chromium ships are all listed, and every one of them offers
        /// several releases, so a workspace can spread across versions the way it can with
        /// Chrome.
        /// </summary>
        private static List<KernelRelease> ChromiumReleases()
        {
            /* How many releases of each channel are read. The stable list is the long one:
               it is the list that carries the history of older majors, which is what a
               workspace needs when it wants profiles on several different versions. */
            string[] channels = { "Canary", "Dev", "Beta", "Stable" };
            int[] counts = { 6, 8, 10, 30 };

            /* The channels are asked for at the same time. Asked one after the other they
               took twenty seconds, and the page sat on "loading" for all of it - which reads
               as a broken application rather than a slow network. */
            var tasks = new Task<List<KernelRelease>>[channels.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(new Func<List<KernelRelease>>(delegate
                {
                    return ChromiumChannelReleases(channels[index], counts[index]);
                }));
            }
            try { Task.WaitAll(tasks, 45000); } catch { }

            var perChannel = new List<KernelRelease>[channels.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                List<KernelRelease> part = null;
                try { part = tasks[i].Result; } catch { }
                perChannel[i] = part ?? new List<KernelRelease>();
            }

            /* A release belongs to one channel, and the publishers list a build under
               several of them once it has been promoted. The current release of a channel is
               the newest entry of its list and is labelled with that channel; anything else
               is labelled with the least advanced channel that lists it, because a build
               that was stable stays stable. */
            var channelsOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var newest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var urls = new Dictionary<string, KernelRelease>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < channels.Length; i++)
            {
                var part = perChannel[i];
                if (part.Count == 0) continue;
                newest[channels[i]] = part[0].Version;
                foreach (var r in part)
                {
                    if (r == null) continue;
                    urls[r.Version] = r;
                    List<string> owners;
                    if (!channelsOf.TryGetValue(r.Version, out owners))
                    {
                        owners = new List<string>();
                        channelsOf[r.Version] = owners;
                    }
                    if (!owners.Contains(channels[i])) owners.Add(channels[i]);
                }
            }

            /* Stable, Beta, Dev, Canary: the order the channels sit in. */
            string[] rank = { "Stable", "Beta", "Dev", "Canary" };
            var list = new List<KernelRelease>();
            foreach (var kv in urls)
            {
                string version = kv.Key;
                var owners = channelsOf[version];
                string label = null;
                /* The current release of a channel is labelled with that channel. */
                foreach (string ch in owners)
                    if (newest.ContainsKey(ch) && string.Equals(newest[ch], version, StringComparison.OrdinalIgnoreCase))
                        label = ch;
                if (label == null)
                {
                    /* Otherwise the least advanced channel that lists it. */
                    foreach (string ch in rank)
                        if (owners.Contains(ch)) { label = ch; break; }
                }
                if (label == null && owners.Count > 0) label = owners[0];
                var src = kv.Value;
                list.Add(new KernelRelease("chromium", version, label ?? "Snapshot", src.Url, src.Kind)
                {
                    SizeMb = src.SizeMb
                });
            }

            /* Newest first: the build a user is most likely to want is at the top, and the
               older majors are still there underneath it. */
            list.Sort(delegate(KernelRelease a, KernelRelease b)
            {
                int ma = BrowserVersions.MajorOf(a.Version);
                int mb = BrowserVersions.MajorOf(b.Version);
                if (ma != mb) return mb.CompareTo(ma);
                return string.CompareOrdinal(b.Version, a.Version);
            });
            return list;
        }

        /// <summary>Compares two four part versions. Returns a negative number when a is older.</summary>
        private static int CompareVersions(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : -1;
            if (string.IsNullOrEmpty(b)) return 1;
            string[] pa = a.Split('.'), pb = b.Split('.');
            for (int i = 0; i < 4; i++)
            {
                int x = 0, y = 0;
                if (i < pa.Length) int.TryParse(pa[i], out x);
                if (i < pb.Length) int.TryParse(pb[i], out y);
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        /// <summary>One channel's Chromium releases.</summary>
        private static List<KernelRelease> ChromiumChannelReleases(string channel, int count)
        {
            var list = new List<KernelRelease>();
            try
            {
                string url = string.Format(ChromiumReleaseApi, channel, count);
                var arr = Json(Get(url)) as object[];
                if (arr == null) return list;
                foreach (object o in arr)
                {
                    var r = o as Dictionary<string, object>;
                    if (r == null) continue;
                    string version = Str(r, "version");
                    if (string.IsNullOrEmpty(version)) continue;
                    long position = 0;
                    object pos;
                    if (!r.TryGetValue("chromium_main_branch_position", out pos)) continue;
                    long.TryParse(Convert.ToString(pos), out position);
                    if (position <= 0) continue;
                    list.Add(new KernelRelease("chromium", version, channel,
                        ChromiumBase + position + "/chrome-win.zip", "zip") { SizeMb = 360 });
                }
            }
            catch (Exception ex) { Log.Error("Chromium " + channel + " catalogue failed", ex); }
            return list;
        }

        /// <summary>Firefox portable zips from the Mozilla archive. Stable, Beta, ESR and Nightly
        /// are the four releases Mozilla ships for Windows; the URL pattern is stable across
        /// every version, so a pinned kernel is one fetch away for any release the catalog
        /// lists.</summary>
        private static List<KernelRelease> FirefoxReleases()
        {
            var list = new List<KernelRelease>();
            try
            {
                var root = Json(Get(FirefoxVersions)) as Dictionary<string, object>;
                if (root == null) return list;
                string stable = Str(root, "LATEST_FIREFOX_VERSION");
                string beta = Str(root, "LATEST_FIREFOX_DEVEL_VERSION");
                string esr = Str(root, "LATEST_FIREFOX_ESR_VERSION");
                if (!string.IsNullOrEmpty(stable))
                    list.Add(new KernelRelease("firefox", stable, "Stable",
                        string.Format(FirefoxZipUrl, stable), "zip") { SizeMb = 60 });
                if (!string.IsNullOrEmpty(beta) && !string.Equals(beta, stable, StringComparison.OrdinalIgnoreCase))
                    list.Add(new KernelRelease("firefox", beta, "Beta",
                        string.Format(FirefoxZipUrl, beta), "zip") { SizeMb = 60 });
                if (!string.IsNullOrEmpty(esr) && !string.Equals(esr, stable, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(esr, beta, StringComparison.OrdinalIgnoreCase))
                    list.Add(new KernelRelease("firefox", esr, "ESR",
                        string.Format(FirefoxZipUrl, esr), "zip") { SizeMb = 60 });
            }
            catch (Exception ex) { Log.Error("Firefox kernel catalog failed", ex); }
            return list;
        }

        /// <summary>Edge enterprise MSIs from the Microsoft Edge updates API. The MSI is the
        /// only published artifact; an administrative install (msiexec /a) extracts its payload
        /// into the kernels folder without registering Edge with Windows, the same way the
        /// zip-based engines land. Only the Stable channel is published for business; the API
        /// returns one current version with its MSI URL.</summary>
        private static List<KernelRelease> EdgeReleases()
        {
            var list = new List<KernelRelease>();
            try
            {
                var root = Json(Get(EdgeVersions)) as Dictionary<string, object>;
                if (root == null) return list;
                string version = Str(root, "Version");
                if (string.IsNullOrEmpty(version))
                {
                    // The API sometimes wraps the version inside the products array.
                    var products = root.ContainsKey("Products") ? root["Products"] as object[] : null;
                    if (products != null && products.Length > 0)
                    {
                        var first = products[0] as Dictionary<string, object>;
                        if (first != null) version = Str(first, "Version");
                    }
                }
                if (string.IsNullOrEmpty(version)) return list;

                string url = null;
                var artifacts = root.ContainsKey("Artifacts") ? root["Artifacts"] as object[] : null;
                if (artifacts != null)
                {
                    foreach (object o in artifacts)
                    {
                        var a = o as Dictionary<string, object>;
                        if (a == null) continue;
                        string artifactName = Str(a, "ArtifactName");
                        if (!string.Equals(artifactName, "msi", StringComparison.OrdinalIgnoreCase)) continue;
                        var locations = a.ContainsKey("Locations") ? a["Locations"] as object[] : null;
                        if (locations == null || locations.Length == 0) continue;
                        var loc = locations[0] as Dictionary<string, object>;
                        if (loc != null) { url = Str(loc, "Url"); break; }
                    }
                }
                if (string.IsNullOrEmpty(url)) return list;
                list.Add(new KernelRelease("edge", version, "Stable", url, "msi") { SizeMb = 180 });
            }
            catch (Exception ex) { Log.Error("Edge kernel catalog failed", ex); }
            return list;
        }

        // ------------------------------------------------------------------ install

        public static Task<KernelInstall> InstallAsync(KernelRelease rel, Action<int, string> progress, CancellationToken ct)
        {
            return Task.Run(new Func<KernelInstall>(() => Install(rel, progress, ct)));
        }

        private static KernelInstall Install(KernelRelease rel, Action<int, string> progress, CancellationToken ct)
        {
            if (rel == null) throw new ArgumentNullException("rel");
            string dir = DirFor(rel.Engine, rel.Version);
            Directory.CreateDirectory(dir);

            long need = ((rel.SizeMb > 0 ? rel.SizeMb : 200) * 4 + 300) * 1048576L;
            DiskSpace.Require(dir, need, "download " + rel.Engine + " " + rel.Version);

            string tmp = Path.Combine(dir, "dl-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            string file = Path.Combine(tmp, rel.Kind == "msi" ? "build.msi" : "build.zip");
            try
            {
                try
                {
                    progress?.Invoke(2, "Connecting\u2026");
                    var ignored = ChromiumDownloader.DownloadFileAsync(rel.Url, file,
                        (pct, msg) => progress?.Invoke(pct, msg), rel.Engine + " " + rel.Version, ct);
                    ignored.GetAwaiter().GetResult();

                    progress?.Invoke(100, "Unpacking\u2026");
                    if (rel.Kind == "msi") ExtractMsi(file, dir);
                    else ExtractZip(file, dir);
                }
                catch
                {
                    try { Directory.Delete(dir, true); } catch { }
                    throw;
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }

            string exe = FindExe(dir, rel.Engine);
            if (exe == null)
            {
                try { Directory.Delete(dir, true); } catch { }
                throw new InvalidOperationException("The " + rel.Engine + " build unpacked but no executable was found.");
            }

            var k = new KernelInstall { Engine = rel.Engine, Version = rel.Version, Dir = dir, Exe = exe };
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(exe);
                k.FileVersion = fvi.ProductVersion ?? fvi.FileVersion ?? "";
            }
            catch { }
            Log.Info("Kernel installed: " + rel.Engine + " " + rel.Version + " -> " + exe);
            KernelDiversity.Invalidate();
            return k;
        }

        private static void ExtractMsi(string msi, string dir)
        {
            /* msiexec /a performs an administrative install: it extracts the payload of an MSI
               into a target folder without registering the product with Windows. Edge's
               enterprise MSI unpacks into a Microsoft\Edge\Application\msedge.exe layout, so the
               build lands inside the kernels folder the same way a zip-based engine does. The
               process is waited on synchronously because the kernel is not ready until the
               payload is on disk. */
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = "/a \"" + msi + "\" /qn TARGETDIR=\"" + dir + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("msiexec could not be started.");
                if (!p.WaitForExit(300000))
                {
                    try { p.Kill(); } catch { }
                    throw new InvalidOperationException("The Edge MSI administrative install did not finish in time.");
                }
                if (p.ExitCode != 0)
                    throw new InvalidOperationException("msiexec exited with code " + p.ExitCode + ".");
            }
        }

        private static void ExtractZip(string zip, string dir)
        {
            string root = Path.GetFullPath(dir);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;
            int failed = 0;
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;   // directory entry
                    string dest = Path.GetFullPath(Path.Combine(dir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    /* Zip-slip: an archive is untrusted input, and a ".." entry used to be
                       extracted wherever it pointed - outside the kernels root. */
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The archive contained an unsafe path: " + entry.FullName);
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    // A single failed file (locked by an antivirus scan) must not lose the build.
                    try { entry.ExtractToFile(dest, true); }
                    catch (Exception)
                    {
                        Thread.Sleep(120);
                        try { entry.ExtractToFile(dest, true); }
                        catch { failed++; }
                    }
                }
            }
            if (failed > 0)
                Log.Error("Kernel unpack lost " + failed + " file(s) to locks; the build may be incomplete");
        }

        // ------------------------------------------------------------------ json helpers

        private static string Get(string url)
        {
            /* One request per call, but the catalogues are fetched in the background while
               the user is already looking at the page, so the connection limit is raised.
               Two is the framework default and it is not enough when four publishers are
               asked at once. */
            try { if (ServicePointManager.DefaultConnectionLimit < 8) ServicePointManager.DefaultConnectionLimit = 8; }
            catch { }
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Exception last = null;
            /* Two goes: a publisher that answers slowly once is a publisher that answers
               slowly again, and the page should not be left on "loading" because of it. */
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "JoeBrowser/1.0";
                    req.Timeout = attempt == 0 ? 15000 : 30000;
                    req.ReadWriteTimeout = 30000;
                    req.KeepAlive = false;
                    req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);
                    if (Log.IsDiagnostic)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var px = req.Proxy == null ? "(none)" : req.Proxy.GetProxy(req.RequestUri).ToString();
                            Log.Info("NET " + url + " via " + px);
                            var ips = System.Net.Dns.GetHostAddresses(req.RequestUri.Host);
                            foreach (var ip in ips) Log.Info("NET  dns " + req.RequestUri.Host + " -> " + ip);
                        }
                        catch (Exception ex) { Log.Info("NET  dns failed: " + ex.Message); }
                        Log.Info("NET  started in " + sw.ElapsedMilliseconds + "ms");
                    }
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        return sr.ReadToEnd();
                }
                catch (Exception ex)
                {
                    last = ex;
                    Log.Error("Catalogue request failed: " + url, ex);
                }
            }
            if (last != null) throw last;
            return null;
        }

        private static object Json(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text); }
            catch { return null; }
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return false;
            bool b;
            return v is bool ? (bool)v : bool.TryParse(Convert.ToString(v), out b) && b;
        }
    }
}
