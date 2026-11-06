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

        public ExtensionItem()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Folder = "";
            Enabled = true;
            Source = "folder";
            StoreUrl = "";
            Version = "";
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

        public static string FilePath { get { return Path.Combine(AppPaths.Root, "extensions.json"); } }
        public static string ImportedRoot { get { return Path.Combine(AppPaths.Extensions, "imported"); } }

        public static List<ExtensionItem> Load()
        {
            var list = new List<ExtensionItem>();
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (!File.Exists(FilePath)) return list;
                var items = Json.Deserialize<List<ExtensionItem>>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (items == null) return list;
                foreach (var e in items)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
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
                File.WriteAllText(tmp, Json.Serialize(items ?? new List<ExtensionItem>()), new UTF8Encoding(true));
                if (File.Exists(FilePath))
                {
                    try { File.Replace(tmp, FilePath, null); }
                    catch { File.Copy(tmp, FilePath, true); File.Delete(tmp); }
                }
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error("Cannot save extension library", ex); }
        }

        /// <summary>Folders that are switched on and really contain a manifest.</summary>
        public static List<string> EnabledFolders()
        {
            var result = new List<string>();
            foreach (var e in Load())
            {
                if (!e.Enabled || string.IsNullOrEmpty(e.Folder)) continue;
                if (e.Folder.IndexOf(',') >= 0) continue; // Chrome splits --load-extension on commas
                try
                {
                    if (Directory.Exists(e.Folder) && File.Exists(Path.Combine(e.Folder, "manifest.json")))
                        if (!result.Contains(e.Folder)) result.Add(e.Folder);
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
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) JoeBrowser/1.2");
                    wc.DownloadFile(endpoint, crx);
                }

                byte[] raw = File.ReadAllBytes(crx);
                int start = FindZipStart(raw);
                if (start < 0) throw new InvalidOperationException("The store did not return a valid extension package. Try adding the unpacked folder instead.");
                File.WriteAllBytes(zip, Slice(raw, start));

                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                using (var archive = ZipFile.OpenRead(zip)) archive.ExtractToDirectory(target);

                string manifest = Path.Combine(target, "manifest.json");
                if (!File.Exists(manifest))
                    throw new InvalidOperationException("The downloaded package has no manifest.json, so Chromium cannot load it.");

                string name = NameFromManifest(manifest) ?? id;
                string version = VersionFromManifest(manifest) ?? "";
                return new ExtensionItem
                {
                    Name = name,
                    Folder = target,
                    Enabled = true,
                    Source = "url",
                    StoreUrl = url,
                    Version = version
                };
            }
            finally
            {
                TryDelete(crx);
                TryDelete(zip);
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
            string safe = Guid.NewGuid().ToString("N").Substring(0, 12);
            string target = Path.Combine(ImportedRoot, safe);
            Directory.CreateDirectory(ImportedRoot);
            CopyTree(folder, target);
            return new ExtensionItem
            {
                Name = name,
                Folder = target,
                Enabled = true,
                Source = "folder",
                Version = VersionFromManifest(Path.Combine(target, "manifest.json"))
            };
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

        public static string StoreSearchUrl(string query)
        {
            return "https://chromewebstore.google.com/search/" + query;
        }
    }
}
