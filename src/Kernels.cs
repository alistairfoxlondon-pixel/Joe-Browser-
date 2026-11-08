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
    /// Chrome for Testing, Brave and the Chromium snapshot all publish a plain Windows zip,
    /// so a build is unpacked into its own folder and never registered with Windows.
    ///
    /// Firefox is deliberately absent. Mozilla ships only installers for Windows, and the MSI
    /// carries its payload as an embedded 7z that Windows Installer expands only during a real
    /// install - an administrative install copies the MSI alone. A portable Firefox would mean
    /// either registering a second Firefox with Windows (which can replace the user's own) or
    /// shipping a 7z decoder. Firefox profiles keep using the installed Firefox instead, which
    /// is also what AdsPower offers.
    /// </summary>
    internal static class KernelStore
    {
        public static readonly string[] Engines = { "chrome", "brave", "chromium" };

        private const string ChromeCatalog =
            "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json";
        private const string BraveCatalog =
            "https://api.github.com/repos/brave/brave-browser/releases?per_page=10";
        private const string ChromiumBase =
            "https://storage.googleapis.com/chromium-browser-snapshots/Win_x64/";

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
                case "chrome": return ChromeReleases();
                case "brave": return BraveReleases();
                case "chromium": return ChromiumReleases();
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

            /* The zip and its unpacked copy both have to fit, and the published size is the
               compressed one: a build that reports 244 MB unpacks to well over 400 MB. */
            long need = ((rel.SizeMb > 0 ? rel.SizeMb : 200) + 120) * 1048576L * 2;
            DiskSpace.Require(dir, need, "download " + rel.Engine + " " + rel.Version);

            string tmp = Path.Combine(dir, "dl-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            string file = Path.Combine(tmp, "build.zip");
            try
            {
                progress?.Invoke(2, "Connecting\u2026");
                var ignored = ChromiumDownloader.DownloadFileAsync(rel.Url, file,
                    (pct, msg) => progress?.Invoke(pct, msg), rel.Engine + " " + rel.Version, ct);
                ignored.GetAwaiter().GetResult();

                progress?.Invoke(100, "Unpacking\u2026");
                ExtractZip(file, dir);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }

            string exe = FindExe(dir, rel.Engine);
            if (exe == null)
            {
                /* A half-unpacked folder would keep showing up in the installed list and in the
                   editor's picker, so a failed install takes itself out again. */
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

        private static void ExtractZip(string zip, string dir)
        {
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;   // directory entry
                    string dest = Path.Combine(dir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    // A single failed file (locked by an antivirus scan) must not lose the build.
                    try { entry.ExtractToFile(dest, true); }
                    catch (IOException) { Thread.Sleep(120); try { entry.ExtractToFile(dest, true); } catch { } }
                }
            }
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
