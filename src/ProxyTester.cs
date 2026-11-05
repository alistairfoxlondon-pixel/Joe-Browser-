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
    internal class ProxyCheckResult
    {
        public bool Ok { get; set; }
        public string Ip { get; set; }
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public string City { get; set; }
        public string Timezone { get; set; }
        public string Error { get; set; }
        public int LatencyMs { get; set; }

        public ProxyCheckResult()
        {
            Ip = ""; Country = ""; CountryCode = ""; City = ""; Timezone = ""; Error = "";
        }
    }

    internal static class ProxyTester
    {
        public static Task<ProxyCheckResult> TestAsync(ProxyConfig cfg)
        {
            return Task.Run(new Func<ProxyCheckResult>(() => Test(cfg)));
        }

        private static ProxyCheckResult _system;
        private static DateTime _systemAt = DateTime.MinValue;

        /// <summary>The public connection of this machine, cached so opening editors stays quick.</summary>
        public static async Task<ProxyCheckResult> SystemAsync()
        {
            if (_system != null && (DateTime.Now - _systemAt).TotalMinutes < 30) return _system;
            var r = await TestSystemAsync().ConfigureAwait(false);
            if (r != null && r.Ok)
            {
                _system = r;
                _systemAt = DateTime.Now;
            }
            return r;
        }

        /// <summary>Reads the timezone and country of the connection this machine already uses.</summary>
        public static Task<ProxyCheckResult> TestSystemAsync()
        {
            return Task.Run(new Func<ProxyCheckResult>(() =>
            {
                var sw = Stopwatch.StartNew();
                try { return Fetch(null, sw); }
                catch (Exception ex) { return new ProxyCheckResult { Ok = false, Error = Friendly(ex) }; }
            }));
        }

        private static ProxyCheckResult Test(ProxyConfig cfg)
        {
            if (cfg == null || !cfg.Enabled)
                return new ProxyCheckResult { Ok = false, Error = "Add a proxy first" };

            var sw = Stopwatch.StartNew();
            try
            {
                using (var relay = new ProxyRelay(cfg))
                    return Fetch(new WebProxy("http://127.0.0.1:" + relay.Port), sw);
            }
            catch (Exception ex)
            {
                return new ProxyCheckResult { Ok = false, Error = Friendly(ex) };
            }
        }

        private static ProxyCheckResult Fetch(IWebProxy proxy, Stopwatch sw)
        {
            string lastError = proxy == null ? "No internet connection" : "Proxy did not respond";
            string[] urls = { "https://ipinfo.io/json", "https://ipapi.co/json/" };
            foreach (string url in urls)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    if (proxy != null) req.Proxy = proxy;
                    else req.Proxy = null;
                    req.Timeout = proxy == null ? 8000 : 20000;
                    req.ReadWriteTimeout = 20000;
                    req.UserAgent = "JoeBrowser/1.0";
                    req.KeepAlive = false;

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                        if (d == null) { lastError = "Unexpected response"; continue; }

                        var r = new ProxyCheckResult
                        {
                            Ok = true,
                            Ip = Str(d, "ip"),
                            City = Str(d, "city"),
                            Country = Str(d, "country_name"),
                            CountryCode = Str(d, "country_code"),
                            Timezone = Str(d, "timezone"),
                            LatencyMs = (int)sw.ElapsedMilliseconds
                        };
                        if (string.IsNullOrEmpty(r.CountryCode)) r.CountryCode = Str(d, "country");
                        if (string.IsNullOrEmpty(r.Country)) r.Country = r.CountryCode;
                        if (string.IsNullOrEmpty(r.Timezone)) r.Timezone = Str(d, "time_zone");
                        if (string.IsNullOrEmpty(r.Ip)) { lastError = "Could not read IP"; continue; }
                        return r;
                    }
                }
                catch (Exception ex)
                {
                    lastError = Friendly(ex);
                }
            }
            return new ProxyCheckResult { Ok = false, Error = lastError, LatencyMs = (int)sw.ElapsedMilliseconds };
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return "";
        }

        private static string Friendly(Exception ex)
        {
            if (ex is WebException)
            {
                var we = (WebException)ex;
                if (we.Status == WebExceptionStatus.Timeout) return "Connection timed out";
                if (we.Status == WebExceptionStatus.ProxyNameResolutionFailure) return "Proxy host not found";
                if (we.Status == WebExceptionStatus.ConnectFailure) return "Could not connect to proxy";
                if (we.Response is HttpWebResponse)
                {
                    int code = (int)((HttpWebResponse)we.Response).StatusCode;
                    if (code == 407) return "Proxy authentication failed";
                    return "Proxy returned HTTP " + code;
                }
            }
            return string.IsNullOrEmpty(ex.Message) ? "Proxy test failed" : ex.Message;
        }
    }
}
