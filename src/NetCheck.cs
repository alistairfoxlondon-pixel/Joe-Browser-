using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    internal sealed class IpReport
    {
        public bool Ok;
        public string Ip = "";
        public string Country = "";
        public string CountryCode = "";
        public string City = "";
        public string Region = "";
        public string Timezone = "";
        public string Isp = "";
        public double Lat, Lon;
        public int LatencyMs;
        public int ClockSkewSeconds;
        public string ResolverIp = "";
        public string ResolverGeo = "";
        public string Error = "";
        /// <summary>Which services answered, so a disagreement is visible instead of hidden.</summary>
        public readonly List<string> Sources = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public string Summary
        {
            get
            {
                if (!Ok) return string.IsNullOrEmpty(Error) ? "Not checked" : Error;
                string where = string.Join(", ", NonEmpty(Country, City));
                return Ip + (where.Length > 0 ? "  \u00b7  " + where : "") +
                       (string.IsNullOrEmpty(Isp) ? "" : "  \u00b7  " + Isp);
            }
        }

        private static string[] NonEmpty(params string[] v)
        {
            var list = new List<string>();
            foreach (string s in v) if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
            return list.ToArray();
        }
    }

    /// <summary>
    /// Reads the public connection through three independent lookup services and compares them.
    /// One service can be stale, rate-limited or lying about the country; a disagreement is
    /// exactly what a user needs to see before they trust a profile with an account.
    /// </summary>
    internal static class NetCheck
    {
        public static Task<IpReport> CheckAsync(ProxyConfig cfg)
        {
            return Task.Run(new Func<IpReport>(() => Check(cfg)));
        }

        private static IpReport Check(ProxyConfig cfg)
        {
            var report = new IpReport();
            var sw = Stopwatch.StartNew();
            bool viaProxy = cfg != null && cfg.Enabled;
            ProxyRelay relay = null;
            IWebProxy proxy = null;
            if (viaProxy)
            {
                relay = new ProxyRelay(cfg);
                proxy = new WebProxy("http://127.0.0.1:" + relay.Port);
            }
            try
            {
                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var good = new List<IpReport>();

                // ip-api first: one request returns the clock zone, the ISP and coordinates.
                var a = IpApi(proxy, viaProxy);
                if (a != null) { good.Add(a); report.Sources.Add("ip-api"); Count(seen, a.Ip); }
                var b = IpApiCo(proxy, viaProxy);
                if (b != null) { good.Add(b); report.Sources.Add("ipapi.co"); Count(seen, b.Ip); }
                var c = IpInfo(proxy, viaProxy);
                if (c != null) { good.Add(c); report.Sources.Add("ipinfo.io"); Count(seen, c.Ip); }

                if (good.Count == 0)
                {
                    report.Error = viaProxy ? "The proxy did not answer any lookup service" : "No internet connection";
                    report.LatencyMs = (int)sw.ElapsedMilliseconds;
                    return report;
                }

                /* Majority IP wins. When all three differ the first service is used and the
                   disagreement is reported - that is a real finding, not something to hide. */
                string winner = good[0].Ip;
                int best = 0;
                foreach (var kv in seen)
                    if (kv.Value > best) { best = kv.Value; winner = kv.Key; }

                foreach (var r in good)
                {
                    if (!string.Equals(r.Ip, winner, StringComparison.OrdinalIgnoreCase)) continue;
                    report.Ip = r.Ip;
                    if (report.Country.Length == 0) report.Country = r.Country;
                    if (report.CountryCode.Length == 0) report.CountryCode = r.CountryCode;
                    if (report.City.Length == 0) report.City = r.City;
                    if (report.Region.Length == 0) report.Region = r.Region;
                    if (report.Timezone.Length == 0) report.Timezone = r.Timezone;
                    if (report.Isp.Length == 0) report.Isp = r.Isp;
                    if (report.Lat == 0 && r.Lat != 0) { report.Lat = r.Lat; report.Lon = r.Lon; }
                    if (r.ClockSkewSeconds != 0) report.ClockSkewSeconds = r.ClockSkewSeconds;
                }

                if (seen.Count > 1)
                    report.Warnings.Add("The lookup services disagree about the exit IP - the connection may be rotating.");

                report.Ok = report.Ip.Length > 0;
                report.LatencyMs = (int)sw.ElapsedMilliseconds;

                if (Math.Abs(report.ClockSkewSeconds) > 120)
                    report.Warnings.Add("This PC's clock is " + Math.Abs(report.ClockSkewSeconds) +
                                        " s off the internet time - pages that compare it with the date header will notice.");

                var dns = Resolver(proxy, viaProxy);
                if (dns != null)
                {
                    report.ResolverIp = dns[0];
                    report.ResolverGeo = dns[1];
                    if (viaProxy && dns.Length > 2 && dns[2].Length > 0 && report.CountryCode.Length > 0 &&
                        !string.Equals(dns[2], report.CountryCode, StringComparison.OrdinalIgnoreCase))
                        report.Warnings.Add("DNS resolves through " + dns[1] + " (" + dns[2] + ") while the exit IP is in " +
                                            report.CountryCode + " - a DNS leak test would flag this.");
                }
                return report;
            }
            catch (Exception ex)
            {
                report.Error = Friendly(ex);
                report.LatencyMs = (int)sw.ElapsedMilliseconds;
                return report;
            }
            finally
            {
                if (relay != null) relay.Dispose();
            }
        }

        private static void Count(Dictionary<string, int> map, string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            int n;
            map.TryGetValue(ip, out n);
            map[ip] = n + 1;
        }

        private static IpReport IpApi(IWebProxy proxy, bool viaProxy)
        {
            try
            {
                int skew;
                var d = GetJson(proxy, "http://ip-api.com/json/?fields=status,message,country,countryCode," +
                                       "regionName,city,timezone,isp,lat,lon,query", viaProxy ? 20000 : 9000, out skew);
                if (d == null || !Str(d, "status").Equals("success", StringComparison.OrdinalIgnoreCase)) return null;
                return new IpReport
                {
                    Ok = true,
                    ClockSkewSeconds = skew,
                    Ip = Str(d, "query"),
                    Country = Str(d, "country"),
                    CountryCode = Str(d, "countryCode"),
                    City = Str(d, "city"),
                    Region = Str(d, "regionName"),
                    Timezone = Str(d, "timezone"),
                    Isp = Str(d, "isp"),
                    Lat = Num(d, "lat"),
                    Lon = Num(d, "lon")
                };
            }
            catch { return null; }
        }

        private static IpReport IpApiCo(IWebProxy proxy, bool viaProxy)
        {
            try
            {
                int skew;
                var d = GetJson(proxy, "https://ipapi.co/json/", viaProxy ? 20000 : 9000, out skew);
                if (d == null) return null;
                string ip = Str(d, "ip");
                if (ip.Length == 0) return null;
                return new IpReport
                {
                    Ok = true,
                    ClockSkewSeconds = skew,
                    Ip = ip,
                    Country = Str(d, "country_name"),
                    CountryCode = Str(d, "country_code"),
                    City = Str(d, "city"),
                    Region = Str(d, "region"),
                    Timezone = Str(d, "timezone"),
                    Isp = Str(d, "org"),
                    Lat = Num(d, "latitude"),
                    Lon = Num(d, "longitude")
                };
            }
            catch { return null; }
        }

        private static IpReport IpInfo(IWebProxy proxy, bool viaProxy)
        {
            try
            {
                int skew;
                var d = GetJson(proxy, "https://ipinfo.io/json", viaProxy ? 20000 : 9000, out skew);
                if (d == null) return null;
                string ip = Str(d, "ip");
                if (ip.Length == 0) return null;
                string loc = Str(d, "loc");
                double lat = 0, lon = 0;
                int comma = loc.IndexOf(',');
                if (comma > 0)
                {
                    double.TryParse(loc.Substring(0, comma), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out lat);
                    double.TryParse(loc.Substring(comma + 1), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out lon);
                }
                return new IpReport
                {
                    Ok = true,
                    ClockSkewSeconds = skew,
                    Ip = ip,
                    Country = Str(d, "country"),
                    CountryCode = Str(d, "country"),
                    City = Str(d, "city"),
                    Region = Str(d, "region"),
                    Timezone = Str(d, "timezone"),
                    Isp = Str(d, "org"),
                    Lat = lat,
                    Lon = lon
                };
            }
            catch { return null; }
        }

        /// <summary>Resolver address and its geography, as an EDNS echo service sees it.</summary>
        private static string[] Resolver(IWebProxy proxy, bool viaProxy)
        {
            try
            {
                int skew;
                var d = GetJson(proxy, "https://edns.ip-api.com/json", viaProxy ? 15000 : 8000, out skew);
                if (d == null) return null;
                var dns = d.ContainsKey("dns") ? d["dns"] as Dictionary<string, object> : null;
                if (dns == null) return null;
                string ip = Str(dns, "ip");
                string geo = Str(dns, "geo");
                if (ip.Length == 0) return null;
                // geo reads like "United States - Dallas"
                string cc = "";
                int dash = geo.IndexOf(" - ", StringComparison.Ordinal);
                string country = dash > 0 ? geo.Substring(0, dash) : geo;
                if (country.Equals("United States", StringComparison.OrdinalIgnoreCase)) cc = "US";
                else if (country.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase)) cc = "GB";
                else if (country.Equals("Germany", StringComparison.OrdinalIgnoreCase)) cc = "DE";
                else if (country.Equals("Netherlands", StringComparison.OrdinalIgnoreCase)) cc = "NL";
                else if (country.Equals("France", StringComparison.OrdinalIgnoreCase)) cc = "FR";
                else if (country.Equals("Canada", StringComparison.OrdinalIgnoreCase)) cc = "CA";
                else if (country.Equals("Singapore", StringComparison.OrdinalIgnoreCase)) cc = "SG";
                else if (country.Equals("Japan", StringComparison.OrdinalIgnoreCase)) cc = "JP";
                else if (country.Equals("Australia", StringComparison.OrdinalIgnoreCase)) cc = "AU";
                else if (country.Equals("Brazil", StringComparison.OrdinalIgnoreCase)) cc = "BR";
                else if (country.Equals("India", StringComparison.OrdinalIgnoreCase)) cc = "IN";
                return new[] { ip, geo, cc };
            }
            catch { return null; }
        }

        private static Dictionary<string, object> GetJson(IWebProxy proxy, string url, int timeout, out int skew)
        {
            skew = 0;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Proxy = proxy;
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            req.UserAgent = "JoeBrowser/1.0";
            req.KeepAlive = false;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string json = sr.ReadToEnd();
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json)
                    as Dictionary<string, object>;
                /* The response date header is the only internet clock available without a
                   separate time service, and a skewed clock is a classic detection signal. */
                try
                {
                    string date = resp.Headers["Date"];
                    DateTime server;
                    if (!string.IsNullOrEmpty(date) &&
                        DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out server))
                        skew = (int)Math.Round((DateTime.UtcNow - server).TotalSeconds);
                }
                catch { }
                return d;
            }
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            double n;
            return double.TryParse(Convert.ToString(v), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        internal static string Friendly(Exception ex)
        {
            var we = ex as WebException;
            if (we != null)
            {
                if (we.Status == WebExceptionStatus.Timeout) return "Connection timed out";
                if (we.Status == WebExceptionStatus.NameResolutionFailure) return "DNS lookup failed";
                if (we.Status == WebExceptionStatus.ProxyNameResolutionFailure) return "Proxy host not found";
                if (we.Status == WebExceptionStatus.ConnectFailure) return "Could not connect";
                var resp = we.Response as HttpWebResponse;
                if (resp != null) return "Service returned HTTP " + (int)resp.StatusCode;
            }
            return string.IsNullOrEmpty(ex.Message) ? "Check failed" : ex.Message;
        }
    }
}
