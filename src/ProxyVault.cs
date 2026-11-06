using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>One saved proxy in the Proxy Library. Secrets are DPAPI-protected at rest.</summary>
    public class ProxyEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Note { get; set; }
        public ProxyConfig Config { get; set; }

        /// <summary>0 = never checked, 1 = working, -1 = failed.</summary>
        public int LastStatus { get; set; }
        public string LastIp { get; set; }
        public string LastCountry { get; set; }
        public string LastCity { get; set; }
        public int LastLatencyMs { get; set; }
        public DateTime LastChecked { get; set; }

        public ProxyEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Note = "";
            Config = new ProxyConfig();
            LastIp = "";
            LastCountry = "";
            LastCity = "";
        }

        public string Connection
        {
            get
            {
                var c = Config;
                if (c == null || !c.Enabled) return "No proxy";
                return c.Type.ToUpperInvariant() + "  " + c.Host + ":" + c.Port;
            }
        }

        public string Location
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LastIp)) return "Not checked yet";
                string place = !string.IsNullOrWhiteSpace(LastCity) ? LastCity : LastCountry;
                if (string.IsNullOrWhiteSpace(place)) return LastIp;
                return LastIp + "  ·  " + place;
            }
        }
    }

    /// <summary>
    /// Local-first proxy library. A proxy is saved once here and picked from a list when a
    /// profile is created, so credentials never have to be re-typed per profile.
    /// </summary>
    internal static class ProxyVault
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JoeBrowser.Proxy.v1");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static string FilePath { get { return Path.Combine(AppPaths.Root, "proxies.json"); } }

        public static List<ProxyEntry> Load()
        {
            var list = new List<ProxyEntry>();
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (!File.Exists(FilePath)) return list;
                var items = Json.Deserialize<List<ProxyEntry>>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (items == null) return list;
                foreach (var e in items)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    if (e.Config == null) e.Config = new ProxyConfig();
                    e.Config.Password = Decrypt(e.Config.PasswordEnc);
                    list.Add(e);
                }
            }
            catch (Exception ex) { Log.Error("Cannot read proxy library", ex); }
            return list;
        }

        public static void Save(List<ProxyEntry> items)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                foreach (var e in items)
                {
                    if (e == null || e.Config == null) continue;
                    if (string.IsNullOrEmpty(e.Config.Password))
                    {
                        // An unreadable DPAPI blob must not silently wipe a stored secret.
                        if (string.IsNullOrEmpty(e.Config.PasswordEnc) || !string.IsNullOrEmpty(Decrypt(e.Config.PasswordEnc)))
                            e.Config.PasswordEnc = "";
                    }
                    else
                    {
                        string enc = Encrypt(e.Config.Password);
                        if (!string.IsNullOrEmpty(enc)) e.Config.PasswordEnc = enc;
                    }
                }
                string tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(items ?? new List<ProxyEntry>()), new UTF8Encoding(true));
                if (File.Exists(FilePath))
                {
                    try { File.Replace(tmp, FilePath, null); }
                    catch { File.Copy(tmp, FilePath, true); File.Delete(tmp); }
                }
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error("Cannot save proxy library", ex); }
        }

        public static ProxyConfig Copy(ProxyConfig c)
        {
            return c == null ? new ProxyConfig() : c.Copy();
        }

        /// <summary>Accepts scheme://user:pass@host:port, host:port and host:port:user:pass.</summary>
        public static ProxyConfig ParseLine(string line)
        {
            var p = new ProxyConfig();
            string s = (line ?? "").Trim();
            if (s.Length == 0) return p;

            string type = "http";
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0)
            {
                string head = s.Substring(0, scheme).ToLowerInvariant();
                if (head == "socks5" || head == "socks5h") type = "socks5";
                else if (head == "https") type = "https";
                s = s.Substring(scheme + 3);
            }
            int slash = s.IndexOf('/');
            if (slash > 0) s = s.Substring(0, slash);

            string user = "", pass = "";
            int at = s.LastIndexOf('@');
            if (at > 0)
            {
                string cred = s.Substring(0, at);
                s = s.Substring(at + 1);
                int colon = cred.IndexOf(':');
                if (colon >= 0) { user = cred.Substring(0, colon); pass = cred.Substring(colon + 1); }
                else user = cred;
            }

            string[] bits = s.Split(':');
            if (bits.Length < 2 || string.IsNullOrWhiteSpace(bits[0])) return p;
            int port;
            if (!int.TryParse(bits[1], out port) || port <= 0 || port > 65535) return p;
            if (bits.Length >= 4) { user = bits[2]; pass = bits[3]; }

            p.Type = type;
            p.Host = bits[0].Trim();
            p.Port = port;
            p.User = user;
            p.Password = pass;
            return p;
        }

        private static string Encrypt(string plain)
        {
            try
            {
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch { return ""; }
        }

        private static string Decrypt(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return "";
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(encoded), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }
    }
}
