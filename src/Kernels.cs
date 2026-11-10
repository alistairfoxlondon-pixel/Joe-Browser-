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

        public static Task<List<KernelRelease>> CatalogAsync(string engine)
        {
            return Task.Run(new Func<List<KernelRelease>>(() => Catalog(engine)));
        }

        private static List<KernelRelease> Catalog(string engine)
        {
            switch (engine)
            {
                /* The channel builds first - they are the ones a user is offered at the top and
                   the ones the newest version lives in - then a run of older exact versions so a
                   workspace of profiles can each have their own. Duplicates are dropped, since
                   the newest channel build is also the newest entry in the history. */
                case "chrome":
                {
                    var current = ChromeReleases();
                    var older = ChromeHistoryReleases(24);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var merged = new List<KernelRelease>();
                    foreach (var r in current)
                        if (r != null && seen.Add(r.Engine + "|" + r.Version)) merged.Add(r);
                    foreach (var r in older)
                        if (r != null && seen.Add(r.Engine + "|" + r.Version)) merged.Add(r);
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
        /// </summary>
        private static List<KernelRelease> ChromeHistoryReleases(int want)
        {
            var list = new List<KernelRelease>();
            try
            {
                var root = Json(Get(ChromeHistory)) as Dictionary<string, object>;
                var versions = root != null && root.ContainsKey("versions") ? root["versions"] as object[] : null;
                if (versions == null) return list;

                /* Walk backwards from the newest: the array is in release order, and the last
                   entries are the most recent builds. */
                for (int i = versions.Length - 1; i >= 0 && list.Count < want; i--)
                {
                    var v = versions[i] as Dictionary<string, object>;
                    if (v == null) continue;
                    string version = Str(v, "version");
                    if (string.IsNullOrEmpty(version)) continue;
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
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Chrome version history failed", ex); }
            return list;
        }

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
                    if (Bool(r, "prerelease")) continue;
                    string tag = Str(r, "tag_name");
                    if (string.IsNullOrEmpty(tag)) continue;
                    string version = tag.TrimStart('v', 'V');
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
                        list.Add(new KernelRelease("brave", version, "Stable", Str(asset, "browser_download_url"), "zip")
                        {
                            SizeMb = size / 1048576
                        });
                        break;
                    }
                    if (list.Count >= 8) break;
                }
            }
            catch (Exception ex) { Log.Error("Brave kernel catalog failed", ex); }
            return list;
        }

        private static List<KernelRelease> ChromiumReleases()
        {
            var list = new List<KernelRelease>();
            try
            {
                string rev = Get(ChromiumBase + "LAST_CHANGE").Trim();
                if (rev.Length == 0 || rev.Length > 20) return list;
                list.Add(new KernelRelease("chromium", "r" + rev, "Snapshot",
                    ChromiumBase + rev + "/chrome-win.zip", "zip") { SizeMb = 360 });
            }
            catch (Exception ex) { Log.Error("Chromium kernel catalog failed", ex); }
            return list;
        }

        /// <summary>Firefox portable zips from the Mozilla archive. Stable and Beta are the two
        /// channels Mozilla officially ships for Windows; the URL pattern is stable across every
        /// version, so a pinned kernel is one fetch away for any release the catalog lists.</summary>
        private static List<KernelRelease> FirefoxReleases()
        {
            var list = new List<KernelRelease>();
            try
            {
                var root = Json(Get(FirefoxVersions)) as Dictionary<string, object>;
                if (root == null) return list;
                string stable = Str(root, "LATEST_FIREFOX_VERSION");
                string beta = Str(root, "LATEST_FIREFOX_DEVEL_VERSION");
                if (!string.IsNullOrEmpty(stable))
                    list.Add(new KernelRelease("firefox", stable, "Stable",
                        string.Format(FirefoxZipUrl, stable), "zip") { SizeMb = 60 });
                if (!string.IsNullOrEmpty(beta) && !string.Equals(beta, stable, StringComparison.OrdinalIgnoreCase))
                    list.Add(new KernelRelease("firefox", beta, "Beta",
                        string.Format(FirefoxZipUrl, beta), "zip") { SizeMb = 60 });
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
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "JoeBrowser/1.0";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.KeepAlive = false;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
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
