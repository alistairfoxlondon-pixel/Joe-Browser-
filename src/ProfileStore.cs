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
                        p.Proxy.Password = Decrypt(p.Proxy.PasswordEnc);
                        if (p.OpenCount < 0) p.OpenCount = 0;
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

        public void Delete(Profile p)
        {
            try
            {
                string file = Path.Combine(AppPaths.Profiles, p.Id + ".json");
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception ex) { Log.Error("Cannot delete profile file", ex); }

            try
            {
                string dir = Path.Combine(AppPaths.Data, p.Id);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex) { Log.Error("Cannot delete profile data", ex); }
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
