using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>A user-added unpacked extension. Source is "url", "folder" or "builtin".</summary>
    public class ExtensionItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }
        public bool Enabled { get; set; }
        public string Source { get; set; }
        public string StoreUrl { get; set; }
        public string Version { get; set; }

        /// <summary>Absolute path of the extension's own logo, read from its manifest, or
        /// empty when the package ships none. Each extension gets its own picture on the
        /// library row instead of one shared puzzle piece.</summary>
        public string Icon { get; set; }

        /// <summary>The id the extension is known by on the store, recovered from the key in
        /// its manifest. Empty for a build of the app's own, and for a folder whose manifest
        /// carries no key.</summary>
        public string StoreId { get; set; }

        public ExtensionItem()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Folder = "";
            Enabled = true;
            Source = "folder";
            StoreUrl = "";
            Version = "";
            Icon = "";
            StoreId = "";
        }

        /// <summary>Where the logo is drawn from, or empty. Checked every time because the
        /// folder can be deleted behind the app's back.</summary>
        [ScriptIgnore]
        public string IconPath
        {
            get
            {
                try { return !string.IsNullOrEmpty(Icon) && File.Exists(Icon) ? Icon : ""; }
                catch { return ""; }
            }
        }
    }

    /// <summary>
    /// Manages extensions the user adds to Joe Browser. Extensions are unpacked into the app's
    /// own extensions folder and passed to Chromium with --load-extension; nothing is installed
    /// into the system browser and no store account is used.
    /// </summary>
    internal static class ExtensionCatalog
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        /// <summary>
        /// Guards every Load/Save of extensions.json. The shipped pair is downloaded on a
        /// background thread while the UI thread reads and writes the same file: an unsynchronised
        /// read that landed inside the writer's File.Replace window used to be treated as a
        /// corrupt file, renamed to .broken, and the freshly downloaded entries were then wiped
        /// by the next save of the stale in-memory list. One lock closes the whole class of race.
        /// </summary>
        private static readonly object StoreGate = new object();

        /// <summary>
        /// Raised on the worker thread after the background fetch of the shipped pair finishes,
        /// successfully or not. The UI subscribes and marshals itself onto its own thread; before
        /// this existed, a fresh install showed "Waiting for download" forever - the rows only
        /// appeared after the user navigated away and back, and a profile launched inside the
        /// download window started without the pair.
        /// </summary>
        public static event Action DefaultsChanged;

        public static string FilePath { get { return Path.Combine(AppPaths.Root, "extensions.json"); } }
        public static string ImportedRoot { get { return Path.Combine(AppPaths.Extensions, "imported"); } }

        /// <summary>
        /// Extensions the user asked to ship with every Chromium profile.
        ///
        /// They used to be fetched from the Chrome Web Store. They are written by the
        /// application instead - see ExtensionManager.WriteShipped - so they are there on
        /// the first run, on a machine behind a proxy that blocks the store, and offline.
        /// The ids are the ones the library already knows, so a profile that had them
        /// keeps them and a user who removed one keeps it removed.
        /// </summary>
        public static readonly string[][] DefaultStoreExtensions = new[]
        {
            new[] { HttpTrackerId, "HTTP Tracker Plus" },
            new[] { RightClickId, "Absolute Enable Right Click and Copy" }
        };

        /// <summary>The request tracker the application ships with.</summary>
        public const string HttpTrackerId = "fklakbbaaknbgcedidhblbnhclijnhbi";

        /// <summary>The right-click and copy tool the application ships with.</summary>
        public const string RightClickId = "okaeekdeohehbdanbbohcfhhlblcmjkm";

        public static string StoreUrlFor(string id)
        {
            return "https://chromewebstore.google.com/detail/" + id;
        }

        /// <summary>
        /// Adds the shipped store extensions that are already unpacked on disk to the library,
        /// with no network access. Called at start, because the library is what the Extensions
        /// page and the launch path read; a download there would put a web request in front of
        /// the first window.
        /// </summary>
        public static void ListPresentDefaults()
        {
            try
            {
                var items = Load();
                bool changed = false;
                foreach (var entry in DefaultStoreExtensions)
                {
                    string id = entry[0];
                    if (IsRemovedByUser(id)) continue;
                    string target = Path.Combine(ImportedRoot, id);
                    /* A folder without a usable manifest is a half-finished download, and it is
                       what made the pair look installed while the browser refused to load them.
                       Remove it so the downloader can try again from a clean state. */
                    if (!UsableManifest(Path.Combine(target, "manifest.json")))
                    {
                        try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { }
                        continue;
                    }
                    bool listed = false;
                    foreach (var e in items)
                    {
                        if (e == null || string.IsNullOrEmpty(e.Folder)) continue;
                        try
                        {
                            if (e.Folder.TrimEnd('\\', '/').EndsWith(id, StringComparison.OrdinalIgnoreCase))
                            { listed = true; break; }
                        }
                        catch { }
                    }
                    if (listed) continue;
                    items.Add(new ExtensionItem
                    {
                        Name = entry[1],
                        Folder = target,
                        Enabled = true,
                        Source = "store",
                        StoreUrl = StoreUrlFor(id),
                        Version = VersionFromManifest(Path.Combine(target, "manifest.json"))
                    });
                    changed = true;
                }
                if (changed) Save(items);
            }
            catch (Exception ex) { Log.Error("Listing the default extensions failed", ex); }
        }

        /* ------------------------------------------------------------------ *
         * Identity and logo, read from the package itself
         * ------------------------------------------------------------------ */

        /// <summary>
        /// The extension id a manifest's public key stands for. Chromium's rule: sha256 of the
        /// DER key, first 32 hex digits, each hex digit mapped 0-f to a-p. Having it means an
        /// imported extension is listed, stored and linked under the same id the store uses.
        /// </summary>
        public static string IdForKey(string base64Key)
        {
            if (string.IsNullOrWhiteSpace(base64Key)) return "";
            try
            {
                byte[] der = Convert.FromBase64String(base64Key.Trim());
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(der);
                    var sb = new StringBuilder(32);
                    for (int i = 0; i < 16; i++)
                    {
                        sb.Append((char)('a' + (hash[i] >> 4)));
                        sb.Append((char)('a' + (hash[i] & 0x0F)));
                    }
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        /// <summary>Extension id from a folder, or empty when its manifest has no key.</summary>
        public static string IdFromFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return "";
            try
            {
                string manifest = Path.Combine(folder, "manifest.json");
                if (!File.Exists(manifest)) return "";
                var d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest, Encoding.UTF8));
                object key;
                if (d == null || !d.TryGetValue("key", out key)) return "";
                return IdForKey(Convert.ToString(key));
            }
            catch { return ""; }
        }

        /// <summary>
        /// The extension's own logo: the largest icon its manifest names, resolved to a file in
        /// the folder. Falls back to the largest icon_NN.png the package ships, so an extension
        /// that declares its icons in code still gets its picture on the row.
        /// </summary>
        public static string IconFromFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return "";
            try
            {
                string manifest = Path.Combine(folder, "manifest.json");
                if (File.Exists(manifest))
                {
                    var d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest, Encoding.UTF8));
                    object node;
                    if (d != null && d.TryGetValue("icons", out node))
                    {
                        var map = node as Dictionary<string, object>;
                        if (map != null)
                        {
                            int bestSize = -1;
                            string bestPath = "";
                            foreach (var kv in map)
                            {
                                int size;
                                if (!int.TryParse(kv.Key, out size)) continue;
                                string rel = Convert.ToString(kv.Value);
                                if (string.IsNullOrWhiteSpace(rel)) continue;
                                string full = Path.Combine(folder, rel.Replace('/', '\\'));
                                if (!File.Exists(full) || size <= bestSize) continue;
                                bestSize = size;
                                bestPath = full;
                            }
                            if (bestPath.Length > 0) return bestPath;
                        }
                    }
                }
                string best = "";
                int bestPixels = -1;
                foreach (string file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    int pixels = 0;
                    int underscore = name.LastIndexOf('_');
                    if (underscore >= 0) int.TryParse(name.Substring(underscore + 1), out pixels);
                    if (pixels <= bestPixels) continue;
                    bestPixels = pixels;
                    best = file;
                }
                return best;
            }
            catch { return ""; }
        }

        /// <summary>The store listing for an extension, when its id is known.</summary>
        public static string StoreUrlForId(string id)
        {
            return string.IsNullOrEmpty(id) ? "" : "https://chromewebstore.google.com/detail/" + id;
        }

        /// <summary>Ensures the shipped store extensions are present in the library, once.
        /// Downloads what is missing, so it belongs off the startup path.</summary>
        /// <summary>
        /// The store extensions this app ships, and marks to remember that the user removed one.
        ///
        /// Two things were wrong with the shipped pair. A removal deleted the folder and the
        /// entry, and the next launch put both straight back - so the only way to be rid of a
        /// shipped extension was to keep the browser closed. And a removal that happened while a
        /// browser had the folder open deleted nothing, so the row came back on the next refresh.
        /// A mark beside the library fixes both: it survives the folder, survives a re-download,
        /// and is what the loader and the listing both consult.
        /// </summary>
        private static string SkipPath(string id)
        {
            return Path.Combine(AppPaths.Extensions, ".removed-" + id);
        }

        /// <summary>True when the user removed this shipped extension and has not added it back.</summary>
        public static bool IsRemovedByUser(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            try { return File.Exists(SkipPath(id)); } catch { return false; }
        }

        /// <summary>Records the user's decision to remove a shipped extension, or forgets it.</summary>
        public static void MarkRemoved(string id, bool removed)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                if (removed)
                {
                    Directory.CreateDirectory(AppPaths.Extensions);
                    File.WriteAllText(SkipPath(id), DateTime.UtcNow.ToString("o"), new UTF8Encoding(false));
                }
                else if (File.Exists(SkipPath(id))) File.Delete(SkipPath(id));
            }
            catch (Exception ex) { Log.Error("Could not record the extension removal " + id, ex); }
        }

        /* ---- a status a page can render -------------------------------------------------------
           The shipped pair arrives by download, and a download can fail: a proxy that blocks the
           store, a machine that is offline at first launch, a region the listing is not served
           in. When that happened the library simply had no row for the extension, so the app
           looked as though it never shipped it - which is indistinguishable, from the user's
           side, from "the extension is not installed". These read the evidence that already
           exists and give the page one of five states to draw. */

        /// <summary>A state of one shipped extension.</summary>
        public enum DefaultState
        {
            /// <summary>Its folder is on disk and Chromium can load it.</summary>
            Present,
            /// <summary>The user removed it on purpose; it stays removed.</summary>
            RemovedByUser,
            /// <summary>Not on disk yet; the next launch will fetch it.</summary>
            Pending,
            /// <summary>The last fetch failed; the reason is kept.</summary>
            Failed,
            /// <summary>Not shipped with this build.</summary>
            Unknown
        }

        /// <summary>The state of a shipped extension, and the reason if it failed.</summary>
        public static DefaultState StateOf(string id, out string detail)
        {
            detail = "";
            string name = "";
            bool known = false;
            foreach (var entry in DefaultStoreExtensions)
                if (string.Equals(entry[0], id, StringComparison.OrdinalIgnoreCase)) { known = true; name = entry[1]; break; }
            if (!known) return DefaultState.Unknown;
            if (IsRemovedByUser(id)) return DefaultState.RemovedByUser;
            string folder = Path.Combine(ImportedRoot, id);
            try
            {
                if (UsableManifest(Path.Combine(folder, "manifest.json"))) return DefaultState.Present;
            }
            catch { }
            string skip = Path.Combine(AppPaths.Extensions, ".store-" + id + ".skip");
            try
            {
                if (File.Exists(skip))
                {
                    detail = File.ReadAllText(skip).Trim();
                    return DefaultState.Failed;
                }
            }
            catch { }
            detail = name;
            return DefaultState.Pending;
        }

        /// <summary>Every shipped extension, as the page lists them: id, display name, state.</summary>
        public static List<string[]> DefaultStatuses()
        {
            var rows = new List<string[]>();
            foreach (var entry in DefaultStoreExtensions)
            {
                string detail;
                var state = StateOf(entry[0], out detail);
                rows.Add(new[] { entry[0], entry[1], StateText(state), detail });
            }
            return rows;
        }

        private static string StateText(DefaultState state)
        {
            switch (state)
            {
                case DefaultState.Present: return "Installed";
                case DefaultState.RemovedByUser: return "Removed";
                case DefaultState.Failed: return "Not installed";
                case DefaultState.Pending: return "Waiting for download";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Fetches every shipped extension that is not present, ignoring the once-a-day backoff.
        /// Called from the Extensions page's own action, so a user whose proxy was blocking the
        /// store can retry the moment they have fixed it.
        /// </summary>
        public static int FetchMissing(bool force)
        {
            int added = 0;
            try
            {
                var items = Load();
                bool changed = false;
                foreach (var entry in DefaultStoreExtensions)
                {
                    string id = entry[0];
                    if (IsRemovedByUser(id)) continue;
                    string target = Path.Combine(ImportedRoot, id);
                    bool present = false;
                    try { present = UsableManifest(Path.Combine(target, "manifest.json")); } catch { }
                    if (present) continue;
                    /* The pair comes from the Chrome Web Store, not from the application.
                       They are the two the owner named, and a user who wants them has the
                       real thing - the listing, the version and the behaviour the store
                       publishes - rather than a look-alike the application wrote. They are
                       installed by default for a new profile and can be removed, and the
                       removal is remembered. */
                    string skip = Path.Combine(AppPaths.Extensions, ".store-" + id + ".skip");
                    if (!force)
                    {
                        try
                        {
                            if (File.Exists(skip) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(skip)).TotalHours < 24) continue;
                        }
                        catch { }
                    }
                    bool listed = false;
                    foreach (var e in items)
                    {
                        if (e == null || string.IsNullOrEmpty(e.Folder)) continue;
                        if (e.Folder.TrimEnd('\\', '/').EndsWith(id, StringComparison.OrdinalIgnoreCase)) { listed = true; break; }
                    }
                    try
                    {
                        var item = ImportFromUrl(StoreUrlFor(id));
                        item.Name = entry[1];
                        item.Source = "store";
                        if (!listed) items.Add(item);
                        else
                        {
                            foreach (var e in items)
                                if (e != null && e.Folder != null &&
                                    e.Folder.TrimEnd('\\', '/').EndsWith(id, StringComparison.OrdinalIgnoreCase))
                                {
                                    e.Folder = item.Folder;
                                    e.Enabled = true;
                                }
                        }
                        changed = true;
                        added++;
                        try { if (File.Exists(skip)) File.Delete(skip); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not fetch default extension " + id, ex);
                        try
                        {
                            Directory.CreateDirectory(AppPaths.Extensions);
                            File.WriteAllText(skip, ShortReason(ex), new UTF8Encoding(false));
                        }
                        catch { }
                    }
                }
                if (changed) Save(items);
            }
            catch (Exception ex) { Log.Error("Default extension fetch failed", ex); }
            return added;
        }

        /// <summary>The one line worth keeping from a download failure.</summary>
        private static string ShortReason(Exception ex)
        {
            if (ex == null) return "";
            var web = ex as System.Net.WebException;
            if (web != null)
            {
                if (web.Status == System.Net.WebExceptionStatus.Timeout) return "The store took too long to answer.";
                if (web.Status == System.Net.WebExceptionStatus.NameResolutionFailure ||
                    web.Status == System.Net.WebExceptionStatus.ConnectFailure)
                    return "The store could not be reached. A proxy or firewall may be blocking it.";
                return "The store did not send a package.";
            }
            string m = ex.Message ?? "";
            int i = m.IndexOfAny(new[] { '\r', '\n' });
            if (i > 0) m = m.Substring(0, i);
            return m.Length > 160 ? m.Substring(0, 157) + "..." : m;
        }

        /// <summary>The store id of a shipped extension, from its folder path. Empty for anything else.</summary>
        public static string DefaultIdOf(ExtensionItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Folder)) return "";
            foreach (var entry in DefaultStoreExtensions)
            {
                string id = entry[0];
                try
                {
                    if (item.Folder.TrimEnd('\\', '/').EndsWith(id, StringComparison.OrdinalIgnoreCase))
                        return id;
                }
                catch { }
            }
            return "";
        }

        public static void EnsureDefaults()
        {
            try
            {
                var items = Load();
                bool changed = false;
                foreach (var entry in DefaultStoreExtensions)
                {
                    string id = entry[0];
                    /* The user removed it. That decision outranks the fact that this app ships
                       it, and it is the difference between "cannot be removed" and "stays
                       removed". */
                    if (IsRemovedByUser(id)) continue;
                    string target = Path.Combine(ImportedRoot, id);
                    /* "Listed" only counts when the folder the entry points at still holds a
                       manifest Chromium will load. The old check accepted any entry whose folder
                       path happened to end in the id - including one Load() had already disabled
                       because its folder was broken - so a half-written download stayed listed
                       and never downloaded again. */
                    bool listed = false;
                    foreach (var e in items)
                    {
                        if (e == null || string.IsNullOrEmpty(e.Folder)) continue;
                        try
                        {
                            if (e.Folder.TrimEnd('\\', '/').EndsWith(id, StringComparison.OrdinalIgnoreCase) &&
                                UsableManifest(Path.Combine(e.Folder, "manifest.json")))
                            { listed = true; break; }
                        }
                        catch { }
                    }
                    if (listed) continue;

                    if (UsableManifest(Path.Combine(target, "manifest.json")))
                    {
                        items.Add(new ExtensionItem
                        {
                            Name = entry[1],
                            Folder = target,
                            Enabled = true,
                            Source = "store",
                            StoreUrl = StoreUrlFor(id),
                            Version = VersionFromManifest(Path.Combine(target, "manifest.json"))
                        });
                        changed = true;
                        continue;
                    }

                    /* A failed download must not stop a browser from launching. Retry at most
                       once a day; the manual import box on the Extensions page remains available. */
                    string skip = Path.Combine(AppPaths.Extensions, ".store-" + id + ".skip");
                    try
                    {
                        if (File.Exists(skip) &&
                            (DateTime.UtcNow - File.GetLastWriteTimeUtc(skip)).TotalHours < 24) continue;
                    }
                    catch { }

                    try
                    {
                        var item = ImportFromUrl(StoreUrlFor(id));
                        item.Name = entry[1];
                        item.Source = "store";
                        items.Add(item);
                        changed = true;
                        try { if (File.Exists(skip)) File.Delete(skip); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not fetch default extension " + id, ex);
                        try
                        {
                            Directory.CreateDirectory(AppPaths.Extensions);
                            File.WriteAllText(skip, ex.Message ?? "failed", new UTF8Encoding(false));
                        }
                        catch { }
                    }
                }
                if (changed) Save(items);
            }
            catch (Exception ex) { Log.Error("Default extension setup failed", ex); }
            finally
            {
                var h = DefaultsChanged;
                if (h != null) { try { h(); } catch { } }
            }
        }

        /// <summary>Folders of the shipped store extensions that are actually present.</summary>
        public static List<string> DefaultFolders()
        {
            var result = new List<string>();
            /* The library's Enabled flag is the user's word: a shipped extension whose row was
               switched off must not load, exactly like any other row. The old version read only
               the disk, so the row's own toast - "takes effect at the next launch" - was false. */
            var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var e in Load())
                {
                    string id = DefaultIdOf(e);
                    if (id.Length > 0) states[id] = e.Enabled;
                }
            }
            catch { }
            foreach (var entry in DefaultStoreExtensions)
            {
                if (IsRemovedByUser(entry[0])) continue;
                bool enabled;
                if (states.TryGetValue(entry[0], out enabled) && !enabled) continue;
                string folder = Path.Combine(ImportedRoot, entry[0]);
                try
                {
                    if (Directory.Exists(folder) && UsableManifest(Path.Combine(folder, "manifest.json")))
                        result.Add(folder);
                }
                catch { }
            }
            return result;
        }

        public static List<ExtensionItem> Load()
        {
            var list = new List<ExtensionItem>();
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (!File.Exists(FilePath)) return list;
                List<ExtensionItem> items;
                lock (StoreGate)
                {
                    try
                    {
                        items = Json.Deserialize<List<ExtensionItem>>(File.ReadAllText(FilePath, Encoding.UTF8));
                    }
                    catch (Exception ex)
                    {
                        /* A truncated or hand-edited library file threw here and the page showed an
                           empty list, as if every extension had been removed. Keep the file under a
                           name the user can send, and start the list again. */
                        Log.Error("Extension library is unreadable; setting it aside", ex);
                        try { File.Move(FilePath, FilePath + ".broken"); } catch { }
                        return list;
                    }
                }
                if (items == null) return list;
                foreach (var e in items)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    /* Entries saved before the logo and the store id were recorded are filled
                       in on load, once, from the package that is already on disk. */
                    try
                    {
                        if (string.IsNullOrEmpty(e.Icon) || string.IsNullOrEmpty(e.StoreId))
                        {
                            if (string.IsNullOrEmpty(e.Icon)) e.Icon = IconFromFolder(e.Folder);
                            if (string.IsNullOrEmpty(e.StoreId))
                            {
                                e.StoreId = IdFromFolder(e.Folder);
                                if (e.StoreId.Length > 0 && string.IsNullOrEmpty(e.StoreUrl))
                                    e.StoreUrl = StoreUrlForId(e.StoreId);
                            }
                        }
                    }
                    catch { }
                    // A deleted folder must not linger as a broken command-line argument.
                    if (!string.IsNullOrEmpty(e.Folder) &&
                        (!Directory.Exists(e.Folder) || !File.Exists(Path.Combine(e.Folder, "manifest.json"))))
                        e.Enabled = false;
                    list.Add(e);
                }
            }
            catch (Exception ex) { Log.Error("Cannot read extension library", ex); }
            return list;
        }

        public static void Save(List<ExtensionItem> items)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                string tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                lock (StoreGate)
                {
                    File.WriteAllText(tmp, Json.Serialize(items ?? new List<ExtensionItem>()), new UTF8Encoding(true));
                    if (File.Exists(FilePath))
                    {
                        try { File.Replace(tmp, FilePath, null); }
                        catch { File.Copy(tmp, FilePath, true); File.Delete(tmp); }
                    }
                    else File.Move(tmp, FilePath);
                }
            }
            catch (Exception ex) { Log.Error("Cannot save extension library", ex); }
        }

        /// <summary>
        /// Folders that are switched on and really contain a manifest. Two entries pointing at
        /// one folder - an import followed by the same file, or a folder the user copied in - used
        /// to be passed to the browser twice; Chromium then refuses the whole list and says
        /// nothing, so every extension in the profile silently disappeared.
        /// </summary>
        public static List<string> EnabledFolders()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Load())
            {
                if (!e.Enabled || string.IsNullOrEmpty(e.Folder)) continue;
                /* The removal mark outranks the library entry. A stale extensions.json written
                   by an older build (or a save that raced the background fetch) would otherwise
                   hand a removed shipped extension straight back to --load-extension. */
                string markId = string.IsNullOrEmpty(e.StoreId) ? IdFromFolder(e.Folder) : e.StoreId;
                if (IsRemovedByUser(markId)) continue;
                if (e.Folder.IndexOf(',') >= 0) continue; // Chrome splits --load-extension on commas
                try
                {
                    if (Directory.Exists(e.Folder) && File.Exists(Path.Combine(e.Folder, "manifest.json")))
                        if (seen.Add(Path.GetFullPath(e.Folder).TrimEnd('\\'))) result.Add(e.Folder);
                }
                catch { }
            }
            return result;
        }

        /// <summary>Pulls the 32-character extension id out of a Chrome Web Store URL.</summary>
        public static string StoreId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string s = url.Trim();
            // A raw id is also accepted.
            if (s.Length == 32 && IsId(s)) return s.ToLowerInvariant();
            /* Only the query is cut here, never the fragment or the whole URL: a Web Store link
               pasted from the address bar ends in a long slug plus "/<id>", and an older build
               split the string on every colon as well, which chopped "https:" into "https" and
               left nothing to match. An id is 32 characters from a-p, so searching the path
               segments from the end finds it whatever slug the store put in front. */
            int q = s.IndexOf('?');
            if (q > 0) s = s.Substring(0, q);
            string[] bits = s.Split(new[] { '/', '=', '&', '#', ':' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = bits.Length - 1; i >= 0; i--)
                if (IsId(bits[i])) return bits[i].ToLowerInvariant();
            return "";
        }

        private static bool IsId(string s)
        {
            if (s == null || s.Length != 32) return false;
            foreach (char c in s)
                if (!((c >= 'a' && c <= 'p') || (c >= 'A' && c <= 'P'))) return false;
            return true;
        }

        /// <summary>
        /// Downloads an unpacked CRX from the Chrome Web Store update endpoint and extracts it.
        /// Works for public extensions that permit direct CRX download; otherwise an informative
        /// exception is thrown and the user can add a local unpacked folder instead.
        /// </summary>
        public static ExtensionItem ImportFromUrl(string url)
        {
            string id = StoreId(url);
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException("That is not a Chrome Web Store extension link. Paste the full extension URL or its 32-character id.");

            /* The folder is named after the store id, so asking for the same extension twice
               finds the same place. That only works if nothing destroys it before the new copy
               is complete, which is what the staging folder below is for. */
            Directory.CreateDirectory(ImportedRoot);
            string target = Path.Combine(ImportedRoot, id);
            string crx = Path.Combine(ImportedRoot, id + ".crx");
            string zip = Path.Combine(ImportedRoot, id + ".zip");

            try
            {
                string endpoint =
                    "https://clients2.google.com/service/update2/crx?response=redirect&prodversion=131.0.0.0&acceptformat=crx2,crx3&x=" +
                    Uri.EscapeDataString("id=" + id + "&uc");

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                /* A store outage or a captive portal must fail in seconds, not freeze the
                   browser launch behind WebClient's two-minute default. */
                var req = (HttpWebRequest)WebRequest.Create(endpoint);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                /* Leave through the first usable proxy in the library, the same rule the proxy
                   tester and the connectivity check follow. A bare request failed on machines
                   whose only egress is a configured proxy, which is what "add extension by
                   store link never works" was. An explicit empty WebProxy still bypasses the
                   system proxy for everyone else; SOCKS cannot ride an HttpWebRequest and is
                   skipped rather than guaranteed to fail. */
                try
                {
                    System.Net.IWebProxy via = null;
                    foreach (var entry in ProxyVault.Load())
                    {
                        var c = entry == null ? null : entry.Config;
                        if (c == null || !c.Enabled || string.IsNullOrWhiteSpace(c.Host) || c.Port <= 0) continue;
                        if (c.Type != null && c.Type.Equals("socks5", StringComparison.OrdinalIgnoreCase)) continue;
                        via = new WebProxy(c.Host, c.Port);
                        break;
                    }
                    req.Proxy = via;   /* null falls back to the system proxy - an explicit empty
                                           WebProxy here silently bypassed it, which killed every
                                           store download on machines whose only egress is the
                                           Windows/PAC proxy. */
                }
                catch { req.Proxy = null; }
                /* A store that answers 404 or 403 for a withdrawn listing throws before any body
                   can be read, and the inner exception is the one that says what happened. */
                try { req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; }
                catch { }
                req.AllowAutoRedirect = true;
                long delivered;
                string contentType, finalUrl;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    /* The store answers a plain 200 with an HTML page for a few cases - an id
                       that does not exist, a listing removed for policy, and the "please install
                       from the store" interstitial. Saving that page as the package is how the
                       old code ended up saying "not a valid package" for what was really "no
                       such extension". Read the answer as what it says it is. */
                    contentType = Convert.ToString(resp.ContentType) ?? "";
                    finalUrl = Convert.ToString(resp.ResponseUri) ?? "";
                    using (var input = resp.GetResponseStream())
                    using (var output = File.Create(crx))
                    {
                        input.CopyTo(output);
                    }
                    delivered = new FileInfo(crx).Length;
                }

                byte[] raw = File.ReadAllBytes(crx);
                int start = FindZipStart(raw);
                if (start < 0)
                {
                    Log.Info("Store answered " + delivered + " bytes of " + contentType + " from " + finalUrl);
                    throw new InvalidOperationException(
                        delivered == 0
                            ? "The store sent nothing back for this id. Check the link, then try the .crx import instead."
                            : "The store did not send an extension package for this id. It may be removed, region locked, or the link is not a store link. Download the .crx and use the file import instead.");
                }
                /* A package the store serves is a zip with a CRX3 header in front. Chrome used
                   to accept a CRX2 header as well; the store no longer serves one, and a body
                   that is neither is an error page that happens to start with something other
                   than plain text. Saying so is better than failing inside the zip reader.
                   The magic is the four bytes "Cr24" - 0x34, not 0x33: the version lives in
                   the header's version field, not in the magic string. */
                if (raw.Length > 4 && raw[0] == 0x43 && raw[1] == 0x72 && raw[2] == 0x32 && raw[3] == 0x34)
                {
                    /* CRX3: fine, the zip starts after the header and FindZipStart already
                       handled it. Nothing to do, this branch is documentation. */
                }
                File.WriteAllBytes(zip, Slice(raw, start));

                /* Unpack beside the target and swap at the end. The old build deleted the
                   installed copy first and then extracted, so a zip that failed halfway - a
                   truncated download, a package with a bad entry - left the user with nothing
                   where the working extension used to be. */
                string staging = target + ".new";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                using (var archive = ZipFile.OpenRead(zip)) archive.ExtractToDirectory(staging);

                string manifest = Path.Combine(staging, "manifest.json");
                if (!File.Exists(manifest))
                {
                    try { Directory.Delete(staging, true); } catch { }
                    throw new InvalidOperationException("The downloaded package has no manifest.json, so Chromium cannot load it.");
                }
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                /* Move file by file rather than Directory.Move: the download folder and the
                   extensions folder can sit on different volumes. */
                foreach (string file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(staging.Length).TrimStart('\\');
                    string dest = Path.Combine(target, rel);
                    string dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(file, dest, true);
                }
                try { Directory.Delete(staging, true); } catch { }

                string name = NameFromManifest(manifest) ?? id;
                string version = VersionFromManifest(manifest) ?? "";
                /* The id in the manifest's key is the identity Chromium will give the folder,
                   and it is usually the id that was asked for. When it is not - a package
                   saved from an older store, a fork with its own key - the honest link is the
                   one the package proves, not the one that was pasted. */
                string realId = IdFromFolder(target);
                return new ExtensionItem
                {
                    Name = name,
                    Folder = target,
                    Enabled = true,
                    Source = "url",
                    StoreUrl = string.IsNullOrEmpty(realId) ? url : StoreUrlForId(realId),
                    Version = version,
                    Icon = IconFromFolder(target),
                    StoreId = realId.Length > 0 ? realId : id
                };
            }
            finally
            {
                TryDelete(crx);
                TryDelete(zip);
            }
        }

        /// <summary>Library folder already holding this extension, or empty.</summary>
        private static string FolderForId(string id)
        {
            if (string.IsNullOrEmpty(id) || !Directory.Exists(ImportedRoot)) return "";
            foreach (string dir in Directory.GetDirectories(ImportedRoot))
            {
                if (string.Equals(Path.GetFileName(dir), id, StringComparison.OrdinalIgnoreCase)) return dir;
            }
            return "";
        }

        /// <summary>
        /// True when a folder holds a manifest Chromium will actually load: a JSON object with a
        /// name, a version and manifest_version 3. An empty or truncated file passed the old
        /// File.Exists test, so a download that died halfway left an extension that was listed,
        /// loaded on the command line and silently rejected by the browser.
        /// </summary>
        public static bool UsableManifest(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                var info = new FileInfo(path);
                if (info.Length < 24) return false;
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (text.IndexOf("\"manifest_version\"", StringComparison.Ordinal) < 0) return false;
                if (text.IndexOf("\"name\"", StringComparison.Ordinal) < 0) return false;
                if (text.IndexOf("\"version\"", StringComparison.Ordinal) < 0) return false;
                try { Json.Deserialize<Dictionary<string, object>>(text); }
                catch { return false; }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// The store id a package proves about itself. Only a manifest key can prove one, so a
        /// package without a key answers empty and is imported under a fresh folder.
        /// </summary>
        private static string IdFromPackage(byte[] raw)
        {
            if (raw == null) return "";
            int start = FindZipStart(raw);
            if (start < 0) return "";
            string probe = Path.Combine(Path.GetTempPath(), "joe-probe-" + Guid.NewGuid().ToString("N") + ".zip");
            string dir = Path.Combine(Path.GetTempPath(), "joe-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(probe, Slice(raw, start));
                Directory.CreateDirectory(dir);
                using (var archive = ZipFile.OpenRead(probe)) archive.ExtractToDirectory(dir);
                return IdFromFolder(dir);
            }
            catch { return ""; }
            finally
            {
                TryDelete(probe);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static int FindZipStart(byte[] raw)
        {
            if (raw == null) return -1;
            int from = raw.Length >= 4 && raw[0] == 0x43 && raw[1] == 0x72 && raw[2] == 0x32 && raw[3] == 0x34 ? 8 : 0;
            for (int i = from; i < raw.Length - 4; i++)
                if (raw[i] == 0x50 && raw[i + 1] == 0x4B && raw[i + 2] == 0x03 && raw[i + 3] == 0x04) return i;
            return -1;
        }

        private static byte[] Slice(byte[] raw, int start)
        {
            var result = new byte[raw.Length - start];
            Array.Copy(raw, start, result, 0, result.Length);
            return result;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string NameFromManifest(string path)
        {
            try
            {
                var d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                object v;
                return d != null && d.TryGetValue("name", out v) ? Convert.ToString(v) : null;
            }
            catch { return null; }
        }

        private static string VersionFromManifest(string path)
        {
            try
            {
                var d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                object v;
                return d != null && d.TryGetValue("version", out v) ? Convert.ToString(v) : "";
            }
            catch { return ""; }
        }

        /// <summary>Copies a local unpacked extension folder into the managed library.</summary>
        public static ExtensionItem ImportFromFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                throw new InvalidOperationException("Choose an existing unpacked extension folder.");
            string manifest = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifest))
                throw new InvalidOperationException("That folder has no manifest.json. Unpack the extension first (it must contain manifest.json at its root).");

            string name = NameFromManifest(manifest) ?? new DirectoryInfo(folder).Name;
            string known = IdFromFolder(folder);
            string existing = FolderForId(known);
            if (existing.Length > 0)
            {
                /* The library already holds this extension. Copying it again would hand
                   Chromium the same extension twice under two paths, which it refuses to load
                   at all. Refresh the copy that is already there instead. */
                CopyTree(folder, existing);
                return new ExtensionItem
                {
                    Name = name,
                    Folder = existing,
                    Enabled = true,
                    Source = "folder",
                    StoreUrl = StoreUrlForId(known),
                    Version = VersionFromManifest(Path.Combine(existing, "manifest.json")),
                    Icon = IconFromFolder(existing),
                    StoreId = known
                };
            }
            string safe = Guid.NewGuid().ToString("N").Substring(0, 12);
            string target = Path.Combine(ImportedRoot, safe);
            Directory.CreateDirectory(ImportedRoot);
            CopyTree(folder, target);
            string id = IdFromFolder(target);
            return new ExtensionItem
            {
                Name = name,
                Folder = target,
                Enabled = true,
                Source = "folder",
                StoreUrl = StoreUrlForId(id),
                Version = VersionFromManifest(Path.Combine(target, "manifest.json")),
                Icon = IconFromFolder(target),
                StoreId = id
            };
        }

        /// <summary>
        /// Imports a downloaded extension as a file: a .crx from the Chrome Web Store, a zip of an
        /// unpacked extension, or a zip of the store package. The store is not reachable from some
        /// networks and an extension can also arrive as a file from elsewhere, so this is the path
        /// for "I already have it" - the same one Chrome itself removed in 137.
        /// </summary>
        public static ExtensionItem ImportFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("Choose an existing .crx or .zip file.");

            byte[] raw = File.ReadAllBytes(path);
            int start = FindZipStart(raw);
            if (start < 0)
                throw new InvalidOperationException("That file is not a packed extension. A .crx or .zip is expected.");

            Directory.CreateDirectory(ImportedRoot);
            string safe = Guid.NewGuid().ToString("N").Substring(0, 12);
            string target = Path.Combine(ImportedRoot, safe);
            /* A package for an extension the library already holds reloads that folder, so the
               user can update an import instead of ending up with two copies. */
            string existing = FolderForId(IdFromPackage(raw));
            if (existing.Length > 0) target = existing;
            string temp = Path.Combine(ImportedRoot, safe + ".zip");
            try
            {
                File.WriteAllBytes(temp, Slice(raw, start));
                /* Same rule as the store path: unpack first, replace second, so a bad package
                   cannot take away the copy that was working. */
                string staging = target + ".new";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                using (var archive = ZipFile.OpenRead(temp)) archive.ExtractToDirectory(staging);

                /* The package has to prove it holds an extension before the installed copy is
                   touched, or a zip of the wrong thing would take the working one with it. */
                if (!File.Exists(Path.Combine(staging, "manifest.json")))
                {
                    string[] subs = Directory.GetDirectories(staging);
                    if (subs.Length != 1 || !File.Exists(Path.Combine(subs[0], "manifest.json")))
                    {
                        try { Directory.Delete(staging, true); } catch { }
                        throw new InvalidOperationException(
                            "The package has no manifest.json, so Chromium cannot load it. Use the store link instead.");
                    }
                }

                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(staging.Length).TrimStart('\\');
                    string dest = Path.Combine(target, rel);
                    string dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(file, dest, true);
                }
                try { Directory.Delete(staging, true); } catch { }

                /* A zip usually wraps the extension in one folder, and the browser will not load
                   it unless manifest.json is at the root of what it is given. */
                string manifest = Path.Combine(target, "manifest.json");
                if (!File.Exists(manifest))
                {
                    string[] dirs = Directory.GetDirectories(target);
                    if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], "manifest.json")))
                        manifest = Path.Combine(dirs[0], "manifest.json");
                }
                if (!File.Exists(manifest))
                    throw new InvalidOperationException(
                        "The package has no manifest.json, so Chromium cannot load it. Use the store link instead.");

                string root = Path.GetDirectoryName(manifest);
                string name = NameFromManifest(manifest) ?? Path.GetFileNameWithoutExtension(path);
                string version = VersionFromManifest(manifest) ?? "";
                if (!string.Equals(root, target, StringComparison.OrdinalIgnoreCase))
                {
                    /* Move the single inner folder up rather than rewriting every path. */
                    foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                        string dest = Path.Combine(target, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(f, dest, true);
                    }
                    try { Directory.Delete(root, true); } catch { }
                }
                string fileId = IdFromFolder(target);
                return new ExtensionItem
                {
                    Name = name,
                    Folder = target,
                    Enabled = true,
                    Source = "file",
                    StoreUrl = StoreUrlForId(fileId),
                    Version = version,
                    Icon = IconFromFolder(target),
                    StoreId = fileId
                };
            }
            finally { TryDelete(temp); }
        }

        /// <summary>
        /// Deletes an extension's own folder, but only when it is inside the managed library.
        /// A folder the user pointed at is theirs to keep, so an entry that refers to one is
        /// removed from the list and nothing else happens to it.
        /// </summary>
        public static bool DeleteFiles(ExtensionItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Folder)) return true;
            try
            {
                string root = Path.GetFullPath(ImportedRoot).TrimEnd(Path.DirectorySeparatorChar) +
                              Path.DirectorySeparatorChar;
                string folder = Path.GetFullPath(item.Folder);
                if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                return true;
            }
            catch (Exception ex)
            {
                /* A running browser holds the folder open. The entry is gone from the list
                   already, and the next attempt after the browser closes will succeed. */
                Log.Error("Could not delete extension folder " + item.Folder, ex);
                return false;
            }
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar);
                string dest = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, true);
            }
        }

        /// <summary>Recommended extensions. Links are store searches, never invented ids.</summary>
        public static string[][] Recommended()
        {
            return new[]
            {
                new[] { "Enable Right Click & Copy", "Restore the context menu and copying on sites that block it.", "enable%20right%20click", "copy" },
                new[] { "HTTP Tracker Plus", "See redirects and request chains while you browse.", "http%20tracker", "route" },
                new[] { "Ad Blocker", "A clean, quiet browsing session without pop-ups.", "uBlock%20Origin", "shield" },
                new[] { "Google Translate", "Translate pages inline without leaving the profile.", "google%20translate", "translate" },
                new[] { "Cookie Editor", "Inspect and edit cookies for the running profile.", "cookie%20editor", "cookie" }
            };
        }

        /// <summary>
        /// A store search. The query is already percent encoded in the table, so it is appended
        /// as it stands; the old shape ignored this and produced "search/ublock%20origin",
        /// which the store answers with its front page rather than results.
        /// </summary>
        public static string StoreSearchUrl(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return "https://chromewebstore.google.com/";
            return "https://chromewebstore.google.com/search/" + query.Trim();
        }
    }
}
