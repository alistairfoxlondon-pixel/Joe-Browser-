using System;
using System.Collections.Generic;
using System.Globalization;

namespace JoeBrowser
{
    internal static class FingerprintFactory
    {
        public static readonly string[] Timezones = new[]
        {
            "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "America/Toronto",
            "America/Sao_Paulo", "America/Mexico_City", "America/Bogota", "America/Argentina/Buenos_Aires",
            "Europe/London", "Europe/Dublin", "Europe/Lisbon", "Europe/Madrid", "Europe/Paris", "Europe/Berlin",
            "Europe/Rome", "Europe/Amsterdam", "Europe/Brussels", "Europe/Vienna", "Europe/Zurich", "Europe/Warsaw",
            "Europe/Stockholm", "Europe/Oslo", "Europe/Copenhagen", "Europe/Helsinki", "Europe/Kyiv", "Europe/Moscow",
            "Europe/Istanbul", "Asia/Karachi", "Asia/Kolkata", "Asia/Dubai", "Asia/Riyadh", "Asia/Jerusalem",
            "Asia/Bangkok", "Asia/Jakarta", "Asia/Manila", "Asia/Singapore", "Asia/Kuala_Lumpur", "Asia/Hong_Kong",
            "Asia/Shanghai", "Asia/Tokyo", "Asia/Seoul", "Australia/Sydney", "Pacific/Auckland", "Africa/Cairo",
            "Africa/Lagos", "Africa/Johannesburg"
        };

        public static readonly string[] Languages = new[]
        {
            "en-US", "en-GB", "en-CA", "en-AU", "de-DE", "fr-FR", "es-ES", "es-MX", "pt-BR", "pt-PT",
            "it-IT", "nl-NL", "pl-PL", "sv-SE", "da-DK", "nb-NO", "fi-FI", "tr-TR", "ru-RU", "uk-UA",
            "ar-SA", "he-IL", "hi-IN", "ja-JP", "ko-KR", "zh-CN", "zh-HK", "id-ID", "th-TH", "vi-VN", "ms-MY"
        };

        /// <summary>Profile targets. The first entry is the honest default: mirror this machine.</summary>
        public static readonly string[] OsList = new[]
        {
            "System default", "Windows 11", "Windows 10", "macOS", "Linux", "Android", "iPhone", "iPad"
        };

        public static readonly int[] CoreOptions = new[] { 2, 4, 6, 8, 10, 12, 16, 24 };

        /// <summary>Chrome rounds deviceMemory down to a power of two and caps it at 8, so anything larger is a tell.</summary>
        public static readonly int[] MemoryOptions = new[] { 2, 4, 8 };

        /// <summary>key, label pairs for the leak protection pickers.</summary>
        public static readonly string[][] WebRtcOptions = new[]
        {
            new[] { "proxy", "Proxy only" },
            new[] { "public", "Public interface" },
            new[] { "off", "Disabled" }
        };

        public static readonly string[][] GeoOptions = new[]
        {
            new[] { "proxy", "Follow proxy" },
            new[] { "system", "Real device" },
            new[] { "custom", "Custom coordinates" },
            new[] { "block", "Deny access" }
        };

        public static string CoresLabel(int cores) { return cores + (cores == 1 ? " core" : " cores"); }
        public static string MemoryLabel(int gb) { return gb + " GB RAM"; }

        public static int IndexOfKey(string[][] options, string key, int fallback)
        {
            if (options != null && key != null)
                for (int i = 0; i < options.Length; i++)
                    if (options[i][0].Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
            return fallback;
        }

        /// <summary>Icon that matches a device target, for the leading edge of the picker.</summary>
        public static string DeviceIcon(string os)
        {
            if (IsIphoneOnly(os)) return "iphone";
            if (os != null && os.StartsWith("iPad", StringComparison.OrdinalIgnoreCase)) return "tablet";
            if (IsAndroid(os)) return "android";
            if (IsMac(os)) return "mac";
            if (IsLinux(os)) return "linux";
            return "desktop";
        }

        private static readonly Dictionary<string, string[]> CountryLocale = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            {"US", new[]{"America/New_York","en-US"}}, {"GB", new[]{"Europe/London","en-GB"}},
            {"CA", new[]{"America/Toronto","en-CA"}}, {"AU", new[]{"Australia/Sydney","en-AU"}},
            {"DE", new[]{"Europe/Berlin","de-DE"}}, {"FR", new[]{"Europe/Paris","fr-FR"}},
            {"ES", new[]{"Europe/Madrid","es-ES"}}, {"IT", new[]{"Europe/Rome","it-IT"}},
            {"NL", new[]{"Europe/Amsterdam","nl-NL"}}, {"BE", new[]{"Europe/Brussels","nl-BE"}},
            {"CH", new[]{"Europe/Zurich","de-CH"}}, {"AT", new[]{"Europe/Vienna","de-AT"}},
            {"SE", new[]{"Europe/Stockholm","sv-SE"}}, {"NO", new[]{"Europe/Oslo","nb-NO"}},
            {"DK", new[]{"Europe/Copenhagen","da-DK"}}, {"FI", new[]{"Europe/Helsinki","fi-FI"}},
            {"PL", new[]{"Europe/Warsaw","pl-PL"}}, {"PT", new[]{"Europe/Lisbon","pt-PT"}},
            {"IE", new[]{"Europe/Dublin","en-IE"}}, {"RU", new[]{"Europe/Moscow","ru-RU"}},
            {"UA", new[]{"Europe/Kyiv","uk-UA"}}, {"TR", new[]{"Europe/Istanbul","tr-TR"}},
            {"BR", new[]{"America/Sao_Paulo","pt-BR"}}, {"MX", new[]{"America/Mexico_City","es-MX"}},
            {"AR", new[]{"America/Argentina/Buenos_Aires","es-AR"}}, {"CO", new[]{"America/Bogota","es-CO"}},
            {"IN", new[]{"Asia/Kolkata","en-IN"}}, {"PK", new[]{"Asia/Karachi","en-PK"}},
            {"AE", new[]{"Asia/Dubai","en-AE"}}, {"SA", new[]{"Asia/Riyadh","ar-SA"}},
            {"IL", new[]{"Asia/Jerusalem","he-IL"}}, {"SG", new[]{"Asia/Singapore","en-SG"}},
            {"MY", new[]{"Asia/Kuala_Lumpur","ms-MY"}}, {"TH", new[]{"Asia/Bangkok","th-TH"}},
            {"VN", new[]{"Asia/Ho_Chi_Minh","vi-VN"}}, {"ID", new[]{"Asia/Jakarta","id-ID"}},
            {"PH", new[]{"Asia/Manila","en-PH"}}, {"HK", new[]{"Asia/Hong_Kong","zh-HK"}},
            {"CN", new[]{"Asia/Shanghai","zh-CN"}}, {"JP", new[]{"Asia/Tokyo","ja-JP"}},
            {"KR", new[]{"Asia/Seoul","ko-KR"}}, {"ZA", new[]{"Africa/Johannesburg","en-ZA"}},
            {"NG", new[]{"Africa/Lagos","en-NG"}}, {"EG", new[]{"Africa/Cairo","ar-EG"}}
        };

        /// <summary>
        /// {cssWidth, cssHeight, devicePixelRatio} triples. Chrome reports CSS pixels on every
        /// platform, so the pair has to stay consistent with the density or matchMedia and
        /// screen.width contradict each other.
        /// </summary>
        private static readonly Dictionary<string, double[][]> Screens = new Dictionary<string, double[][]>
        {
            {"Windows 11", new double[][]{ new double[]{1920d,1080,1}, new double[]{2560,1440,1}, new double[]{1536,864,1.25}, new double[]{1366,768,1}, new double[]{1920,1200,1}, new double[]{1280,720,1} }},
            {"Windows 10", new double[][]{ new double[]{1366d,768,1}, new double[]{1920,1080,1}, new double[]{1600,900,1}, new double[]{2560,1440,1}, new double[]{1440,900,1}, new double[]{1280,720,1} }},
            {"macOS", new double[][]{ new double[]{1512d,982,2}, new double[]{1728,1117,2}, new double[]{1440,900,2}, new double[]{2560,1440,2}, new double[]{2304,1296,2} }},
            {"Linux", new double[][]{ new double[]{1920d,1080,1}, new double[]{1366,768,1}, new double[]{2560,1440,1}, new double[]{1600,900,1}, new double[]{3840,2160,1} }},
            {"Android", new double[][]{ new double[]{412d,915,2.625}, new double[]{384,854,2.75}, new double[]{393,873,2.75}, new double[]{411,731,2.6}, new double[]{360,800,2} }},
            {"iPhone", new double[][]{ new double[]{390d,844,3}, new double[]{393,852,3}, new double[]{430,932,3}, new double[]{375,812,3}, new double[]{428,926,3} }},
            {"iPad", new double[][]{ new double[]{1024d,1366,2}, new double[]{820,1180,2}, new double[]{1080,1194,2}, new double[]{1194,834,2} }}
        };

        private static readonly Dictionary<string, string[][]> Gpus = new Dictionary<string, string[][]>
        {
            {"Windows", new[]{
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4070 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 3080 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce GTX 1660 SUPER Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 3050 Laptop GPU Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 6700 XT Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 7800 XT Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon Graphics (Raphael) Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) Iris(R) Xe Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) UHD Graphics 770 Direct3D11 vs_5_0 ps_5_0, D3D11)"}
            }},
            {"macOS", new[]{
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M1, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M2, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M3 Pro, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M3, Unspecified Version)"},
                // Intel Macs use the same Metal renderer string shape, just with the Intel chip name.
                new[]{"Google Inc. (Intel)","ANGLE (Intel, ANGLE Metal Renderer: Intel(R) Iris(TM) Plus Graphics 655, Unspecified Version)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, ANGLE Metal Renderer: Intel(R) UHD Graphics 630, Unspecified Version)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, ANGLE Metal Renderer: AMD Radeon Pro 5500M, Unspecified Version)"}
            }},
            {"Linux", new[]{
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Mesa Intel(R) UHD Graphics 620 (KBL GT2), OpenGL 4.6)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Mesa Intel(R) Xe Graphics (TGL GT2), OpenGL 4.6)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Mesa Intel(R) UHD Graphics 630 (CFL GT2), OpenGL 4.6)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 580 Series (POLARIS10, DRM 3.42.0, 5.15.0-91-generic, LLVM 15.0.7), OpenGL 4.6)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 6600 (NAVI23, DRM 3.49.0, 6.5.0-14-generic, LLVM 17.0.6), OpenGL 4.6)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce GTX 1050 Ti (0x00001C03), OpenGL 4.6)"}
            }},
            {"Android", new[]{
                new[]{"Google Inc. (Adreno)","ANGLE (Adreno, Adreno (TM) 640, OpenGL ES 3.2)"},
                new[]{"Google Inc. (Adreno)","ANGLE (Adreno, Adreno (TM) 660, OpenGL ES 3.2)"},
                new[]{"Google Inc. (Adreno)","ANGLE (Adreno, Adreno (TM) 730, OpenGL ES 3.2)"},
                new[]{"Google Inc. (Adreno)","ANGLE (Adreno, Adreno (TM) 740, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM-Mali)","ANGLE (ARM, Mali-G57 MC2, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM-Mali)","ANGLE (ARM, Mali-G78, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM-Mali)","ANGLE (ARM, Mali-G68, OpenGL ES 3.2)"}
            }},
            {"iOS", new[]{
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple A15 GPU, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple A17 Pro, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple GPU, Unspecified Version)"}
            }}
        };

        /// <summary>Windows timezone ids to IANA names: .NET Framework has no built-in table.</summary>
        private static readonly Dictionary<string, string> WindowsZone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Eastern Standard Time","America/New_York"}, {"Central Standard Time","America/Chicago"},
            {"Mountain Standard Time","America/Denver"}, {"Pacific Standard Time","America/Los_Angeles"},
            {"Arizona Standard Time","America/Phoenix"}, {"Mountain Standard Time (Mexico)","America/Mexico_City"},
            {"Central America Standard Time","America/Mexico_City"}, {"US Mountain Standard Time","America/Phoenix"},
            {"Alaskan Standard Time","America/Anchorage"}, {"Hawaiian Standard Time","Pacific/Honolulu"},
            {"Atlantic Standard Time","America/Halifax"}, {"Newfoundland Standard Time","America/St_Johns"},
            {"Saint Pierre Standard Time","America/Miquelon"},
            {"GMT Standard Time","Europe/London"}, {"UK Standard Time","Europe/London"},
            {"Irish Standard Time","Europe/Dublin"}, {"Central European Standard Time","Europe/Berlin"},
            {"W. Europe Standard Time","Europe/Berlin"}, {"Romance Standard Time","Europe/Paris"},
            {"Paris Standard Time","Europe/Paris"}, {"Atlantic Standard Time (Mexico)","America/Cancun"},
            {"Portugal Standard Time","Europe/Lisbon"}, {"Spain Standard Time","Europe/Madrid"},
            {"Central Europe Standard Time","Europe/Vienna"}, {"Central Brazilian Standard Time","America/Cuiaba"},
            {"SA Eastern Standard Time","America/Sao_Paulo"}, {"Pacific SA Standard Time","America/Santiago"},
            {"Argentina Standard Time","America/Argentina/Buenos_Aires"}, {"Venezuela Standard Time","America/Caracas"},
            {"Eastern European Standard Time","Europe/Bucharest"}, {"GTB Standard Time","Europe/Athens"},
            {"FLE Standard Time","Europe/Kyiv"}, {"Turkey Standard Time","Europe/Istanbul"},
            {"Russian Standard Time","Europe/Moscow"}, {"Kaliningrad Standard Time","Europe/Kaliningrad"},
            {"Nordic Standard Time","Europe/Stockholm"}, {"W. Central Africa Standard Time","Africa/Lagos"},
            {"South Africa Standard Time","Africa/Johannesburg"}, {"Egypt Standard Time","Africa/Cairo"},
            {"Israel Standard Time","Asia/Jerusalem"}, {"Arab Standard Time","Asia/Riyadh"},
            {"Arabian Standard Time","Asia/Dubai"}, {"Pakistan Standard Time","Asia/Karachi"},
            {"India Standard Time","Asia/Kolkata"}, {"Sri Lanka Standard Time","Asia/Colombo"},
            {"Nepal Standard Time","Asia/Kathmandu"}, {"Bangladesh Standard Time","Asia/Dhaka"},
            {"Myanmar Standard Time","Asia/Yangon"}, {"SE Asia Standard Time","Asia/Bangkok"},
            {"Singapore Standard Time","Asia/Singapore"}, {"Malay Peninsula Standard Time","Asia/Kuala_Lumpur"},
            {"China Standard Time","Asia/Shanghai"}, {"Taipei Standard Time","Asia/Taipei"},
            {"Tokyo Standard Time","Asia/Tokyo"}, {"Korea Standard Time","Asia/Seoul"},
            {"AUS Eastern Standard Time","Australia/Sydney"}, {"New Zealand Standard Time","Pacific/Auckland"},
            {"Caucasus Standard Time","Asia/Yerevan"}, {"Georgian Standard Time","Asia/Tbilisi"},
            {"Azerbaijan Standard Time","Asia/Baku"}, {"Iran Standard Time","Asia/Tehran"},
            {"Jordan Standard Time","Asia/Amman"}, {"West Bank Standard Time","Asia/Hebron"},
            {"E. Africa Standard Time","Africa/Nairobi"}, {"Namibia Standard Time","Africa/Windhoek"},
            {"Libya Standard Time","Africa/Tripoli"}, {"Sudan Standard Time","Africa/Khartoum"},
            {"Morocco Standard Time","Africa/Casablanca"}
        };

        public static bool IsMac(string os) { return os != null && os.StartsWith("macOS", StringComparison.OrdinalIgnoreCase); }
        public static bool IsLinux(string os) { return os != null && os.StartsWith("Linux", StringComparison.OrdinalIgnoreCase); }
        public static bool IsAndroid(string os) { return os != null && os.StartsWith("Android", StringComparison.OrdinalIgnoreCase); }
        public static bool IsIos(string os) { return os != null && (os.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase) || os.StartsWith("iPad", StringComparison.OrdinalIgnoreCase)); }
        public static bool IsMobileOs(string os) { return IsAndroid(os) || IsIos(os); }

        public static string DeviceTypeFor(string os)
        {
            if (IsAndroid(os) || IsIphoneOnly(os)) return "mobile";
            if (os != null && os.StartsWith("iPad", StringComparison.OrdinalIgnoreCase)) return "tablet";
            return "desktop";
        }

        private static bool IsIphoneOnly(string os) { return os != null && os.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase); }

        /// <summary>Real screen of this machine, used when the profile mirrors the system.</summary>
        public static double[] SystemScreen()
        {
            try
            {
                var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                // The process is per-monitor DPI aware, so Bounds is in physical pixels; Chrome
                // reports CSS pixels, so the scale has to be divided back out and kept as the DPR.
                double dpr = Ui.Scale > 0 ? Ui.Scale : 1;
                int w = (int)Math.Round(b.Width / dpr), h = (int)Math.Round(b.Height / dpr);
                if (w >= 800 && h >= 600) return new[] { (double)w, h, dpr };
            }
            catch { }
            return new[] { 1920d, 1080, 1 };
        }

        public static string SystemTimezone()
        {
            try
            {
                string id = TimeZoneInfo.Local.Id;
                string iana;
                if (WindowsZone.TryGetValue(id, out iana)) return iana;
                // Nearest match from the curated list by UTC offset keeps the clock honest.
                TimeSpan off = TimeZoneInfo.Local.BaseUtcOffset;
                foreach (string tz in Timezones)
                {
                    int mins = ApproxOffsetMinutes(tz);
                    if (mins == (int)off.TotalMinutes) return tz;
                }
            }
            catch { }
            return "America/New_York";
        }

        private static int ApproxOffsetMinutes(string tz)
        {
            int slash = tz.IndexOf('/');
            string city = slash >= 0 ? tz.Substring(slash + 1).Replace('_', ' ') : tz;
            foreach (KeyValuePair<string, string> kv in WindowsZone)
            {
                int s = kv.Value.IndexOf('/');
                if (s >= 0 && string.Equals(kv.Value.Substring(s + 1), city, StringComparison.OrdinalIgnoreCase))
                    return OffsetOf(kv.Key);
            }
            return int.MinValue;
        }

        private static int OffsetOf(string windowsId)
        {
            try { return (int)TimeZoneInfo.FindSystemTimeZoneById(windowsId).BaseUtcOffset.TotalMinutes; }
            catch { return int.MinValue; }
        }

        public static string SystemLanguage()
        {
            try
            {
                string name = CultureInfo.CurrentCulture.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return "en-US";
        }

        /// <summary>
        /// A seed drawn from a fresh GUID rather than the clock: TickCount-derived seeds collide
        /// between profiles created in the same second, and colliding seeds mean identical noise.
        /// </summary>
        public static int NewSeed()
        {
            byte[] b = Guid.NewGuid().ToByteArray();
            return BitConverter.ToInt32(b, 0) ^ BitConverter.ToInt32(b, 4) ^ BitConverter.ToInt32(b, 8) ^ BitConverter.ToInt32(b, 12);
        }

        public static void Regenerate(FingerprintConfig fp, string os, ProxyConfig proxy)
        {
            if (string.IsNullOrEmpty(os) || os == "System default") os = SystemOsName();
            fp.Seed = NewSeed();
            var r = new Random(fp.Seed);

            fp.Os = os;
            fp.DeviceType = DeviceTypeFor(os);

            double[][] screens;
            if (!Screens.TryGetValue(os, out screens)) screens = Screens["Windows 11"];
            bool mirrorsThisPc = IsWindows(os) && os == SystemOsName();
            double[] screen = mirrorsThisPc
                ? SystemScreen()
                : screens[r.Next(screens.Length)];
            ApplyScreen(fp, os, screen);
            fp.ColorDepth = 24;

            string gpuKey = IsMac(os) ? "macOS" : (IsLinux(os) ? "Linux" : (IsAndroid(os) ? "Android" : (IsIos(os) ? "iOS" : "Windows")));
            var gpus = Gpus[gpuKey];
            var pair = gpus[r.Next(gpus.Length)];
            fp.WebGlVendor = pair[0];
            fp.WebGlRenderer = pair[1];

            int[] cores = IsMobileOs(os) ? new[] { 4, 6, 8, 8 } : new[] { 4, 8, 8, 12, 16 };
            int[] mem = IsMobileOs(os) ? new[] { 4, 4, 8 } : new[] { 4, 8, 8 };
            fp.Cores = cores[r.Next(cores.Length)];
            fp.Memory = mem[r.Next(mem.Length)];

            fp.CanvasNoise = true;
            fp.AudioNoise = true;
            fp.FontsNoise = true;
            fp.WebRtcProtect = true;
            fp.WebRtcMode = "proxy";
            fp.SpoofBattery = true;
            fp.SpoofMac = true;
            fp.DoNotTrack = false;
            fp.GeoMode = "proxy";
            fp.AutoUserAgent = true;
            fp.UserAgentOverride = "";

            ApplyLocale(fp, proxy, r);

            fp.UserAgent = "";
            fp.Platform = "";
            fp.Brand = "";
        }

        /// <summary>Proxy IP wins, then this machine's own IP, then a coherent random choice.</summary>
        public static void ApplyLocale(FingerprintConfig fp, ProxyConfig proxy, Random r)
        {
            if (proxy != null && proxy.Enabled && fp.AutoMatchProxy && !string.IsNullOrEmpty(proxy.LastTimezone))
            {
                fp.Timezone = proxy.LastTimezone;
                fp.Language = LanguageForTimezone(proxy.LastTimezone);
                fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
                return;
            }
            if (proxy != null && proxy.Enabled && fp.AutoMatchProxy && !string.IsNullOrEmpty(proxy.LastCountryCode))
            {
                string[] loc;
                if (CountryLocale.TryGetValue(proxy.LastCountryCode, out loc))
                {
                    fp.Timezone = loc[0];
                    fp.Language = loc[1];
                    fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
                    return;
                }
            }
            fp.Timezone = SystemTimezone();
            fp.Language = SystemLanguage();
            if (Array.IndexOf(Languages, fp.Language) < 0) fp.Language = LanguageForTimezone(fp.Timezone);
            fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
        }

        public static string AcceptLanguageFor(string language)
        {
            if (string.IsNullOrEmpty(language)) return "en-US,en;q=0.9";
            string baseLang = language;
            int dash = language.IndexOf('-');
            if (dash > 0) baseLang = language.Substring(0, dash);
            return language + "," + baseLang + ";q=0.9,en;q=0.8";
        }

        public static string SystemOsName()
        {
            try
            {
                var v = Environment.OSVersion.Version;
                // Build 22000 is the first Windows 11 release; the major number still says 10.
                if (v.Major >= 10 && v.Build >= 22000) return "Windows 11";
                if (v.Major >= 10) return "Windows 10";
            }
            catch { }
            return "Windows 11";
        }

        private static bool IsWindows(string os)
        {
            return os != null && os.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Pairs a resolution with the chrome and pixel density that device really has.</summary>
        public static void ApplyScreen(FingerprintConfig fp, string os, double[] screen)
        {
            fp.ScreenW = (int)Math.Round(screen[0]);
            fp.ScreenH = (int)Math.Round(screen[1]);
            fp.AvailW = fp.ScreenW;
            fp.AvailH = IsMobileOs(os) ? fp.ScreenH : fp.ScreenH - (IsMac(os) ? 25 : (IsLinux(os) ? 27 : 40));
            fp.PixelRatio = screen.Length > 2 && screen[2] > 0
                ? screen[2]
                : (IsMac(os) ? 2 : (IsIos(os) ? 3 : (IsAndroid(os) ? 2.625 : 1)));
            fp.TouchPoints = IsMobileOs(os) ? 5 : 0;
            fp.DeviceType = DeviceTypeFor(os);
        }

        public static List<double[]> ScreensFor(string os)
        {
            double[][] arr;
            if (!Screens.TryGetValue(os ?? "", out arr)) arr = Screens["Windows 11"];
            var list = new List<double[]>();
            foreach (var s in arr) list.Add(s);
            return list;
        }

        public static List<string[]> GpusFor(string os)
        {
            string key = IsMac(os) ? "macOS" : (IsLinux(os) ? "Linux" : (IsAndroid(os) ? "Android" : (IsIos(os) ? "iOS" : "Windows")));
            string[][] arr;
            if (!Gpus.TryGetValue(key, out arr)) arr = Gpus["Windows"];
            var list = new List<string[]>();
            foreach (var g in arr) list.Add(g);
            return list;
        }

        public static string GpuLabel(string[] pair)
        {
            if (pair == null || pair.Length < 2) return "Auto";
            string r = pair[1];
            if (r.StartsWith("ANGLE (", StringComparison.Ordinal))
            {
                string inner = r.Substring(7);
                string[] parts = inner.Split(new[] { ", " }, StringSplitOptions.None);
                if (parts.Length >= 2)
                {
                    string gpu = parts[1];
                    int d3d = gpu.IndexOf(" Direct3D", StringComparison.Ordinal);
                    int mesa = gpu.IndexOf(" (", StringComparison.Ordinal);
                    int metal = gpu.IndexOf(" Metal", StringComparison.Ordinal);
                    if (d3d > 0) gpu = gpu.Substring(0, d3d);
                    else if (mesa > 0) gpu = gpu.Substring(0, mesa);
                    else if (metal > 0) gpu = gpu.Substring(0, metal);
                    int v3 = gpu.IndexOf(") V3", StringComparison.Ordinal);
                    if (v3 > 0) gpu = gpu.Substring(0, v3) + ")";
                    if (gpu.Length > 44) gpu = gpu.Substring(0, 41) + "...";
                    string vendor = parts[0].Replace("ANGLE (", "").Trim();
                    if (vendor.Length > 1 && gpu.IndexOf(vendor, StringComparison.OrdinalIgnoreCase) >= 0) return gpu;
                    return vendor + ", " + gpu;
                }
            }
            if (r.Length > 48) r = r.Substring(0, 45) + "...";
            return r;
        }

        public static void ApplyProxyLocale(FingerprintConfig fp, ProxyConfig proxy)
        {
            if (fp == null || proxy == null || !fp.AutoMatchProxy) return;

            string cc = proxy.LastCountryCode;
            string[] loc;
            if (!string.IsNullOrEmpty(cc) && CountryLocale.TryGetValue(cc, out loc))
            {
                fp.Timezone = loc[0];
                fp.Language = loc[1];
                fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
                return;
            }
            if (!string.IsNullOrEmpty(proxy.LastTimezone))
            {
                fp.Timezone = proxy.LastTimezone;
                fp.Language = LanguageForTimezone(proxy.LastTimezone);
                fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
            }
        }

        /// <summary>Country code behind a locale tag, so the language list can show a flag.</summary>
        public static string CountryForLanguage(string language)
        {
            if (string.IsNullOrEmpty(language)) return "US";
            int dash = language.IndexOf('-');
            if (dash <= 0 || dash + 2 > language.Length) return "US";
            return language.Substring(dash + 1).ToUpperInvariant();
        }

        public static string LanguageForTimezone(string tz)
        {
            if (string.IsNullOrEmpty(tz)) return "en-US";
            if (tz.StartsWith("Europe/London", StringComparison.Ordinal)) return "en-GB";
            if (tz.StartsWith("Europe/Dublin", StringComparison.Ordinal)) return "en-IE";
            if (tz.StartsWith("Europe/Paris", StringComparison.Ordinal)) return "fr-FR";
            if (tz.StartsWith("Europe/Berlin", StringComparison.Ordinal) || tz.StartsWith("Europe/Vienna", StringComparison.Ordinal) || tz.StartsWith("Europe/Zurich", StringComparison.Ordinal)) return "de-DE";
            if (tz.StartsWith("Europe/Madrid", StringComparison.Ordinal)) return "es-ES";
            if (tz.StartsWith("Europe/Rome", StringComparison.Ordinal)) return "it-IT";
            if (tz.StartsWith("Europe/Amsterdam", StringComparison.Ordinal) || tz.StartsWith("Europe/Brussels", StringComparison.Ordinal)) return "nl-NL";
            if (tz.StartsWith("Europe/Warsaw", StringComparison.Ordinal)) return "pl-PL";
            if (tz.StartsWith("Europe/Lisbon", StringComparison.Ordinal)) return "pt-PT";
            if (tz.StartsWith("Europe/Moscow", StringComparison.Ordinal)) return "ru-RU";
            if (tz.StartsWith("Europe/Kyiv", StringComparison.Ordinal) || tz.StartsWith("Europe/Bucharest", StringComparison.Ordinal)) return "uk-UA";
            if (tz.StartsWith("Europe/Istanbul", StringComparison.Ordinal)) return "tr-TR";
            if (tz.StartsWith("Europe/Stockholm", StringComparison.Ordinal)) return "sv-SE";
            if (tz.StartsWith("Europe/Oslo", StringComparison.Ordinal)) return "nb-NO";
            if (tz.StartsWith("Europe/Copenhagen", StringComparison.Ordinal)) return "da-DK";
            if (tz.StartsWith("Europe/Helsinki", StringComparison.Ordinal)) return "fi-FI";
            if (tz.StartsWith("Europe/Athens", StringComparison.Ordinal)) return "el-GR";
            if (tz.StartsWith("America/Sao_Paulo", StringComparison.Ordinal) || tz.StartsWith("America/Cuiaba", StringComparison.Ordinal)) return "pt-BR";
            if (tz.StartsWith("America/Mexico_City", StringComparison.Ordinal)) return "es-MX";
            if (tz.StartsWith("America/Argentina", StringComparison.Ordinal)) return "es-AR";
            if (tz.StartsWith("America/Bogota", StringComparison.Ordinal)) return "es-CO";
            if (tz.StartsWith("America/Santiago", StringComparison.Ordinal)) return "es-CL";
            if (tz.StartsWith("America/Anchorage", StringComparison.Ordinal) || tz.StartsWith("America/Los_Angeles", StringComparison.Ordinal)) return "en-US";
            if (tz.StartsWith("America/Toronto", StringComparison.Ordinal) || tz.StartsWith("America/Halifax", StringComparison.Ordinal)) return "en-CA";
            if (tz.StartsWith("Asia/Kolkata", StringComparison.Ordinal)) return "en-IN";
            if (tz.StartsWith("Asia/Karachi", StringComparison.Ordinal)) return "en-PK";
            if (tz.StartsWith("Asia/Dubai", StringComparison.Ordinal)) return "en-AE";
            if (tz.StartsWith("Asia/Riyadh", StringComparison.Ordinal)) return "ar-SA";
            if (tz.StartsWith("Asia/Jerusalem", StringComparison.Ordinal)) return "he-IL";
            if (tz.StartsWith("Asia/Bangkok", StringComparison.Ordinal)) return "th-TH";
            if (tz.StartsWith("Asia/Jakarta", StringComparison.Ordinal)) return "id-ID";
            if (tz.StartsWith("Asia/Manila", StringComparison.Ordinal)) return "en-PH";
            if (tz.StartsWith("Asia/Singapore", StringComparison.Ordinal)) return "en-SG";
            if (tz.StartsWith("Asia/Kuala_Lumpur", StringComparison.Ordinal)) return "ms-MY";
            if (tz.StartsWith("Asia/Hong_Kong", StringComparison.Ordinal)) return "zh-HK";
            if (tz.StartsWith("Asia/Shanghai", StringComparison.Ordinal)) return "zh-CN";
            if (tz.StartsWith("Asia/Tokyo", StringComparison.Ordinal)) return "ja-JP";
            if (tz.StartsWith("Asia/Seoul", StringComparison.Ordinal)) return "ko-KR";
            if (tz.StartsWith("Asia/Ho_Chi_Minh", StringComparison.Ordinal)) return "vi-VN";
            if (tz.StartsWith("Australia/", StringComparison.Ordinal)) return "en-AU";
            if (tz.StartsWith("Pacific/Auckland", StringComparison.Ordinal)) return "en-NZ";
            if (tz.StartsWith("Pacific/Honolulu", StringComparison.Ordinal)) return "en-US";
            if (tz.StartsWith("Africa/Cairo", StringComparison.Ordinal)) return "ar-EG";
            if (tz.StartsWith("Africa/Johannesburg", StringComparison.Ordinal)) return "en-ZA";
            if (tz.StartsWith("Africa/Lagos", StringComparison.Ordinal)) return "en-NG";
            return "en-US";
        }

        /// <summary>Builds a coherent UA + platform for the profile against the real installed browser.</summary>
        public static void ResolveUserAgent(FingerprintConfig fp, BrowserInfo browser)
        {
            if (fp == null) return;
            if (!fp.AutoUserAgent && !string.IsNullOrWhiteSpace(fp.UserAgentOverride))
            {
                fp.UserAgent = fp.UserAgentOverride.Trim();
                ApplyPlatformFromUa(fp);
                return;
            }
            if (!string.IsNullOrWhiteSpace(fp.UserAgent)) return;

            int major = browser != null && browser.MajorVersion > 0 ? browser.MajorVersion : 131;
            bool edge = browser != null && browser.IsEdge;
            bool firefox = browser != null && browser.IsFirefox;
            string device = string.IsNullOrEmpty(fp.DeviceType) ? "desktop" : fp.DeviceType;

            if (IsIos(fp.Os))
            {
                // iOS browsers are all WebKit: match the Safari string, not a Chrome one.
                bool pad = fp.Os.StartsWith("iPad", StringComparison.OrdinalIgnoreCase);
                fp.UserAgent = "Mozilla/5.0 (" + (pad ? "iPad; CPU OS 17_5 like Mac OS X" : "iPhone; CPU iPhone OS 17_5 like Mac OS X") +
                    ") AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
                fp.Platform = pad ? "iPad" : "iPhone";
                fp.Brand = "Safari";
                return;
            }

            if (firefox)
            {
                string platToken;
                if (IsMac(fp.Os)) platToken = "Macintosh; Intel Mac OS X 10.15";
                else if (IsLinux(fp.Os)) platToken = "X11; Linux x86_64";
                else if (IsAndroid(fp.Os)) platToken = "Android 10; Mobile";
                else platToken = "Windows NT 10.0; Win64; x64";
                int fmajor = browser != null && browser.MajorVersion > 0 ? browser.MajorVersion : 128;
                fp.UserAgent = "Mozilla/5.0 (" + platToken + "; rv:" + fmajor + ".0) Gecko/20100101 Firefox/" + fmajor + ".0";
                fp.Platform = IsMac(fp.Os) ? "MacIntel" : (IsLinux(fp.Os) ? "Linux x86_64" : "Win32");
                fp.Brand = "Firefox";
                return;
            }

            string platToken2, navPlatform;
            if (IsMac(fp.Os)) { platToken2 = "Macintosh; Intel Mac OS X 10_15_7"; navPlatform = "MacIntel"; }
            else if (IsLinux(fp.Os)) { platToken2 = "X11; Linux x86_64"; navPlatform = "Linux x86_64"; }
            else if (IsAndroid(fp.Os))
            {
                platToken2 = device == "tablet" ? "Linux; Android 14; SM-X710" : "Linux; Android 14; SM-S918B";
                navPlatform = "Linux armv8l";
            }
            else { platToken2 = "Windows NT 10.0; Win64; x64"; navPlatform = "Win32"; }

            string full = FullChromiumVersion(browser, major);
            string ua = "Mozilla/5.0 (" + platToken2 + ") AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" + full + " Safari/537.36";
            if (IsMobileOs(fp.Os)) ua = ua.Replace("Safari/537.36", device == "tablet" ? "Safari/537.36" : "Mobile Safari/537.36");
            if (edge) ua += " Edg/" + full;

            fp.UserAgent = ua;
            fp.Platform = navPlatform;
            fp.Brand = edge ? "Edge" : "Chrome";
        }

        /// <summary>
        /// Chrome and Edge always ship a four-part build in the UA string. "131.0.0.0" is a
        /// pattern no real browser produces, so the installed build number is reused where possible.
        /// </summary>
        public static string FullChromiumVersion(BrowserInfo browser, int major)
        {
            string v = browser != null ? browser.Version : null;
            if (!string.IsNullOrEmpty(v))
            {
                string[] parts = v.Split('.');
                if (parts.Length == 4)
                {
                    int head;
                    if (int.TryParse(parts[0], out head) && head > 0) return v;
                }
            }
            return major + ".0.0.0";
        }

        private static void ApplyPlatformFromUa(FingerprintConfig fp)
        {
            string ua = fp.UserAgent ?? "";
            if (ua.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0) { fp.Platform = "iPhone"; fp.Brand = "Safari"; }
            else if (ua.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0) { fp.Platform = "iPad"; fp.Brand = "Safari"; }
            else if (ua.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0) { fp.Platform = "Linux armv8l"; fp.Brand = "Chrome"; }
            else if (ua.IndexOf("Firefox/", StringComparison.OrdinalIgnoreCase) >= 0) { fp.Brand = "Firefox"; fp.Platform = "Win32"; }
            else if (ua.IndexOf("Mac OS X", StringComparison.OrdinalIgnoreCase) >= 0) { fp.Platform = "MacIntel"; fp.Brand = "Chrome"; }
            else if (ua.IndexOf("Linux", StringComparison.OrdinalIgnoreCase) >= 0) { fp.Platform = "Linux x86_64"; fp.Brand = "Chrome"; }
            else { fp.Platform = "Win32"; fp.Brand = "Chrome"; }
        }

        public static string LocaleFromLanguage(string language)
        {
            return string.IsNullOrEmpty(language) ? "en-US" : language;
        }
    }
}
