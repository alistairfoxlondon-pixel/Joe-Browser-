using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace JoeBrowser
{
    /// <summary>
    /// Builds and owns the three original Joe Browser extensions. They are unpacked MV3
    /// extensions written to the local user data folder - nothing is downloaded, no store
    /// account is involved, and no third-party code is bundled.
    ///
    /// Chrome 137+ ignores --load-extension on branded builds, so every tool also has a
    /// first-party CDP fallback (BuildToolScript) that is injected into each document. The
    /// unpacked extension and the injected script share one protocol, so either one working
    /// is enough for the feature to be usable.
    /// </summary>
    internal static class ExtensionManager
    {
        public const string HumanPaste = "joe-human-paste";
        public const string ProxyWatch = "joe-proxy-watch";
        public const string RightClickEnable = "joe-right-click-enable";
        /// <summary>Old folder name, kept so EnsureAll can retire it after an update.</summary>
        public const string ContextGuard = "joe-context-guard";

        private static readonly object Gate = new object();
        private static bool _built;

        /* Only the proxy watcher ever talks to a public IP service. Requesting those hosts
           from the paste / context-menu extensions would be needless over-permission. */
        private static readonly string[] NoHosts = new string[0];
        private static readonly string[] IpHosts =
        {
            "https://ipwho.is/*", "https://api.ipify.org/*", "https://ipapi.co/*"
        };

        public static string Dir(string id) { return Path.Combine(AppPaths.Extensions, id); }

        /// <summary>Writes all built-in extensions once per process. Safe to call repeatedly.</summary>
        public static void EnsureAll()
        {
            lock (Gate)
            {
                if (_built) return;
                try
                {
                    Directory.CreateDirectory(AppPaths.Extensions);
                    Write(HumanPaste, "manifest.json", Manifest("Joe Human Paste", "1.1.0",
                        "Types clipboard text one character at a time with human-like timing.",
                        new[] { "activeTab", "clipboardRead", "storage", "scripting", "contextMenus", "tabs" },
                        NoHosts, "service-worker.js", "content.js", false));
                    Write(HumanPaste, "service-worker.js", HumanPasteWorker);
                    Write(HumanPaste, "content.js", HumanPasteContent);
                    WriteIcon(HumanPaste, "H", Color.FromArgb(0, 120, 215));

                    Write(ProxyWatch, "manifest.json", Manifest("Joe Proxy Watch", "1.1.0",
                        "Warns when the connection IP rotates or the proxy goes offline.",
                        new[] { "alarms", "notifications", "storage", "tabs", "scripting" },
                        IpHosts, "service-worker.js", "content.js", true));
                    Write(ProxyWatch, "service-worker.js", ProxyWatchWorker);
                    Write(ProxyWatch, "content.js", ProxyWatchContent);
                    Write(ProxyWatch, "banner.css", ProxyWatchCss);
                    WriteIcon(ProxyWatch, "P", Color.FromArgb(0, 150, 136));

                    Write(RightClickEnable, "manifest.json", Manifest("Joe Right Click Enable", "1.2.0",
                        "Restores the right-click menu and copy on sites that block them.",
                        new[] { "storage", "activeTab", "scripting", "tabs" },
                        NoHosts, "service-worker.js", "content.js", false));
                    Write(RightClickEnable, "service-worker.js", RightClickWorker);
                    Write(RightClickEnable, "content.js", RightClickContent);
                    WriteIcon(RightClickEnable, "R", Color.FromArgb(0, 120, 215));

                    // Retire the old guard folder so a stale copy is never loaded alongside
                    // the new enable-right-click extension.
                    try { if (Directory.Exists(Dir(ContextGuard))) Directory.Delete(Dir(ContextGuard), true); }
                    catch { }

                    _built = true;
                }
                catch (Exception ex)
                {
                    Log.Error("Could not write built-in extensions", ex);
                }
            }
        }

        private static string Manifest(string name, string version, string desc, string[] perms,
            string[] hosts, string worker, string content, bool banner)
        {
            /* Build the member list and join it, so adding or omitting an optional member
               can never leave a dangling comma in the emitted JSON. */
            var lines = new List<string>();
            lines.Add("  \"manifest_version\": 3");
            lines.Add("  \"name\": \"" + name + "\"");
            lines.Add("  \"version\": \"" + version + "\"");
            lines.Add("  \"description\": \"" + desc + "\"");
            lines.Add("  \"permissions\": [" + string.Join(", ", Quote(perms)) + "]");
            if (hosts != null && hosts.Length > 0)
                lines.Add("  \"host_permissions\": [" + string.Join(", ", Quote(hosts)) + "]");
            lines.Add("  \"background\": { \"service_worker\": \"" + worker + "\" }");
            lines.Add("  \"action\": { \"default_title\": \"" + name + "\", \"default_icon\": \"icon.png\" }");
            lines.Add("  \"icons\": { \"48\": \"icon.png\" }");
            if (content != null)
            {
                /* One list entry, so the join below adds a comma only after the whole member. */
                lines.Add("  \"content_scripts\": [{\n" +
                          "    \"matches\": [\"<all_urls>\"],\n" +
                          "    \"js\": [\"" + content + "\"],\n" +
                          "    \"run_at\": \"document_start\",\n" +
                          "    \"all_frames\": false\n" +
                          "  }]");
            }
            if (banner)
                lines.Add("  \"web_accessible_resources\": [{ \"resources\": [\"banner.css\"], \"matches\": [\"<all_urls>\"] }]");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine(string.Join(",\n", lines.ToArray()));
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void WriteIcon(string id, string letter, Color tint)
        {
            string path = Path.Combine(Dir(id), "icon.png");
            if (File.Exists(path)) return;
            try
            {
                using (var bmp = new Bitmap(48, 48, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                using (var f = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(tint))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.FillEllipse(brush, 1, 1, 46, 46);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(letter, f, Brushes.White, new RectangleF(0, 0, 48, 48), sf);
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            catch (Exception ex) { Log.Error("Extension icon failed", ex); }
            try
            {
                // Every manifest references banner.css; keep the file present in each folder.
                string css = Path.Combine(Dir(id), "banner.css");
                if (!File.Exists(css)) File.WriteAllText(css, ProxyWatchCss, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string[] Quote(string[] values)
        {
            var outp = new string[values.Length];
            for (int i = 0; i < values.Length; i++) outp[i] = "\"" + values[i] + "\"";
            return outp;
        }

        private static void Write(string id, string file, string content)
        {
            string dir = Dir(id);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, file);
            // Rewrite when the source changed so an app update repairs old extension files.
            bool same = false;
            try { same = File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content; } catch { }
            if (!same) File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>
        /// Folders passed to --load-extension. Validates that each path really holds a manifest.
        /// Custom folders are copied into the profile-neutral extensions root so the command
        /// line never contains a path with a comma (Chrome's separator).
        /// </summary>
        public static List<string> EnabledFolders(ToolConfig tools, bool masterSwitch, bool customExtensions = true, bool hasProxy = true)
        {
            var result = new List<string>();
            if (!masterSwitch || tools == null) return result;
            EnsureAll();
            /* The two store extensions requested for Chrome and Brave are fetched once into
               the managed library. They load for every Chromium profile regardless of the
               "custom extensions" switch, because they are shipped defaults, not user imports. */
            try { ExtensionCatalog.EnsureDefaults(); } catch (Exception ex) { Log.Error("Default extensions", ex); }
            foreach (string folder in ExtensionCatalog.DefaultFolders()) Add(result, folder);
            if (tools.HumanPaste) Add(result, Dir(HumanPaste));
            /* The proxy watcher extension is always loaded so that adding a proxy to a running
               profile works without a restart. It stays idle until the page runtime tells it
               that a proxy is actually configured - on a direct connection it makes no
               requests and shows nothing. */
            if (tools.ProxyWatch) Add(result, Dir(ProxyWatch));
            if (tools.EnableRightClick) Add(result, Dir(RightClickEnable));

            // Extensions added from the Extensions page are applied to every Chromium profile.
            if (customExtensions)
            {
                foreach (string folder in ExtensionCatalog.EnabledFolders()) Add(result, folder);
            }

            string extra = tools.CustomExtensions ?? "";
            foreach (string raw in extra.Replace('\r', '\n').Split('\n'))
            {
                string p = raw.Trim().Trim('"');
                if (p.Length == 0) continue;
                try
                {
                    if (!Directory.Exists(p) || !File.Exists(Path.Combine(p, "manifest.json"))) continue;
                    if (p.IndexOf(',') >= 0) continue; // Chrome splits on commas; skip unsafe paths
                    Add(result, p);
                }
                catch { }
            }
            return result;
        }

        private static void Add(List<string> list, string path)
        {
            if (!list.Contains(path)) list.Add(path);
        }

        /// <summary>
        /// The guaranteed fallback. A single self-contained IIFE that is injected through CDP
        /// before any page script, so the tools work on Chrome 137+, Edge, Chromium and any
        /// build that refuses unpacked extensions. It also powers the on-page banners the
        /// extension service worker asks for.
        /// </summary>
        public static string BuildToolScript(ToolConfig tools, string profileName, bool hasProxy)
        {
            if (tools == null) tools = new ToolConfig();
            string speed = tools.HumanPasteSpeed ?? "natural";
            int minDelay, maxDelay;
            switch (speed)
            {
                case "relaxed": minDelay = 90; maxDelay = 240; break;
                case "fast": minDelay = 18; maxDelay = 60; break;
                case "instant": minDelay = 0; maxDelay = 6; break;
                default: minDelay = 38; maxDelay = 135; break;
            }
            int interval = Math.Max(30, Math.Min(900, tools.ProxyWatchInterval <= 0 ? 60 : tools.ProxyWatchInterval));

            var sb = new StringBuilder();
            sb.AppendLine("(function(){");
            sb.AppendLine("if (window.__JB_TOOLS__) return; window.__JB_TOOLS__ = 1;");
            sb.AppendLine("var CFG = {");
            sb.AppendLine("  human: " + (tools.HumanPaste ? "true" : "false") + ",");
            sb.AppendLine("  minDelay: " + minDelay + ", maxDelay: " + maxDelay + ",");
            sb.AppendLine("  watch: " + ((tools.ProxyWatch && hasProxy) ? "true" : "false") + ",");
            sb.AppendLine("  hasProxy: " + (hasProxy ? "true" : "false") + ",");
            sb.AppendLine("  interval: " + interval + ",");
            sb.AppendLine("  rightClick: " + (tools.EnableRightClick ? "true" : "false") + ",");
            sb.AppendLine("  unlockSelect: " + (tools.UnlockSelection ? "true" : "false") + ",");
            sb.AppendLine("  profile: " + Js(profileName ?? "") + ",");
            sb.AppendLine("  extension: false");
            sb.AppendLine("};");
            sb.AppendLine(ToolRuntimeJs);
            sb.AppendLine("})();");
            return sb.ToString();
        }

        public static string Js(string s)
        {
            if (string.IsNullOrEmpty(s)) return "''";
            return "'" + s.Replace("\\", "\\\\").Replace("'", "\\'")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("<", "\\u003c") + "'";
        }

        /// <summary>Runtime shared by the injected script and the unpacked content scripts.</summary>
        public const string ToolRuntimeJs = @"
/* ---------- Joe Browser tools runtime (first-party) ---------- */
/* A named window property is the shortest detection shortcut there is, so the re-injection
   guard is a symbol and the runtime API stays inside this closure. */
var JB_GUARD = Symbol.for('jb.tools');
try {
  if (window[JB_GUARD]) return;
  Object.defineProperty(window, JB_GUARD, { value: 1, configurable: true });
} catch (e) {}
var JB = {};
var cfg = CFG;
/* Keep the native method: the right-click unlocker below deliberately replaces
   preventDefault for page-driven events, but our own paste handler still needs it. */
var nativePrevent = Event.prototype.preventDefault;

/* Windows 11 style notification stack: one card per event, newest at the bottom, so a
   rotation notice is never overwritten by the back-online notice that follows it.
   Neutral surface, one accent rule, an icon tile, a timestamp and a single quiet action. */
function copyText(t){
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) { navigator.clipboard.writeText(t); return; }
  } catch(e) {}
  try {
    var ta = document.createElement('textarea');
    ta.value = t; ta.style.position = 'fixed'; ta.style.opacity = '0';
    document.body.appendChild(ta); ta.select();
    document.execCommand('copy');
    document.body.removeChild(ta);
  } catch(e) {}
}

function bannerHost(){
  var host = document.getElementById('jb-banners');
  if (!host) {
    if (!document.body) return null;
    host = document.createElement('div');
    host.id = 'jb-banners';
    host.setAttribute('style', 'position:fixed;z-index:2147483647;right:16px;bottom:16px;display:flex;' +
      'flex-direction:column;gap:8px;width:372px;max-width:calc(100vw - 32px);pointer-events:none;' +
      'text-align:left;direction:ltr;');
    document.body.appendChild(host);
  }
  return host;
}

function stamp(){
  try {
    var d = new Date();
    return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2) + ':' + ('0' + d.getSeconds()).slice(-2);
  } catch(e) { return ''; }
}

function banner(title, body, tone, sticky, action){
  try {
    var host = bannerHost();
    if (!host) {
      document.addEventListener('DOMContentLoaded', function(){ banner(title, body, tone, sticky, action); }, { once: true });
      return;
    }
    var accent = tone === 'warn' ? '#f7b32b' : (tone === 'bad' ? '#e9494a' : '#4cc2ff');
    var tint = tone === 'warn' ? 'rgba(247,179,43,.14)' : (tone === 'bad' ? 'rgba(233,73,74,.15)' : 'rgba(76,194,255,.14)');

    var el = document.createElement('div');
    el.setAttribute('role', 'status');
    el.setAttribute('aria-live', 'polite');
    el.setAttribute('style', 'position:relative;box-sizing:border-box;pointer-events:auto;overflow:hidden;' +
      'text-align:left;direction:ltr;unicode-bidi:isolate;' +
      'font:13px/1.45 ""Segoe UI Variable Text"",""Segoe UI"",system-ui,sans-serif;color:#fff;' +
      'background:#202020;border:1px solid rgba(255,255,255,.09);border-radius:8px;' +
      'box-shadow:0 14px 38px rgba(0,0,0,.46),0 2px 6px rgba(0,0,0,.28);' +
      'transform:translateY(10px);opacity:0;transition:transform .18s ease,opacity .18s ease;');

    var bar = document.createElement('div');
    bar.setAttribute('style', 'height:3px;background:' + accent + ';');

    var row = document.createElement('div');
    row.setAttribute('style', 'display:flex;align-items:flex-start;gap:12px;padding:13px 14px 14px;');

    var icon = document.createElement('div');
    icon.textContent = tone === 'bad' ? '\u2715' : (tone === 'warn' ? '!' : '\u2713');
    icon.setAttribute('style', 'flex:0 0 32px;width:32px;height:32px;border-radius:8px;display:flex;' +
      'align-items:center;justify-content:center;font-size:15px;font-weight:600;color:' + accent + ';background:' + tint + ';');

    var text = document.createElement('div');
    text.setAttribute('style', 'flex:1 1 auto;min-width:0;');

    var h = document.createElement('div');
    h.textContent = title;
    h.setAttribute('style', 'font-size:13.5px;font-weight:600;letter-spacing:.1px;margin:0 0 3px;');

    var p = document.createElement('div');
    p.textContent = body;
    p.setAttribute('style', 'font-size:12px;line-height:1.5;color:rgba(255,255,255,.72);word-wrap:break-word;');

    text.appendChild(h);
    text.appendChild(p);

    if (action && action.label) {
      var act = document.createElement('button');
      act.type = 'button';
      act.textContent = action.label;
      act.setAttribute('style', 'margin-top:9px;padding:4px 10px;border-radius:5px;border:1px solid rgba(255,255,255,.14);' +
        'background:rgba(255,255,255,.06);color:rgba(255,255,255,.9);font:11.5px/1.4 ""Segoe UI"",sans-serif;cursor:pointer;');
      act.onmouseenter = function(){ act.style.background = 'rgba(255,255,255,.12)'; };
      act.onmouseleave = function(){ act.style.background = 'rgba(255,255,255,.06)'; };
      act.onclick = function(){
        try { if (action.onClick) action.onClick(); } catch(e) {}
        act.textContent = 'Copied';
        act.disabled = true;
        setTimeout(function(){ if (act.parentNode) act.parentNode.removeChild(act); }, 1400);
      };
      text.appendChild(act);
    }

    var meta = document.createElement('div');
    meta.textContent = stamp();
    meta.setAttribute('style', 'margin-top:6px;font-size:11px;color:rgba(255,255,255,.42);font-variant-numeric:tabular-nums;');
    text.appendChild(meta);

    var x = document.createElement('button');
    x.type = 'button';
    x.textContent = '\u00d7';
    x.setAttribute('aria-label', 'Dismiss');
    x.setAttribute('style', 'flex:0 0 auto;margin:-2px -2px 0 0;padding:0 2px;border:0;background:transparent;' +
      'color:rgba(255,255,255,.5);font:16px/1 ""Segoe UI"",sans-serif;cursor:pointer;');
    x.onmouseenter = function(){ x.style.color = 'rgba(255,255,255,.9)'; };
    x.onmouseleave = function(){ x.style.color = 'rgba(255,255,255,.5)'; };
    x.onclick = function(){ dismiss(); };

    row.appendChild(icon); row.appendChild(text); row.appendChild(x);
    el.appendChild(bar); el.appendChild(row);
    host.appendChild(el);

    /* A long session must not grow the stack without bound. */
    while (host.children.length > 3) host.removeChild(host.firstChild);

    requestAnimationFrame(function(){ el.style.transform = 'translateY(0)'; el.style.opacity = '1'; });

    var timer = 0;
    function dismiss(){
      clearTimeout(timer);
      try { el.style.opacity = '0'; el.style.transform = 'translateY(10px)'; } catch(e) {}
      setTimeout(function(){ if (el.parentNode) el.parentNode.removeChild(el); }, 200);
    }
    if (!sticky) timer = setTimeout(dismiss, 9000);
  } catch(e) {}
}
JB.banner = banner;

/* ---------- Paste as human ---------- */
/* The real paste event carries the clipboard text, so no clipboard permission is needed.
   Arm with Alt+V (or Shift+Ctrl+V); the next paste is typed character by character. */
var armed = false, typing = false;
function armPaste(){
  armed = true;
  banner('Human paste armed', 'Press Ctrl+V. The text will be typed like a person.', 'info', false);
  setTimeout(function(){ if (!typing) armed = false; }, 12000);
}
JB.armPaste = armPaste;
function delay(base){ return cfg.minDelay + Math.random() * (cfg.maxDelay - cfg.minDelay) + base; }
function wait(ms){ return new Promise(function(r){ setTimeout(r, ms); }); }
/* A paste that only calls execCommand leaves zero keydown/keyup events behind, so a page that
   counts keystrokes sees characters appear from nowhere - the classic automated-paste tell.
   Each character now goes through the same order a physical key produces:
   keydown -> keypress -> beforeinput -> DOM change -> input -> keyup, with the real key, code,
   keyCode, charCode and shiftKey for the character. */
var SHIFTED = {'!':'1','@':'2','#':'3','$':'4','%':'5','^':'6','&':'7','*':'8','(':'9',')':'0',
  '_':'-','+':'=','{':'[','}':']','|':'\\',':':';','<':',','>':'.','?':'/','~':'`'};
var PUNCT = {'`':'Backquote','-':'Minus','=':'Equal','[':'BracketLeft',']':'BracketRight',
  '\\':'Backslash',';':'Semicolon',',':'Comma','.':'Period','/':'Slash'};
var VK = {'Backquote':192,'Minus':189,'Equal':187,'BracketLeft':219,'BracketRight':221,
  'Backslash':220,'Semicolon':186,'Quote':222,'Comma':188,'Period':190,'Slash':191};
/* The two quote characters are built from char codes so this file needs no literal quotes. */
SHIFTED[String.fromCharCode(34)] = String.fromCharCode(39);
PUNCT[String.fromCharCode(39)] = 'Quote';

function keyInfo(ch){
  if (ch === ' ') return { key: ' ', code: 'Space', keyCode: 32, shift: false, printable: true };
  if (ch === '\n') return { key: 'Enter', code: 'Enter', keyCode: 13, shift: false, printable: false };
  if (ch === '\t') return { key: 'Tab', code: 'Tab', keyCode: 9, shift: false, printable: false };
  var cc = ch.charCodeAt(0);
  /* Outside ASCII there is no physical key. An IME reports keyCode 229 and an empty code,
     which is exactly what a real non-Latin keystroke looks like. */
  if (cc > 126 || cc < 32) return { key: ch, code: '', keyCode: 229, shift: false, printable: true };
  var lower = ch.toLowerCase(), upper = ch.toUpperCase();
  if (lower >= 'a' && lower <= 'z')
    return { key: ch, code: 'Key' + upper, keyCode: upper.charCodeAt(0), shift: ch !== lower, printable: true };
  if (ch >= '0' && ch <= '9')
    return { key: ch, code: 'Digit' + ch, keyCode: cc, shift: false, printable: true };
  if (SHIFTED[ch]) {
    var base = SHIFTED[ch];
    var code = PUNCT[base] || ((base >= '0' && base <= '9') ? 'Digit' + base : 'Key' + base.toUpperCase());
    return { key: ch, code: code, keyCode: VK[code] || base.charCodeAt(0), shift: true, printable: true };
  }
  var pc = PUNCT[ch] || '';
  return { key: ch, code: pc, keyCode: VK[pc] || cc, shift: false, printable: true };
}

function makeKeyEvent(type, info, charCode){
  var ev;
  try {
    ev = new KeyboardEvent(type, {
      key: info.key, code: info.code, location: 0,
      ctrlKey: false, shiftKey: !!info.shift, altKey: false, metaKey: false,
      repeat: false, isComposing: false, bubbles: true, cancelable: true, composed: true
    });
  } catch(e) {
    try {
      ev = document.createEvent('KeyboardEvent');
      ev.initKeyboardEvent(type, true, true, window, info.key, 0, '', false, '');
    } catch(_) { ev = document.createEvent('Event'); ev.initEvent(type, true, true); }
  }
  var kc = info.keyCode, isPress = type === 'keypress';
  try { Object.defineProperty(ev, 'keyCode', { get: function(){ return kc; } }); } catch(e) {}
  try { Object.defineProperty(ev, 'charCode', { get: function(){ return isPress ? (charCode || 0) : 0; } }); } catch(e) {}
  try { Object.defineProperty(ev, 'which', { get: function(){ return isPress ? (charCode || 0) : kc; } }); } catch(e) {}
  return ev;
}

/* React and Vue install their own value tracker on the instance. Assigning .value directly
   updates the tracker first and the framework then sees no change, so the paste looks fine
   but the state stays empty. Calling the prototype setter and dispatching InputEvent is the
   path a real keystroke follows. */
function setNativeValue(a, value, ch){
  try {
    var proto = (a.tagName === 'TEXTAREA') ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    var d = Object.getOwnPropertyDescriptor(proto, 'value');
    if (d && d.set) d.set.call(a, value); else a.value = value;
  } catch(e) { try { a.value = value; } catch(_) {} }
  try {
    a.dispatchEvent(new InputEvent('input', { bubbles: true, cancelable: false, inputType: 'insertText', data: ch }));
  } catch(e) {
    try { a.dispatchEvent(new Event('input', { bubbles: true })); } catch(_) {}
  }
}

function typeTarget(){
  var a = document.activeElement;
  if (a && a !== document.body && a !== document.documentElement) return a;
  try {
    var sel = window.getSelection();
    if (sel && sel.anchorNode) {
      var n = sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement;
      if (n) return n;
    }
  } catch(e) {}
  return a || document.body;
}

function insertAt(target, ch){
  if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')) {
    var s = target.selectionStart == null ? target.value.length : target.selectionStart;
    var e = target.selectionEnd == null ? s : target.selectionEnd;
    setNativeValue(target, target.value.substring(0, s) + ch + target.value.substring(e), ch);
    try { target.setSelectionRange(s + 1, s + 1); } catch(_) {}
    return;
  }
  try { if (document.execCommand) document.execCommand('insertText', false, ch); } catch(e) {}
}

function typeChar(target, ch){
  var info = keyInfo(ch);
  var allowed = target.dispatchEvent(makeKeyEvent('keydown', info, 0));
  if (allowed && info.printable) target.dispatchEvent(makeKeyEvent('keypress', info, ch.charCodeAt(0)));
  if (allowed) {
    try {
      target.dispatchEvent(new InputEvent('beforeinput', { bubbles: true, cancelable: true, inputType: 'insertText', data: ch }));
    } catch(e) {}
    insertAt(target, ch);
  }
  target.dispatchEvent(makeKeyEvent('keyup', info, 0));
}

async function humanType(text){
  if (typing) return;
  typing = true;
  try {
    if (text.length > 20000) text = text.substring(0, 20000);
    var lastWasSpace = false;
    for (var i = 0; i < text.length; i++) {
      var ch = text.charAt(i);
      typeChar(typeTarget(), ch);
      var pause = 0;
      if (ch === ' ' || ch === '\n' || ch === '.' || ch === ',' || ch === '!' || ch === '?') {
        if (!lastWasSpace && Math.random() < 0.28) pause = 130 + Math.random() * 320;
        lastWasSpace = true;
      } else { lastWasSpace = false; }
      if (Math.random() < 0.012) pause += 180 + Math.random() * 380;
      var ms = delay(pause);
      if (ms > 2) await wait(ms);
    }
  } finally { typing = false; armed = false; }
}
if (cfg.human) {
  document.addEventListener('keydown', function(e){
    if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'v' || e.key === 'V')) { e.preventDefault(); armPaste(); }
  }, true);
  document.addEventListener('paste', function(e){
    /* Automatic by default: any paste into a page is typed in with human timing. Alt+V is
       still there as an explicit arm for clipboard managers that synthesize a paste without
       clipboardData, but it is no longer required for the feature to do anything. */
    var arm = cfg.human || armed || e.shiftKey;
    if (!arm || typing) return;
    var clip = (e.clipboardData || window.clipboardData);
    var text = clip ? clip.getData('text/plain') : '';
    if (!text) return;
    nativePrevent.call(e); e.stopPropagation();
    armed = false;
    humanType(text);
  }, true);
  window.addEventListener('message', function(e){
    var m = e.data;
    if (!m || !m.__jb || e.source !== window) return;
    if (m.__jb === 'armPaste') armPaste();
    else if (m.__jb === 'rightClick') cfg.rightClick = !!m.enabled;
    else if (m.__jb === 'checkProxy' && JB.checkProxy) JB.checkProxy();
    else if (m.__jb === 'proxyState') {
      if (m.state === 'rotated') {
        var from = m.from || 'unknown', to = m.to || 'unknown';
        var where = m.country ? ' \u00b7 ' + m.country : '';
        banner('IP address rotated', from + ' \u2192 ' + to + where +
          '. Re-check any open session before you sign in.', 'warn', true, {
            label: 'Copy new IP',
            onClick: function(){ copyText(m.to || ''); }
          });
      }
      else if (m.state === 'offline') {
        /* Only meaningful for a profile that actually routes through a proxy; on a direct
           connection a failed lookup is not something the user needs to be told about. */
        if (cfg.hasProxy) banner('Proxy appears offline', 'The connection check failed twice. Check it before logging in.', 'bad', true);
      }
      else if (m.state === 'online') banner('Proxy is back online', m.ip || '', 'info', false);
    }
  });
  if (window.chrome && chrome.runtime && chrome.runtime.onMessage) {
    chrome.runtime.onMessage.addListener(function(m){
      if (m && m.__jb === 'armPaste') armPaste();
    });
  }
}

/* ---------- Enable right click & copy ----------
   Many sites install handlers that call preventDefault() on contextmenu / copy /
   selectstart, which makes copying impossible. This tool puts the native menu and
   clipboard back: page handlers never see those trusted events, inline blockers are
   stripped, and selection CSS is unlocked. This is the opposite of a right-click
   blocker - it does not remove any browser feature. */
if (cfg.rightClick) {
  var UNBLOCK = { contextmenu:1, copy:1, cut:1, selectstart:1, dragstart:1, mousedown:1, mouseup:1 };
  Event.prototype.preventDefault = function(){
    try { if (this && UNBLOCK[this.type] && this.isTrusted) return; } catch(e){}
    return nativePrevent.call(this);
  };
  function unlock(root){
    try {
      if (!root) return;
      var attrs = ['oncontextmenu','onselectstart','ondragstart','oncopy','oncut','onmousedown','onmouseup'];
      if (root.removeAttribute) for (var a = 0; a < attrs.length; a++) root.removeAttribute(attrs[a]);
      root.oncontextmenu = null; root.onselectstart = null; root.ondragstart = null;
      root.oncopy = null; root.oncut = null;
      if (cfg.unlockSelect && root.querySelectorAll) {
        var els = root.querySelectorAll('[oncontextmenu],[onselectstart],[ondragstart],[oncopy],[oncut]');
        for (var i = 0; i < els.length; i++)
          for (var b = 0; b < attrs.length; b++) els[i].removeAttribute(attrs[b]);
      }
    } catch(e){}
  }
  function unlockAll(){ unlock(document.documentElement); unlock(document.body); }
  unlockAll();
  document.addEventListener('DOMContentLoaded', unlockAll, {once:true});
  if (cfg.unlockSelect) {
    try {
      var st = document.createElement('style');
      st.setAttribute('data-jb', 'selection');
      st.textContent = 'html,body{-webkit-user-select:text!important;-moz-user-select:text!important;-ms-user-select:text!important;user-select:text!important}';
      (document.head || document.documentElement).appendChild(st);
    } catch(e){}
  }
  document.addEventListener('contextmenu', function(e){
    if (e.isTrusted) e.stopImmediatePropagation();
  }, true);
  JB.enableRightClick = function(on){ cfg.rightClick = !!on; };
  JB.restoreContextMenu = unlockAll;
}

/* ---------- Proxy rotation / offline watch ---------- */
function readStore(k, d){ try { var v = localStorage.getItem(k); return v == null ? d : v; } catch(e){ return d; } }
function writeStore(k, v){ try { localStorage.setItem(k, v); } catch(e){} }
async function probe(){
  var urls = ['https://ipwho.is/', 'https://api.ipify.org?format=json'];
  for (var i = 0; i < urls.length; i++) {
    try {
      var ctrl = new AbortController();
      var t = setTimeout(function(){ ctrl.abort(); }, 8000);
      var r = await fetch(urls[i], { cache: 'no-store', signal: ctrl.signal });
      clearTimeout(t);
      if (!r.ok) continue;
      var j = await r.json();
      var ip = j && j.ip ? j.ip : '';
      if (!ip) continue;
      return { ip: ip, country: j.country || j.country_name || '', city: j.city || '' };
    } catch(e) {}
  }
  return null;
}
/* Tell the Proxy Watch extension whether this profile really has a proxy, so it can stay
   quiet on a direct connection instead of warning about a proxy that does not exist. */
try {
  window.postMessage({ __jb: 'proxyConfig', hasProxy: !!cfg.hasProxy }, '*');
} catch (e) {}

if (cfg.watch) {
  /* When the Proxy Watch extension is loaded it owns the polling and the desktop
     notification, and it broadcasts the state back to the page. Polling from both places
     would double the requests to the IP services for no gain, so the in-page fallback
     only runs when the extension is not present. */
  var extPolling = !!(window.chrome && chrome.runtime && chrome.runtime.id);
  var KEY = 'jb.proxy.baseline';
  var FAIL = 'jb.proxy.fails';
  var SEEN = 'jb.proxy.event';
  async function check(){
    var now = await probe();
    if (!now) {
      /* On a direct connection there is no proxy to be offline: an unreachable IP service is a
         network hiccup, not a ban risk, and warning about it trains the user to ignore the real one. */
      if (!cfg.hasProxy) return;
      var fails = (parseInt(readStore(FAIL, '0'), 10) || 0) + 1;
      writeStore(FAIL, String(fails));
      if (fails >= 2 && readStore(SEEN, '') !== 'offline') {
        writeStore(SEEN, 'offline');
        banner('Proxy appears offline', cfg.profile
          ? 'Profile \u201c' + cfg.profile + '\u201d could not reach the IP service. Stop before logging in.'
          : 'The connection could not reach the IP service.', 'bad', true);
        try { if (window.Notification && Notification.permission === 'granted') new Notification('Joe Browser', { body: 'Proxy offline for ' + cfg.profile }); } catch(e){}
      }
      return;
    }
    writeStore(FAIL, '0');
    var base = readStore(KEY, '');
    if (!base) { writeStore(KEY, now.ip); return; }
    if (base !== now.ip) {
      writeStore(KEY, now.ip);
      writeStore(SEEN, 'rotate');
      var where = now.country ? ' (' + now.country + ')' : '';
      banner('IP address rotated', base + ' \u2192 ' + now.ip + where +
        '. Re-check any open session before you sign in.', 'warn', true, {
          label: 'Copy new IP',
          onClick: function(){ copyText(now.ip || ''); }
        });
      try { if (window.Notification && Notification.permission === 'granted') new Notification('Joe Browser', { body: 'Proxy IP changed: ' + base + ' \u2192 ' + now.ip }); } catch(e){}
      window.dispatchEvent(new CustomEvent('jb-proxy-rotated', { detail: { from: base, to: now.ip } }));
    } else if (readStore(SEEN, '') === 'offline') {
      writeStore(SEEN, '');
      banner('Proxy is back online', now.ip + (now.country ? ' \u00b7 ' + now.country : ''), 'info', false);
    }
  }
  if (!extPolling) {
    setTimeout(check, 3500);
    setInterval(check, Math.max(30000, cfg.interval * 1000));
  }
  JB.checkProxy = check;
}
";

        // ---------------------------------------------------------------- extension sources

        private const string HumanPasteWorker = @"
/* Joe Human Paste - original service worker. Adds a context menu entry and relays the
   'arm' request to the active tab. The content script does the actual typing. */
chrome.runtime.onInstalled.addListener(function(){
  try {
    chrome.contextMenus.create({ id: 'jb-human-paste', title: 'Paste as human (next Ctrl+V)', contexts: ['editable'] });
  } catch(e) {}
});
chrome.contextMenus.onClicked.addListener(function(info, tab){
  if (info.menuItemId !== 'jb-human-paste' || !tab || tab.id == null) return;
  chrome.tabs.sendMessage(tab.id, { __jb: 'armPaste' }, function(){ void chrome.runtime.lastError; });
});
chrome.action.onClicked.addListener(function(tab){
  if (tab && tab.id != null) chrome.tabs.sendMessage(tab.id, { __jb: 'armPaste' }, function(){ void chrome.runtime.lastError; });
});
chrome.runtime.onMessage.addListener(function(msg, sender, send){
  if (msg && msg.__jb === 'ping') send({ ok: true });
});
";

        private const string HumanPasteContent = @"
/* Pure message bridge. Content scripts live in an isolated world, so this file never
   installs a paste handler of its own: the first-party CDP runtime in the main world
   owns the typing and there is exactly one handler per document. */
(function(){
  if (!chrome.runtime || !chrome.runtime.onMessage) return;
  chrome.runtime.onMessage.addListener(function(m){
    if (m && m.__jb === 'armPaste') window.postMessage({ __jb: 'armPaste' }, '*');
  });
})();
";

        private const string ProxyWatchWorker = @"
/* Joe Proxy Watch - original MV3 service worker. Polls the public IP through the
   browser's own network stack (i.e. through the profile proxy) and raises a real
   desktop notification on rotation or outage. */
const KEY = 'baseline', FAIL = 'fails', LAST = 'lastState';
async function probe(){
  for (const url of ['https://ipwho.is/', 'https://api.ipify.org?format=json']) {
    try {
      const ctrl = new AbortController();
      const timer = setTimeout(() => ctrl.abort(), 9000);
      const r = await fetch(url, { cache: 'no-store', signal: ctrl.signal });
      clearTimeout(timer);
      if (!r.ok) continue;
      const j = await r.json();
      if (j && j.ip) return { ip: j.ip, country: j.country || '', city: j.city || '' };
    } catch(e) {}
  }
  return null;
}
async function notify(title, message){
  try {
    await chrome.notifications.create('jb-proxy-' + Date.now(), {
      type: 'basic', iconUrl: chrome.runtime.getURL('icon.png'), title: title, message: message, priority: 2
    });
  } catch(e) {}
  try { await chrome.action.setBadgeText({ text: '!' }); await chrome.action.setBadgeBackgroundColor({ color: '#d94841' }); } catch(e) {}
}
async function check(){
  if (!armed()) return;
  const cur = await probe();
  const st = await chrome.storage.local.get([KEY, FAIL, LAST]);
  if (!cur) {
    const fails = (st[FAIL] || 0) + 1;
    await chrome.storage.local.set({ [FAIL]: fails, [LAST]: 'offline' });
    if (fails >= 2) {
      await chrome.storage.local.set({ [FAIL]: 0 });
      await notify('Connection check failed', 'Joe Browser could not reach the IP service. If this profile uses a proxy, check it before logging in.');
      broadcast({ __jb: 'proxyState', state: 'offline' });
    }
    return;
  }
  await chrome.storage.local.set({ [FAIL]: 0 });
  if (!st[KEY]) { await chrome.storage.local.set({ [KEY]: cur.ip, [LAST]: 'online' }); return; }
  if (st[KEY] !== cur.ip) {
    await chrome.storage.local.set({ [KEY]: cur.ip, [LAST]: 'rotated' });
    const where = cur.country ? ' (' + cur.country + ')' : '';
    await notify('Proxy IP rotated', st[KEY] + ' \u2192 ' + cur.ip + where + '. Pause account activity and check the session.');
    broadcast({ __jb: 'proxyState', state: 'rotated', from: st[KEY], to: cur.ip, country: cur.country });
  } else if (st[LAST] !== 'online') {
    await chrome.storage.local.set({ [LAST]: 'online' });
    try { await chrome.action.setBadgeText({ text: '' }); } catch(e) {}
    broadcast({ __jb: 'proxyState', state: 'online', ip: cur.ip });
  }
}
function broadcast(msg){
  chrome.tabs.query({}, function(tabs){
    for (const t of tabs) if (t.id != null) chrome.tabs.sendMessage(t.id, msg, () => void chrome.runtime.lastError);
  });
}
/* The watcher is only meaningful when the profile routes through a proxy. The page runtime
   tells the worker whether one is configured; without that signal the worker stays quiet
   rather than polling public IP services on every profile and warning about a proxy that
   does not exist. */
let HAS_PROXY = false;
function armed(){
  return HAS_PROXY;
}
chrome.runtime.onInstalled.addListener(function(){
  chrome.alarms.create('jb-proxy-check', { periodInMinutes: 1 });
});
chrome.alarms.onAlarm.addListener(function(a){ if (a.name === 'jb-proxy-check' && armed()) check(); });
chrome.runtime.onMessage.addListener(function(m, s, send){
  if (m && m.__jb === 'proxyConfig') {
    HAS_PROXY = !!m.hasProxy;
    chrome.storage.local.set({ hasProxy: HAS_PROXY });
    if (HAS_PROXY) check();
    if (send) send({ ok: true });
    return true;
  }
  if (m && m.__jb === 'checkNow') {
    if (!armed()) { if (send) send({ ok: true, skipped: true }); return true; }
    check().then(() => send({ ok: true }));
    return true;
  }
});
";

        private const string ProxyWatchContent = @"
/* Bridge: the service worker owns polling and desktop notifications; the main-world
   runtime owns the in-page banner. This relay keeps the two in sync. */
(function(){
  if (!chrome.runtime || !chrome.runtime.onMessage) return;
  chrome.runtime.onMessage.addListener(function(m){
    if (m && m.__jb === 'proxyState') window.postMessage(m, '*');
  });
  /* The main-world runtime knows whether this profile has a proxy configured. Forward that
     to the worker so it can stay silent on a direct connection. */
  window.addEventListener('message', function(e){
    if (!e.data || e.data.__jb !== 'proxyConfig') return;
    try { chrome.runtime.sendMessage({ __jb: 'proxyConfig', hasProxy: !!e.data.hasProxy }); } catch(err){}
  });
})();
";

        private const string ProxyWatchCss = @"
#jb-banner { font-family: Segoe UI, Arial, sans-serif; }
";

        private const string RightClickWorker = @"
/* Joe Right Click Enable - toolbar button toggles copy / context-menu restoration. */
chrome.runtime.onInstalled.addListener(function(){
  chrome.storage.local.set({ enabled: true });
});
chrome.action.onClicked.addListener(async function(tab){
  const st = await chrome.storage.local.get(['enabled']);
  const next = !(st.enabled !== false);
  await chrome.storage.local.set({ enabled: next });
  try { await chrome.action.setBadgeText({ text: next ? 'ON' : '' }); } catch(e) {}
  if (tab && tab.id != null) chrome.tabs.sendMessage(tab.id, { __jb: 'rightClick', enabled: next }, () => void chrome.runtime.lastError);
});
chrome.runtime.onMessage.addListener(function(m, s, send){
  if (m && m.__jb === 'rightClickState') chrome.storage.local.get(['enabled']).then(send);
  return true;
});
";

        private const string RightClickContent = @"
/* Bridge: forwards toolbar toggles to the main-world runtime, which owns the single
   contextmenu listener for the document. */
(function(){
  if (!chrome.runtime || !chrome.runtime.onMessage) return;
  chrome.runtime.onMessage.addListener(function(m){
    if (m && m.__jb === 'rightClick') window.postMessage(m, '*');
  });
})();
";
    }
}
