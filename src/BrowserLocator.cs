using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace JoeBrowser
{
    internal class BrowserInfo
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public int MajorVersion { get; set; }
        public bool IsEdge { get; set; }
        public bool IsBrave { get; set; }
        public bool IsFirefox { get; set; }
        public bool Found { get { return !string.IsNullOrEmpty(Path) && File.Exists(Path); } }

        /// <summary>Chromium engines accept CDP and command line fingerprint flags.</summary>
        public bool IsChromium { get { return !IsFirefox; } }

        /// <summary>Set when the executable came from a pinned kernel, so a Chromium build is
        /// never mistaken for Google Chrome just because its binary is called chrome.exe.</summary>
        public string ForcedEngine { get; set; }

        public string EngineKey
        {
            get
            {
                if (!string.IsNullOrEmpty(ForcedEngine)) return ForcedEngine;
                if (IsFirefox) return "firefox";
                if (IsEdge) return "edge";
                if (IsBrave) return "brave";
                if (Name != null && Name.IndexOf("chromium", StringComparison.OrdinalIgnoreCase) >= 0) return "chromium";
                return "chrome";
            }
        }
    }

    internal static class BrowserLocator
    {
        public static BrowserInfo Detect(string preference, string customPath)
        {
            var list = FindAll();
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                return FromFile(customPath);

            if (!string.IsNullOrEmpty(preference) && preference != "auto")
            {
                foreach (var b in list)
                {
                    if (preference.Equals("edge", StringComparison.OrdinalIgnoreCase) && b.IsEdge) return b;
                    if (preference.Equals("brave", StringComparison.OrdinalIgnoreCase) && b.IsBrave) return b;
                    if (preference.Equals("firefox", StringComparison.OrdinalIgnoreCase) && b.IsFirefox) return b;
                    if (preference.Equals("chrome", StringComparison.OrdinalIgnoreCase) && !b.IsEdge && !b.IsBrave && !b.IsFirefox && b.Name == "Chrome") return b;
                    if (preference.Equals("chromium", StringComparison.OrdinalIgnoreCase) && b.EngineKey == "chromium") return b;
                }
            }

            /* "auto" means Joe Browser's own kernel, the way AdsPower and GoLogin behave: the
               engine this app downloaded into its own folder always wins, so a profile never
               silently rides the Chrome or Edge that happens to sit on the PC. A local browser
               is only used when the user picks it by name - never by default. */
            var own = OwnKernel(list);
            if (own != null) return own;

            foreach (var b in list)
                if (b.Name == "Chrome") return b;
            foreach (var b in list)
                if (!b.IsFirefox) return b;
            foreach (var b in list)
                return b;

            return new BrowserInfo { Name = "Not found", Path = "", Version = "", MajorVersion = 0 };
        }

        /// <summary>Engines the user can pick, with whether they are installed on this PC.</summary>
        public static List<string[]> Options()
        {
            var list = FindAll();
            var installed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in list) installed[b.EngineKey] = true;

            var rows = new List<string[]>();
            /* The built-in kernel leads the picker: it is what a profile should run on, and the
               local browsers follow as explicit choices for people who really want them. */
            Add(rows, installed, "chromium", "Chromium (built-in)");
            rows.Add(new[] { "auto", "Detect automatically" });
            Add(rows, installed, "chrome", "Google Chrome");
            Add(rows, installed, "edge", "Microsoft Edge");
            Add(rows, installed, "brave", "Brave");
            Add(rows, installed, "firefox", "Firefox");
            return rows;
        }

        /// <summary>The engine Joe Browser downloaded into its own folder, or null.</summary>
        public static BrowserInfo OwnKernel(List<BrowserInfo> list)
        {
            if (list == null) return null;
            foreach (var b in list)
            {
                try
                {
                    if (b.Path != null && string.Equals(System.IO.Path.GetFullPath(b.Path),
                        System.IO.Path.GetFullPath(AppPaths.ChromiumExe), StringComparison.OrdinalIgnoreCase))
                        return b;
                }
                catch { }
            }
            return null;
        }

        private static void Add(List<string[]> rows, Dictionary<string, bool> installed, string key, string label)
        {
            rows.Add(new[] { key, installed.ContainsKey(key) ? label : label + " (not installed)" });
        }

        public static List<BrowserInfo> FindAll()
        {
            var result = new List<BrowserInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var candidates = new List<string>();
            AddRegistry(candidates, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            AddRegistry(candidates, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            AddRegistry(candidates, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\brave.exe");
            AddRegistry(candidates, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe");

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            candidates.Add(System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"));
            candidates.Add(System.IO.Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"));
            candidates.Add(System.IO.Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
            candidates.Add(System.IO.Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"));
            candidates.Add(System.IO.Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            candidates.Add(System.IO.Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"));
            candidates.Add(System.IO.Path.Combine(pf86, @"BraveSoftware\Brave-Browser\Application\brave.exe"));
            candidates.Add(System.IO.Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe"));
            candidates.Add(System.IO.Path.Combine(pf, @"Mozilla Firefox\firefox.exe"));
            candidates.Add(System.IO.Path.Combine(pf86, @"Mozilla Firefox\firefox.exe"));
            candidates.Add(System.IO.Path.Combine(local, @"Mozilla Firefox\firefox.exe"));
            candidates.Add(System.IO.Path.Combine(local, @"Programs\Firefox\firefox.exe"));
            candidates.Add(System.IO.Path.Combine(appdata, @"Firefox\firefox.exe"));

            // Joe Browser's own kernel first: "auto" and the engine picker must resolve to it
            // before any browser that came with the PC.
            candidates.Insert(0, AppPaths.ChromiumExe);

            foreach (string c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c) || !File.Exists(c)) continue;
                string full;
                try { full = System.IO.Path.GetFullPath(c); } catch { continue; }
                if (!seen.Add(full)) continue;
                var info = FromFile(full);
                /* FromFile reads the version resource, and a read that fails must not remove an
                   executable that is demonstrably on disk: an entry with no version is a true
                   statement, a missing entry is not. Found is derived from the path, so it stays
                   correct either way. */
                if (info == null) info = new BrowserInfo { Path = full, Name = EngineNameFromPath(full) };
                result.Add(info);
            }
            /* A failed candidate must never be able to throw the whole list away: an empty list
               is indistinguishable from "no browser on this PC". */
            return result;
        }

        /// <summary>A display name from the file name alone, used when the version resource is unreadable.</summary>
        private static string EngineNameFromPath(string path)
        {
            string n = path ?? "";
            if (n.EndsWith("msedge.exe", StringComparison.OrdinalIgnoreCase)) return "Edge";
            if (n.EndsWith("brave.exe", StringComparison.OrdinalIgnoreCase)) return "Brave";
            if (n.EndsWith("firefox.exe", StringComparison.OrdinalIgnoreCase)) return "Firefox";
            if (n.IndexOf("chromium", StringComparison.OrdinalIgnoreCase) >= 0) return "Chromium";
            return "Chrome";
        }

        private static void AddRegistry(List<string> list, string key)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(key))
                {
                    if (k != null)
                    {
                        var v = k.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim('"'));
                    }
                }
                using (var k = Registry.CurrentUser.OpenSubKey(key))
                {
                    if (k != null)
                    {
                        var v = k.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim('"'));
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Wraps an executable that is not in a standard install location - a pinned kernel, for
        /// instance. The engine is supplied by the caller because a Chromium snapshot and Google
        /// Chrome ship the same chrome.exe and cannot be told apart by file name.
        /// </summary>
        public static BrowserInfo FromPath(string path, string engineKey)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var info = FromFile(path);
            info.ForcedEngine = engineKey;
            if (engineKey == "chromium") info.Name = "Chromium";
            else if (engineKey == "brave") { info.Name = "Brave"; info.IsBrave = true; }
            else if (engineKey == "firefox") { info.Name = "Firefox"; info.IsFirefox = true; }
            else if (engineKey == "edge") { info.Name = "Edge"; info.IsEdge = true; }
            else if (engineKey == "chrome") { info.Name = "Chrome"; info.IsEdge = false; info.IsBrave = false; }
            return info;
        }

        private static BrowserInfo FromFile(string path)
        {
            var info = new BrowserInfo { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) };            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                info.Version = fvi.ProductVersion ?? fvi.FileVersion ?? "";
                string[] bits = info.Version.Split('.');
                int major;
                if (bits.Length > 0 && int.TryParse(bits[0], out major)) info.MajorVersion = major;
                info.IsEdge = info.Name.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0;
                info.IsBrave = info.Name.IndexOf("brave", StringComparison.OrdinalIgnoreCase) >= 0;
                info.IsFirefox = info.Name.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) >= 0;
                if (info.IsEdge) info.Name = "Edge";
                else if (info.IsBrave) info.Name = "Brave";
                else if (info.IsFirefox) info.Name = "Firefox";
                else if (info.Name.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0) info.Name = "Chrome";
                // The browser Joe Browser downloads for itself lives under the app data folder;
                // it is Chromium, not Google Chrome, and must appear as such in the pickers.
                try
                {
                    if (string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(AppPaths.ChromiumExe), StringComparison.OrdinalIgnoreCase))
                        info.Name = "Chromium";
                }
                catch { }
            }
            catch (Exception ex)
            {
                Log.Error("Cannot read browser version: " + path, ex);
            }
            return info;
        }
    }
}
