using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    internal sealed class LeakItem
    {
        public string Name;
        public bool Ok;
        public string Detail;
        public bool Skipped;

        public LeakItem(string name, bool ok, string detail)
        {
            Name = name; Ok = ok; Detail = detail;
        }
    }

    internal sealed class LeakResult
    {
        public readonly List<LeakItem> Items = new List<LeakItem>();
        public string Error = "";

        public int Passed
        {
            get
            {
                int n = 0;
                foreach (var i in Items) if (!i.Skipped && i.Ok) n++;
                return n;
            }
        }

        public int Total
        {
            get
            {
                int n = 0;
                foreach (var i in Items) if (!i.Skipped) n++;
                return n;
            }
        }

        public int Score
        {
            get { return Total == 0 ? 0 : (int)Math.Round(Passed * 100.0 / Total); }
        }

        public bool Clean { get { return Total > 0 && Passed == Total; } }

        public string Verdict
        {
            get
            {
                if (Total == 0) return "Not checked";
                if (Clean) return "No leak found";
                return (Total - Passed) + (Total - Passed == 1 ? " check failed" : " checks failed");
            }
        }
    }

    /// <summary>
    /// Reads back what the live tab actually reports and compares it with the profile it was
    /// supposed to be. This is the same set of questions a fingerprint page asks - automation
    /// flags, the user agent, the platform, the GPU, the clock, the touch count, the font count
    /// and whether WebRTC hands out a real address - answered from inside the session.
    /// </summary>
    internal static class LeakTest
    {
        private const string Probe = @"
(function () {
  var o = {};
  o.webdriver = navigator.webdriver === true;
  o.cdc = !!(window.cdc_adoQpoasnfa76pfcZLmcfl_Array || window.$cdc_asdjflasutopfhvcZLmcfl_ ||
             window.__nightmare || window._phantom || window.callPhantom ||
             window.__selenium_unwrapped || window.__webdriver_evaluate ||
             window.__driver_evaluate || window.domAutomation);
  o.chrome = !!(window.chrome && window.chrome.runtime);
  o.plugins = navigator.plugins ? navigator.plugins.length : 0;
  o.ua = navigator.userAgent || '';
  o.platform = navigator.platform || '';
  o.cores = navigator.hardwareConcurrency || 0;
  o.memory = navigator.deviceMemory || 0;
  try { o.tz = Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch (e) { o.tz = ''; }
  o.screen = screen.width + 'x' + screen.height;
  o.inner = window.innerWidth + 'x' + window.innerHeight;
  o.dpr = window.devicePixelRatio || 1;
  o.touch = navigator.maxTouchPoints || 0;
  o.lang = (navigator.languages || [navigator.language]).join(',');
  try {
    var c = document.createElement('canvas');
    var g = c.getContext('webgl2') || c.getContext('webgl');
    if (!g) o.gl = 'none';
    else {
      var e = g.getExtension('WEBGL_debug_renderer_info');
      o.gl = e ? (g.getParameter(e.UNMASKED_VENDOR_WEBGL) + ' | ' + g.getParameter(e.UNMASKED_RENDERER_WEBGL)) : 'no-ext';
    }
  } catch (e) { o.gl = 'error'; }
  try {
    var cc = document.createElement('canvas');
    cc.width = 220; cc.height = 30;
    var x = cc.getContext('2d');
    x.textBaseline = 'top'; x.font = '14px Arial';
    x.fillStyle = '#f60'; x.fillRect(0, 0, 100, 20);
    x.fillStyle = '#069'; x.fillText('JoeBrowser 123', 2, 2);
    o.canvas = cc.toDataURL().length;
  } catch (e) { o.canvas = -1; }
  try {
    var base = ['monospace', 'sans-serif', 'serif'];
    var probe = 'mmmmmmmmmmlli';
    var ctx = document.createElement('canvas').getContext('2d');
    var ref = {};
    for (var i = 0; i < base.length; i++) { ctx.font = '72px ' + base[i]; ref[base[i]] = ctx.measureText(probe).width; }
    var fams = ['Arial', 'Calibri', 'Segoe UI', 'Helvetica', 'Times New Roman', 'Courier New', 'DejaVu Sans', 'Menlo'];
    var found = 0;
    for (var f = 0; f < fams.length; f++) {
      var hit = false;
      for (var b = 0; b < base.length; b++) {
        ctx.font = '72px ""' + fams[f] + '"",' + base[b];
        if (ctx.measureText(probe).width !== ref[base[b]]) hit = true;
      }
      if (hit) found++;
    }
    o.fonts = found;
  } catch (e) { o.fonts = -1; }
  return JSON.stringify(o);
})()";

        private const string WebRtcProbe = @"
new Promise(function (resolve) {
  try {
    var PC = window.RTCPeerConnection;
    if (!PC) { resolve('none'); return; }
    var pc = new PC({ iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] });
    var ips = {};
    var done = false;
    function finish() {
      if (done) return;
      done = true;
      try { pc.close(); } catch (e) {}
      resolve(Object.keys(ips).join(','));
    }
    pc.onicecandidate = function (e) {
      if (!e.candidate) { finish(); return; }
      var m = /([0-9]{1,3}(\.[0-9]{1,3}){3}|[a-f0-9]{1,4}(:[a-f0-9]{0,4}){2,7})/i.exec(e.candidate.candidate);
      if (m) ips[m[1]] = 1;
    };
    pc.createDataChannel('x');
    pc.createOffer().then(function (d) { return pc.setLocalDescription(d); });
    setTimeout(finish, 2000);
  } catch (e) { resolve('error'); }
})";

        public static async Task<LeakResult> RunAsync(BrowserSession session, FingerprintConfig fp)
        {
            var result = new LeakResult();
            if (session == null || !session.IsRunning)
            {
                result.Error = "Open the profile first.";
                return result;
            }
            if (session.IsFirefox)
            {
                result.Error = "The live check needs a Chromium-based engine. Firefox profiles keep the host values by design.";
                return result;
            }

            string json;
            try { json = await session.EvaluateAsync(Probe, 20000).ConfigureAwait(false); }
            catch (Exception ex)
            {
                result.Error = "The page did not answer: " + ex.Message;
                return result;
            }
            if (string.IsNullOrEmpty(json))
            {
                result.Error = "The page did not answer. It may still be loading.";
                return result;
            }

            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>; }
            catch { d = null; }
            if (d == null)
            {
                result.Error = "Could not read the answer from the page.";
                return result;
            }

            // --- automation surface -------------------------------------------------
            result.Items.Add(new LeakItem("Automation flag",
                !Bool(d, "webdriver"), Bool(d, "webdriver") ? "navigator.webdriver is true" : "navigator.webdriver is false"));

            result.Items.Add(new LeakItem("Driver artefacts",
                !Bool(d, "cdc"), Bool(d, "cdc") ? "A WebDriver object is present on window" : "No WebDriver objects found"));

            bool chromeObj = Bool(d, "chrome");
            result.Items.Add(new LeakItem("Browser object", chromeObj,
                chromeObj ? "window.chrome is present" : "window.chrome is missing - Chromium always exposes it"));

            // --- identity -----------------------------------------------------------
            string ua = Str(d, "ua");
            bool uaOk = string.IsNullOrEmpty(fp.UserAgent) || ua == fp.UserAgent;
            result.Items.Add(new LeakItem("User agent", uaOk,
                uaOk ? "Matches the profile" : "Expected " + fp.UserAgent + ", got " + ua));

            string platform = Str(d, "platform");
            bool platOk = string.IsNullOrEmpty(fp.Platform) || platform == fp.Platform;
            result.Items.Add(new LeakItem("Platform", platOk,
                platOk ? platform : "Expected " + fp.Platform + ", got " + platform));

            int cores = Int(d, "cores");
            result.Items.Add(new LeakItem("CPU cores", cores == fp.Cores,
                cores + (cores == fp.Cores ? "" : " (expected " + fp.Cores + ")")));

            int mem = Int(d, "memory");
            bool memOk = fp.Memory <= 0 || mem == Math.Min(8, fp.Memory);
            result.Items.Add(new LeakItem("Memory", memOk,
                mem + " GB" + (memOk ? "" : " (expected " + Math.Min(8, fp.Memory) + ")")));

            string tz = Str(d, "tz");
            bool tzOk = string.IsNullOrEmpty(fp.Timezone) || tz == fp.Timezone;
            result.Items.Add(new LeakItem("Timezone", tzOk,
                tzOk ? tz : "Expected " + fp.Timezone + ", got " + tz));

            string screen = Str(d, "screen");
            string want = fp.ScreenW + "x" + fp.ScreenH;
            result.Items.Add(new LeakItem("Screen", screen == want,
                screen == want ? screen : "Expected " + want + ", got " + screen));

            int touch = Int(d, "touch");
            result.Items.Add(new LeakItem("Touch points", touch == fp.TouchPoints,
                touch + (touch == fp.TouchPoints ? "" : " (expected " + fp.TouchPoints + ")")));

            string lang = Str(d, "lang");
            bool langOk = string.IsNullOrEmpty(fp.Language) ||
                          lang.StartsWith(fp.Language, StringComparison.OrdinalIgnoreCase);
            result.Items.Add(new LeakItem("Languages", langOk,
                langOk ? lang : "Expected " + fp.Language + ", got " + lang));

            // --- graphics -----------------------------------------------------------
            string gl = Str(d, "gl");
            bool glOk = gl.IndexOf("|", StringComparison.Ordinal) > 0 &&
                        (string.IsNullOrEmpty(fp.WebGlRenderer) || gl.IndexOf(fp.WebGlRenderer, StringComparison.Ordinal) >= 0);
            result.Items.Add(new LeakItem("WebGL renderer", glOk,
                glOk ? gl : (gl == "none" ? "No WebGL context at all" : "Expected " + fp.WebGlRenderer)));

            long canvas = Long(d, "canvas");
            result.Items.Add(new LeakItem("Canvas readable", canvas > 0,
                canvas > 0 ? "Hash length " + canvas : "Canvas export failed"));

            int fonts = Int(d, "fonts");
            result.Items.Add(new LeakItem("Font families", fonts >= 3,
                fonts < 0 ? "Font probe failed" : fonts + " of 8 common families present"));

            // --- network ------------------------------------------------------------
            string rtc = null;
            try { rtc = await session.EvaluateAsync(WebRtcProbe, 9000).ConfigureAwait(false); }
            catch { rtc = "error"; }
            if (rtc == "none")
                result.Items.Add(new LeakItem("WebRTC", true, "WebRTC is not available"));
            else if (rtc == "error")
            {
                var skip = new LeakItem("WebRTC", true, "Probe did not complete");
                skip.Skipped = true;
                result.Items.Add(skip);
            }
            else
            {
                bool localOnly = string.IsNullOrEmpty(rtc) || rtc == "127.0.0.1" || rtc.StartsWith("127.0.0.1,") ||
                                 rtc.IndexOf(".local", StringComparison.OrdinalIgnoreCase) >= 0;
                string mode = fp.WebRtcMode ?? "proxy";
                bool expectOpen = mode.Equals("public", StringComparison.OrdinalIgnoreCase);
                result.Items.Add(new LeakItem("WebRTC", expectOpen || localOnly,
                    string.IsNullOrEmpty(rtc) ? "No candidate addresses exposed"
                        : (expectOpen ? "Exposes " + rtc : "Exposes " + rtc)));
            }

            return result;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            bool b;
            return bool.TryParse(Convert.ToString(v), out b) && b;
        }

        private static int Int(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            int n;
            return int.TryParse(Convert.ToString(v), out n) ? n : 0;
        }

        private static long Long(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            long n;
            return long.TryParse(Convert.ToString(v), out n) ? n : 0;
        }
    }
}
