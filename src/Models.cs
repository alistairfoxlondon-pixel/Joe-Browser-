using System;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    public class ProxyConfig
    {
        public string Type { get; set; }        // none | http | https | socks5
        public string Host { get; set; }
        public int Port { get; set; }
        public string User { get; set; }

        [ScriptIgnore]
        public string Password { get; set; }

        public string PasswordEnc { get; set; }

        // cached last successful check (used for automatic timezone matching)
        public string LastIp { get; set; }
        public string LastCountry { get; set; }
        public string LastCountryCode { get; set; }
        public string LastTimezone { get; set; }
        public string LastCity { get; set; }

        public ProxyConfig()
        {
            Type = "none";
            Host = "";
            User = "";
            Password = "";
            PasswordEnc = "";
            LastIp = "";
            LastCountry = "";
            LastCountryCode = "";
            LastTimezone = "";
            LastCity = "";
        }

        public bool Enabled
        {
            get { return !IsNone(Type) && !string.IsNullOrWhiteSpace(Host) && Port > 0; }
        }

        public static bool IsNone(string type)
        {
            return string.IsNullOrEmpty(type) || type.Equals("none", StringComparison.OrdinalIgnoreCase);
        }

        public string Label
        {
            get
            {
                if (!Enabled) return "Direct";
                if (!string.IsNullOrEmpty(LastCountryCode)) return "Proxy " + LastCountryCode;
                return "Proxy";
            }
        }

        public string Detail
        {
            get
            {
                if (!Enabled) return "No proxy";
                return Type.ToUpperInvariant() + " " + Host + ":" + Port;
            }
        }

        public ProxyConfig Copy()
        {
            return new ProxyConfig
            {
                Type = Type,
                Host = Host,
                Port = Port,
                User = User,
                Password = Password,
                PasswordEnc = PasswordEnc,
                LastIp = LastIp,
                LastCountry = LastCountry,
                LastCountryCode = LastCountryCode,
                LastTimezone = LastTimezone,
                LastCity = LastCity
            };
        }
    }

    public class FingerprintConfig
    {
        public int Seed { get; set; }
        public string Os { get; set; }
        public string DeviceType { get; set; }    // desktop | mobile | tablet
        public string Timezone { get; set; }
        public string Language { get; set; }
        public string AcceptLanguage { get; set; }
        public int ScreenW { get; set; }
        public int ScreenH { get; set; }
        public int AvailW { get; set; }
        public int AvailH { get; set; }
        public int ColorDepth { get; set; }
        public double PixelRatio { get; set; }
        public string WebGlVendor { get; set; }
        public string WebGlRenderer { get; set; }
        public int Cores { get; set; }
        public int Memory { get; set; }
        public int TouchPoints { get; set; }
        public bool CanvasNoise { get; set; }
        public bool AudioNoise { get; set; }
        public bool FontsNoise { get; set; }
        public bool DoNotTrack { get; set; }
        public bool SpoofBattery { get; set; }
        public bool SpoofMac { get; set; }
        public bool WebRtcProtect { get; set; }
        public string WebRtcMode { get; set; }    // disabled | proxy | public
        public string GeoMode { get; set; }       // proxy | system | custom | block
        public string GeoLat { get; set; }
        public string GeoLng { get; set; }
        public bool AutoMatchProxy { get; set; }
        public bool AutoUserAgent { get; set; }

        // resolved at launch time
        [ScriptIgnore]
        public string UserAgent { get; set; }
        [ScriptIgnore]
        public string Platform { get; set; }
        [ScriptIgnore]
        public string Brand { get; set; }

        public string UserAgentOverride { get; set; }

        public FingerprintConfig()
        {
            Os = "Windows 11";
            DeviceType = "desktop";
            Timezone = "America/New_York";
            Language = "en-US";
            AcceptLanguage = "en-US,en;q=0.9";
            ScreenW = 1920;
            ScreenH = 1080;
            AvailW = 1920;
            AvailH = 1032;
            ColorDepth = 24;
            PixelRatio = 1;
            Cores = 8;
            Memory = 8;
            TouchPoints = 0;
            CanvasNoise = true;
            AudioNoise = true;
            FontsNoise = true;
            DoNotTrack = false;
            SpoofBattery = true;
            SpoofMac = true;
            WebRtcProtect = true;
            WebRtcMode = "proxy";
            GeoMode = "proxy";
            GeoLat = "";
            GeoLng = "";
            AutoMatchProxy = true;
            AutoUserAgent = true;
            WebGlVendor = "Google Inc. (NVIDIA)";
            WebGlRenderer = "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)";
            Platform = "Win32";
            Brand = "Chrome";
            UserAgent = "";
            UserAgentOverride = "";
        }

        public string TimezoneShort
        {
            get
            {
                if (string.IsNullOrEmpty(Timezone)) return "";
                int i = Timezone.LastIndexOf('/');
                return i >= 0 ? Timezone.Substring(i + 1).Replace('_', ' ') : Timezone;
            }
        }

        public FingerprintConfig Copy()
        {
            var c = (FingerprintConfig)MemberwiseClone();
            return c;
        }
    }

    public class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Notes { get; set; }
        public string StartUrl { get; set; }
        public string BrowserKind { get; set; }   // auto | chrome | edge | brave | firefox | custom
        public string BrowserPath { get; set; }
        public int OpenCount { get; set; }
        public DateTime Created { get; set; }
        public DateTime LastOpened { get; set; }
        public FingerprintConfig Fp { get; set; }
        public ProxyConfig Proxy { get; set; }

        public Profile()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Notes = "";
            StartUrl = "";
            BrowserKind = "auto";
            BrowserPath = "";
            Created = DateTime.Now;
            LastOpened = DateTime.MinValue;
            Fp = new FingerprintConfig();
            Proxy = new ProxyConfig();
        }

        public string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return "JB";
                string n = Name.Trim();
                string[] parts = n.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpperInvariant();
                if (n.Length >= 2) return n.Substring(0, 2).ToUpperInvariant();
                return n.ToUpperInvariant();
            }
        }

        public string LastOpenedText
        {
            get
            {
                if (LastOpened == DateTime.MinValue) return "Never opened";
                var d = DateTime.Now - LastOpened;
                if (d.TotalSeconds < 90) return "Opened just now";
                if (d.TotalMinutes < 60) return "Opened " + (int)d.TotalMinutes + "m ago";
                if (d.TotalHours < 24) return "Opened " + (int)d.TotalHours + "h ago";
                if (d.TotalDays < 30) return "Opened " + (int)d.TotalDays + "d ago";
                return "Opened " + LastOpened.ToString("MMM d");
            }
        }

        public Profile Copy(bool freshId)
        {
            var p = new Profile
            {
                Id = freshId ? Guid.NewGuid().ToString("N") : Id,
                Name = Name,
                Notes = Notes,
                StartUrl = StartUrl,
                BrowserKind = BrowserKind,
                BrowserPath = BrowserPath,
                OpenCount = freshId ? 0 : OpenCount,
                Created = freshId ? DateTime.Now : Created,
                LastOpened = freshId ? DateTime.MinValue : LastOpened,
                Fp = Fp.Copy(),
                Proxy = Proxy.Copy()
            };
            return p;
        }
    }

    public class AppSettings
    {
        public string BrowserPath { get; set; }
        public string DefaultStartUrl { get; set; }
        /// <summary>0 = never chosen (dark), 1 = dark, 2 = light.</summary>
        public int ThemeMode { get; set; }
        /// <summary>0 recent, 1 name, 2 created, 3 running first, 4 operating system.</summary>
        public int SortBy { get; set; }

        public AppSettings()
        {
            BrowserPath = "";
            DefaultStartUrl = "https://www.google.com";
            ThemeMode = 0;
            SortBy = 0;
        }
    }
}
