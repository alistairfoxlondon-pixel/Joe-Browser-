using System;
using System.Collections.Generic;
using System.Globalization;
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

        /* A fresh serializer per call: the shared JavaScriptSerializer is not thread-safe for
           concurrent calls, and its default ~2 MB cap made a long proxy list fail Save/Load. */
        private static JavaScriptSerializer NewJson()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public static string FilePath { get { return Path.Combine(AppPaths.Root, "proxies.json"); } }

        public static List<ProxyEntry> Load()
        {
            var list = new List<ProxyEntry>();
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (!File.Exists(FilePath)) return list;
                string text;
                try { text = File.ReadAllText(FilePath, Encoding.UTF8); }
                catch (Exception ex)
                {
                    Log.Error("Cannot read proxy library", ex);
                    return list;
                }
                List<ProxyEntry> items;
                try { items = NewJson().Deserialize<List<ProxyEntry>>(text); }
                catch (Exception ex)
                {
                    /* A corrupt or half-written vault used to come back as an empty list, the
                       page then said "No proxies saved yet", and the next Save overwrote the
                       file with that emptiness - the user's whole proxy library gone, quietly.
                       The unreadable file is set aside under a timestamped name the user can
                       send, exactly like the extension library does. A fixed ".broken" name
                       threw when a previous copy was already there, and then nothing was set
                       aside at all. */
                    Log.Error("Proxy library is unreadable; setting it aside", ex);
                    try
                    {
                        string bak = FilePath + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                        File.Move(FilePath, bak);
                    }
                    catch (Exception ex2) { Log.Error("Proxy library backup failed", ex2); }
                    return list;
                }
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

        /// <summary>True when the vault reached the disk. Callers surface a failed write
        /// instead of toasting "saved" and losing the row on the next visit.</summary>
        public static bool Save(List<ProxyEntry> items)
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
                File.WriteAllText(tmp, NewJson().Serialize(items ?? new List<ProxyEntry>()), new UTF8Encoding(true));
                if (File.Exists(FilePath))
                {
                    try { File.Replace(tmp, FilePath, null); }
                    catch { File.Copy(tmp, FilePath, true); File.Delete(tmp); }
                }
                else File.Move(tmp, FilePath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Cannot save proxy library", ex);
                return false;
            }
        }

        public static ProxyConfig Copy(ProxyConfig c)
        {
            return c == null ? new ProxyConfig() : c.Copy();
        }

        /// <summary>
        /// Accepts scheme://user:pass@host:port, host:port and host:port:user:pass - with IPv6
        /// in its bracketed form, [2001:db8::1]:8080, which the colon splitter used to shred
        /// into six parts and then reject as "not a proxy".
        /// </summary>
        public static ProxyConfig ParseLine(string line)
        {
            var p = new ProxyConfig();
            /* A full-width colon is what an IME in a CJK layout types, and "host ： 8080" is
               the same address as "host:8080" once the separator is normalised. */
            string s = (line ?? "").Trim().Replace('\uFF1A', ':');
            if (s.Length == 0) return p;

            string type = "http";
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0)
            {
                string head = s.Substring(0, scheme).ToLowerInvariant();
                if (head == "socks5" || head == "socks5h") type = "socks5";
                else if (head == "https") type = "https";
                else if (head == "socks4" || head == "socks4a") type = "socks5";   // best effort: Chrome speaks socks5
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

            /* IPv6 arrives bracketed. Everything up to ']' is the host, whatever colons it
               holds; the port is the digits after the closing bracket. */
            string host;
            int port;
            if (s.Length > 0 && s[0] == '[')
            {
                int close = s.IndexOf(']');
                if (close < 0) return p;
                host = s.Substring(1, close - 1);
                string rest = s.Substring(close + 1);
                if (!rest.StartsWith(":") ||
                    !int.TryParse(rest.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ||
                    port <= 0 || port > 65535) return p;
            }
            else
            {
                string[] bits = s.Split(':');
                if (bits.Length < 2 || string.IsNullOrWhiteSpace(bits[0])) return p;
                if (!int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ||
                    port <= 0 || port > 65535) return p;
                host = bits[0].Trim();
                if (bits.Length >= 4) { user = bits[2]; pass = bits[3]; }
            }

            p.Type = type;
            p.Host = host.Trim();
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
