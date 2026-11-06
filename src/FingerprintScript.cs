using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    internal static class FingerprintScript
    {
        public static string Build(FingerprintConfig fp, string ua, string platform, string language, string[] languages)
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
            cfg["mobile"] = fp.DeviceType != null && fp.DeviceType != "desktop";
            cfg["safari"] = FingerprintFactory.IsIos(fp.Os);
            cfg["webrtc"] = (fp.WebRtcMode ?? (fp.WebRtcProtect ? "proxy" : "off")).ToLowerInvariant();
            cfg["brand"] = fp.Brand ?? "";
            cfg["os"] = fp.Os ?? "";
            cfg["accept"] = fp.AcceptLanguage ?? language ?? "en-US";
            cfg["tz"] = fp.Timezone ?? "";
            cfg["geo"] = fp.GeoMode ?? "proxy";

            string json = new JavaScriptSerializer().Serialize(cfg);
            return Template.Replace("__CFG__", json);
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

  function getter(obj, prop, value) {
    if (!obj) return;
    try {
      var g = mark(function () { return value; }, 'get ' + prop, 0);
      Object.defineProperty(obj, prop, { get: g, set: undefined, enumerable: true, configurable: true });
    } catch (e) {}
  }

  function liveGetter(obj, prop, fn) {
    if (!obj) return;
    try {
      Object.defineProperty(obj, prop, {
        get: mark(fn, 'get ' + prop, 0), configurable: true, enumerable: true
      });
    } catch (e) {}
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
    var a = [];
    try {
      /* An empty [] reports '[object Array]' where Chrome reports '[object PluginArray]'. */
      Object.defineProperty(a, Symbol.toStringTag, { value: tag, configurable: true });
      Object.defineProperty(a, 'item', { value: mark(function item(index) { return a[index]; }, 'item', 1), configurable: true });
      Object.defineProperty(a, 'namedItem', { value: mark(function namedItem(name) { return null; }, 'namedItem', 1), configurable: true });
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
    if (uad && !isSafari) {
      var UD = Object.getPrototypeOf(uad);
      getter(UD, 'platform', uadPlatform);
      getter(UD, 'mobile', F.mobile);
      var origHigh = UD.getHighEntropyValues;
      if (typeof origHigh === 'function') {
        method(UD, 'getHighEntropyValues', function getHighEntropyValues(hints) {
          return origHigh.call(this, hints).then(function (h) {
            try {
              h.platform = uadPlatform;
              h.mobile = F.mobile;
              if (!F.mobile) h.model = '';
              if (F.brand === 'Edge' && !h.platformVersion) h.platformVersion = '15.0.0';
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
    var connProto = (typeof NetworkInformation !== 'undefined' && NetworkInformation.prototype)
      ? NetworkInformation.prototype : Object.prototype;
    var conn = Object.create(connProto);
    getter(conn, 'effectiveType', '4g');
    getter(conn, 'rtt', 40 + (SEED % 60));
    getter(conn, 'downlink', Math.round((10 + (SEED % 40) / 10) * 10) / 10);
    getter(conn, 'saveData', false);
    getter(conn, 'type', 'wifi');
    method(conn, 'addEventListener', function addEventListener(type, listener) { }, 2);
    getter(NP, 'connection', conn);
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
    /* outerWidth/outerHeight report the real window and contradict a claimed maximised screen. */
    liveGetter(window, 'outerWidth', function () { return F.sw; });
    liveGetter(window, 'outerHeight', function () { return F.ah; });
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
    glParams[7936] = F.glVendor;
    glParams[7937] = F.glRenderer;
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

  /* ---- Canvas / pixel noise ---- */
  var CH = (typeof HTMLCanvasElement !== 'undefined') ? HTMLCanvasElement.prototype : null;
  var C2 = (typeof CanvasRenderingContext2D !== 'undefined') ? CanvasRenderingContext2D.prototype : null;
  var canvasKind = (typeof WeakMap === 'function') ? new WeakMap() : null;

  function perturb(data) {
    try {
      for (var i = 0; i < data.length; i += 4 * 997) {
        var d = data[i] + ((noise(i) * 3 | 0) - 1);
        data[i] = d < 0 ? 0 : (d > 255 ? 255 : d);
        var j = i + 1;
        if (j < data.length) {
          var d2 = data[j] + ((noise(i + 7) * 3 | 0) - 1);
          data[j] = d2 < 0 ? 0 : (d2 > 255 ? 255 : d2);
        }
      }
    } catch (e) {}
    return data;
  }

  if (F.canvas && C2 && CH) {
    try {
      var origData = C2.getImageData;
      var origURL = CH.toDataURL;
      var origBlob = CH.toBlob;
      var origGetCtx = CH.getContext;
      var rpPairs = [];

      /* Remember how each canvas was created. drawImage() of a WebGL canvas that was not opened
         with preserveDrawingBuffer yields a blank bitmap, so the old copy-and-encode path both
         broke the image and disagreed with gl.readPixels. */
      method(CH, 'getContext', function getContext(type, attributes) {
        var ctx = origGetCtx.apply(this, arguments);
        try {
          var t = String(type || '').toLowerCase();
          if (ctx && canvasKind) canvasKind.set(this, /webgl/.test(t) ? 'gl' : '2d');
        } catch (e) {}
        return ctx;
      }, 1);

      function noisyEncode(self, encode) {
        try {
          var w = self.width, h = self.height;
          if (!w || !h || w * h > 16000000) return null;
          var tmp = document.createElement('canvas');
          tmp.width = w; tmp.height = h;
          var ctx = origGetCtx.call(tmp, '2d');
          if (!ctx) return null;
          if (canvasKind && canvasKind.get(self) === 'gl') {
            var gl = null;
            try { gl = origGetCtx.call(self, 'webgl') || origGetCtx.call(self, 'webgl2'); } catch (e) {}
            if (!gl) return null;
            /* The patched readPixels already perturbs, so the encode path has to lift the pixels
               with the untouched one or the same canvas hashes twice. */
            var rp = null;
            for (var qi = 0; qi < rpPairs.length; qi++) {
              try { if (rpPairs[qi][0].isPrototypeOf(gl)) { rp = rpPairs[qi][1]; break; } } catch (e) {}
            }
            if (!rp) return null;
            var px = new Uint8Array(w * h * 4);
            rp.call(gl, 0, 0, w, h, gl.RGBA, gl.UNSIGNED_BYTE, px);
            var img = ctx.createImageData(w, h);
            img.data.set(px);
            perturb(img.data);
            ctx.putImageData(img, 0, 0);
          } else {
            ctx.drawImage(self, 0, 0);
            var raw = origData.call(ctx, 0, 0, w, h);
            perturb(raw.data);
            ctx.putImageData(raw, 0, 0);
          }
          return encode(tmp);
        } catch (e) { return null; }
      }

      method(C2, 'getImageData', function getImageData(sx, sy, sw, sh) {
        var img = origData.apply(this, arguments);
        try { perturb(img.data); } catch (e) {}
        return img;
      }, 4);

      method(CH, 'toDataURL', function toDataURL(type, quality) {
        var a = arguments, self = this;
        var out = noisyEncode(self, function (tmp) { return origURL.apply(tmp, a); });
        return out != null ? out : origURL.apply(self, a);
      }, 0);

      /* toBlob hands back nothing, so success has to be signalled by the encode callback itself —
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
            try { if (pixels && pixels.length) perturb(pixels); } catch (e) {}
          }, 7);
        })(RG.prototype);
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
        var o = V ? Object.create(V.prototype) : {};
        getter(o, 'name', rows[vi][0]);
        getter(o, 'voiceURI', rows[vi][0]);
        getter(o, 'lang', rows[vi][1] || F.language);
        getter(o, 'localService', rows[vi][2]);
        getter(o, 'default', vi === 0);
        list.push(o);
      }
      method(speechSynthesis, 'getVoices', function getVoices() { return list.slice(); }, 0);
    }
  } catch (e) {}

  /* ---- Font metrics ---- */
  if (F.fonts && C2) {
    try {
      var origMeasure = C2.measureText;
      method(C2, 'measureText', function measureText(text) {
        var m = origMeasure.call(this, text);
        try {
          var s = String(text || '');
          var h = 0;
          for (var k = 0; k < s.length; k++) h = (h * 31 + s.charCodeAt(k)) | 0;
          var jitter = (noise(h) - 0.5) * 0.012 * m.width;
          Object.defineProperty(m, 'width', { value: Math.max(0, m.width + jitter), configurable: true });
        } catch (e) {}
        return m;
      }, 1);
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

  /* ---- WebRTC ---- */
  /* --force-webrtc-ip-handling-policy only covers Chrome's internal path. A site can still
     enumerate candidates through JS, so the API itself must refuse when the profile says the
     transport is disabled. */
  try {
    if (F.webrtc === 'off' || F.webrtc === 'disabled') {
      Object.defineProperty(window, 'RTCPeerConnection', {
        value: mark(function RTCPeerConnection() {
          throw new TypeError('RTCPeerConnection is not available');
        }, 'RTCPeerConnection', 0),
        writable: true, configurable: true
      });
      Object.defineProperty(window, 'webkitRTCPeerConnection', { value: undefined, configurable: true });

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
    }
}
