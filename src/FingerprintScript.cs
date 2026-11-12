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
            c["brand"] = fp.Brand ?? "";
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

  /* Google's sign-in and sign-up risk engine scores canvas/audio tampering on its own pages
     and fails the login with this browser may not be secure. The fingerprint noise stays
     on everywhere else; on Google's own auth domains it steps aside so accounts.google.com
     behaves exactly like the real browser underneath. */
  try {
    var __h = (location && location.hostname) ? location.hostname : '';
    if (/(^|\.)accounts\.google\.com$|(^|\.)accounts\.youtube\.com$/.test(__h)) {
      F.canvas = false; F.audio = false; F.webgl = false; F.hwNoise = false;
    }
  } catch (e) {}
  /* The realm guard: a second registration of this script (a stamped worker, a replayed
     injection) must not double-wrap every function. It used to reference GUARD before
     declaring it, which threw, was swallowed, and left the realm unmarked. */
  var GUARD = Symbol.for('jb.realm');
  var NP = (typeof Navigator !== 'undefined' && Navigator.prototype) ? Navigator.prototype : Object.getPrototypeOf(navigator);
  try { if (NP[GUARD]) return; Object.defineProperty(NP, GUARD, { value: 1, configurable: true }); } catch (e) {}

  var origToString = Function.prototype.toString;
  var fakes = (typeof WeakMap === 'function') ? new WeakMap() : null;

  /* A patched native has to look native in three places at once: toString(), the arity reported
     by .length, and the parameter list toString() renders. V8 prints an accessor as
     'function get x() { [native code] }', so the replacement is built from the real function's
     own parameter names and that shape - inventing a marker string of ours would simply be a
     signature a detector could match against.

     The parameter list comes from the function being REPLACED, not from the replacement. A
     WebIDL method prints without its argument names - a real Chrome answers
     'function getParameter() { [native code] }' - while the replacement is written in
     JavaScript and carries them: 'function getParameter(pname) { [native code] }'. The
     difference between the two is one line of text, and it is exactly what a scanner
     compares: it is the 'WebGL exception' and the 'Canvas Tampering' verdict, both of them
     raised against a browser that was never tampered with. */
  var FN_SRC = /^\s*(?:async\s+)?function[^(]*\(([^)]*)\)/;

  function mark(fn, name, len, native, params) {
    try {
      if (fakes) {
        var ps = null;
        try {
          /* The native it replaces is the reference. Its own source is the shape the
             replacement has to reproduce. A method this script already replaced is its own
             reference: the recorded shape is the native one, while its real source is
             JavaScript again - so the recorded shape is read first. */
          var src = null;
          if (native && fakes && fakes.has(native)) src = fakes.get(native);
          else src = native ? origToString.call(native) : origToString.call(fn);
          var m = FN_SRC.exec(src);
          if (m) ps = m[1];
        } catch (e) {}
        if (params != null) ps = params;
        fakes.set(fn, 'function ' + name + '(' + (ps == null ? '' : ps) + ') { [native code] }');
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
      }, 'Blob', 2, OrigBlob);
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
      /* The native being replaced is the reference for the printed signature, and its
         descriptor is the reference for the shape of the property. A WebIDL operation on a
         prototype is enumerable, and a replacement that is not is visible to anything that
         walks the prototype - which is how a scanner sees a canvas method that has been
         replaced rather than a canvas that works. */
      var native = null, desc = null;
      try {
        desc = Object.getOwnPropertyDescriptor(obj, prop);
        var cur = obj[prop];
        if (typeof cur === 'function') native = cur;
      } catch (e) {}
      Object.defineProperty(obj, prop, {
        value: mark(fn, prop, len, native),
        writable: desc ? desc.writable !== false : true,
        enumerable: desc ? desc.enumerable === true : true,
        configurable: true
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

  /* ---- automation leftovers a bot checker reads first ---- */
  /* Every one of these exists only when a browser is driven by a framework. They are cheap to
     look for, so a page looks: window.webdriver, the document's own webdriver flag, Playwright's
     bindings, the __driver_evaluate family, and the Chromium devtools marker. A profile must not
     carry any of them, and deleting is done through the prototype where the value lives so the
     deletion itself does not leave a fresh own-property behind. */
  try { delete Document.prototype.$cdc_asdjflasutopfhvcZLmcfl_; } catch (e) {}
  try { delete Document.prototype.$wdc_; } catch (e) {}
  try { delete window.__webdriver_evaluate; } catch (e) {}
  try { delete window.__selenium_evaluate; } catch (e) {}
  try { delete window.__webdriver_script_fn; } catch (e) {}
  try { delete window.__driver_evaluate; } catch (e) {}
  try { delete window.__lastWatirAlert; } catch (e) {}
  try { delete window.__playwright; } catch (e) {}
  try { delete window.__pw_manual; } catch (e) {}
  try { delete window.__puppeteer_evaluation_script__; } catch (e) {}
  try { delete window.callPhantom; } catch (e) {}
  try { delete window._phantom; } catch (e) {}
  /* Only a Node-style Buffer is a leftover. A page that bundles one onto window owns it, and
     removing that would break the page rather than hide anything. */
  try {
    if (window.Buffer && typeof window.Buffer.from === 'function' && typeof window.Buffer.alloc === 'function')
      delete window.Buffer;
  } catch (e) {}

  /* A devtools session that is attached makes Chromium print a warning line at the top of the
     console, and it also makes this symbol live. Running browsers here are never under devtools,
     so the symbol is removed when it is there at all. */
  try {
    if (window.devtools) { try { delete window.devtools; } catch (e) { window.devtools = undefined; } }
  } catch (e) {}

  /* Headless builds leave their own fingerprints in the WebGL context list and in the amount of
     GPU information they report. The renderer and vendor are already overridden by the profile;
     what is added here is the SwiftShader marker that appears when a headless build falls back to
     software rendering, and the empty extension list that only a headless context returns. */
  try {
    var __jbBad = /swiftshader|llvmpipe|software|mesa offscreen|angle \(google, vulkan/i;
    var __jbRE = WebGLRenderingContext.prototype.getExtension;
    WebGLRenderingContext.prototype.getExtension = mark(function getExtension(name) {
      var out = __jbRE.apply(this, arguments);
      /* A headless context answers getExtension for the debug renderer info with a software
         adapter even when getParameter has already been overridden. Reporting nothing is what a
         machine without the extension does, and it matches the hardware renderer the profile
         claims. */
      if (out && name && /WEBGL_debug_renderer_info/i.test(String(name))) {
        try {
          var vendor = this.getParameter(out.UNMASKED_VENDOR_WEBGL);
          if (vendor && __jbBad.test(String(vendor))) return null;
        } catch (e) {}
      }
      return out;
    }, 'getExtension', 1, __jbRE);
  } catch (e) {}

  function emptyList(tag) {
    /* Built on the real interface prototype, not on Array: a phone still answers
       navigator.plugins instanceof PluginArray === true, and an Array fails that check
       while also reporting '[object Array]' to Object.prototype.toString. */
    var proto = null;
    try { proto = tag === 'MimeTypeArray' ? MimeTypeArray.prototype : PluginArray.prototype; } catch (e) {}
    var a = proto ? Object.create(proto) : [];
    try {
      Object.defineProperty(a, 'length', { value: 0, configurable: true });
      /* The collection methods are native WebIDL operations and print without their
         argument names, so the replacements have to print that way too - and they are
         enumerable on a real collection, or a walk of the prototype finds them missing. */
      Object.defineProperty(a, 'item', { value: mark(function item(index) { return null; }, 'item', 1, null, ''), writable: true, enumerable: true, configurable: true });
      Object.defineProperty(a, 'namedItem', { value: mark(function namedItem(name) { return null; }, 'namedItem', 1, null, ''), writable: true, enumerable: true, configurable: true });
      if (tag === 'PluginArray')
        Object.defineProperty(a, 'refresh', { value: mark(function refresh() { }, 'refresh', 0, null, ''), writable: true, enumerable: true, configurable: true });
    } catch (e) {}
    return a;
  }

  /* ---- window.chrome ----
     A Chromium browser always exposes this object, and its absence is one of the oldest bot
     checks in existence: a page that finds navigator.userAgent saying Chrome and no
     window.chrome has found automation, not a browser. Some of the ways this app drives a
     profile - the CDP session, an isolated world, an extension loaded late - can leave it
     missing, so it is put back rather than hoped for, with the shape a real Chrome has even
     when no extension is installed. Chrome's own object has no runtime.id in that case, and
     inventing one would be the new tell. */
  try {
    if (!window.chrome) {
      var chromeStub = {};
      chromeStub.app = {
        isInstalled: false,
        InstallState: { DISABLED: 'disabled', INSTALLED: 'installed', NOT_INSTALLED: 'not_installed' },
        RunningState: { CANNOT_RUN: 'cannot_run', READY_TO_RUN: 'ready_to_run', RUNNING: 'running' }
      };
      chromeStub.runtime = {
        OnInstalledReason: {
          CHROME_UPDATE: 'chrome_update', INSTALL: 'install',
          SHARED_MODULE_UPDATE: 'shared_module_update', UPDATE: 'update'
        },
        OnRestartRequiredReason: { APP_UPDATE: 'app_update', OS_UPDATE: 'os_update', PERIODIC: 'periodic' },
        PlatformArch: { ARM: 'arm', ARM64: 'arm64', MIPS: 'mips', MIPS64: 'mips64', X86_32: 'x86-32', X86_64: 'x86-64' },
        PlatformOs: { ANDROID: 'android', CROS: 'cros', LINUX: 'linux', MAC: 'mac', OPENBSD: 'openbsd', WIN: 'win' }
      };
      chromeStub.runtime.connect = mark(function connect() { throw new Error('Could not establish connection. Receiving end does not exist.'); }, 'connect', 0, null, '');
      chromeStub.runtime.sendMessage = mark(function sendMessage() { throw new Error('Could not establish connection. Receiving end does not exist.'); }, 'sendMessage', 1, null, '');
      /* Real Chrome returns an object from both; null is what a broken stub returns. */
      chromeStub.loadTimes = mark(function loadTimes() {
        return { requestTime: 0, startLoad: 0, commitLoad: 0, finishDocumentLoad: 0, finishLoad: 0,
          firstPaintTime: 0, firstPaintAfterLoadTime: 0, navigationType: 'Other',
          wasFetchedViaSpdy: false, wasNpnNegotiated: true, npnNegotiatedProtocol: 'h2',
          wasAlternateProtocolAvailable: false, connectionInfo: 'h2' };
      }, 'loadTimes', 0, null, '');
      chromeStub.csi = mark(function csi() {
        return { startE: 0, onloadT: 0, pageT: Math.round(performance.now()), tran: 15 };
      }, 'csi', 0, null, '');
      Object.defineProperty(window, 'chrome', { value: chromeStub, writable: true, configurable: true, enumerable: true });
    }
  } catch (e) {}

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
              /* Notification.permission is forced to 'default' below; a query that answers
                 with the real state would contradict the other read. */
              if (name === 'notifications')
                Object.defineProperty(st, 'state', { value: 'default', configurable: true });
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
          // Elements only report a gutter when they actually scroll with a classic bar;
          // subtracting it from every clipped box would make overflow:hidden containers
          // too narrow by one scrollbar.
          var scrolls = false;
          try {
            var cs = (this.ownerDocument && this.ownerDocument.defaultView)
              ? this.ownerDocument.defaultView.getComputedStyle(this) : null;
            var oy = cs ? String(cs.overflowY || '') : '';
            scrolls = (oy === 'auto' || oy === 'scroll' || oy === 'overlay')
              && this.scrollHeight > (this.clientHeight || 0);
          } catch (e) { scrolls = false; }
          return Math.max(0, v - (scrolls ? SBW : 0));
        }, 'get ' + prop, 0)
      });
    }
    if (typeof Element !== 'undefined' && Element.prototype) {
      layoutOverride(Element.prototype, 'clientWidth');
    }

    /* documentElement is an Element, so the patch above already answers it; adding a second
       clientWidth on Document would be an own property real Chrome does not have, and the
       value captured here would already be gutter-reduced, doubling the subtraction. */

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
    /* The adapter this machine really has, read before anything is replaced.
       The application asks the page for it once and keeps it: a profile that claims a
       card the machine does not have is one of the few things a scanner can see without
       a database of anything - the name says one GPU and the limits that come with it
       come from another. */
    try {
      var rc = document.createElement('canvas');
      var rg = rc.getContext('webgl2') || rc.getContext('webgl');
      if (rg) {
        var re = rg.getExtension('WEBGL_debug_renderer_info');
        if (re) {
          window.__jbRealGl = String(rg.getParameter(re.UNMASKED_RENDERER_WEBGL)) +
            '||' + String(rg.getParameter(re.UNMASKED_VENDOR_WEBGL));
        }
      }
    } catch (e2) {}

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
    /* The host adapter's vendor-specific extensions (NV_*) answer under a renderer string
       that claims a different GPU family - a one-line mismatch scanners check. */
    var claimedNv = /nvidia/i.test(String(F.glVendor || '') + ' ' + String(F.glRenderer || ''));
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
        if (typeof origExt === 'function') {
          method(proto, 'getExtension', function getExtension(name) {
            try {
              var n = String(name || '').toLowerCase();
              if (!claimedNv && n.indexOf('nv_') === 0) return null;
              /* Answering the debug-renderer extension with an object and its constants with
                 null is a contradiction of a different kind: WebKit simply does not hand it
                 out, so under a Safari identity the extension itself goes away. */
              if (isSafari && n === 'webgl_debug_renderer_info') return null;
            } catch (e) {}
            return origExt.call(this, name);
          }, 1);
        }
        var origSup = proto.getSupportedExtensions;
        if (typeof origSup === 'function' && !claimedNv) {
          method(proto, 'getSupportedExtensions', function getSupportedExtensions() {
            var list = origSup.apply(this, arguments);
            try {
              var out = [];
              for (var xi = 0; xi < list.length; xi++)
                if (!/^NV_/i.test(String(list[xi]))) out.push(list[xi]);
              return out;
            } catch (e) { return list; }
          }, 0);
        }
      })(G.prototype);
    }
  } catch (e) {}

  /* ---- WebGPU ----
     A modern checker reads adapter.info straight out of navigator.gpu and reads the real
     host GPU even when every WebGL surface is spoofed. The adapter object answers from the
     same identity the WebGL renderer string claims. */
  try {
    if (typeof GPUAdapter !== 'undefined' && GPUAdapter.prototype && 'info' in GPUAdapter.prototype) {
      var r0 = String(F.glRenderer || '');
      var inner0 = r0.replace(/^ANGLE\s*\((.*)\)\s*$/, '$1');
      var vlow0 = String(F.glVendor || '').toLowerCase().replace('google inc.', '');
      var wvendor = 'unknown';
      if (/nvidia/.test(vlow0)) wvendor = 'nvidia';
      else if (/apple/.test(vlow0)) wvendor = 'apple';
      else if (/intel/.test(vlow0)) wvendor = 'intel';
      else if (/amd|radeon/.test(vlow0)) wvendor = 'amd';
      else if (/qualcomm|adreno/.test(vlow0)) wvendor = 'qualcomm';
      else if (/mali|arm/.test(vlow0)) wvendor = 'arm';
      getter(GPUAdapter.prototype, 'info', { vendor: wvendor, architecture: '', device: '', description: inner0 });
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
          /* One path for every canvas kind. drawImage of a WebGL canvas snapshots the same
             composited frame the real encode path uses, so a context without
             preserveDrawingBuffer encodes exactly what real Chrome would encode - whereas
             readPixels on that same context answers zeros once the frame has been presented,
             which is a blank PNG and an instant 'WebGL render failed' verdict. */
          ctx.drawImage(self, 0, 0);
          var raw = origData.call(ctx, 0, 0, w, h);
          perturb(raw.data, w, h, 0, 0);
          ctx.putImageData(raw, 0, 0);
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
        }, 0);
      }
    } catch (e) {}
  }

  /* ---- Audio ----
     getChannelData hands back the buffer's LIVE internal array, so an additive delta drifts:
     a second read lands on top of the first and the two hashes disagree - the exact
     'call it twice and compare' check the audio test runs. The perturbation is the same
     idempotent quantisation the canvas uses: each sampled index snaps to the nearest value
     congruent to a per-index target mod 3, so reading again cannot move it. */
  if (F.audio) {
    try {
      if (typeof AudioBuffer !== 'undefined') {
        var origChannel = AudioBuffer.prototype.getChannelData;
        method(AudioBuffer.prototype, 'getChannelData', function getChannelData(index) {
          var arr = origChannel.apply(this, arguments);
          try {
            var q = 3e-7;
            for (var i = 0; i < arr.length; i += 1000) {
              var tgt = Math.floor(noise(i) * 3) % 3;
              var m = Math.round(arr[i] / q);
              var r = m % 3; if (r < 0) r += 3;
              var d = tgt - r; if (d > 1) d -= 3; else if (d < -1) d += 3;
              arr[i] = (m + d) * q;
            }
          } catch (e) {}
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
        /* Only the interactive context: forcing BaseAudioContext also rewrote
           OfflineAudioContext(44100, ...), which then reported a rate it did not render at. */
        getter(AudioContext.prototype, 'sampleRate', 48000);
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
        /* The always-on enumerateDevices wrapper above already blanks labels and ids; a
           second wrapper here only stacked and achieved the same result twice. */
      }
    }
  } catch (e) {}

  /* ---- Element rects ----
   getBoundingClientRect is a font-and-DPI fingerprint of its own: a page that measures a
   hidden probe span reads the host's exact text metrics. The answer carries a deterministic
   sub-0.01 px offset, cached per element so repeated reads agree, and small enough that no
   layout or hit test can notice it. */
  try {
    if (typeof Element !== 'undefined' && Element.prototype && Element.prototype.getBoundingClientRect) {
      var origGBCR = Element.prototype.getBoundingClientRect;
      var rectCache = (typeof WeakMap === 'function') ? new WeakMap() : null;
      method(Element.prototype, 'getBoundingClientRect', function getBoundingClientRect() {
        var r = origGBCR.apply(this, arguments);
        try {
          var dj = rectCache ? rectCache.get(this) : null;
          if (!dj) {
            var tag = '';
            try { tag = String(this.tagName || ''); } catch (e0) {}
            var h = (SEED ^ Math.imul(tag.length + 3, 2654435761)) | 0;
            h = Math.imul(h ^ (h >>> 13), 1274126177) | 0;
            dj = [((h >>> 0) % 7) - 3, (((h >>> 3) % 7) - 3)];   // -0.003..+0.003 px each axis
            if (rectCache) rectCache.set(this, dj);
          }
          r.x += dj[0] / 1000; r.y += dj[1] / 1000;
        } catch (e) {}
        return r;
      }, 0);
    }
  } catch (e) {}

  /* Shared hook: the tool scripts patch page APIs too (right-click unlock, clipboard
     watchers). Without it their replacements printed their own source through
     Function.prototype.toString - a tampering tell on every page of every profile that
     enabled them. The symbol is not enumerable, so a property scan finds nothing. */
  try {
    var MK = Symbol.for('jb.nativeMark');
    Object.defineProperty(Function.prototype, MK, {
      value: function (name, len) { return mark(this, name, len); },
      writable: true, enumerable: false, configurable: true
    });
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
  try {
    var __h = (location && location.hostname) ? location.hostname : '';
    if (/(^|\.)accounts\.google\.com$/.test(__h)) { F.canvas = false; F.audio = false; }
  } catch (e) {}
  var NP = (typeof WorkerNavigator !== 'undefined' && WorkerNavigator.prototype)
    ? WorkerNavigator.prototype : Object.getPrototypeOf(navigator);
  var G = Symbol.for('jb.realm');
  try { if (NP[G]) return; Object.defineProperty(NP, G, { value: 1, configurable: true }); } catch (e) {}

  /* Native-looking functions, same as the page realm: a worker that prints JS source for its
     patched getters is its own tampering signal, and the bootstrap runs before the full
     stamp can arrive. */
  var wFakes = (typeof WeakMap === 'function') ? new WeakMap() : null;
  var wOrigTS = Function.prototype.toString;
  try {
    Object.defineProperty(Function.prototype, 'toString', {
      value: named(function toString() {
        try { if (wFakes && wFakes.has(this)) return wFakes.get(this); } catch (e) {}
        return wOrigTS.call(this);
      }, 'toString'),
      writable: true, enumerable: false, configurable: true
    });
  } catch (e) {}

  function named(fn, n) {
    try {
      if (wFakes) wFakes.set(fn, 'function ' + n + '() { [native code] }');
      Object.defineProperty(fn, 'name', { value: n, configurable: true });
    } catch (e) {}
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
      /* The descriptor of the method being replaced is the reference: a WebIDL operation on
         a prototype is enumerable, and a replacement that is not is visible to anything
         that walks the prototype. */
      var desc = null;
      try { desc = Object.getOwnPropertyDescriptor(o, p); } catch (e) {}
      Object.defineProperty(o, p, {
        value: named(fn, p),
        writable: desc ? desc.writable !== false : true,
        enumerable: desc ? desc.enumerable === true : true,
        configurable: true
      });
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
  /* deviceMemory is Chromium-only; shipping it under a Firefox identity is a self-inflicted
     contradiction in both realms. */
  if (!F.safari && F.brand !== 'Firefox') get(NP, 'deviceMemory', F.memory);
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
        }, 0);
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
