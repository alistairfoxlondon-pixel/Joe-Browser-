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

        public string EngineKey
        {
            get
            {
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
                }
            }

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
            rows.Add(new[] { "auto", installed.Count > 0 ? "Detect automatically" : "Detect automatically (none found)" });
            Add(rows, installed, "chrome", "Google Chrome");
            Add(rows, installed, "edge", "Microsoft Edge");
            Add(rows, installed, "brave", "Brave");
            Add(rows, installed, "chromium", "Chromium");
            Add(rows, installed, "firefox", "Firefox");
            return rows;
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

            // Chromium fetched by Joe Browser (used when nothing else is installed)
            candidates.Add(AppPaths.ChromiumExe);

            foreach (string c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c) || !File.Exists(c)) continue;
                string full;
                try { full = System.IO.Path.GetFullPath(c); } catch { continue; }
                if (!seen.Add(full)) continue;
                result.Add(FromFile(full));
            }
            return result;
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

        private static BrowserInfo FromFile(string path)
        {
            var info = new BrowserInfo { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) };
            try
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
            }
            catch (Exception ex)
            {
                Log.Error("Cannot read browser version: " + path, ex);
            }
            return info;
        }
    }
}
