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
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public List<Profile> LoadAll()
        {
            var list = new List<Profile>();
            /* Older builds drew seeds from the clock, so profiles created in the same
               millisecond share one seed - which means identical canvas, audio and font
               noise. Repair those on load so every stored profile stays unique. */
            var seeds = new HashSet<int>();
            try
            {
                Directory.CreateDirectory(AppPaths.Profiles);
                foreach (string file in Directory.GetFiles(AppPaths.Profiles, "*.json"))
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

                        if (p.Fp.Seed == 0 || seeds.Contains(p.Fp.Seed))
                        {
                            int fresh;
                            do { fresh = FingerprintFactory.NewSeed(); }
                            while (fresh == 0 || seeds.Contains(fresh));
                            p.Fp.Seed = fresh;
                            try { Save(p); } catch (Exception ex) { Log.Error("Cannot repair fingerprint seed", ex); }
                        }
                        seeds.Add(p.Fp.Seed);
                        list.Add(p);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Cannot read profile " + file, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Cannot list profiles", ex);
            }
            return list.OrderByDescending(p => p.LastOpened == DateTime.MinValue ? p.Created : p.LastOpened).ToList();
        }

        public void Save(Profile p)
        {
            Directory.CreateDirectory(AppPaths.Profiles);
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

            string target = Path.Combine(AppPaths.Profiles, p.Id + ".json");
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
            string file = Path.Combine(AppPaths.Profiles, p.Id + ".json");
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
                catch (Exception ex) { Log.Error("Cannot move profile to trash", ex); }
            }

            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) { Log.Error("Cannot delete profile file", ex); }

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
                return (string.IsNullOrWhiteSpace(Name) ? Id : Name) + "   -   " + Deleted.ToString("yyyy-MM-dd HH:mm");
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
            catch (Exception ex) { Log.Error("Cannot read settings", ex); }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(AppPaths.SettingsFile, _json.Serialize(s), new UTF8Encoding(true));
            }
            catch (Exception ex) { Log.Error("Cannot save settings", ex); }
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
