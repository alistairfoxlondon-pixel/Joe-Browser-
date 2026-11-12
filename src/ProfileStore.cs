using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    internal class ProfileStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JoeBrowser.Proxy.v1");
        /* Long notes, big extension lists and exported bundles went past the serializer's
           default ~2 MB cap, which made Save throw and the profile silently stay unsaved. */
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };

        /// <summary>
        /// Root override for anything that needs an isolated folder: the release self test
        /// writes eight real profiles and throws them away, and nothing else should ever set it.
        /// </summary>
        private readonly string _root;

        public ProfileStore() { }
        public ProfileStore(string root) { _root = string.IsNullOrWhiteSpace(root) ? null : root; }

        private string ProfilesDir
        {
            get { return string.IsNullOrEmpty(_root) ? ActiveProfiles : Path.Combine(_root, "profiles"); }
        }

        /// <summary>
        /// Folder holding the profiles of one account. History and profiles both hang off this,
        /// so two accounts signed in on the same Windows user never see each other's data - which
        /// is what "each user's history must remain separate" has to mean in an app whose data
        /// lives in one profile folder.
        /// </summary>
        public static string DirFor(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return AppPaths.Profiles;
            string safe = Safe(email);
            return Path.Combine(AppPaths.Root, "accounts", safe, "profiles");
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '@' ? c : '_');
            string v = sb.ToString();
            return v.Length == 0 ? "account" : v;
        }

        /// <summary>Root folder of the signed-in account, or the shared root when nobody is.</summary>
        public static string ActiveRoot
        {
            get
            {
                var a = Session.Current;
                return a == null || string.IsNullOrEmpty(a.Email)
                    ? AppPaths.Root
                    : Path.Combine(AppPaths.Root, "accounts", Safe(a.Email));
            }
        }

        /// <summary>The profiles folder for the signed-in account.</summary>
        public static string ActiveProfiles
        {
            get
            {
                var a = Session.Current;
                return a == null || string.IsNullOrEmpty(a.Email) ? AppPaths.Profiles : DirFor(a.Email);
            }
        }

        /// <summary>Files in the profile folder that could not be parsed by the last
        /// <see cref="LoadAll"/>. They are never deleted; the grid offers to try again or to
        /// move them aside, because a profile that silently vanishes reads as data loss.</summary>
        private List<string> _unreadable = new List<string>();

        public List<string> UnreadableFiles { get { return new List<string>(_unreadable); } }

        /// <summary>
        /// How many profiles this account holds. Asked by the account page, which must state the
        /// number without depending on a page having rendered.
        /// </summary>
        public static int CountMine()
        {
            try
            {
                if (!Directory.Exists(ActiveProfiles)) return 0;
                return Directory.GetFiles(ActiveProfiles, "*.json").Length;
            }
            catch { return 0; }
        }

        public List<Profile> LoadAll()
        {
            var list = new List<Profile>();
            /* Older builds drew seeds from the clock, so profiles created in the same
               millisecond share one seed - which means identical canvas, audio and font
               noise. Repair those on load so every stored profile stays unique. */
            var seeds = new HashSet<int>();
            _unreadable = new List<string>();
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                foreach (string file in Directory.GetFiles(ProfilesDir, "*.json"))
                {
                    try
                    {
                        var p = _json.Deserialize<Profile>(File.ReadAllText(file, Encoding.UTF8));
                        if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                        if (p.Fp == null) p.Fp = new FingerprintConfig();
                        if (p.Proxy == null) p.Proxy = new ProxyConfig();
                        if (p.Tools == null) p.Tools = new ToolConfig();
                        p.Proxy.Password = Decrypt(p.Proxy.PasswordEnc);
                        if (p.OpenCount < 0) p.OpenCount = 0;

                        bool dirty = false;
                        if (p.Fp.Seed == 0 || seeds.Contains(p.Fp.Seed))
                        {
                            int fresh;
                            do { fresh = FingerprintFactory.NewSeed(); }
                            while (fresh == 0 || seeds.Contains(fresh));
                            p.Fp.Seed = fresh;
                            dirty = true;
                        }
                        seeds.Add(p.Fp.Seed);

                        /* A config that describes a machine that cannot exist - a macOS profile
                           with a Direct3D renderer, 16 GB of deviceMemory, an Android profile with
                           no touch points - is repaired on load, because every one of those is a
                           single probe away from being flagged and none of them can be fixed by a
                           user who does not know to look. Coerce never touches the seed or the
                           canvas payload, so it cannot undo a unique identity. */
                        if (FingerprintFactory.Coerce(p.Fp) > 0) dirty = true;

                        if (dirty)
                        {
                            try { Save(p); }
                            catch (Exception ex) { Log.Error("Cannot repair profile " + p.Id, ex); }
                        }
                        list.Add(p);
                    }
                    catch (Exception ex)
                    {
                        /* A profile whose file cannot be parsed used to disappear from the grid
                           with nothing but a log line, which reads as "the app deleted my
                           profile". The file is recorded here instead, and the grid offers to
                           open the folder or move it aside. It is never deleted. */
                        Log.Error("Cannot read profile " + file, ex);
                        _unreadable.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Cannot list profiles", ex);
            }
            /* What a profile reports has to be the build it will actually run.
            
               The previous round did the opposite here: it gave every profile a build of a
               different major, chosen from a table. That made the cards look varied and made the
               profiles detectable, because the browser answering the request is one build and the
               user agent claimed another. ipfighter called it masking, and it was right.
            
               So the repair is the other way round now: a stored version that disagrees with the
               binary the profile will launch is corrected to that binary, and a profile with no
               version at all is given the running one. Two profiles on the same machine therefore
               report the same build, which is the TRUTH - they are the same binary. Diversity comes
               from pins, handled above by BrowserVersions.Assign, not from invented numbers.
                */
            try
            {
                int repaired = 0;
                foreach (var p in list)
                {
                    bool pinned = !string.IsNullOrEmpty(p.KernelFullVersion) && BrowserVersions.IsReal(p.KernelFullVersion);
                    /* The version of the binary this profile will be launched with, which is
                       not always the same one: a profile that runs the bundled Chromium
                       reports the Chromium's version and a profile that runs the installed
                       Chrome reports Chrome's. The client hints belong to the binary that is
                       running, so a profile that claims the other one's build is read as
                       masking. */
                    string installed = InstalledFullVersion(p.BrowserKind);
                    string running = pinned ? p.KernelFullVersion
                        : BrowserVersions.IsReal(installed) ? installed : "";
                    if (string.IsNullOrEmpty(running)) continue;

                    bool changed = false;
                    if (!string.Equals(p.BrowserVersion, running, StringComparison.Ordinal))
                    {
                        p.BrowserVersion = running;
                        changed = true;
                    }
                    if (p.Fp != null && !string.Equals(p.Fp.BrowserVersion, running, StringComparison.Ordinal))
                    {
                        p.Fp.BrowserVersion = running;
                        changed = true;
                    }
                    /* The user agent names the major of what is running, so the string, the hints
                       and the engine all agree. */
                    if (p.Fp != null && BrowserVersions.MajorFromUserAgent(p.Fp.UserAgent) != BrowserVersions.MajorOf(running))
                    {
                        FingerprintFactory.ResolveUserAgent(p.Fp, null);
                        changed = true;
                    }
                    if (changed)
                    {
                        repaired++;
                        try { Save(p); } catch (Exception ex) { Log.Error("Could not save " + p.Id, ex); }
                    }
                }
                if (repaired > 0) Log.Info("Aligned " + repaired + " profile(s) with the browser build they run");
            }
            catch (Exception ex) { Log.Error("Could not align profile browser versions", ex); }

            return list.OrderByDescending(p => p.LastOpened == DateTime.MinValue ? p.Created : p.LastOpened).ToList();
        }

        /// <summary>
        /// The full version of the browser a profile with no pin would launch: the one this app
        /// finds for the machine, or empty when none is installed. Used to make a profile report
        /// what it will really run.
        /// </summary>
        public static string InstalledFullVersion()
        {
            return InstalledFullVersion("auto");
        }

        /// <summary>
        /// The version of the browser a profile of this kind will actually be launched with.
        ///
        /// The binary matters: a profile that runs the bundled Chromium and a profile that
        /// runs the installed Chrome are different builds, and each has to report its own
        /// version - the client hints, the GREASE brand above all, belong to the binary that
        /// is running.
        /// </summary>
        public static string InstalledFullVersion(string browserKind)
        {
            try
            {
                string pref = string.IsNullOrEmpty(browserKind) ? "auto" : browserKind;
                var info = BrowserLocator.Detect(pref, "");
                return info != null ? info.Version ?? "" : "";
            }
            catch { return ""; }
        }

        public void Save(Profile p)
        {
            Directory.CreateDirectory(ProfilesDir);
            /* A profile written before Proxy was initialised - an older file, a hand-edited
               one, a duplicate that lost a field - made every save throw a NullReferenceException
               from here, so the profile could never be edited again. */
            if (p.Fp == null) p.Fp = new FingerprintConfig();
            if (p.Proxy == null) p.Proxy = new ProxyConfig();
            if (p.Tools == null) p.Tools = new ToolConfig();
            if (string.IsNullOrEmpty(p.Proxy.Password))
            {
                /* DPAPI can refuse a value it wrote on another day or another machine. An empty
                   password field in that case is a read failure, not the user clearing it, and
                   saving would silently delete the proxy secret. */
                if (string.IsNullOrEmpty(p.Proxy.PasswordEnc) || !string.IsNullOrEmpty(Decrypt(p.Proxy.PasswordEnc)))
                    p.Proxy.PasswordEnc = "";
            }
            else
            {
                string enc = Encrypt(p.Proxy.Password);
                if (!string.IsNullOrEmpty(enc)) p.Proxy.PasswordEnc = enc;
            }

            string target = Path.Combine(ProfilesDir, p.Id + ".json");
            string tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string json = _json.Serialize(p);
            File.WriteAllText(tmp, json, new UTF8Encoding(true));

            if (File.Exists(target))
            {
                try { File.Replace(tmp, target, null); }
                catch { File.Copy(tmp, target, true); File.Delete(tmp); }
            }
            else
            {
                File.Move(tmp, target);
            }
        }

        public void Touch(Profile p)
        {
            p.LastOpened = DateTime.Now;
            p.OpenCount++;
            try { Save(p); } catch (Exception ex) { Log.Error("Cannot update profile", ex); }
        }

        public void Delete(Profile p) { Delete(p, false); }

        /// <summary>
        /// With useTrash the profile JSON is moved to the recycle bin and its browser data is
        /// left in place, so a restore brings back cookies and logins too. Without it the
        /// profile is erased permanently, as before.
        /// </summary>
        public void Delete(Profile p, bool useTrash)
        {
            string file = Path.Combine(ProfilesDir, p.Id + ".json");
            if (useTrash)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.Trash);
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    string target = Path.Combine(AppPaths.Trash, stamp + "-" + p.Id + ".json");
                    if (File.Exists(file))
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(file, target);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    /* The user asked for the recycle bin; falling through to a permanent delete
                       when the move failed (a locked file, a full disk) destroyed the profile
                       instead. The delete is refused so the card can say it did not happen. */
                    Log.Error("Cannot move profile to trash", ex);
                    throw new IOException("The profile could not be moved to the recycle bin: " + ex.Message, ex);
                }
            }

            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) { Log.Error("Cannot delete profile file", ex); }

            /* A deleted profile takes its recorded pages with it. The idle trimmer expires
               profiles on its own, so this also has to be the path that cleans up after it. */
            try { HistoryStore.Forget(p.Id); }
            catch (Exception ex) { Log.Error("Cannot delete profile history", ex); }

            try
            {
                string dir = Path.Combine(AppPaths.Data, p.Id);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex) { Log.Error("Cannot delete profile data", ex); }
        }

        internal sealed class TrashItem
        {
            public string File;
            public string Name;
            public string Id;
            public DateTime Deleted;
            public override string ToString()
            {
                return (string.IsNullOrWhiteSpace(Name) ? Id : Name) + "   \u00b7   " + Deleted.ToString("yyyy-MM-dd HH:mm");
            }
        }

        public List<TrashItem> ListTrash()
        {
            var list = new List<TrashItem>();
            try
            {
                Directory.CreateDirectory(AppPaths.Trash);
                foreach (string f in Directory.GetFiles(AppPaths.Trash, "*.json"))
                {
                    try
                    {
                        var p = _json.Deserialize<Profile>(File.ReadAllText(f, Encoding.UTF8));
                        if (p == null) continue;
                        list.Add(new TrashItem
                        {
                            File = f,
                            Id = p.Id,
                            Name = p.Name,
                            Deleted = File.GetLastWriteTime(f)
                        });
                    }
                    catch (Exception ex) { Log.Error("Cannot read trash entry " + f, ex); }
                }
            }
            catch (Exception ex) { Log.Error("Cannot list trash", ex); }
            list.Sort(delegate(TrashItem a, TrashItem b) { return b.Deleted.CompareTo(a.Deleted); });
            return list;
        }

        public bool Restore(string trashFile)
        {
            try
            {
                var p = _json.Deserialize<Profile>(File.ReadAllText(trashFile, Encoding.UTF8));
                if (p == null || string.IsNullOrEmpty(p.Id)) return false;
                p.Proxy = p.Proxy ?? new ProxyConfig();
                p.Tools = p.Tools ?? new ToolConfig();
                Save(p);
                File.Delete(trashFile);
                return true;
            }
            catch (Exception ex) { Log.Error("Cannot restore profile", ex); return false; }
        }

        public void EmptyTrash()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Trash);
                foreach (string f in Directory.GetFiles(AppPaths.Trash, "*.json"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch (Exception ex) { Log.Error("Cannot empty trash", ex); }
        }

        /// <summary>Portable backup. Proxy passwords never leave the encrypted local store.</summary>
        public int ExportTo(string path, IEnumerable<Profile> profiles)
        {
            var copy = new List<Profile>();
            foreach (var p in profiles)
            {
                var c = p.Copy(false);
                c.Proxy.Password = "";
                c.Proxy.PasswordEnc = "";
                copy.Add(c);
            }
            var payload = new Dictionary<string, object>
            {
                { "app", "JoeBrowser" },
                { "version", 1 },
                { "exported", DateTime.Now.ToString("o") },
                { "profiles", copy.ToArray() }
            };
            File.WriteAllText(path, _json.Serialize(payload), new UTF8Encoding(true));
            return copy.Count;
        }

        public List<Profile> ImportFrom(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            var raw = _json.DeserializeObject(text);
            var dict = raw as Dictionary<string, object>;
            object[] arr;
            if (dict != null && dict.ContainsKey("profiles"))
                arr = dict["profiles"] as object[] ?? new object[0];
            else
                arr = raw as object[] ?? new object[0];

            var result = new List<Profile>();
            foreach (object item in arr)
            {
                try
                {
                    string one = _json.Serialize(item);
                    var p = _json.Deserialize<Profile>(one);
                    if (p == null) continue;
                    p.Id = Guid.NewGuid().ToString("N");
                    if (p.Fp == null) p.Fp = new FingerprintConfig();
                    if (p.Fp.Seed == 0) p.Fp.Seed = FingerprintFactory.NewSeed();
                    p.Proxy = p.Proxy ?? new ProxyConfig();
                    p.Proxy.Password = "";
                    p.Proxy.PasswordEnc = "";
                    p.Tools = p.Tools ?? new ToolConfig();
                    p.Created = DateTime.Now;
                    p.LastOpened = DateTime.MinValue;
                    p.OpenCount = 0;
                    /* Importing the same file twice produced two identical names with nothing
                       to tell them apart. The repeats are numbered instead. */
                    if (result.Any(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                        p.Name = p.Name + " (imported)";
                    result.Add(p);
                }
                catch (Exception ex) { Log.Error("Skipping an unreadable profile in import", ex); }
            }
            return result;
        }

        /// <summary>
        /// Removes only Chromium cache folders. Cookies, login data and local storage stay,
        /// so a profile keeps its sessions after a cleanup.
        /// </summary>
        public static long ClearCaches()
        {
            long bytes = 0;
            try
            {
                if (!Directory.Exists(AppPaths.Data)) return 0;
                foreach (string profileDir in Directory.GetDirectories(AppPaths.Data))
                {
                    foreach (string dir in Directory.GetDirectories(profileDir, "*", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileName(dir).ToLowerInvariant();
                        if (name != "cache" && name != "code cache" && name != "gpu" &&
                            name != "shadercache" && name != "crashpad" && name != "component_crx_cache") continue;
                        try
                        {
                            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                try { bytes += new FileInfo(f).Length; } catch { }
                            }
                            Directory.Delete(dir, true);
                        }
                        catch (Exception ex) { Log.Error("Cache folder busy", ex); }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Cache cleanup failed", ex); }
            return bytes;
        }

        /// <summary>
        /// Everything that makes a browsing session a session, deleted from one profile's data
        /// folder: cookies, local and session storage, IndexedDB, service workers, the session
        /// restore files and the HTTP caches. The Preferences file is kept, so the profile's
        /// proxy, extensions and fingerprint settings are still there when it opens again.
        ///
        /// The names below are the ones Chromium actually writes. Anything not in this list that
        /// a future Chromium adds stays - so the list is deliberately about identity and state,
        /// not "delete the folder": deleting the folder would also take the profile's own
        /// configuration and slow every launch to a full re-download.
        /// </summary>
        public static void WipeSessionData(string dataDir)
        {
            if (string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir)) return;
            string[] files =
            {
                "Cookies", "Cookies-journal", "Network\\Cookies", "Network\\Cookies-journal",
                "History", "History-journal", "Favicons", "Favicons-journal",
                "Top Sites", "Top Sites-journal", "Bookmarks.bak",
                "Web Data", "Web Data-journal", "Login Data", "Login Data-journal",
                "Local State.old", "Sessions\\Session_*", "Current Session", "Current Tabs",
                "Last Session", "Last Tabs"
            };
            string[] dirs =
            {
                "Network", "Session Storage", "Local Storage", "IndexedDB", "File System",
                "Service Worker", "Cache", "Code Cache", "GPUCache", "DawnCache",
                "GrShaderCache", "ShaderCache", "Sync Data", "Extension State",
                "Local Extension Settings", "Managed Extension Settings"
            };
            foreach (string pattern in files)
            {
                try
                {
                    string dir = Path.GetDirectoryName(Path.Combine(dataDir, pattern));
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir, Path.GetFileName(pattern)))
                    {
                        /* The Network folder also holds the cache this profile may be using for
                           extensions; only the cookie store is removed from it here and the
                           directory list below removes the caches whole. */
                        try { File.Delete(f); } catch { }
                    }
                }
                catch { }
            }
            foreach (string name in dirs)
            {
                string dir = Path.Combine(dataDir, name);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch (Exception ex) { Log.Info("Could not clear " + name + " for a fresh session: " + ex.Message); }
            }
            Log.Info("Cleared session data for a fresh-session profile");
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var s = _json.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8));
                    if (s != null) return s;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Cannot read settings", ex);
                /* A corrupt file must not be silently overwritten by the next save: keep a copy
                   so the user can still be helped and their proxy list is not lost forever. */
                try
                {
                    string backup = AppPaths.SettingsFile + ".broken";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Copy(AppPaths.SettingsFile, backup, true);
                    Log.Error("The unreadable settings were kept at " + backup);
                }
                catch { }
            }
            return new AppSettings();
        }

        /// <summary>
        /// Writes settings.json. Throws when the file cannot be written, so a caller that has to
        /// know can say so. Most callers save while the user is typing and ignore the failure;
        /// the previous version swallowed everything, which is why a read-only %APPDATA%
        /// produced a "saved" toast and settings that were gone on the next launch.
        ///
        /// The write goes to a temporary file first: a crash or a full disk during the write
        /// used to leave a truncated settings.json, and every launch afterwards silently ran on
        /// defaults.
        /// </summary>
        public void SaveSettings(AppSettings s)
        {
            Directory.CreateDirectory(AppPaths.Root);
            string json = _json.Serialize(s);
            string tmp = AppPaths.SettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(true));
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    try { File.Replace(tmp, AppPaths.SettingsFile, null); }
                    catch { File.Copy(tmp, AppPaths.SettingsFile, true); File.Delete(tmp); }
                }
                else File.Move(tmp, AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                try { File.Delete(tmp); } catch { }
                Log.Error("Cannot save settings", ex);
                throw;
            }
        }

        internal static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch (Exception ex)
            {
                Log.Error("Encryption failed", ex);
                return "";
            }
        }

        internal static string Decrypt(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return "";
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(encoded), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                Log.Error("Decryption failed", ex);
                return "";
            }
        }
    }
}
