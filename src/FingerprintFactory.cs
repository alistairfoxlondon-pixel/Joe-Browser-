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

        /// <summary>Android releases a Chromium build still runs on, newest first. "auto" takes
        /// the build the handset behind the viewport actually shipped.</summary>
        public static readonly string[] AndroidVersions = new[] { "auto", "16", "15", "14", "13", "12", "11" };

        /// <summary>iOS versions used in the Safari user agent. "auto" follows the device table.</summary>
        public static readonly string[] IosVersions = new[] { "auto", "26.2", "26.1", "18.3", "17.6", "16.7" };

        /// <summary>Font lists a profile can advertise. Auto derives it from the device target.</summary>
        public static readonly string[][] FontSets = new[]
        {
            new[] { "auto", "Automatic" },
            new[] { "windows", "Windows" },
            new[] { "macos", "macOS" },
            new[] { "linux", "Linux" },
            new[] { "minimal", "Minimal" }
        };

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
            {"Windows 11", new double[][]{
                new double[]{1920d,1080,1}, new double[]{2560,1440,1}, new double[]{3840,2160,1.5},
                new double[]{1536,864,1.25}, new double[]{1366,768,1}, new double[]{1920,1200,1},
                new double[]{1280,720,1}, new double[]{1600,900,1}, new double[]{1440,900,1},
                new double[]{2560,1600,1}, new double[]{3440,1440,1}, new double[]{1280,1024,1},
                new double[]{2880,1800,1.5}, new double[]{1920,1080,1.25}, new double[]{1680,1050,1} }},
            {"Windows 10", new double[][]{
                new double[]{1366d,768,1}, new double[]{1920,1080,1}, new double[]{1600,900,1},
                new double[]{2560,1440,1}, new double[]{1440,900,1}, new double[]{1280,720,1},
                new double[]{1600,1200,1}, new double[]{1920,1200,1}, new double[]{1280,1024,1},
                new double[]{1024,768,1}, new double[]{2560,1080,1}, new double[]{3840,2160,1.5} }},
            {"macOS", new double[][]{
                new double[]{1512d,982,2}, new double[]{1728,1117,2}, new double[]{1440,900,2},
                new double[]{2560,1440,2}, new double[]{2304,1296,2}, new double[]{1680,1050,2},
                new double[]{1920,1080,2}, new double[]{2560,1600,2}, new double[]{3024,1964,2},
                new double[]{1440,960,2} }},
            {"Linux", new double[][]{
                new double[]{1920d,1080,1}, new double[]{1366,768,1}, new double[]{2560,1440,1},
                new double[]{1600,900,1}, new double[]{3840,2160,1}, new double[]{1280,1024,1},
                new double[]{1920,1200,1}, new double[]{1680,1050,1} }},
            {"Android", new double[][]{ new double[]{411d,915,2.625}, new double[]{427,952,3}, new double[]{411,891,3.5}, new double[]{393,851,2.75}, new double[]{360,780,3}, new double[]{411,869,3.5} }},
            {"iPhone", new double[][]{ new double[]{393d,852,3}, new double[]{390,844,3}, new double[]{430,932,3}, new double[]{420,912,3}, new double[]{375,812,3} }},
            {"iPad", new double[][]{ new double[]{820d,1180,2}, new double[]{1024,1366,2}, new double[]{1194,834,2}, new double[]{744,1133,2} }}
        };

        /// <summary>
        /// Phone model, Android build and the GPU that model really ships, keyed by the same "WxH"
        /// the Screens table uses. A 2025 flagship viewport must not carry a 2017 model token, and
        /// the WebGL renderer has to be the silicon that handset actually has: an S10 answering a
        /// Mali-G68, or a Pixel 9 answering an Adreno, is a device that was never built.
        /// </summary>
        private static readonly Dictionary<string, string[]> AndroidDevices = new Dictionary<string, string[]>
        {
            {"411x915", new[]{"Pixel 9", "16", "Google Inc. (ARM)", "ANGLE (ARM, Mali-G715, OpenGL ES 3.2)"}},
            {"427x952", new[]{"Pixel 9 Pro XL", "16", "Google Inc. (ARM)", "ANGLE (ARM, Mali-G715, OpenGL ES 3.2)"}},
            {"411x891", new[]{"SM-S931B", "16", "Google Inc. (Qualcomm)", "ANGLE (Qualcomm, Adreno (TM) 830, OpenGL ES 3.2)"}},
            {"393x851", new[]{"SM-S908B", "15", "Google Inc. (Qualcomm)", "ANGLE (Qualcomm, Adreno (TM) 730, OpenGL ES 3.2)"}},
            {"360x780",  new[]{"SM-A155F", "15", "Google Inc. (ARM)", "ANGLE (ARM, Mali-G57, OpenGL ES 3.2)"}},
            {"411x869",  new[]{"SM-G973F", "12", "Google Inc. (ARM)", "ANGLE (ARM, Mali-G76, OpenGL ES 3.2)"}}
        };

        /// <summary>iPhone / iPad model plus the iOS version token for the Safari user agent.</summary>
        private static readonly Dictionary<string, string[]> IosDevices = new Dictionary<string, string[]>
        {
            {"393x852",   new[]{"iPhone 16", "26_1", "26.1"}},
            {"390x844",   new[]{"iPhone 15", "26_1", "26.1"}},
            {"430x932",   new[]{"iPhone 16 Pro Max", "26_1", "26.1"}},
            {"420x912",   new[]{"iPhone 17 Pro", "26_2", "26.2"}},
            {"375x812",   new[]{"iPhone 13 mini", "26_1", "26.1"}},
            {"820x1180",  new[]{"iPad Air 11", "26_1", "26.1"}},
            {"1024x1366", new[]{"iPad Pro 13", "26_1", "26.1"}},
            {"1194x834",  new[]{"iPad Pro 11", "26_1", "26.1"}},
            {"744x1133",  new[]{"iPad mini 7", "26_1", "26.1"}}
        };

        /// <summary>Tablet model token for an Android profile on a large viewport.</summary>
        private static readonly Dictionary<string, string[]> AndroidTablets = new Dictionary<string, string[]>
        {
            {"600x960", new[]{"SM-X510", "15", "Google Inc. (ARM)", "ANGLE (ARM, Mali-G68, OpenGL ES 3.2)"}},
            {"800x1280", new[]{"SM-X810", "16", "Google Inc. (Qualcomm)", "ANGLE (Qualcomm, Adreno (TM) 830, OpenGL ES 3.2)"}},
            {"900x1440", new[]{"SM-X920", "16", "Google Inc. (Qualcomm)", "ANGLE (Qualcomm, Adreno (TM) 830, OpenGL ES 3.2)"}}
        };

        /// <summary>
        /// ANGLE strings ordered newest-first, so the index doubles as the device tier: entry 0 is
        /// what a current high-end machine reports and the tail is what older hardware still shows.
        /// Regenerate picks from the top of the list and derives cores and memory from that tier.
        /// </summary>
        private static readonly Dictionary<string, string[][]> Gpus = new Dictionary<string, string[][]>
        {
            {"Windows", new[]{
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 5090 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 5080 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 5070 Ti Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 5070 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4090 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4080 SUPER Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4070 SUPER Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4070 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4060 Ti Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4060 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 3080 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 3070 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 5060 Laptop GPU Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce GTX 1660 SUPER Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 9070 XT Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 9070 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 7900 XTX Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 7800 XT Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 7700 XT Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 7600 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 6800 XT Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 580 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon 890M (Ryzen 9000 Series) Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon 780M (Ryzen 7000 Series) Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon Graphics (Raphael) Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) Arc(TM) B580 Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) Arc(TM) A770 Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) Arc(TM) A380 Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) Iris(R) Xe Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) UHD Graphics 770 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Intel(R) HD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)"}
            }},
            {"macOS", new[]{
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M4 Max, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M4 Pro, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M4, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M3 Pro, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M3, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M2, Unspecified Version)"},
                new[]{"Google Inc. (Apple)","ANGLE (Apple, ANGLE Metal Renderer: Apple M1, Unspecified Version)"},
                // Intel Macs use the same Metal renderer shape, just with the Intel chip name.
                new[]{"Google Inc. (Intel)","ANGLE (Intel, ANGLE Metal Renderer: Intel(R) Iris(TM) Plus Graphics 655, Unspecified Version)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, ANGLE Metal Renderer: AMD Radeon Pro 5500M, Unspecified Version)"}
            }},
            {"Linux", new[]{
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 4070 SUPER (0x00002705), OpenGL 4.6)"},
                new[]{"Google Inc. (NVIDIA)","ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 (0x00002503), OpenGL 4.6)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 7900 XT (gfx1101, LLVM 19.1.1, DRM 3.59, 6.11.0-19-generic), OpenGL 4.6)"},
                new[]{"Google Inc. (AMD)","ANGLE (AMD, AMD Radeon RX 6600 (NAVI23, LLVM 19.1.1, DRM 3.59, 6.11.0-19-generic), OpenGL 4.6)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Mesa Intel(R) Arc(TM) A770 Graphics (ADLM GT2), OpenGL 4.6)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Mesa Intel(R) Xe Graphics (TGL GT2), OpenGL 4.6)"},
                new[]{"Google Inc. (Intel)","ANGLE (Intel, Mesa Intel(R) UHD Graphics 620 (KBL GT2), OpenGL 4.6)"}
            }},
            {"Android", new[]{
                new[]{"Google Inc. (Qualcomm)","ANGLE (Qualcomm, Adreno (TM) 830, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM)","ANGLE (ARM, Immortalis-G720, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM)","ANGLE (ARM, Mali-G720, OpenGL ES 3.2)"},
                new[]{"Google Inc. (Qualcomm)","ANGLE (Qualcomm, Adreno (TM) 750, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM)","ANGLE (ARM, Mali-G715, OpenGL ES 3.2)"},
                new[]{"Google Inc. (Qualcomm)","ANGLE (Qualcomm, Adreno (TM) 730, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM)","ANGLE (ARM, Mali-G57, OpenGL ES 3.2)"},
                new[]{"Google Inc. (ARM)","ANGLE (ARM, Mali-G76, OpenGL ES 3.2)"}
            }},
            // Safari and Chrome on iOS both come out of the same Metal driver and report no ANGLE
            // layer at all: the vendor is Apple and the renderer is the bare "Apple GPU" string.
            {"iOS", new[]{
                /* WebKit on iOS exposes no ANGLE layer at all: gl.VENDOR is "Apple Inc." and the
                   renderer is the generic "Apple GPU" on every iPhone and iPad, so one entry is
                   the only honest value and there is nothing to tier. */
                new[]{"Apple Inc.","Apple GPU"}
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
        public static bool IsWindowsTarget(string os) { return string.IsNullOrEmpty(os) || os.StartsWith("Windows", StringComparison.OrdinalIgnoreCase); }

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
            Regenerate(fp, os, proxy, false);
        }

        /// <summary>
        /// Rolls a new fingerprint.
        ///
        /// <paramref name="widePool"/> is the escalation used when a workspace is dense enough
        /// that ordinary rolls keep landing on a GPU another profile already claims: the tier
        /// bias is dropped so the older half of the pool is as likely as the new half. The
        /// hardware stays plausible - it is the same list of real cards - but the overlap falls
        /// sharply, which is what makes "unique in this workspace" achievable at 50 profiles
        /// instead of only at 5.
        /// </summary>
        public static void Regenerate(FingerprintConfig fp, string os, ProxyConfig proxy, bool widePool)
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
            int tier = widePool ? r.Next(gpus.Length) : PickTier(gpus.Length, r);
            /* An Android handset carries its own silicon, so the model chosen for the viewport
               wins over the roll; the pool index of that GPU then sets the device class. */
            string[] device = DeviceIdentity(fp);
            var pair = device.Length >= 4 && !string.IsNullOrEmpty(device[3])
                ? new[] { device[2], device[3] }
                : gpus[tier];
            if (pair != gpus[tier])
            {
                for (int i = 0; i < gpus.Length; i++)
                    if (gpus[i][1] == pair[1]) { tier = i; break; }
            }
            fp.WebGlVendor = pair[0];
            fp.WebGlRenderer = pair[1];

            // Cores and memory have to describe the same machine class as the GPU. A 24-core
            // desktop behind an integrated UHD 620, or 4 GB on an M4 Max, is a contradiction
            // that a single hardware-concurrency check exposes.
            bool high = tier < (int)Math.Ceiling(gpus.Length / 3.0);
            bool low = tier >= (int)Math.Ceiling(gpus.Length * 2.0 / 3.0);
            int[] cores, mem;
            /* navigator.deviceMemory only ever answers a power of two, capped at 8 by every
               Chromium, so 6 or 12 GB is a value no real browser can produce. */
            if (IsIos(os))
            {
                /* Every A-series chip is 6-wide, an iPad Air is 8 and an iPad Pro M4 is 10; the
                   tier roll would invent 4-core iPhones and 16-core iPads Apple never shipped. */
                string model = device.Length > 0 ? device[0] : "";
                if (model.StartsWith("iPad Pro", StringComparison.OrdinalIgnoreCase)) cores = new[] { 10, 10, 8 };
                else if (model.StartsWith("iPad Air", StringComparison.OrdinalIgnoreCase)) cores = new[] { 8, 8 };
                else cores = new[] { 6, 6 };
                mem = new[] { 8 };
            }
            else if (IsAndroid(os))
            {
                // Every Android SoC a Chrome build ships on is octa-core, at every price point.
                cores = new[] { 8, 8 };
                mem = low ? new[] { 4 } : new[] { 8 };
            }
            else if (IsMobileOs(os))
            {
                cores = high ? new[] { 8, 8 } : (low ? new[] { 4, 6 } : new[] { 6, 8 });
                mem = low ? new[] { 4 } : new[] { 8 };
            }
            else
            {
                cores = high ? new[] { 16, 16, 24 } : (low ? new[] { 4, 6, 8 } : new[] { 8, 12, 16 });
                // navigator.deviceMemory is capped at 8 by every Chromium, whatever the machine has.
                mem = low ? new[] { 4 } : new[] { 8 };
            }
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
            /* Regenerating rolls the hardware, not the user's explicit picks: an Android
               version or font list chosen by hand is a decision, not a random value. */
            if (string.IsNullOrEmpty(fp.AndroidVersion)) fp.AndroidVersion = "auto";
            if (string.IsNullOrEmpty(fp.IosVersion)) fp.IosVersion = "auto";
            if (string.IsNullOrEmpty(fp.FontSet)) fp.FontSet = "auto";

            ApplyLocale(fp, proxy, r);

            fp.UserAgent = "";
            fp.Platform = "";
            fp.Brand = "";
        }

        /// <summary>
        /// Newest hardware is the most common answer, so the index is biased toward the head of the
        /// pool: two uniforms multiplied collapse the distribution toward zero without ever
        /// excluding the older entries a real fleet still contains.
        /// </summary>
        private static int PickTier(int count, Random r)
        {
            if (count <= 1) return 0;
            int bias = (int)(r.NextDouble() * r.NextDouble() * count);
            return bias < count ? bias : count - 1;
        }

        /// <summary>
        /// {model, osVersion} for the viewport the profile advertises. The screen size is what a
        /// real device reports, so it is also what ties the model token in the user agent and the
        /// model in the client hints to the same phone or tablet.
        /// </summary>
        /// <summary>Table row for the viewport the profile advertises.</summary>
        public static string[] DeviceIdentity(FingerprintConfig fp)
        {
            string[] none = { "", "" };
            if (fp == null || string.IsNullOrEmpty(fp.Os)) return none;
            string key = fp.ScreenW + "x" + fp.ScreenH;
            Dictionary<string, string[]> table;
            if (IsIos(fp.Os)) table = IosDevices;
            else if ((fp.DeviceType ?? "").Equals("tablet", StringComparison.OrdinalIgnoreCase)) table = AndroidTablets;
            else if (IsAndroid(fp.Os)) table = AndroidDevices;
            else return none;
            string[] found;
            return table.TryGetValue(key, out found) ? found : none;
        }

        /// <summary>
        /// Android release the profile reports. A pinned value has to reach every surface that
        /// mentions it - the user agent, the client hint and the device model line - or the
        /// browser contradicts itself inside a single request.
        /// </summary>
        public static string AndroidVersionFor(FingerprintConfig fp)
        {
            if (fp != null && !string.IsNullOrEmpty(fp.AndroidVersion) &&
                !fp.AndroidVersion.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return fp.AndroidVersion.Trim();
            string[] d = DeviceIdentity(fp);
            return d.Length > 1 && d[1].Length > 0 ? d[1] : "16";
        }

        /// <summary>iOS release in dotted form, e.g. "17.6".</summary>
        public static string IosVersionFor(FingerprintConfig fp)
        {
            if (fp != null && !string.IsNullOrEmpty(fp.IosVersion) &&
                !fp.IosVersion.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return fp.IosVersion.Trim();
            string[] d = DeviceIdentity(fp);
            return d.Length > 2 && d[2].Length > 0 ? d[2] : "26.1";
        }

        /// <summary>The same version in the underscore form Safari puts in its user agent.</summary>
        public static string IosVersionToken(FingerprintConfig fp)
        {
            return IosVersionFor(fp).Replace('.', '_');
        }

        public static string OsVersionLabel(FingerprintConfig fp)
        {
            if (fp == null) return "";
            if (IsAndroid(fp.Os)) return "Android " + AndroidVersionFor(fp);
            if (IsIos(fp.Os)) return "iOS " + IosVersionFor(fp);
            return "";
        }

        /// <summary>
        /// The public IP a site actually sees wins. That is the proxy IP when one is set, and the
        /// machine's own detected IP on a direct connection. The clock, the language and the
        /// Accept-Language header must all describe the same country or a single request can
        /// expose the mismatch.
        /// </summary>
        public static void ApplyLocale(FingerprintConfig fp, ProxyConfig proxy, Random r)
        {
            if (proxy != null && fp.AutoMatchProxy)
            {
                /* The IP's own timezone is the strongest signal: a geo lookup that says
                   "Los Angeles" but a clock on America/New_York is a country-level guess that
                   every IP-intelligence page flags. Use the exact zone from the lookup first,
                   and only fall back to the country default when the lookup had no zone. */
                if (proxy.Enabled && !string.IsNullOrEmpty(proxy.LastTimezone))
                {
                    fp.Timezone = proxy.LastTimezone;
                    fp.Language = LanguageForTimezone(proxy.LastTimezone);
                    fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
                    return;
                }
                string[] loc;
                if (!string.IsNullOrEmpty(proxy.LastCountryCode) &&
                    (proxy.Enabled || !string.IsNullOrEmpty(proxy.LastIp)) &&
                    CountryLocale.TryGetValue(proxy.LastCountryCode, out loc))
                {
                    fp.Timezone = loc[0];
                    fp.Language = loc[1];
                    fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
                    return;
                }
            }

            /* No usable IP lookup: mirror the machine, but never advertise a culture from a
               different country than its own clock (en-US over Europe/London, ru-RU over
               Europe/Berlin). Those pairings are the easiest kind of incoherence to detect. */
            fp.Timezone = SystemTimezone();
            string natural = LanguageForTimezone(fp.Timezone);
            string local = SystemLanguage();
            if (Array.IndexOf(Languages, local) < 0 ||
                !string.Equals(CountryForLanguage(local), CountryForLanguage(natural), StringComparison.OrdinalIgnoreCase))
                local = natural;
            fp.Language = local;
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

        public static bool IsWindows11(string os)
        {
            return os != null && os.StartsWith("Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Repairs a configuration that describes a machine that cannot exist.
        ///
        /// Profiles arrive from places that can each produce an impossible combination: an
        /// imported JSON file written by an older build, a profile edited by hand, and the OS
        /// picker, which changes the target without re-deriving everything that follows from it.
        /// A macOS profile answering with a Direct3D renderer, a browser reporting 16 GB of
        /// deviceMemory (Chromium caps it at 8), or an Android profile with no touch points are
        /// each one probe away from being flagged.
        ///
        /// Every fix here is <em>coherence</em>, never identity: the seed, the canvas payload and
        /// the renderer choice itself are left alone, so this cannot undo the uniqueness work or
        /// silently change a fingerprint a user has been relying on. Returns how many fields it
        /// had to repair, which is what the caller reports.
        /// </summary>
        public static int Coerce(FingerprintConfig fp)
        {
            if (fp == null) return 0;
            int repaired = 0;

            string os = fp.Os ?? "";
            bool mobile = IsMobileOs(os);

            // ---- WebGL has to belong to the platform -------------------------------------
            var gpuPool = GpusFor(os);
            if (gpuPool.Count > 0)
            {
                /* One pass answers both questions: is this renderer one of ours, and what vendor
                   does it actually ship with. Every browser reports the two together, so a
                   renderer with the wrong vendor beside it is as much of a tell as a renderer
                   from another platform. */
                int at = -1;
                for (int i = 0; i < gpuPool.Count; i++)
                {
                    if (gpuPool[i] == null || gpuPool[i].Length < 2) continue;
                    if (string.Equals(gpuPool[i][1], fp.WebGlRenderer, StringComparison.Ordinal)) { at = i; break; }
                }

                if (at >= 0)
                {
                    if (!string.Equals(gpuPool[at][0], fp.WebGlVendor, StringComparison.Ordinal))
                    {
                        fp.WebGlVendor = gpuPool[at][0];
                        repaired++;
                    }
                }
                else
                {
                    /* Keep the vendor family when this platform has it - an NVIDIA string on a
                       Windows profile stays NVIDIA - and otherwise take the pool's head. A real
                       card of the right platform beats a coherent-looking lie. */
                    int sameVendor = -1;
                    for (int i = 0; i < gpuPool.Count; i++)
                    {
                        if (gpuPool[i] == null || gpuPool[i].Length < 2) continue;
                        if (string.Equals(gpuPool[i][0], fp.WebGlVendor, StringComparison.Ordinal)) { sameVendor = i; break; }
                    }
                    int pick = sameVendor >= 0 ? sameVendor : 0;
                    fp.WebGlRenderer = gpuPool[pick][1];
                    fp.WebGlVendor = gpuPool[pick][0];
                    repaired++;
                }
            }
            else if (fp.WebGlVendor == null) { fp.WebGlVendor = ""; }

            // ---- deviceMemory is a power of two, capped at 8 -----------------------------
            if (fp.Memory != 2 && fp.Memory != 4 && fp.Memory != 8)
            {
                fp.Memory = fp.Memory >= 8 ? 8 : (fp.Memory >= 4 ? 4 : 2);
                repaired++;
            }

            // ---- core counts are real values, and phones are octa-core -------------------
            bool plausible = false;
            foreach (int c in CoreOptions) if (c == fp.Cores) { plausible = true; break; }
            if (!plausible)
            {
                // Snap to the nearest offered value rather than forcing a default: a profile that
                // said 12 cores should become 12, not 8.
                int best = CoreOptions[0], bestDelta = Math.Abs(fp.Cores - best);
                foreach (int c in CoreOptions)
                {
                    int d = Math.Abs(fp.Cores - c);
                    if (d < bestDelta) { best = c; bestDelta = d; }
                }
                fp.Cores = best;
                repaired++;
            }
            /* Every Android SoC a Chrome build ships on is octa-core. iPhones are 6-wide and
               iPads 8 or 10, so the iOS values are left alone: forcing them to 8 would describe
               an A-series chip that does not exist, which is the opposite of the intent here. */
            if (IsAndroid(os) && fp.Cores != 8) { fp.Cores = 8; repaired++; }

            // ---- touch points follow the device, not the last picker entry ---------------
            int touch = mobile ? 5 : 0;
            if (fp.TouchPoints != touch) { fp.TouchPoints = touch; repaired++; }

            // ---- the screen's derived fields have to match the screen -------------------
            if (fp.ScreenW > 0 && fp.ScreenH > 0 && !IsSystemTarget(os))
            {
                /* Deliberately *not* snapping an unusual resolution to the table. The picker
                   preserves a custom value as its own row on purpose, so replacing it here would
                   silently overwrite an explicit choice. What is repaired is everything derived
                   from the screen - density, available height, touch points - and an out-of-table
                   resolution is reported by the audit instead, where the user can decide. */
                if (fp.PixelRatio <= 0 || fp.AvailW <= 0 || fp.AvailH <= 0 || fp.AvailW > fp.ScreenW || fp.AvailH > fp.ScreenH)
                {
                    int wantW = fp.ScreenW, wantH = fp.ScreenH;
                    double ratio = fp.PixelRatio;
                    ApplyScreen(fp, os, new[] { (double)wantW, wantH, ratio > 0 ? ratio : 0 });
                    repaired++;
                }
            }

            // ---- locale has to be internally consistent --------------------------------
            if (string.IsNullOrEmpty(fp.Timezone))
            {
                fp.Timezone = "America/New_York";
                repaired++;
            }
            if (string.IsNullOrEmpty(fp.Language))
            {
                fp.Language = LanguageForTimezone(fp.Timezone);
                repaired++;
            }
            if (string.IsNullOrEmpty(fp.AcceptLanguage))
            {
                fp.AcceptLanguage = AcceptLanguageFor(fp.Language);
                repaired++;
            }

            // ---- WebRTC and geolocation modes have to be values the script understands --
            if (!IsOneOf(fp.WebRtcMode, "off", "proxy", "public"))
            {
                fp.WebRtcMode = fp.WebRtcProtect ? "proxy" : "off";
                repaired++;
            }
            if (!IsOneOf(fp.GeoMode, "proxy", "system", "custom", "block"))
            {
                fp.GeoMode = "proxy";
                repaired++;
            }

            // ---- device type follows the OS --------------------------------------------
            string device = DeviceTypeFor(os);
            if (!string.Equals(fp.DeviceType, device, StringComparison.OrdinalIgnoreCase))
            {
                fp.DeviceType = device;
                repaired++;
            }

            return repaired;
        }

        private static bool IsOneOf(string value, string a, string b, string c = null, string d = null)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (string.Equals(value, a, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (c != null && string.Equals(value, c, StringComparison.OrdinalIgnoreCase)) return true;
            if (d != null && string.Equals(value, d, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>True when the target means "this machine", whose screen is whatever it is.</summary>
        private static bool IsSystemTarget(string os)
        {
            return string.IsNullOrEmpty(os) || os.Equals("System default", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(os, SystemOsName(), StringComparison.OrdinalIgnoreCase);
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
                : (IsMac(os) ? 2 : (IsIos(os)
                    ? (os.StartsWith("iPad", StringComparison.OrdinalIgnoreCase) ? 2 : 3)
                    : (IsAndroid(os) ? 2.625 : 1)));
            fp.TouchPoints = IsMobileOs(os) ? 5 : 0;
            fp.DeviceType = DeviceTypeFor(os);
        }

        /// <summary>
        /// Height the emulated page really shows. On a phone the address and tool bars are part of
        /// the screen, so window.innerHeight is always smaller than screen.height; a profile that
        /// reports the two as equal describes a device that does not exist.
        /// </summary>
        public static int MobileViewHeight(FingerprintConfig fp)
        {
            if (fp == null || fp.ScreenH <= 0 || !IsMobileOs(fp.Os)) return 0;
            bool pad = fp.DeviceType == "tablet";
            int bars = IsIos(fp.Os) ? (pad ? 92 : 124) : (pad ? 96 : 120);
            int h = fp.ScreenH - bars;
            int floor = (int)Math.Round(fp.ScreenH * 0.7);
            return h < floor ? floor : h;
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

        /// <summary>Readable locale name for the pickers and profile tiles, e.g. "German (Germany)".</summary>
        public static string LanguageName(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            try
            {
                var ci = CultureInfo.GetCultureInfo(code.Trim());
                string name = ci.EnglishName;
                if (!string.IsNullOrEmpty(name) && !name.Equals(code, StringComparison.OrdinalIgnoreCase)) return name;
            }
            catch { }
            return code;
        }

        /// <summary>Short label for tight spaces, e.g. "German".</summary>
        public static string ShortLanguageName(string code)
        {
            string full = LanguageName(code);
            int p = full.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? full.Substring(0, p) : full;
        }

        /// <summary>Readable city/zone name, e.g. "Europe/Berlin" -> "Berlin".</summary>
        public static string TimezoneName(string tz)
        {
            if (string.IsNullOrWhiteSpace(tz)) return "";
            int i = tz.LastIndexOf('/');
            return (i >= 0 ? tz.Substring(i + 1) : tz).Replace('_', ' ');
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
            /* An auto user agent is rebuilt from the browser that is actually launching. Keeping
               the cached string meant a profile created before a Chrome update kept advertising
               the old version - the exact "different browser version" mismatch scanners print. */

            int major = browser != null && browser.MajorVersion > 0 ? browser.MajorVersion : 131;
            bool edge = browser != null && browser.IsEdge;
            bool firefox = browser != null && browser.IsFirefox;
            string device = string.IsNullOrEmpty(fp.DeviceType) ? "desktop" : fp.DeviceType;

            if (IsIos(fp.Os))
            {
                // iOS browsers are all WebKit: match the Safari string, not a Chrome one. The model
                // and the OS build come from the advertised viewport, so the UA, the screen and the
                // pixel density all describe the same device.
                bool pad = fp.Os.StartsWith("iPad", StringComparison.OrdinalIgnoreCase);
                string token = IosVersionToken(fp);
                string ver = IosVersionFor(fp);
                string head = pad ? "iPad; CPU OS " + token : "iPhone; CPU iPhone OS " + token;
                fp.UserAgent = "Mozilla/5.0 (" + head + " like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/" +
                    ver + " Mobile/15E148 Safari/604.1";
                fp.Platform = pad ? "iPad" : "iPhone";
                fp.Brand = "Safari";
                return;
            }

            if (firefox)
            {
                string platToken, navPlatformFf;
                if (IsMac(fp.Os)) { platToken = "Macintosh; Intel Mac OS X 10.15"; navPlatformFf = "MacIntel"; }
                else if (IsLinux(fp.Os)) { platToken = "X11; Linux x86_64"; navPlatformFf = "Linux x86_64"; }
                else if (IsAndroid(fp.Os))
                {
                    string[] dev = DeviceIdentity(fp);
                    platToken = "Android " + AndroidVersionFor(fp) + "; " +
                        (device == "tablet" ? "Tablet" : "Mobile");
                    // Firefox on Android reports its own platform token, never a desktop one.
                    navPlatformFf = "Android";
                }
                else { platToken = "Windows NT 10.0; Win64; x64"; navPlatformFf = "Win32"; }
                int fmajor = browser != null && browser.MajorVersion > 0 ? browser.MajorVersion : 128;
                fp.UserAgent = "Mozilla/5.0 (" + platToken + "; rv:" + fmajor + ".0) Gecko/20100101 Firefox/" + fmajor + ".0";
                fp.Platform = navPlatformFf;
                fp.Brand = "Firefox";
                return;
            }

            string platToken2, navPlatform;
            if (IsMac(fp.Os)) { platToken2 = "Macintosh; Intel Mac OS X 10_15_7"; navPlatform = "MacIntel"; }
            else if (IsLinux(fp.Os)) { platToken2 = "X11; Linux x86_64"; navPlatform = "Linux x86_64"; }
            else if (IsAndroid(fp.Os))
            {
                /* Android Chrome puts the real model and OS build in the UA, so both come from the
                   viewport the profile advertises. A viewport no handset uses gets the unified
                   "Android 10; K" string Chrome actually still ships, rather than a flagship model
                   attached to a screen that model never had. */
                string[] dev = DeviceIdentity(fp);
                if (dev.Length > 1 && dev[0].Length > 0)
                    platToken2 = "Linux; Android " + AndroidVersionFor(fp) + "; " + dev[0];
                else platToken2 = "Linux; Android " + AndroidVersionFor(fp) + "; K";
                navPlatform = "Linux armv8l";
            }
            else { platToken2 = "Windows NT 10.0; Win64; x64"; navPlatform = "Win32"; }

            /* Chrome has shipped the reduced user agent since M113: the UA string carries the
               major version and zeros for the rest ("Chrome/141.0.0.0"), while the real build
               number only appears in the client hints. Emitting a four-part build here is a
               self-inflicted "different browser version" flag, because no current Chrome or
               Edge ever puts its build number in navigator.userAgent. */
            string uaVer = major + ".0.0.0";
            string ua = "Mozilla/5.0 (" + platToken2 + ") AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" + uaVer + " Safari/537.36";
            if (IsMobileOs(fp.Os)) ua = ua.Replace("Safari/537.36", device == "tablet" ? "Safari/537.36" : "Mobile Safari/537.36");
            if (edge) ua += " Edg/" + uaVer;

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
            if (major <= 0) major = 131;
            string v = browser != null ? browser.Version : null;
            if (!string.IsNullOrEmpty(v))
            {
                string[] parts = v.Trim().Split('.');
                int head;
                if (parts.Length == 4 && int.TryParse(parts[0], out head) && head > 0)
                {
                    int buildNo, patchNo;
                    int.TryParse(parts[2], out buildNo);
                    int.TryParse(parts[3], out patchNo);
                    // Continuous Chromium builds can report "159.0.0.0"; real Chromium never
                    // ships an all-zero build and patch, so synthesise the missing parts.
                    if (buildNo > 0 && patchNo > 0) return v.Trim();
                    major = head;
                }
                else if (parts.Length == 3 && int.TryParse(parts[0], out head) && head > 0)
                    return v.Trim() + "." + (40 + StableHash(v) % 160);
            }
            // A real Chromium build is always four numeric parts with non-zero build and patch.
            // "131.0.0.0" never appears in the wild and is an instant mismatch against client hints.
            int build = 6000 + StableHash("b" + major) % 1300;
            int patch = 40 + StableHash("p" + major) % 190;
            return major + ".0." + build + "." + patch;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int h = unchecked((int)2166136261u);
                for (int i = 0; i < value.Length; i++) h = (h ^ value[i]) * 16777619;
                return h & 0x7fffffff;
            }
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
