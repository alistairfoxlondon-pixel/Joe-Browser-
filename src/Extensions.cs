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
        public static List<string> EnabledFolders(ToolConfig tools, bool masterSwitch, bool customExtensions = true)
        {
            var result = new List<string>();
            if (!masterSwitch || tools == null) return result;
            EnsureAll();
            if (tools.HumanPaste) Add(result, Dir(HumanPaste));
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
            sb.AppendLine("  watch: " + (tools.ProxyWatch ? "true" : "false") + ",");
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
var JB = window.__JB_API__ = window.__JB_API__ || {};
var cfg = CFG;
/* Keep the native method: the right-click unlocker below deliberately replaces
   preventDefault for page-driven events, but our own paste handler still needs it. */
var nativePrevent = Event.prototype.preventDefault;

function banner(title, body, tone, sticky){
  try {
    var id = 'jb-banner';
    var el = document.getElementById(id);
    if (!el) {
      if (!document.body) { document.addEventListener('DOMContentLoaded', function(){ banner(title, body, tone, sticky); }, {once:true}); return; }
      el = document.createElement('div');
      el.id = id;
      el.setAttribute('style', 'position:fixed;z-index:2147483647;right:18px;bottom:18px;max-width:360px;' +
        'font:13px/1.45 -apple-system,Segoe UI,Roboto,Arial,sans-serif;padding:13px 15px;border-radius:10px;' +
        'box-shadow:0 10px 34px rgba(0,0,0,.34);color:#fff;background:#1b2430;' +
        'border-left:4px solid #3da9fc;opacity:.98;pointer-events:auto;');
      document.body.appendChild(el);
    }
    el.style.borderLeftColor = tone === 'warn' ? '#ffb02e' : (tone === 'bad' ? '#ff5c5c' : '#3da9fc');
    el.innerHTML = '';
    var h = document.createElement('div');
    h.textContent = title;
    h.style.fontWeight = '600';
    h.style.marginBottom = '4px';
    var p = document.createElement('div');
    p.textContent = body;
    p.style.opacity = '.86';
    var x = document.createElement('span');
    x.textContent = '\u00d7';
    x.setAttribute('style', 'position:absolute;top:6px;right:10px;cursor:pointer;opacity:.6;font-size:17px');
    x.onclick = function(){ el.remove(); };
    el.appendChild(x); el.appendChild(h); el.appendChild(p);
    clearTimeout(el.__timer);
    if (!sticky) el.__timer = setTimeout(function(){ if (el.parentNode) el.remove(); }, 9000);
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
function insertChar(ch){
  try {
    if (document.execCommand) return document.execCommand('insertText', false, ch);
  } catch(e) {}
  return false;
}
async function humanType(text){
  if (typing) return;
  typing = true;
  try {
    if (text.length > 20000) text = text.substring(0, 20000);
    var lastWasSpace = false;
    for (var i = 0; i < text.length; i++) {
      var ch = text.charAt(i);
      var ok = insertChar(ch === '\n' ? '\n' : ch);
      if (!ok) {
        var a = document.activeElement;
        if (a && (a.tagName === 'INPUT' || a.tagName === 'TEXTAREA')) {
          var s = a.selectionStart == null ? a.value.length : a.selectionStart;
          var e = a.selectionEnd == null ? s : a.selectionEnd;
          a.value = a.value.substring(0, s) + ch + a.value.substring(e);
          try { a.setSelectionRange(s + 1, s + 1); } catch(_){}
          a.dispatchEvent(new Event('input', { bubbles: true }));
        }
      }
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
    var arm = armed || e.shiftKey;
    if (!arm || typing) return;
    var text = (e.clipboardData || window.clipboardData);
    text = text ? text.getData('text/plain') : '';
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
      if (m.state === 'rotated') banner('Proxy IP rotated', (m.from || '?') + ' \u2192 ' + (m.to || '?') + (m.country ? ' (' + m.country + ')' : '') + '. Pause logins.', 'warn', true);
      else if (m.state === 'offline') { if (cfg.hasProxy) banner('Proxy appears offline', 'The connection check failed twice. Check it before logging in.', 'bad', true); }
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
if (cfg.watch) {
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
      banner('Proxy IP rotated', base + ' \u2192 ' + now.ip + where + '. Pause logins and check the account.', 'warn', true);
      try { if (window.Notification && Notification.permission === 'granted') new Notification('Joe Browser', { body: 'Proxy IP changed: ' + base + ' \u2192 ' + now.ip }); } catch(e){}
      window.dispatchEvent(new CustomEvent('jb-proxy-rotated', { detail: { from: base, to: now.ip } }));
    } else if (readStore(SEEN, '') === 'offline') {
      writeStore(SEEN, '');
      banner('Proxy is back online', now.ip + (now.country ? ' \u00b7 ' + now.country : ''), 'info', false);
    }
  }
  setTimeout(check, 3500);
  setInterval(check, Math.max(30000, cfg.interval * 1000));
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
chrome.runtime.onInstalled.addListener(function(){
  chrome.alarms.create('jb-proxy-check', { periodInMinutes: 1 });
  check();
});
chrome.alarms.onAlarm.addListener(function(a){ if (a.name === 'jb-proxy-check') check(); });
chrome.runtime.onMessage.addListener(function(m, s, send){
  if (m && m.__jb === 'checkNow') { check().then(() => send({ ok: true })); return true; }
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
