using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    internal static class FingerprintScript
    {
        public static string Build(FingerprintConfig fp, string ua, string platform, string language, string[] languages)
        {
            return Build(fp, ua, platform, language, languages, null);
        }

        public static string Build(FingerprintConfig fp, string ua, string platform, string language, string[] languages,
            Dictionary<string, object> uaMetadata)
        {
            var cfg = new Dictionary<string, object>();
            cfg["ua"] = ua ?? "";
            cfg["platform"] = platform ?? "Win32";
            cfg["language"] = language ?? "en-US";
            cfg["languages"] = languages ?? new[] { language ?? "en-US" };
            cfg["cores"] = fp.Cores;
            // navigator.deviceMemory is clamped by the spec to 0.5/1/2/4/8; anything larger
            // is a value no browser can emit and flags the profile immediately.
            cfg["memory"] = Math.Min(8, fp.Memory > 0 ? fp.Memory : 8);
            cfg["sw"] = fp.ScreenW;
            cfg["sh"] = fp.ScreenH;
            cfg["aw"] = fp.AvailW;
            cfg["ah"] = fp.AvailH;
            cfg["cd"] = fp.ColorDepth > 0 ? fp.ColorDepth : 24;
            cfg["dpr"] = fp.PixelRatio > 0 ? fp.PixelRatio : 1.0;
            cfg["glVendor"] = fp.WebGlVendor ?? "";
            cfg["glRenderer"] = fp.WebGlRenderer ?? "";
            cfg["canvas"] = fp.CanvasNoise;
            cfg["audio"] = fp.AudioNoise;
            cfg["fonts"] = fp.FontsNoise;
            cfg["dnt"] = fp.DoNotTrack ? "1" : (object)null;
            cfg["battery"] = fp.SpoofBattery;
            cfg["seed"] = fp.Seed;
            cfg["touch"] = fp.TouchPoints;
            /* Layout width is one of the few surfaces a page can measure without asking for
               anything: innerWidth minus documentElement.clientWidth is exactly the width of the
               scrollbar the HOST operating system draws. A Windows host therefore reports 17 px
               to a page being told the profile is a Mac or a phone, contradicting the platform
               string in the same breath - the cheapest anti-detect tell there is. This is the
               scrollbar of the device the profile claims to be. */
            cfg["sbw"] = FingerprintFactory.ScrollbarWidthFor(fp);
            cfg["mobile"] = fp.DeviceType != null && fp.DeviceType != "desktop";
            cfg["safari"] = FingerprintFactory.IsIos(fp.Os);
            cfg["webrtc"] = (fp.WebRtcMode ?? (fp.WebRtcProtect ? "proxy" : "off")).ToLowerInvariant();
            cfg["brand"] = fp.Brand ?? "";
            cfg["os"] = fp.Os ?? "";
            cfg["accept"] = fp.AcceptLanguage ?? language ?? "en-US";
            cfg["tz"] = fp.Timezone ?? "";
            cfg["geo"] = fp.GeoMode ?? "proxy";
            cfg["fontSet"] = string.IsNullOrEmpty(fp.FontSet) ? "auto" : fp.FontSet;
            cfg["hwNoise"] = fp.HardwareNoise;
            /* The version the device actually reports, resolved once so every surface that
               mentions it (user agent, hint, screen) carries the same number. */
            cfg["androidVer"] = FingerprintFactory.AndroidVersionFor(fp);
            cfg["iosVer"] = FingerprintFactory.IosVersionFor(fp);

            // Client hints have to come from the same identity as the user agent. The navigator
            // object is patched with exactly the metadata CDP sends in the request headers, so a
            // page that reads both cannot see the host browser's real brands or version.
            if (uaMetadata != null)
            {
                object v;
                cfg["brands"] = uaMetadata.TryGetValue("brands", out v) ? v : null;
                cfg["fullVersionList"] = uaMetadata.TryGetValue("fullVersionList", out v) ? v : null;
                cfg["uaFullVersion"] = uaMetadata.TryGetValue("fullVersion", out v) ? v : "";
                cfg["uaPlatform"] = uaMetadata.TryGetValue("platform", out v) ? v : "";
                cfg["platformVersion"] = uaMetadata.TryGetValue("platformVersion", out v) ? v : "";
                cfg["architecture"] = uaMetadata.TryGetValue("architecture", out v) ? v : "";
                cfg["bitness"] = uaMetadata.TryGetValue("bitness", out v) ? v : "";
                cfg["model"] = uaMetadata.TryGetValue("model", out v) ? v : "";
                cfg["wow64"] = uaMetadata.TryGetValue("wow64", out v) ? v : false;
            }

            /* Workers are a separate realm: Page.addScriptToEvaluateOnNewDocument never reaches
               them, and CDP worker sessions are not reliably attachable on every build. The page
               therefore prepends this bootstrap to every JavaScript Blob, which is how nearly all
               fingerprint libraries build a worker. Without it a worker reports the host's real
               navigator/WebGL while the page reports the profile - a one-message contradiction. */
            cfg["wjs"] = WorkerBootstrap(fp, ua, platform, language, languages);
            string json = new JavaScriptSerializer().Serialize(cfg);

            return Template.Replace("__CFG__", json);
        }

        /// <summary>Compact worker-realm patch: navigator, timezone, OffscreenCanvas, WebGL.</summary>
        private static string WorkerBootstrap(FingerprintConfig fp, string ua, string platform,
            string language, string[] languages)
        {
            var c = new Dictionary<string, object>();
            c["ua"] = ua ?? "";
            c["platform"] = platform ?? "Win32";
            c["language"] = language ?? "en-US";
            c["languages"] = languages ?? new[] { language ?? "en-US" };
            c["cores"] = fp.Cores;
            c["memory"] = Math.Min(8, fp.Memory > 0 ? fp.Memory : 8);
            c["touch"] = fp.TouchPoints;
            c["tz"] = fp.Timezone ?? "";
            c["glVendor"] = fp.WebGlVendor ?? "";
            c["glRenderer"] = fp.WebGlRenderer ?? "";
            c["canvas"] = fp.CanvasNoise;
            c["seed"] = fp.Seed;
            c["safari"] = FingerprintFactory.IsIos(fp.Os);
            return WorkerTemplate.Replace("__CFG__", new JavaScriptSerializer().Serialize(c));
        }

        private const string Template = @"
(function () {
  'use strict';
  var F = __CFG__;

  /* Idempotence guard. A named window property is the shortest detection shortcut there is
     (Object.getOwnPropertyNames(window) names the tool), so the marker is a symbol on
     Navigator.prototype instead: reachable again on re-injection, invisible to name scans. */
  var NP = (typeof Navigator !== 'undefined' && Navigator.prototype) ? Navigator.prototype : Object.getPrototypeOf(navigator);
  var GUARD = Symbol.for('jb.realm');
  try { if (NP[GUARD]) return; Object.defineProperty(NP, GUARD, { value: 1, configurable: true }); } catch (e) {}

  var origToString = Function.prototype.toString;
  var fakes = (typeof WeakMap === 'function') ? new WeakMap() : null;

  /* A patched native has to look native in three places at once: toString(), the arity reported
     by .length, and the parameter list toString() renders. V8 prints an accessor as
     'function get x() { [native code] }', so the replacement is built from the real function's
     own parameter names and that shape - inventing a marker string of ours would simply be a
     signature a detector could match against. */
  var FN_SRC = /^\s*(?:async\s+)?function[^(]*\(([^)]*)\)/;

  function mark(fn, name, len) {
    try {
      if (fakes) {
        var params = '';
        try {
          var m = FN_SRC.exec(origToString.call(fn));
          if (m) params = m[1];
        } catch (e) {}
        fakes.set(fn, 'function ' + name + '(' + params + ') { [native code] }');
      }
      /* A native accessor carries its own name ('get userAgent'); an anonymous replacement
         reports name === '', which is the first thing a descriptor scan looks for. */
      if (name) {
        try { Object.defineProperty(fn, 'name', { value: name, configurable: true }); } catch (e) {}
      }
      if (len != null) Object.defineProperty(fn, 'length', { value: len, configurable: true });
    } catch (e) {}
    return fn;
  }

  try {
    Function.prototype.toString = mark(function toString() {
      try { if (fakes && fakes.has(this)) return fakes.get(this); } catch (e) {}
      return origToString.call(this);
    }, 'toString', 0);
  } catch (e) {}

  /* ---- Worker realm ----
     A worker gets a fresh global and never sees this script. Fingerprint libraries build their
     worker from a JavaScript Blob, so the bootstrap is prepended there. Without it the page
     reports the profile while the worker reports the host's real navigator, cores, timezone,
     canvas hash and GPU - a contradiction one postMessage exposes. */
  try {
    if (F.wjs && typeof Blob === 'function') {
      var OrigBlob = window.Blob;
      var JbBlob = mark(function Blob(parts, options) {
        try {
          var t = options && options.type ? String(options.type) : '';
          if (/javascript|ecmascript/i.test(t) && parts && parts.length) {
            var list = Array.prototype.slice.call(parts);
            list.unshift(F.wjs);
            parts = list;
          }
        } catch (e) {}
        return new OrigBlob(parts, options);
      }, 'Blob', 2);
      try { JbBlob.prototype = OrigBlob.prototype; } catch (e) {}
      window.Blob = JbBlob;
    }
  } catch (e) {}

  /* The interface a getter may be invoked on. Native accessors throw TypeError for any other
     this, and checkers rely on it: they capture the getter and call it with null expecting the
     call to blow up, and they look for own prototype/arguments/caller on the function. A plain
     function fails every one of those, so both factories below build object-literal accessors
     instead - those carry no own prototype, and the guard throws for a wrong this. */
  function ownerIface(obj) {
    try {
      var I = obj && obj.constructor;
      if (typeof I === 'function' && I.prototype) return I;
    } catch (e) {}
    return Object;
  }

  function attachGetter(obj, prop, holder) {
    var d = Object.getOwnPropertyDescriptor(holder, prop);
    if (!d || !d.get) return false;
    mark(d.get, 'get ' + prop, 0);
    Object.defineProperty(obj, prop, { get: d.get, set: undefined, enumerable: true, configurable: true });
    return true;
  }

  function getter(obj, prop, value) {
    if (!obj) return;
    try {
      var I = ownerIface(obj);
      var holder = {
        get [prop]() {
          if (!(this instanceof I)) throw new TypeError('Illegal invocation');
          return value;
        }
      };
      if (attachGetter(obj, prop, holder)) return;
    } catch (e) {}
    try {
      var g = mark(function () { return value; }, 'get ' + prop, 0);
      Object.defineProperty(obj, prop, { get: g, set: undefined, enumerable: true, configurable: true });
    } catch (e2) {}
  }

  function liveGetter(obj, prop, fn) {
    if (!obj) return;
    try {
      var I = ownerIface(obj);
      var holder = {
        get [prop]() {
          if (!(this instanceof I)) throw new TypeError('Illegal invocation');
          return fn.call(this);
        }
      };
      if (attachGetter(obj, prop, holder)) return;
    } catch (e) {}
    try {
      Object.defineProperty(obj, prop, { get: mark(fn, 'get ' + prop, 0), configurable: true, enumerable: true });
    } catch (e2) {}
  }

  function method(obj, prop, fn, len) {
    try {
      Object.defineProperty(obj, prop, {
        value: mark(fn, prop, len), writable: true, enumerable: false, configurable: true
      });
    } catch (e) {}
  }

  /* Deterministic per profile, and spread over the whole 32-bit seed rather than seed % 100000,
     which collided two profiles onto the same noise pattern. */
  var SEED = (F.seed >>> 0) || 1;
  function noise(i) {
    var t = (i + SEED) | 0;
    t = Math.imul(t ^ (t >>> 15), 2246822507);
    t = Math.imul(t ^ (t >>> 13), 3266489917);
    return (((t ^ (t >>> 16)) >>> 0) % 2000) / 2000;
  }

  /* ---- Navigator ---- */
  var isSafari = !!F.safari;
  getter(NP, 'userAgent', F.ua);
  getter(NP, 'appVersion', F.ua.replace('Mozilla/', ''));
  getter(NP, 'appName', 'Netscape');
  getter(NP, 'appCodeName', 'Mozilla');
  getter(NP, 'platform', F.platform);
  getter(NP, 'vendor', isSafari ? 'Apple Computer, Inc.' : 'Google Inc.');
  getter(NP, 'vendorSub', '');
  getter(NP, 'productSub', '20030107');
  getter(NP, 'product', 'Gecko');
  getter(NP, 'language', F.language);
  /* Real Chrome hands out a fresh array per read; a frozen one makes navigator.languages.push
     throw, which no browser does. */
  liveGetter(NP, 'languages', function () { return F.languages.slice(); });
  getter(NP, 'hardwareConcurrency', F.cores);
  /* navigator.deviceMemory is a Chromium-only API. Shipping it next to a Safari or Firefox UA is
     a contradiction the profile creates itself, and it is the first thing a checker reads. */
  if (!isSafari && F.brand !== 'Firefox') getter(NP, 'deviceMemory', F.memory);
  getter(NP, 'maxTouchPoints', F.touch);
  getter(NP, 'webdriver', false);
  getter(NP, 'doNotTrack', F.dnt);

  function emptyList(tag) {
    /* Built on the real interface prototype, not on Array: a phone still answers
       navigator.plugins instanceof PluginArray === true, and an Array fails that check
       while also reporting '[object Array]' to Object.prototype.toString. */
    var proto = null;
    try { proto = tag === 'MimeTypeArray' ? MimeTypeArray.prototype : PluginArray.prototype; } catch (e) {}
    var a = proto ? Object.create(proto) : [];
    try {
      Object.defineProperty(a, 'length', { value: 0, configurable: true });
      Object.defineProperty(a, 'item', { value: mark(function item(index) { return null; }, 'item', 1), configurable: true });
      Object.defineProperty(a, 'namedItem', { value: mark(function namedItem(name) { return null; }, 'namedItem', 1), configurable: true });
      if (tag === 'PluginArray')
        Object.defineProperty(a, 'refresh', { value: mark(function refresh() { }, 'refresh', 0), configurable: true });
    } catch (e) {}
    return a;
  }

  /* ---- iOS and Android tell-tales ---- */
  try {
    if (isSafari) {
      /* WebKit exposes no UA-Client-Hints at all. Deleting is enough; re-defining the property
         with an undefined getter leaves 'userAgentData' in navigator === true on an iPhone. */
      try { delete Navigator.prototype.userAgentData; } catch (e) {}
      try { delete window.chrome; } catch (e) {}
      /* deviceMemory is a Chromium API too, so the host browser's own value has to go as well. */
      try { delete NP.deviceMemory; } catch (e) {}
    }
    if (F.mobile) {
      getter(NP, 'plugins', emptyList('PluginArray'));
      getter(NP, 'mimeTypes', emptyList('MimeTypeArray'));
      Object.defineProperty(window, 'ondeviceorientation', { value: null, configurable: true });
    }
  } catch (e) {}

  /* ---- Client hints, engine and plugin tell-tales ---- */
  try {
    var uad = navigator.userAgentData;
    var uadPlatform = F.platform === 'Win32' ? 'Windows'
      : (F.platform === 'MacIntel' ? 'macOS'
      : (F.platform === 'Linux x86_64' ? 'Linux'
      : (F.platform === 'iPhone' || F.platform === 'iPad' ? 'iOS' : 'Android')));
    function hintCopy(list) {
      var out = [];
      if (list && list.length) for (var i = 0; i < list.length; i++) {
        var b = list[i] || {};
        out.push({ brand: String(b.brand || ''), version: String(b.version || '') });
      }
      return out;
    }
    /* Chromium exposes navigator.userAgentData.brands straight from the binary. Leaving the
       host's array in place next to a spoofed UA is the single easiest 'different browser
       version' check, so both the reduced and high-entropy lists are replaced as one unit. */
    if (uad && !isSafari) {
      var UD = Object.getPrototypeOf(uad);
      getter(UD, 'platform', uadPlatform);
      getter(UD, 'mobile', F.mobile);
      if (F.brands) liveGetter(UD, 'brands', function () { return hintCopy(F.brands); });
      var origHigh = UD.getHighEntropyValues;
      if (typeof origHigh === 'function') {
        method(UD, 'getHighEntropyValues', function getHighEntropyValues(hints) {
          return origHigh.call(this, hints).then(function (h) {
            try {
              if (F.brands) h.brands = hintCopy(F.brands);
              if (F.fullVersionList) h.fullVersionList = hintCopy(F.fullVersionList);
              if (F.uaFullVersion) h.uaFullVersion = F.uaFullVersion;
              h.platform = uadPlatform;
              h.mobile = F.mobile;
              if (F.platformVersion) h.platformVersion = F.platformVersion;
              if (F.architecture) h.architecture = F.architecture;
              if (F.bitness) h.bitness = F.bitness;
              h.model = F.mobile ? (F.model || '') : '';
              h.wow64 = !!F.wow64;
            } catch (e) {}
            return h;
          });
        }, 1);
      }
    }
    if (F.brand === 'Firefox') {
      /* Chromium-only surfaces would out the engine behind a Firefox UA. */
      try { delete window.chrome; } catch (e) {}
      try { delete NP.deviceMemory; } catch (e) {}
      try { delete NP.userAgentData; } catch (e) {}
    }
    /* Brave advertises an extra navigator.brave object. It is not part of a Chrome or Edge
       identity, so a profile that claims either brand must not leak it. */
    if (F.brand !== 'Brave' && !isSafari) {
      try { delete Navigator.prototype.brave; } catch (e) {}
      try { if (navigator.brave) Object.defineProperty(navigator, 'brave', { value: undefined, configurable: true }); } catch (e) {}
    }
    if (!isSafari) getter(NP, 'pdfViewerEnabled', true);
    if (isSafari) getter(NP, 'standalone', false);
  } catch (e) {}

  /* ---- Intl / timezone coherence (CDP sets the same value; this is the JS fallback) ---- */
  try {
    if (F.tz && typeof Intl !== 'undefined' && Intl.DateTimeFormat) {
      var origResolved = Intl.DateTimeFormat.prototype.resolvedOptions;
      method(Intl.DateTimeFormat.prototype, 'resolvedOptions', function resolvedOptions() {
        var o = origResolved.call(this);
        try { Object.defineProperty(o, 'timeZone', { value: F.tz, configurable: true, enumerable: true }); } catch (e) {}
        return o;
      }, 0);
    }
  } catch (e) {}

  /* ---- Network information ---- */
  try {
    var realConn = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
    var connProto = realConn ? Object.getPrototypeOf(realConn)
      : ((typeof NetworkInformation !== 'undefined' && NetworkInformation.prototype) ? NetworkInformation.prototype : null);
    if (connProto) {
      /* Chrome defines every field as an accessor on NetworkInformation.prototype. Putting
         them on the instance instead leaves own properties that a descriptor scan flags. */
      getter(connProto, 'effectiveType', '4g');
      getter(connProto, 'rtt', 40 + (SEED % 60));
      getter(connProto, 'downlink', Math.round((10 + (SEED % 40) / 10) * 10) / 10);
      getter(connProto, 'saveData', false);
      getter(connProto, 'type', 'wifi');
      getter(NP, 'connection', realConn || Object.create(connProto));
    }
  } catch (e) {}

  /* ---- Permissions, notifications and media devices ---- */
  try {
    var perms = navigator.permissions;
    if (perms) {
      var PP = Object.getPrototypeOf(perms);
      var origQuery = PP.query;
      if (typeof origQuery === 'function') {
        method(PP, 'query', function query(desc) {
          var name = '';
          try { name = String(desc && desc.name || ''); } catch (e) {}
          return origQuery.call(this, desc).then(function (st) {
            try {
              if (name === 'geolocation' && F.geo === 'block')
                Object.defineProperty(st, 'state', { value: 'denied', configurable: true });
            } catch (e) {}
            return st;
          });
        }, 1);
      }
    }
    if (typeof Notification !== 'undefined') getter(Notification, 'permission', 'default');
  } catch (e) {}

  /* Labels and stable ids are hidden until a permission is granted; normalising them removes
     a direct read of the host's audio hardware without changing the API's shape. */
  try {
    var md0 = navigator.mediaDevices;
    if (md0 && md0.enumerateDevices) {
      var origEnumAll = md0.enumerateDevices;
      method(md0, 'enumerateDevices', function enumerateDevices() {
        return origEnumAll.call(md0).then(function (list) {
          try {
            return Array.prototype.map.call(list, function (d) {
              try { Object.defineProperty(d, 'label', { value: '', configurable: true }); } catch (e) {}
              try { Object.defineProperty(d, 'deviceId', { value: '', configurable: true }); } catch (e) {}
              return d;
            });
          } catch (e) { return list; }
        });
      }, 0);
    }
  } catch (e) {}

  /* ---- Screen and viewport ---- */
  var SP = (typeof Screen !== 'undefined' && Screen.prototype) ? Screen.prototype : null;
  getter(SP, 'width', F.sw);
  getter(SP, 'height', F.sh);
  getter(SP, 'availWidth', F.aw);
  getter(SP, 'availHeight', F.ah);
  getter(SP, 'colorDepth', F.cd);
  getter(SP, 'pixelDepth', F.cd);
  getter(SP, 'isExtended', false);

  try {
    getter(SP, 'availLeft', 0);
    getter(SP, 'availTop', 0);
    var so = screen.orientation;
    if (so) {
      var SOP = Object.getPrototypeOf(so);
      getter(SOP, 'type', F.mobile ? 'portrait-primary' : 'landscape-primary');
      getter(SOP, 'angle', 0);
    }
  } catch (e) {}

  /* A DPR getter is half a fix: (min-resolution: Ndppx) media queries still answer with the
     host's real density, which contradicts window.devicePixelRatio. */
  function matchesResolution(query) {
    var m = String(query).match(/\(min-resolution:\s*([\d.]+)\s*(dppx|dpcm|dpi)\s*\)/);
    if (m) {
      var css = parseFloat(m[1]) * (m[2] === 'dpcm' ? 2.54 : 1) / (m[2] === 'dpi' ? 96 : 1);
      return F.dpr >= css;
    }
    m = String(query).match(/\(max-resolution:\s*([\d.]+)\s*(dppx|dpcm|dpi)\s*\)/);
    if (m) {
      var css2 = parseFloat(m[1]) * (m[2] === 'dpcm' ? 2.54 : 1) / (m[2] === 'dpi' ? 96 : 1);
      return F.dpr <= css2;
    }
    return null;
  }

  try {
    Object.defineProperty(window, 'devicePixelRatio', {
      get: mark(function () { return F.dpr; }, 'get devicePixelRatio', 0),
      enumerable: true, configurable: true
    });
    if (window.matchMedia) {
      var origMM = window.matchMedia;
      method(window, 'matchMedia', function matchMedia(query) {
        var wanted = matchesResolution(query);
        if (wanted === null) return origMM.call(window, query);
        var real = origMM.call(window, query);
        var out = Object.create(Object.getPrototypeOf(real));
        try {
          Object.defineProperty(out, 'media', { get: function () { return real.media; }, configurable: true, enumerable: true });
          Object.defineProperty(out, 'matches', { get: function () { return wanted; }, configurable: true, enumerable: true });
          ['addEventListener', 'addEventListener', 'removeEventListener', 'addListener', 'removeListener'].forEach(function (k) {
            if (typeof real[k] === 'function') {
              Object.defineProperty(out, k, {
                value: function () { return real[k].apply(real, arguments); }, configurable: true, writable: true
              });
            }
          });
        } catch (e) { return real; }
        return out;
      }, 1);
    }
    /* outerWidth/outerHeight have to exceed the viewport by exactly the browser chrome they
       wrap. Faking them as the full screen made outer - inner ~340px on a maximised window,
       which every bot check reads as a docked DevTools panel. The real gap is measured before
       the getter is replaced, kept as-is, and only clamped so a window can never report being
       larger than the screen the profile claims. */
    var realOuterW = 0, realOuterH = 0, chromeGapW = 16, chromeGapH = 140;
    try { realOuterW = window.outerWidth; } catch (e) {}
    try { realOuterH = window.outerHeight; } catch (e) {}
    try {
      if (realOuterW > 0 && window.innerWidth > 0) chromeGapW = Math.max(0, realOuterW - window.innerWidth);
      if (realOuterH > 0 && window.innerHeight > 0) chromeGapH = Math.max(0, realOuterH - window.innerHeight);
    } catch (e) {}
    /* A phone or tablet browser wraps nothing: Chrome on Android and Safari on iOS report
       outer == inner, so the desktop offsets would invent a window the device cannot have. */
    liveGetter(window, 'outerWidth', function () {
      var iw = 0;
      try { iw = window.innerWidth; } catch (e) {}
      if (F.mobile || F.touch > 0) return iw;
      var v = (iw > 0 ? iw : 1280) + chromeGapW;
      return Math.min(v, F.sw > 0 ? F.sw : v);
    });
    liveGetter(window, 'outerHeight', function () {
      var ih = 0;
      try { ih = window.innerHeight; } catch (e) {}
      if (F.mobile || F.touch > 0) return ih;
      var v = (ih > 0 ? ih : 800) + chromeGapH;
      return Math.min(v, F.ah > 0 ? F.ah : v);
    });
  } catch (e) {}

  /* ---- Layout width, or the scrollbar leak ---- */
  /* These three facts have to agree with each other and with the claimed platform:
       innerWidth - documentElement.clientWidth  == width of the OS scrollbar
       window.scrollbars.visible                 == whether that scrollbar is showing
       (scrollbar-gutter / scrollbar-width)      == whether the OS uses overlay bars
     On a Windows host a macOS profile reported 17 px of gutter and a visible classic
     scrollbar; a phone profile reported both, which no phone can do. The gutter is now
     emulated per device: 0 where the platform overlays (iOS, Android), 15 on macOS, 17 on
     Windows. Only the gutter is written - the content box keeps its real size, so pages are
     never asked to lay out inside an imaginary strip. */
  try {
    var SBW = (typeof F.sbw === 'number') ? F.sbw : 17;
    var overlayBars = (F.mobile || F.touch > 0) || SBW === 0;

    function layoutOverride(proto, prop, extra) {
      if (!proto) return;
      var desc = Object.getOwnPropertyDescriptor(proto, prop);
      if (!desc || !desc.get) return;
      var real = desc.get;
      Object.defineProperty(proto, prop, {
        configurable: true,
        enumerable: desc.enumerable,
        get: mark(function () {
          var v = 0;
          try { v = real.call(this); } catch (e) { return 0; }
          if (typeof v !== 'number' || v <= 0) return v;
          // Elements only report a gutter when they actually scroll; subtracting it from every
          // element would make every non-scrolling box too narrow by one scrollbar.
          var scrolls = false;
          try { scrolls = this.scrollHeight > (this.clientHeight || 0); } catch (e) { scrolls = false; }
          return Math.max(0, v - (scrolls ? SBW : 0));
        }, 'get ' + prop, 0)
      });
    }
    if (typeof Element !== 'undefined' && Element.prototype) {
      layoutOverride(Element.prototype, 'clientWidth');
    }

    /* window.innerWidth is authoritative for the page's layout width, so it keeps its value.
       What must change is the difference between it and the document's client width, which is
       how a script measures the scrollbar without asking for anything. */
    if (typeof Document !== 'undefined' && Document.prototype) {
      var cwDesc = Object.getOwnPropertyDescriptor(Element.prototype, 'clientWidth');
      if (cwDesc && cwDesc.get) {
        var realCw = cwDesc.get;
        Object.defineProperty(Document.prototype, 'clientWidth', {
          configurable: true,
          get: mark(function () {
            var v = 0;
            try { v = realCw.call(this.documentElement); } catch (e) { return 0; }
            if (typeof v !== 'number' || v <= 0) return v;
            var scrolls = false;
            try { scrolls = this.documentElement.scrollHeight > (this.documentElement.clientHeight || 0); } catch (e) { scrolls = false; }
            return Math.max(0, v - (scrolls ? SBW : 0));
          }, 'get clientWidth', 0)
        });
      }
    }

    /* CSS media queries answer the same question independently:
         (scrollbar-width: none)  - true when the platform overlays its scrollbars
         (scrollbar-width: thin)  - Windows 11 and anything themed thin */
    if (window.matchMedia) {
      var origScrollbarMM = window.matchMedia;
      method(window, 'matchMedia', function matchMediaScroll(query) {
        var q = String(query).toLowerCase();
        var wanted = null;
        if (q.indexOf('scrollbar-width') >= 0) {
          if (q.indexOf('none') >= 0) wanted = overlayBars;
          else if (q.indexOf('thin') >= 0) wanted = !overlayBars && SBW !== 17;
          else if (q.indexOf('auto') >= 0) wanted = !overlayBars && SBW === 17;
        }
        if (wanted === null) return origScrollbarMM.call(window, query);
        var real = origScrollbarMM.call(window, query);
        try {
          Object.defineProperty(real, 'matches', { configurable: true, get: function () { return wanted; } });
          Object.defineProperty(real, 'media', { configurable: true, get: function () { return String(query); } });
        } catch (e) { }
        return real;
      }, 1);
    }
  } catch (e) {}

  /* ---- WebGL ---- */
  try {
    var glParams = {};
    /* WebKit on iOS never ships WEBGL_debug_renderer_info, so the unmasked pair reads back as
       null there. Answering it under a Safari UA is a self-inflicted contradiction. */
    if (!isSafari) {
      glParams[37445] = F.glVendor;
      glParams[37446] = F.glRenderer;
    }
    /* VENDOR/RENDERER are the *masked* values every Chromium (and WebKit) build reports;
       only UNMASKED_VENDOR/RENDERER carry the ANGLE string. Answering the masked pair with
       the unmasked string is a contradiction that reads as a broken WebGL adapter - the
       'WebGL exception' most scanners print for a misconfigured spoof. */
    glParams[7936] = 'WebKit';
    glParams[7937] = 'WebKit WebGL';
    var glNames = ['WebGLRenderingContext', 'WebGL2RenderingContext'];
    for (var gi = 0; gi < glNames.length; gi++) {
      var G = window[glNames[gi]];
      if (!G || !G.prototype || !G.prototype.getParameter) continue;
      (function (proto) {
        var orig = proto.getParameter;
        method(proto, 'getParameter', function getParameter(pname) {
          try {
            if (isSafari && (pname === 37445 || pname === 37446)) return null;
            if (Object.prototype.hasOwnProperty.call(glParams, pname)) return glParams[pname];
          } catch (e) {}
          return orig.call(this, pname);
        }, 1);
        var origExt = proto.getExtension;
        if (isSafari && typeof origExt === 'function') {
          /* Answering the extension with an object and its constants with null is a
             contradiction of a different kind: WebKit simply does not hand it out. */
          method(proto, 'getExtension', function getExtension(name) {
            try { if (String(name).toLowerCase() === 'webgl_debug_renderer_info') return null; } catch (e) {}
            return origExt.call(this, name);
          }, 1);
        }
      })(G.prototype);
    }
  } catch (e) {}

  /* ---- Canvas / pixel noise ----
     The noise is addressed by absolute pixel coordinate, not by an index into whatever slice
     was read. A detector that hashes the full canvas, then a sub-rectangle of the same canvas,
     then the encode path, gets three views of one deterministic image; the old index-based
     version produced three different hashes and was reported as 'canvas tampering'.

     The perturbation is a *quantisation*, not an addition: each channel is snapped to the
     nearest value congruent to a per-coordinate target mod 3. Adding +-1 made the transform
     non-idempotent, so read -> putImageData -> toDataURL (exactly what a tampering check does)
     applied the delta twice and produced a different hash on the second pass. Snapping is
     idempotent: feeding an already-snapped pixel through again returns the same value, so the
     bitmap, the read path and both encode paths all agree forever. */
  var CH = (typeof HTMLCanvasElement !== 'undefined') ? HTMLCanvasElement.prototype : null;
  var C2 = (typeof CanvasRenderingContext2D !== 'undefined') ? CanvasRenderingContext2D.prototype : null;
  var OC = (typeof OffscreenCanvas !== 'undefined') ? OffscreenCanvas.prototype : null;
  var O2 = (typeof OffscreenCanvasRenderingContext2D !== 'undefined') ? OffscreenCanvasRenderingContext2D.prototype : null;
  var canvasKind = (typeof WeakMap === 'function') ? new WeakMap() : null;

  function pixelTarget(x, y, channel) {
    var h = (SEED ^ Math.imul(x + 0x9e37, 73856093) ^ Math.imul(y + 0x85eb, 19349663) ^ Math.imul(channel + 3, 83492791)) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177) | 0;
    return ((h >>> 0) % 3) | 0;
  }

  /* Snap v to the unique value in {v-2..v} that is congruent to target mod 3. Applying the
     same snap twice cannot change the result, which is what keeps every read path in sync. */
  function snap(v, x, y, channel) {
    var d = pixelTarget(x, y, channel);
    var q = v - (v % 3) + d;
    return q > 255 ? q - 3 : q;
  }

  function perturb(data, width, height, ox, oy) {
    try {
      if (!data || !data.length) return data;
      var w = width > 0 ? width : 1;
      var count = Math.floor(data.length / 4);
      for (var p = 0; p < count; p++) {
        if ((p & 7) !== 0) continue;
        var x = (p % w) + (ox | 0), y = ((p / w) | 0) + (oy | 0);
        var i = p * 4;
        data[i] = snap(data[i], x, y, 0);
        data[i + 1] = snap(data[i + 1], x, y, 1);
        data[i + 2] = snap(data[i + 2], x, y, 2);
      }
    } catch (e) {}
    return data;
  }

  var origData = C2 ? C2.getImageData : null;
  var origOffData = O2 ? O2.getImageData : null;
  var origURL = CH ? CH.toDataURL : null;
  var origBlob = CH ? CH.toBlob : null;
  var origGetCtx = CH ? CH.getContext : null;
  var origOffConv = OC ? OC.convertToBlob : null;
  var rpPairs = [];

  if (F.canvas && CH && C2) {
    try {
      /* Remember how each canvas was created. drawImage() of a WebGL canvas that was not opened
         with preserveDrawingBuffer yields a blank bitmap, so the encode path has to lift pixels
         with gl.readPixels instead. */
      method(CH, 'getContext', function getContext(type) {
        var ctx = origGetCtx.apply(this, arguments);
        try {
          var t = String(type || '').toLowerCase();
          if (ctx && canvasKind) canvasKind.set(this, t.indexOf('webgl2') >= 0 ? 'gl2' : (t.indexOf('webgl') >= 0 ? 'gl' : '2d'));
        } catch (e) {}
        return ctx;
      }, 1);

      function noisyEncode(self, encode) {
        try {
          var w = self.width, h = self.height;
          if (!w || !h || w * h > 20000000) return null;
          var tmp = document.createElement('canvas');
          tmp.width = w; tmp.height = h;
          var ctx = origGetCtx.call(tmp, '2d');
          if (!ctx) return null;
          var kind = canvasKind ? canvasKind.get(self) : null;
          if (kind === 'gl' || kind === 'gl2') {
            var gl = null;
            try { gl = origGetCtx.call(self, kind === 'gl2' ? 'webgl2' : 'webgl'); } catch (e) {}
            if (!gl) return null;
            var rp = null;
            for (var qi = 0; qi < rpPairs.length; qi++) {
              try { if (rpPairs[qi][0].isPrototypeOf(gl)) { rp = rpPairs[qi][1]; break; } } catch (e) {}
            }
            if (!rp) return null;
            var px = new Uint8Array(w * h * 4);
            rp.call(gl, 0, 0, w, h, gl.RGBA, gl.UNSIGNED_BYTE, px);
            var img = ctx.createImageData(w, h);
            img.data.set(px);
            perturb(img.data, w, h, 0, 0);
            ctx.putImageData(img, 0, 0);
          } else {
            ctx.drawImage(self, 0, 0);
            var raw = origData.call(ctx, 0, 0, w, h);
            perturb(raw.data, w, h, 0, 0);
            ctx.putImageData(raw, 0, 0);
          }
          return encode(tmp);
        } catch (e) { return null; }
      }

      method(C2, 'getImageData', function getImageData(sx, sy, sw, sh) {
        var img = origData.apply(this, arguments);
        try { perturb(img.data, img.width, img.height, sx, sy); } catch (e) {}
        return img;
      }, 4);

      method(CH, 'toDataURL', function toDataURL(type, quality) {
        var a = arguments, self = this;
        var out = noisyEncode(self, function (tmp) { return origURL.apply(tmp, a); });
        return out != null ? out : origURL.apply(self, a);
      }, 0);

      /* toBlob hands back nothing, so success has to be signalled by the encode callback itself -
         and it must call the captured native, not tmp.toBlob(), which would re-enter this method. */
      method(CH, 'toBlob', function toBlob(callback, type, quality) {
        var self = this;
        var done = noisyEncode(self, function (tmp) { origBlob.call(tmp, callback, type, quality); return true; });
        if (done !== true) origBlob.call(self, callback, type, quality);
      }, 1);

      /* readPixels is the other way to lift the same pixels; leaving it clean both leaks the
         real GPU hash and makes the two paths disagree. */
      var glNames2 = ['WebGLRenderingContext', 'WebGL2RenderingContext'];
      for (var ri = 0; ri < glNames2.length; ri++) {
        var RG = window[glNames2[ri]];
        if (!RG || !RG.prototype || !RG.prototype.readPixels) continue;
        (function (proto) {
          var origRP = proto.readPixels;
          rpPairs.push([proto, origRP]);
          method(proto, 'readPixels', function readPixels(x, y, width, height, format, type, pixels) {
            origRP.apply(this, arguments);
            try { if (pixels && pixels.length) perturb(pixels, width, height, x, y); } catch (e) {}
          }, 7);
        })(RG.prototype);
      }
    } catch (e) {}
  }

  /* OffscreenCanvas has its own 2d context prototype. If only the on-screen canvas is noised,
     the same drawing hashes differently through convertToBlob - a textbook tampering signal. */
  if (F.canvas && OC && O2 && origOffData) {
    try {
      method(O2, 'getImageData', function getImageData(sx, sy, sw, sh) {
        var img = origOffData.apply(this, arguments);
        try { perturb(img.data, img.width, img.height, sx, sy); } catch (e) {}
        return img;
      }, 4);
      if (origOffConv) {
        method(OC, 'convertToBlob', function convertToBlob(type, quality) {
          var self = this;
          try {
            var w = self.width, h = self.height;
            if (!w || !h || w * h > 20000000) return origOffConv.apply(self, arguments);
            var tmp = new OffscreenCanvas(w, h);
            var ctx = tmp.getContext('2d');
            if (!ctx) return origOffConv.apply(self, arguments);
            ctx.drawImage(self, 0, 0);
            var raw = origOffData.call(ctx, 0, 0, w, h);
            perturb(raw.data, w, h, 0, 0);
            ctx.putImageData(raw, 0, 0);
            return origOffConv.apply(tmp, arguments);
          } catch (e) { return origOffConv.apply(self, arguments); }
        }, 1);
      }
    } catch (e) {}
  }

  /* ---- Audio ---- */
  if (F.audio) {
    try {
      if (typeof AudioBuffer !== 'undefined') {
        var origChannel = AudioBuffer.prototype.getChannelData;
        method(AudioBuffer.prototype, 'getChannelData', function getChannelData(index) {
          var arr = origChannel.apply(this, arguments);
          try { for (var i = 0; i < arr.length; i += 1000) arr[i] += (noise(i) - 0.5) * 1e-7; } catch (e) {}
          return arr;
        }, 1);
      }
      if (typeof AnalyserNode !== 'undefined') {
        var origFreq = AnalyserNode.prototype.getFloatFrequencyData;
        method(AnalyserNode.prototype, 'getFloatFrequencyData', function getFloatFrequencyData(array) {
          origFreq.apply(this, arguments);
          try { for (var i = 0; i < array.length; i += 100) array[i] += (noise(i) - 0.5) * 1e-4; } catch (e) {}
        }, 1);
      }
      if (typeof AudioContext !== 'undefined') {
        var BAP = Object.getPrototypeOf(AudioContext.prototype);
        /* A real device runs at 44.1 or 48 kHz; the host's exact rate is a stable hardware id. */
        if (BAP) getter(BAP, 'sampleRate', 48000);
      }
    } catch (e) {}
  }

  /* ---- Speech synthesis ---- */
  try {
    if (typeof speechSynthesis !== 'undefined') {
      var base0 = String(F.language || 'en-US').split('-')[0];
      var table = {
        en: [['Microsoft David - English (United States)', 'en-US', true], ['Microsoft Zira - English (United States)', 'en-US', true], ['Google US English', 'en-US', false]],
        de: [['Microsoft Katja - German (Germany)', 'de-DE', true], ['Microsoft Stefan - German (Germany)', 'de-DE', true], ['Google Deutsch', 'de-DE', false]],
        fr: [['Microsoft Hortense - French (France)', 'fr-FR', true], ['Microsoft Paul - French (France)', 'fr-FR', true], ['Google français', 'fr-FR', false]],
        es: [['Microsoft Helena - Spanish (Spain)', 'es-ES', true], ['Microsoft Pablo - Spanish (Spain)', 'es-ES', true], ['Google español', 'es-ES', false]],
        pt: [['Microsoft Maria - Portuguese (Brazil)', 'pt-BR', true], ['Microsoft Daniel - Portuguese (Brazil)', 'pt-BR', true], ['Google português do Brasil', 'pt-BR', false]],
        it: [['Microsoft Elsa - Italian (Italy)', 'it-IT', true], ['Microsoft Cosimo - Italian (Italy)', 'it-IT', true], ['Google italiano', 'it-IT', false]],
        nl: [['Microsoft Colette - Dutch (Netherlands)', 'nl-NL', true], ['Microsoft Maarten - Dutch (Netherlands)', 'nl-NL', true]],
        pl: [['Microsoft Paulina - Polish (Poland)', 'pl-PL', true], ['Microsoft Marek - Polish (Poland)', 'pl-PL', true]],
        sv: [['Microsoft Hedvig - Swedish (Sweden)', 'sv-SE', true], ['Microsoft Bengt - Swedish (Sweden)', 'sv-SE', true]],
        da: [['Microsoft Helle - Danish (Denmark)', 'da-DK', true], ['Microsoft Mads - Danish (Denmark)', 'da-DK', true]],
        nb: [['Microsoft Hedda - Norwegian (Norway)', 'nb-NO', true], ['Microsoft Magnus - Norwegian (Norway)', 'nb-NO', true]],
        fi: [['Microsoft Heidi - Finnish (Finland)', 'fi-FI', true], ['Microsoft Mikko - Finnish (Finland)', 'fi-FI', true]],
        tr: [['Microsoft Tolga - Turkish (Turkey)', 'tr-TR', true], ['Microsoft Filiz - Turkish (Turkey)', 'tr-TR', true]],
        ru: [['Microsoft Irina - Russian (Russia)', 'ru-RU', true], ['Microsoft Pavel - Russian (Russia)', 'ru-RU', true]],
        uk: [['Microsoft Polina - Ukrainian (Ukraine)', 'uk-UA', true], ['Microsoft Ostap - Ukrainian (Ukraine)', 'uk-UA', true]],
        ar: [['Microsoft Hoda - Arabic (Saudi Arabia)', 'ar-SA', true], ['Microsoft Naayf - Arabic (Saudi Arabia)', 'ar-SA', true]],
        he: [['Microsoft Carmit - Hebrew (Israel)', 'he-IL', true], ['Microsoft Asaf - Hebrew (Israel)', 'he-IL', true]],
        hi: [['Microsoft Kalpana - Hindi (India)', 'hi-IN', true], ['Microsoft Hemant - Hindi (India)', 'hi-IN', true]],
        ja: [['Microsoft Ayumi - Japanese (Japan)', 'ja-JP', true], ['Microsoft Ichiro - Japanese (Japan)', 'ja-JP', true], ['Google 日本語', 'ja-JP', false]],
        ko: [['Microsoft Heami - Korean (Korea)', 'ko-KR', true], ['Microsoft Injoon - Korean (Korea)', 'ko-KR', true]],
        zh: [['Microsoft Huihui - Chinese (Simplified, PRC)', 'zh-CN', true], ['Microsoft Kangkang - Chinese (Simplified, PRC)', 'zh-CN', true]],
        id: [['Microsoft Andika - Indonesian (Indonesia)', 'id-ID', true], ['Microsoft Gadis - Indonesian (Indonesia)', 'id-ID', true]],
        th: [['Microsoft Pattara - Thai (Thailand)', 'th-TH', true], ['Microsoft Somsak - Thai (Thailand)', 'th-TH', true]],
        vi: [['Microsoft Linh - Vietnamese (Vietnam)', 'vi-VN', true], ['Microsoft An - Vietnamese (Vietnam)', 'vi-VN', true]],
        ms: [['Microsoft Siti - Malay (Malaysia)', 'ms-MY', true]]
      };
      var rows = table[base0] || table.en;
      var V = window.SpeechSynthesisVoice;
      var list = [];
      for (var vi = 0; vi < rows.length; vi++) {
        /* Native voices expose name/lang/default through the interface prototype, so the
           values live on a per-voice prototype instead of as own properties - an own-property
           scan of a voice object is otherwise a one-line tell. */
        var vproto = V ? Object.create(V.prototype) : {};
        getter(vproto, 'name', rows[vi][0]);
        getter(vproto, 'voiceURI', rows[vi][0]);
        getter(vproto, 'lang', rows[vi][1] || F.language);
        getter(vproto, 'localService', rows[vi][2]);
        getter(vproto, 'default', vi === 0);
        list.push(Object.create(vproto));
      }
      /* getVoices belongs to SpeechSynthesis.prototype; defining it on the instance leaves an
         own property that no real browser has. */
      var SS = Object.getPrototypeOf(speechSynthesis);
      method(SS || speechSynthesis, 'getVoices', function getVoices() { return list.slice(); }, 0);
    }
  } catch (e) {}

  /* ---- Local font list ----
     The Local Font Access API is the one place a page can read the real installed families.
     Answering with the set the profile claims keeps a Windows target on Windows fonts and a
     Mac target on Mac fonts; the metric jitter below covers the older width-probing method. */
  try {
    var fontSets = {
      windows: ['Arial','Calibri','Cambria','Candara','Comic Sans MS','Consolas','Constantia','Corbel',
        'Courier New','Ebrima','Franklin Gothic Medium','Gabriola','Gadugi','Georgia','Impact',
        'Leelawadee UI','Lucida Console','Malgun Gothic','Microsoft Himalaya','Microsoft JhengHei',
        'Microsoft Sans Serif','Microsoft Tai Le','Microsoft YaHei','MingLiU-ExtB','MS Gothic','MV Boli',
        'Myanmar Text','Nirmala UI','Palatino Linotype','Segoe MDL2 Assets','Segoe Print','Segoe Script',
        'Segoe UI','Segoe UI Emoji','Segoe UI Historic','Segoe UI Symbol','SimSun','Sitka','Sylfaen',
        'Symbol','Tahoma','Times New Roman','Trebuchet MS','Verdana','Webdings','Wingdings','Yu Gothic'],
      macos: ['American Typewriter','Andale Mono','Apple Chancery','Apple Color Emoji','Arial','Arial Black',
        'Arial Hebrew','Arial Rounded MT Bold','Avenir','Avenir Next','Baskerville','Big Caslon','Bodoni 72',
        'Bradley Hand','Brush Script MT','Chalkboard','Chalkduster','Cochin','Comic Sans MS','Copperplate',
        'Courier New','Didot','DIN Alternate','Futura','Geneva','Georgia','Gill Sans','Helvetica',
        'Helvetica Neue','Herculanum','Hoefler Text','Impact','Lucida Grande','Luminari','Marker Felt',
        'Menlo','Monaco','Noteworthy','Optima','Palatino','Papyrus','Phosphate','Rockwell','Savoye LET',
        'SignPainter','Skia','Snell Roundhand','Tahoma','Times New Roman','Trattatello','Trebuchet MS',
        'Verdana','Zapf Dingbats'],
      linux: ['Arial','Bitstream Charter','Cantarell','Century Schoolbook L','Comic Sans MS','Courier New',
        'DejaVu Sans','DejaVu Sans Mono','DejaVu Serif','Droid Sans','FreeMono','FreeSans','FreeSerif',
        'Georgia','Impact','Liberation Mono','Liberation Sans','Liberation Serif','Nimbus Mono PS',
        'Nimbus Roman','Nimbus Sans','Noto Color Emoji','Noto Sans','Noto Serif','Times New Roman',
        'Trebuchet MS','Ubuntu','Ubuntu Mono','Verdana'],
      minimal: ['Arial','Courier New','Georgia','Times New Roman','Trebuchet MS','Verdana']
    };
    var wantSet = String(F.fontSet || 'auto').toLowerCase();
    if (wantSet === 'auto') {
      var osName = String(F.os || '');
      wantSet = /mac/i.test(osName) ? 'macos' : (/linux/i.test(osName) ? 'linux' : 'windows');
    }
    var fams = fontSets[wantSet] || fontSets.windows;
    var fontData = [];
    for (var fi = 0; fi < fams.length; fi++) {
      fontData.push({
        family: fams[fi], fullName: fams[fi], postscriptName: fams[fi].replace(/ /g, ''),
        style: 'Regular', weight: '400', blob: function () { return Promise.resolve(new Blob([])); }
      });
    }
    var queryFonts = function queryLocalFonts() { return Promise.resolve(fontData.slice()); };
    if (window.queryLocalFonts) method(window, 'queryLocalFonts', queryFonts, 0);
    if (navigator.fonts) method(navigator.fonts, 'query', queryFonts, 0);
  } catch (e) {}

  /* ---- Font metrics ---- */
  if (F.fonts && C2) {    try {
      var origMeasure = C2.measureText;
      method(C2, 'measureText', function measureText(text) {
        var m = origMeasure.call(this, text);
        try {
          var s = String(text || '');
          var h = 0;
          for (var k = 0; k < s.length; k++) h = (h * 31 + s.charCodeAt(k)) | 0;
          /* A sub-pixel per-profile wobble is enough to make the font fingerprint unique
             without changing text layout (the previous 1.2% scale visibly reflowed pages,
             which is both broken and a stronger tampering signal than the noise it avoided). */
          var jitter = (noise(h) - 0.5) * 0.06;
          Object.defineProperty(m, 'width', { value: Math.max(0, m.width + jitter), configurable: true });
        } catch (e) {}
        return m;
      }, 1);
    } catch (e) {}
  }

  /* ---- Hardware noise ----
     Read-back of the framebuffer is the other half of a GPU fingerprint: two machines with the
     same renderer string still differ there. Applied only when the profile asks for it, through
     the same deterministic snap the canvas path uses so repeated reads agree with each other. */
  if (F.hwNoise && !F.canvas) {
    try {
      var hwNames = ['WebGLRenderingContext', 'WebGL2RenderingContext'];
      for (var hi = 0; hi < hwNames.length; hi++) {
        var HG = window[hwNames[hi]];
        if (!HG || !HG.prototype || !HG.prototype.readPixels) continue;
        (function (proto) {
          var origHw = proto.readPixels;
          method(proto, 'readPixels', function readPixels(x, y, width, height, format, type, pixels) {
            origHw.apply(this, arguments);
            try { if (pixels && pixels.length) perturb(pixels, width, height, x, y); } catch (e) {}
          }, 7);
        })(HG.prototype);
      }
    } catch (e) {}
  }

  /* ---- Battery ---- */
  if (F.battery) {
    try {
      var level = Math.min(1, 0.35 + (SEED % 60) / 100);
      var bProto = (typeof BatteryManager !== 'undefined' && BatteryManager.prototype)
        ? BatteryManager.prototype : Object.prototype;
      var fake = Object.create(bProto);
      getter(fake, 'level', level);
      /* A desktop is plugged in; reporting discharging on a claimed Windows rig is a tell. */
      getter(fake, 'charging', !F.mobile);
      getter(fake, 'chargingTime', F.mobile ? Infinity : 1800);
      getter(fake, 'dischargingTime', Infinity);
      method(fake, 'addEventListener', function addEventListener(type, listener) { }, 2);
      method(NP, 'getBattery', function getBattery() { return Promise.resolve(fake); }, 0);
    } catch (e) {}
  }

  /* ---- WebRTC ----
     The command-line policy only covers Chrome's own ICE path; a page can still create a
     peer connection and read candidates out of the SDP or the icecandidate event. Strict
     modes (off / proxy) therefore keep the API present but answer with no usable candidate,
     which is what a machine behind a corporate proxy looks like. Throwing from the
     constructor or deleting the object is louder than the leak it was meant to stop. */
  try {
    var webrtcMode = String(F.webrtc || 'off').toLowerCase();
    var strictRtc = webrtcMode !== 'public' && webrtcMode !== 'system';
    var RealPC = window.RTCPeerConnection || window.webkitRTCPeerConnection;
    var mediaMode = webrtcMode === 'off' || webrtcMode === 'disabled';
    if (RealPC && RealPC.prototype) {
      var proto = RealPC.prototype;

      function cleanSdp(sdp) {
        try {
          var lines = String(sdp || '').split('\r\n');
          var out = [];
          for (var i = 0; i < lines.length; i++) {
            var line = lines[i];
            if (line.indexOf('a=candidate:') === 0) {
              if (strictRtc) continue;
              var host = / typ host/.test(line);
              var privateAddr = /(\.local| 10\.| 192\.168\.| 172\.(1[6-9]|2[0-9]|3[01])\.| 169\.254\.| 127\.| ::1| fe80:| fd[0-9a-f]{2}:)/i.test(line);
              if (host || privateAddr) continue;
            }
            if (strictRtc && line.indexOf('a=end-of-candidates') === 0) continue;
            out.push(line);
          }
          return out.join('\r\n');
        } catch (e) { return sdp; }
      }
      function cleanDesc(desc) {
        try {
          if (desc && typeof desc === 'object' && typeof desc.sdp === 'string')
            return { type: desc.type, sdp: cleanSdp(desc.sdp) };
          if (typeof desc === 'string') return cleanSdp(desc);
        } catch (e) {}
        return desc;
      }

      var origSetLocal = proto.setLocalDescription;
      if (typeof origSetLocal === 'function') {
        method(proto, 'setLocalDescription', function setLocalDescription(desc) {
          var a = Array.prototype.slice.call(arguments);
          a[0] = cleanDesc(a[0]);
          return origSetLocal.apply(this, a);
        }, 1);
      }
      ['createOffer', 'createAnswer'].forEach(function (name) {
        var orig = proto[name];
        if (typeof orig !== 'function') return;
        method(proto, name, function () {
          var self = this, args = arguments;
          return orig.apply(self, args).then(function (d) { return cleanDesc(d); });
        }, name === 'createOffer' ? 1 : 0);
      });

      /* Candidate events are the live path; wrap the event handler and listeners on the
         prototype so no per-instance own property gives the patch away. */
      var addOrig = proto.addEventListener;
      if (typeof addOrig === 'function') {
        method(proto, 'addEventListener', function addEventListener(type, fn, opts) {
          if (String(type) === 'icecandidate' && typeof fn === 'function' && strictRtc) {
            return addOrig.call(this, type, function (ev) {
              try { if (ev && ev.candidate) return; } catch (e) {}
              return fn.apply(this, arguments);
            }, opts);
          }
          return addOrig.apply(this, arguments);
        }, 2);
      }
      var od = null;
      try { od = Object.getOwnPropertyDescriptor(proto, 'onicecandidate'); } catch (e) {}
      if (od && (od.set || od.get)) {
        try {
          Object.defineProperty(proto, 'onicecandidate', {
            configurable: true, enumerable: od.enumerable,
            get: function () { try { return od.get ? od.get.call(this) : undefined; } catch (e) { return undefined; } },
            set: function (fn) {
              try {
                if (typeof fn !== 'function' || !strictRtc) { if (od.set) od.set.call(this, fn); return; }
                var self = this;
                od.set.call(this, function (ev) {
                  if (ev && ev.candidate) return;
                  return fn.call(self, ev);
                });
              } catch (e) {}
            }
          });
        } catch (e) {}
      }
    }

    if (mediaMode) {
      if (typeof MediaStream !== 'undefined' && MediaStream.prototype) {
        method(MediaStream.prototype, 'getTracks', function getTracks() { return []; }, 0);
        method(MediaStream.prototype, 'getVideoTracks', function getVideoTracks() { return []; }, 0);
        method(MediaStream.prototype, 'getAudioTracks', function getAudioTracks() { return []; }, 0);
      }
      var md = navigator.mediaDevices;
      if (md) {
        method(md, 'getUserMedia', function getUserMedia() {
          return Promise.reject(new DOMException('Permission denied', 'NotAllowedError'));
        }, 1);
        if (md.enumerateDevices) {
          var origEnum = md.enumerateDevices;
          method(md, 'enumerateDevices', function enumerateDevices() {
            return origEnum.call(md).then(function (list) {
              try {
                return list.map(function (d) {
                  try { Object.defineProperty(d, 'deviceId', { value: '', configurable: true }); } catch (e) {}
                  try { Object.defineProperty(d, 'label', { value: '', configurable: true }); } catch (e) {}
                  return d;
                });
              } catch (e) { return list; }
            });
          }, 0);
        }
      }
    }
  } catch (e) {}
})();
";

        /// <summary>
        /// Worker-realm patch, prepended to every JavaScript Blob by the page script. Workers
        /// have no window, no document and no HTMLCanvasElement, so only the surfaces a worker
        /// can actually read are covered: navigator, Intl timezone, OffscreenCanvas and WebGL.
        /// </summary>
        private const string WorkerTemplate = @"
(function () {
  'use strict';
  var F = __CFG__;
  var NP = (typeof WorkerNavigator !== 'undefined' && WorkerNavigator.prototype)
    ? WorkerNavigator.prototype : Object.getPrototypeOf(navigator);
  var G = Symbol.for('jb.realm');
  try { if (NP[G]) return; Object.defineProperty(NP, G, { value: 1, configurable: true }); } catch (e) {}

  function named(fn, n) {
    try { Object.defineProperty(fn, 'name', { value: n, configurable: true }); } catch (e) {}
    return fn;
  }
  function get(o, p, v) {
    if (!o) return;
    try {
      Object.defineProperty(o, p, { get: named(function () { return v; }, 'get ' + p), set: undefined, enumerable: true, configurable: true });
    } catch (e) {}
  }
  function method(o, p, fn, len) {
    try {
      Object.defineProperty(o, p, { value: named(fn, p), writable: true, enumerable: false, configurable: true });
      if (len != null) Object.defineProperty(fn, 'length', { value: len, configurable: true });
    } catch (e) {}
  }

  get(NP, 'userAgent', F.ua);
  get(NP, 'appVersion', F.ua.replace('Mozilla/', ''));
  get(NP, 'platform', F.platform);
  get(NP, 'language', F.language);
  try {
    Object.defineProperty(NP, 'languages', {
      get: named(function () { return F.languages.slice(); }, 'get languages'), enumerable: true, configurable: true
    });
  } catch (e) {}
  get(NP, 'hardwareConcurrency', F.cores);
  if (!F.safari) get(NP, 'deviceMemory', F.memory);
  get(NP, 'maxTouchPoints', F.touch);
  get(NP, 'webdriver', false);

  try {
    if (F.tz && typeof Intl !== 'undefined' && Intl.DateTimeFormat) {
      var ro = Intl.DateTimeFormat.prototype.resolvedOptions;
      method(Intl.DateTimeFormat.prototype, 'resolvedOptions', function resolvedOptions() {
        var o = ro.call(this);
        try { Object.defineProperty(o, 'timeZone', { value: F.tz, configurable: true, enumerable: true }); } catch (e) {}
        return o;
      }, 0);
    }
  } catch (e) {}

  /* Same idempotent quantisation as the page, keyed by absolute pixel coordinate, so a worker
     and the page produce one hash for one drawing instead of two. */
  var SEED = (F.seed >>> 0) || 1;
  function target(x, y, ch) {
    var h = (SEED ^ Math.imul(x + 0x9e37, 73856093) ^ Math.imul(y + 0x85eb, 19349663) ^ Math.imul(ch + 3, 83492791)) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177) | 0;
    return ((h >>> 0) % 3) | 0;
  }
  function snap(v, x, y, ch) {
    var q = v - (v % 3) + target(x, y, ch);
    return q > 255 ? q - 3 : q;
  }
  function perturb(data, w, ox, oy) {
    try {
      if (!data || !data.length) return data;
      w = w > 0 ? w : 1;
      var n = Math.floor(data.length / 4);
      for (var p = 0; p < n; p++) {
        if ((p & 7) !== 0) continue;
        var x = (p % w) + (ox | 0), y = ((p / w) | 0) + (oy | 0), i = p * 4;
        data[i] = snap(data[i], x, y, 0);
        data[i + 1] = snap(data[i + 1], x, y, 1);
        data[i + 2] = snap(data[i + 2], x, y, 2);
      }
    } catch (e) {}
    return data;
  }

  if (F.canvas && typeof OffscreenCanvas !== 'undefined') {
    try {
      var O2 = (typeof OffscreenCanvasRenderingContext2D !== 'undefined') ? OffscreenCanvasRenderingContext2D.prototype : null;
      var gid = O2 && O2.getImageData ? O2.getImageData : null;
      if (gid) {
        method(O2, 'getImageData', function getImageData(sx, sy, sw, sh) {
          var img = gid.apply(this, arguments);
          try { perturb(img.data, img.width, sx, sy); } catch (e) {}
          return img;
        }, 4);
      }
      var OC = OffscreenCanvas.prototype;
      if (gid && OC && OC.convertToBlob) {
        var conv = OC.convertToBlob;
        method(OC, 'convertToBlob', function convertToBlob(type, quality) {
          var self = this;
          try {
            var w = self.width, h = self.height;
            if (!w || !h || w * h > 20000000) return conv.apply(self, arguments);
            var tmp = new OffscreenCanvas(w, h);
            var ctx = tmp.getContext('2d');
            if (!ctx) return conv.apply(self, arguments);
            ctx.drawImage(self, 0, 0);
            var raw = gid.call(ctx, 0, 0, w, h);
            perturb(raw.data, w, 0, 0);
            ctx.putImageData(raw, 0, 0);
            return conv.apply(tmp, arguments);
          } catch (e) { return conv.apply(self, arguments); }
        }, 1);
      }
    } catch (e) {}
  }

  try {
    var names = ['WebGLRenderingContext', 'WebGL2RenderingContext'];
    for (var i = 0; i < names.length; i++) {
      var W = self[names[i]];
      if (!W || !W.prototype || !W.prototype.getParameter) continue;
      (function (proto) {
        var orig = proto.getParameter;
        method(proto, 'getParameter', function getParameter(pname) {
          try {
            if (F.safari && (pname === 37445 || pname === 37446)) return null;
            if (pname === 37445) return F.glVendor;
            if (pname === 37446) return F.glRenderer;
            if (pname === 7936) return 'WebKit';
            if (pname === 7937) return 'WebKit WebGL';
          } catch (e) {}
          return orig.call(this, pname);
        }, 1);
        if (F.canvas && typeof proto.readPixels === 'function') {
          var rp = proto.readPixels;
          method(proto, 'readPixels', function readPixels(x, y, width, height, format, type, pixels) {
            rp.apply(this, arguments);
            try { if (pixels && pixels.length) perturb(pixels, width, x, y); } catch (e) {}
          }, 7);
        }
      })(W.prototype);
    }
  } catch (e) {}
})();
";
    }
}
