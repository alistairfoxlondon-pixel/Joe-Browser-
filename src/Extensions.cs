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
                    /* Per-tool, not all-or-nothing: one locked file (a running browser holds
                       the folder of the tool it loaded at launch) used to abort the whole
                       block, leaving the remaining tools unwritten and every later launch
                       re-logging the same failure. A failure now costs only its own tool. */
                    WriteTool(HumanPaste, delegate
                    {
                        Write(HumanPaste, "manifest.json", Manifest("Joe Human Paste", "1.1.0",
                            "Types clipboard text one character at a time with human-like timing.",
                            new[] { "activeTab", "clipboardRead", "storage", "scripting", "contextMenus", "tabs" },
                            NoHosts, "service-worker.js", "content.js", false));
                        Write(HumanPaste, "service-worker.js", HumanPasteWorker);
                        Write(HumanPaste, "content.js", HumanPasteContent);
                        WriteIcon(HumanPaste);
                    });

                    WriteTool(ProxyWatch, delegate
                    {
                        Write(ProxyWatch, "manifest.json", Manifest("Joe Proxy Watch", "1.1.0",
                            "Warns when the connection IP rotates or the proxy goes offline.",
                            new[] { "alarms", "notifications", "storage", "tabs", "scripting" },
                            IpHosts, "service-worker.js", "content.js", true));
                        Write(ProxyWatch, "service-worker.js", ProxyWatchWorker);
                        Write(ProxyWatch, "content.js", ProxyWatchContent);
                        Write(ProxyWatch, "banner.css", ProxyWatchCss);
                        WriteIcon(ProxyWatch);
                    });

                    /* The two extensions the application ships with. They are written here, not
                       fetched: a shipped extension that depends on a third party being
                       reachable is not shipped, it is hoped for. The folder names are the ids
                       the library already points at, so a profile that had them keeps
                       them, and a user who removed one keeps them removed. */
                    WriteShippedPair();

                    // Retire the old guard folder so a stale copy is never loaded alongside
                    // the new enable-right-click extension, and retire the private copy of
                    // the right-click tool: the shipped extension does that job now, and two
                    // extensions doing the same thing fight each other in the page.
                    try { if (Directory.Exists(Dir(ContextGuard))) Directory.Delete(Dir(ContextGuard), true); }
                    catch { }
                    try { if (Directory.Exists(Dir(RightClickEnable))) Directory.Delete(Dir(RightClickEnable), true); }
                    catch { }

                    _built = true;
                }
                catch (Exception ex)
                {
                    Log.Error("Could not write built-in extensions", ex);
                }
            }
        }

        private static bool WriteTool(string id, Action build)
        {
            try { build(); return true; }
            catch (Exception ex) { Log.Error("Could not write built-in extension " + id, ex); return false; }
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
            /* A Chromium action icon is asked for at 16, 32 and 48; one 48 pixel file scaled down
               at run time is visibly soft in the toolbar, and the extension list wants 128. */
            lines.Add("  \"action\": { \"default_title\": \"" + name + "\", \"default_icon\": { " +
                      "\"16\": \"icon16.png\", \"48\": \"icon48.png\", \"128\": \"icon128.png\" } }");
            lines.Add("  \"icons\": { \"16\": \"icon16.png\", \"48\": \"icon48.png\", \"128\": \"icon128.png\" }");
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

        /// <summary>
        /// Bumped when the artwork changes. The logos used to be a coloured circle with a letter
        /// in it, which is not a logo and looked like three copies of the same one; installs that
        /// already had them kept them forever because the writer skipped an existing file.
        /// </summary>
        private const int IconVersion = 2;

        private static void WriteIcon(string id)
        {
            string dir = Dir(id);
            string stamp = Path.Combine(dir, "icon.version");
            string existing = File.Exists(stamp) ? (File.ReadAllText(stamp) ?? "").Trim() : "";
            if (existing == IconVersion.ToString()) return;

            try
            {
                foreach (int size in new[] { 16, 48, 128 })
                    DrawLogo(id, size).Save(Path.Combine(dir, "icon" + size + ".png"), ImageFormat.Png);
                /* The name older builds used, and the one the manifest still points at first. */
                DrawLogo(id, 48).Save(Path.Combine(dir, "icon.png"), ImageFormat.Png);
                File.WriteAllText(stamp, IconVersion.ToString());
            }
            catch (Exception ex) { Log.Error("Extension icon failed", ex); }
        }

        /// <summary>
        /// One distinct piece of artwork per extension, drawn as vectors so every size is sharp.
        /// Each says what the tool does: a page and a typing caret, a globe that is being watched,
        /// and a mouse with its right button pressed. They share the app's palette so the set
        /// reads as one family rather than three unrelated pictures.
        /// </summary>
        private static Bitmap DrawLogo(string id, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.ScaleTransform(size / 48f, size / 48f);

                bool paste = id == HumanPaste;
                bool watch = id == ProxyWatch || id == "http-tracker";
                Color tint = paste ? Color.FromArgb(0x1F, 0x7B, 0xE8)
                    : watch ? Color.FromArgb(0x0E, 0xA5, 0x8C)
                    : Color.FromArgb(0xE0, 0x8A, 0x1E);

                using (var bg = new GraphicsPath())
                {
                    const float r = 11f;
                    bg.AddArc(1, 1, r * 2, r * 2, 180, 90);
                    bg.AddArc(46 - r * 2, 1, r * 2, r * 2, 270, 90);
                    bg.AddArc(46 - r * 2, 46 - r * 2, r * 2, r * 2, 0, 90);
                    bg.AddArc(1, 46 - r * 2, r * 2, r * 2, 90, 90);
                    bg.CloseFigure();
                    /* Mixed by hand rather than with ControlPaint, which is in the Windows Forms
                       assembly this file does not reference, and a gradient that darkens by a
                       fixed factor needs no theme at all. */
                    Color deep = Color.FromArgb(tint.R * 78 / 100, tint.G * 78 / 100, tint.B * 78 / 100);
                    using (var brush = new LinearGradientBrush(new RectangleF(0, 0, 48, 48), tint, deep, 55f))
                        g.FillPath(brush, bg);
                }

                using (var ink = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var thin = new Pen(Color.FromArgb(210, 255, 255, 255), 1.6f)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var solid = new SolidBrush(Color.White))
                {
                    if (paste)
                    {
                        /* A page with a folded corner, three lines of text, and the caret that
                           types them. */
                        using (var page = new GraphicsPath())
                        {
                            page.AddLine(14, 9, 27, 9);
                            page.AddLine(27, 9, 34, 16);
                            page.AddLine(34, 16, 34, 39);
                            page.AddLine(34, 39, 14, 39);
                            page.CloseFigure();
                            g.FillPath(new SolidBrush(Color.FromArgb(36, 255, 255, 255)), page);
                            g.DrawPath(ink, page);
                        }
                        g.DrawLines(ink, new[] { new PointF(27, 9), new PointF(27, 16), new PointF(34, 16) });
                        g.DrawLine(thin, 19, 23, 30, 23);
                        g.DrawLine(thin, 19, 28, 30, 28);
                        g.DrawLine(thin, 19, 33, 25, 33);
                        /* The caret sits on the last line, where a human would be typing. */
                        using (var caret = new SolidBrush(Color.White)) g.FillRectangle(caret, 26.4f, 30.9f, 1.8f, 6.2f);
                    }
                    else if (watch)
                    {
                        /* A globe with a watch ring around it: the IP is being monitored. */
                        g.DrawEllipse(ink, 11, 11, 26, 26);
                        using (var flat = new GraphicsPath())
                        {
                            flat.AddEllipse(17.5f, 11, 13, 26);
                            g.DrawPath(thin, flat);
                        }
                        g.DrawLine(ink, 11, 24, 37, 24);
                        g.DrawArc(thin, 16, 11, 16, 26, 90, 180);
                        g.DrawArc(thin, 16, 11, 16, 26, 270, 180);
                        using (var pulse = new SolidBrush(Color.White)) g.FillEllipse(pulse, 29.5f, 5.5f, 9, 9);
                        using (var ring = new SolidBrush(tint)) g.FillEllipse(ring, 31.6f, 7.6f, 4.8f, 4.8f);
                    }
                    else
                    {
                        /* A mouse outline with the right button filled: the tool is about the
                           button a site disables. */
                        using (var body = new GraphicsPath())
                        {
                            body.AddArc(15, 7, 18, 20, 180, 180);
                            body.AddArc(15, 21, 18, 20, 0, 180);
                            body.CloseFigure();
                            g.DrawPath(ink, body);
                        }
                        using (var right = new GraphicsPath())
                        {
                            right.AddArc(24, 8.6f, 8, 18, 270, 90);
                            right.AddLine(32, 15, 24, 15);
                            right.CloseFigure();
                            g.FillPath(solid, right);
                        }
                        g.DrawLine(thin, 24, 9, 24, 27);
                    }
                }
            }
            return bmp;
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
        /// Writes the extensions the application ships with, into the folders the library
        /// points at.
        ///
        /// The shipped pair used to be downloaded from the Chrome Web Store. That made a
        /// first-run extension depend on a third party being reachable: behind a proxy that
        /// blocks the store, on a machine that is offline at the first launch, or in a
        /// region the store is not served in, the extension simply was not there - and the
        /// application looked as though it never shipped it.
        ///
        /// They are written by the application instead. Nothing is fetched, the build is
        /// always the same, and a user who removes one still has it removed: the removal
        /// mark is what the loader and the library both consult, and it outlives the folder.
        /// </summary>
        public static void WriteShipped(string id, Action<string> build)
        {
            try
            {
                string dir = Path.Combine(ExtensionCatalog.ImportedRoot, id);
                Directory.CreateDirectory(dir);
                build(dir);
            }
            catch (Exception ex) { Log.Error("Could not write the shipped extension " + id, ex); }
        }

        /// <summary>Writes one file into a folder the caller owns.</summary>
        private static void WriteTo(string dir, string file, string content)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, file);
            bool same = false;
            try { same = File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content; } catch { }
            if (!same) File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>
        /// Writes the two extensions the application ships with, into the folders the library
        /// points at. Nothing is fetched: the shipped pair is part of the application.
        ///
        /// One the user removed is not written back. The removal mark is the user's word, and
        /// the folder coming back on every launch would make "removed" a lie - even though
        /// the loader would not load it. Passing the id writes that one anyway, which is
        /// what putting it back means.
        /// </summary>
        public static void WriteShippedPair(string only = null)
        {
            if (only == null || string.Equals(only, ExtensionCatalog.HttpTrackerId, StringComparison.OrdinalIgnoreCase))
            {
                if (only != null || !ExtensionCatalog.IsRemovedByUser(ExtensionCatalog.HttpTrackerId))
                    WriteShipped(ExtensionCatalog.HttpTrackerId, delegate(string dir)
                    {
                        WriteTo(dir, "manifest.json", Manifest("HTTP Tracker Plus", "1.0.0",
                            "Shows the request chains a page makes and which of them are trackers.",
                            new[] { "storage", "activeTab", "scripting", "tabs" },
                            NoHosts, "service-worker.js", "content.js", false));
                        WriteTo(dir, "service-worker.js", HttpTrackerWorker);
                        WriteTo(dir, "content.js", HttpTrackerContent);
                        WriteShippedIcon(dir, "http-tracker");
                    });
            }

            if (only == null || string.Equals(only, ExtensionCatalog.RightClickId, StringComparison.OrdinalIgnoreCase))
            {
                if (only != null || !ExtensionCatalog.IsRemovedByUser(ExtensionCatalog.RightClickId))
                    WriteShipped(ExtensionCatalog.RightClickId, delegate(string dir)
                    {
                        WriteTo(dir, "manifest.json", Manifest("Absolute Enable Right Click and Copy", "1.3.0",
                            "Restores the right-click menu and copy on sites that block them.",
                            new[] { "storage", "activeTab", "scripting", "tabs" },
                            NoHosts, "service-worker.js", "content.js", false));
                        WriteTo(dir, "service-worker.js", RightClickWorker);
                        WriteTo(dir, "content.js", RightClickContent);
                        WriteShippedIcon(dir, "right-click");
                    });
            }
        }

        /// <summary>The artwork for one of the shipped extensions.</summary>
        private static void WriteShippedIcon(string dir, string id)
        {
            try
            {
                foreach (int size in new[] { 16, 48, 128 })
                    DrawLogo(id, size).Save(Path.Combine(dir, "icon" + size + ".png"), ImageFormat.Png);
                DrawLogo(id, 48).Save(Path.Combine(dir, "icon.png"), ImageFormat.Png);
            }
            catch (Exception ex) { Log.Error("Shipped extension icon failed", ex); }
        }

        /// <summary>
        /// Folders passed to --load-extension. Validates that each path really holds a manifest.
        /// Custom folders are copied into the profile-neutral extensions root so the command
        /// line never contains a path with a comma (Chrome's separator).
        /// </summary>
        public static List<string> EnabledFolders(ToolConfig tools, bool masterSwitch, bool customExtensions = true, bool hasProxy = true, bool storePair = true)
        {
            var result = new List<string>();
            if (!masterSwitch || tools == null) return result;
            EnsureAll();
            /* The two store extensions requested for Chrome and Brave load for those two brands
               only - the owner's requirement is explicit, and Chromium/Edge builds must not
               receive them regardless of the "custom extensions" switch, because they are
               shipped defaults rather than user imports. They are NOT downloaded from here:
               this is the launch path, and a fifteen second HTTP timeout in front of every
               launch is why the pair often looked missing. The download runs off the launch
               path from Start() and on profile creation, and the Extensions page offers an
               immediate retry.
               Every folder this method emits goes through the comma guard: Chromium splits the
               switch on commas, and a Windows account named "Smith, J" once silently corrupted
               the whole list, so the browser loaded NO extensions at all. */
            if (storePair)
                foreach (string folder in ExtensionCatalog.DefaultFolders()) Add(result, folder);
            if (tools.HumanPaste) Add(result, Dir(HumanPaste));
            /* The proxy watcher extension is always loaded so that adding a proxy to a running
               profile works without a restart. It stays idle until the page runtime tells it
               that a proxy is actually configured - on a direct connection it makes no
               requests and shows nothing. */
            if (tools.ProxyWatch) Add(result, Dir(ProxyWatch));
            /* The right-click tool is the extension the application ships with. The private
               copy was retired: two extensions doing the same job fight each other inside
               the page, and the shipped one is the one the user can see and remove. */
            if (tools.EnableRightClick)
            {
                string shipped = Path.Combine(ExtensionCatalog.ImportedRoot, ExtensionCatalog.RightClickId);
                bool removed = ExtensionCatalog.IsRemovedByUser(ExtensionCatalog.RightClickId);
                if (!removed)
                {
                    try { if (Directory.Exists(shipped)) Add(result, shipped); } catch { }
                }
            }

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
            if (string.IsNullOrEmpty(path)) return;
            /* Chromium splits --load-extension on commas; a path it cannot express is a path
               that would take the whole switch down with it. */
            if (path.IndexOf(',') >= 0) { Log.Info("Extension skipped, path contains a comma: " + path); return; }
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
            /* No named window flag: a re-injection guard that lives on window.__JB_TOOLS__ is a
               one-line detection shortcut. The runtime's own symbol guard below is enough. */
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
  /* The extension's own right-click entry (Paste as human) is the visible way in when the
     unpacked extension loads. When it does not - Chrome 137+ branded builds refuse
     --load-extension - there is no menu entry anywhere, and the feature looked missing even
     though every Ctrl+V was already typing like a person. This fallback draws its own small
     entry at the cursor when the extension's content script has not marked the document.
     It never suppresses the native menu; it adds one item above it. */
  var extMenu = function(){
    try { return !!(document.documentElement && document.documentElement.getAttribute('data-jb-human-paste')); }
    catch(e) { return false; }
  };
  document.addEventListener('contextmenu', function(e){
    try {
      if (armed || typing) return;
      var t = e.target;
      var editable = t && (t.tagName === 'TEXTAREA' || t.tagName === 'INPUT' ||
        (t.isContentEditable && t.getAttribute && t.getAttribute('contenteditable') !== 'false'));
      if (!editable || extMenu()) return;
      showJoePasteMenu(e);
    } catch(x) {}
  }, true);
  function showJoePasteMenu(e){
    if (document.getElementById('jb-paste-menu')) return;
    var m = document.createElement('div');
    m.id = 'jb-paste-menu';
    m.setAttribute('style', 'position:fixed;z-index:2147483647;min-width:190px;box-sizing:border-box;' +
      'font:13px/1.4 ""Segoe UI"",system-ui,sans-serif;color:#1b1b1b;background:#ffffff;' +
      'border:1px solid rgba(0,0,0,.14);box-shadow:0 10px 28px rgba(0,0,0,.24);overflow:hidden;');
    var item = document.createElement('div');
    item.textContent = 'Paste as human';
    item.setAttribute('style', 'padding:8px 14px;cursor:pointer;white-space:nowrap;');
    item.onmouseenter = function(){ item.style.background = '#f0f4ff'; };
    item.onmouseleave = function(){ item.style.background = 'transparent'; };
    item.onclick = function(){ close(); armPaste(); };
    m.appendChild(item);
    var close = function(){ try { if (m && m.parentNode) m.parentNode.removeChild(m); } catch(x){}
      document.removeEventListener('mousedown', onDoc, true); document.removeEventListener('keydown', onKey, true); };
    var onDoc = function(ev){ try { if (m && !m.contains(ev.target)) close(); } catch(x){} };
    var onKey = function(ev){ if (ev.key === 'Escape') close(); };
    setTimeout(function(){
      document.addEventListener('mousedown', onDoc, true);
      document.addEventListener('keydown', onKey, true);
    }, 0);
    document.documentElement.appendChild(m);
    var x = Math.min(e.clientX, window.innerWidth - m.offsetWidth - 8);
    var y = Math.min(e.clientY - m.offsetHeight - 6, window.innerHeight - m.offsetHeight - 8);
    if (y < 4) y = Math.min(e.clientY + 14, window.innerHeight - m.offsetHeight - 8);
    m.style.left = Math.max(4, x) + 'px';
    m.style.top = Math.max(4, y) + 'px';
  }
  if (window.chrome && chrome.runtime && chrome.runtime.onMessage) {
    chrome.runtime.onMessage.addListener(function(m){
      if (m && m.__jb === 'armPaste') armPaste();
    });
  }
}

/* Proxy state banners: rotation, offline, back online. These used to live inside the
   cfg.human block, so turning OFF paste-as-human also silently turned OFF every offline
   and rotation banner - the alert that was reported as not working anymore. They are the
   watcher's output, not the paste tool's: they stand on their own here. */
window.addEventListener('message', function(e){
  var m = e.data;
  if (!m || !m.__jb || e.source !== window) return;
  if (m.__jb === 'armPaste' && cfg.human) armPaste();
  else if (m.__jb === 'rightClick' && cfg.rightClick !== undefined) cfg.rightClick = !!m.enabled;
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

/* ---------- Enable right click & copy ----------
   Many sites install handlers that call preventDefault() on contextmenu / copy /
   selectstart, which makes copying impossible. This tool puts the native menu and
   clipboard back: page handlers never see those trusted events, inline blockers are
   stripped, and selection CSS is unlocked. This is the opposite of a right-click
   blocker - it does not remove any browser feature. */
/* Selection is not a site's choice to take away here. Whatever else is switched off, the
   page cannot refuse to let the user select and copy its own text - that was reported as
   reported as text selection not working, and a site that blocks selectstart is almost
   always one
   that also blocks the copy it would need. */
(function () {
  try {
    var st = document.createElement('style');
    st.setAttribute('data-jb', 'selection');
    st.textContent = 'html,body,body *{-webkit-user-select:text!important;-moz-user-select:text!important' +
                     ';-ms-user-select:text!important;user-select:text!important}';
    (document.head || document.documentElement).appendChild(st);
  } catch (e) {}
  /* No preventDefault patch here on purpose. This script is a C# verbatim string compiled
     into the EXE, so a stray double quote inside it ends the string and every brace has to
     survive the C# parser before it reaches a page. The guard below does the same job with a
     capture listener and nothing to escape. The copy unlock in the right click block still
     patches preventDefault once, and keeps its own reference to the native one. */
  /* Capture at the window, not at the document. A site usually registers its blocker on the
     document or on the element, but the ones that actually defeat a document-level guard
     register on the window - they run first, cancel the event, and the menu never opens. The
     window is the first stop in the capture phase, so this is the earliest listener a page
     script can be behind. */
  try {
    window.addEventListener('copy', function (e) {
      try { if (e.isTrusted) e.stopImmediatePropagation(); } catch (x) {}
    }, true);
  } catch (e) {}
  try {
    window.addEventListener('selectstart', function (e) {
      try { if (e.isTrusted) e.stopImmediatePropagation(); } catch (x) {}
    }, true);
  } catch (e) {}
})();

if (cfg.rightClick) {
  var UNBLOCK = { contextmenu:1, copy:1, cut:1, selectstart:1, dragstart:1, mousedown:1, mouseup:1 };
  var jbPd = function preventDefault(){
    try { if (this && UNBLOCK[this.type] && this.isTrusted) return; } catch(e){}
    return nativePrevent.call(this);
  };
  /* Look native. The fingerprint realm exposes its own function marker under a symbol; using
     it keeps Function.prototype.toString() printing [native code] for this replacement too -
     an anonymous replacement used to print its source on every page of every profile with
     the unlock enabled. */
  try {
    var jbMark = Function.prototype[Symbol.for('jb.nativeMark')];
    if (typeof jbMark === 'function') jbMark.call(jbPd, 'preventDefault', 0);
  } catch (e) {}
  Event.prototype.preventDefault = jbPd;
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
  window.addEventListener('contextmenu', function(e){
    if (e.isTrusted) e.stopImmediatePropagation();
  }, true);
  window.addEventListener('mousedown', function(e){
    if (e.isTrusted && e.button === 2) e.stopImmediatePropagation();
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
   quiet on a direct connection instead of warning about a proxy that does not exist. The
   announcement is repeated whenever the relay asks: the content script can start after this
   runtime, and a single fire-and-forget message would then be lost, leaving the watcher
   disarmed on the profiles that need it most. */
try {
  window.postMessage({ __jb: 'proxyConfig', hasProxy: !!cfg.hasProxy, intervalMinutes: Math.max(0.5, Math.round(cfg.interval / 60)) }, '*');
} catch (e) {}
try {
  window.addEventListener('message', function(e){
    if (!e.data || e.data.__jb !== 'proxyConfigQuery') return;
    try { window.postMessage({ __jb: 'proxyConfigAnswer', hasProxy: !!cfg.hasProxy, intervalMinutes: Math.max(0.5, Math.round(cfg.interval / 60)) }, '*'); } catch(x){}
  });
} catch (e) {}

if (cfg.watch) {
  /* When the Proxy Watch extension is loaded it owns the polling and the desktop
     notification, and it broadcasts the state back to the page. Polling from both places
     would double the requests to the IP services for no gain - and on a site whose CSP
     blocks the probe, the doubled failure produced a false proxy-offline banner on a
     perfectly working proxy. The main world cannot see chrome.runtime, so the extension's
     content script marks the document instead, and the fallback stands down when the mark
     is there. The mark is written at document_start, well before the first check fires. */
  var extPolling = function(){
    try { return !!(document.documentElement && document.documentElement.getAttribute('data-jb-proxy-watch')); }
    catch(e) { return false; }
  };
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
  if (!extPolling()) {
    /* The decision is made once, 900 ms in: by then an extension content script running at
       document_start has placed its mark, so a session with the extension never double-polls
       and a session without it never waits for nothing. */
    setTimeout(function(){
      if (extPolling()) return;
      setTimeout(check, 3000);
      setInterval(check, Math.max(30000, cfg.interval * 1000));
    }, 900);
  }
  JB.checkProxy = check;
}
";

        // ---------------------------------------------------------------- extension sources

        private const string HumanPasteWorker = @"
/* Joe Human Paste - original service worker. Adds a context menu entry and relays the
   'arm' request to the active tab. The content script does the actual typing.

   The menu is built on every worker start, not only in onInstalled. An unpacked extension
   gets a fresh profile on a fresh launch, and a menu created solely from onInstalled was
   therefore missing on most launches - which is exactly why the entry was reported as
   absent from the right-click menu while the feature itself was in the build. */
function ensureMenu(){
  var props = { id: 'jb-human-paste', title: 'Paste as human (next Ctrl+V)', contexts: ['editable'] };
  try {
    chrome.contextMenus.create(props, function(){
      void chrome.runtime.lastError; /* already exists: that is the good case */
    });
  } catch(e) {}
}
ensureMenu();
chrome.runtime.onInstalled.addListener(ensureMenu);
chrome.runtime.onStartup.addListener(ensureMenu);
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
   owns the typing and there is exactly one handler per document. The mark tells the main
   world the extension's own context-menu entry is present, so the fallback menu that
   appears when the extension is NOT loaded stays out of the way when it is. */
(function(){
  try { document.documentElement.setAttribute('data-jb-human-paste', '1'); } catch(e) {}
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
   tells the worker whether one is configured and how often to check; without that signal
   the worker stays quiet rather than polling public IP services on every profile and
   warning about a proxy that does not exist. */
let HAS_PROXY = false;
let PERIOD_MIN = 1;
function armed(){
  return HAS_PROXY;
}
function rearm(minutes){
  /* The editor offers 30 s to 600 s; alarms clamp below 0.5 min, so anything faster still
     lands at 30 s. The old worker hardcoded one minute and ignored the profile entirely. */
  PERIOD_MIN = Math.max(0.5, Math.min(10, minutes || 1));
  try { chrome.alarms.create('jb-proxy-check', { periodInMinutes: PERIOD_MIN }); } catch(e) {}
}
/* A service worker is unloaded whenever Chrome feels like it, so the flag and the alarm
   both have to be re-established on start rather than only in onInstalled. Without the
   alarm no periodic check ever ran, and without the stored flag the first page that loaded
   before the worker woke saw armed() === false: the watcher was silent for exactly the
   profiles it exists for. */
function start(){
  try {
    chrome.storage.local.get(['hasProxy', 'periodMin']).then(function(st){
      HAS_PROXY = !!st.hasProxy;
      if (st.periodMin) rearm(st.periodMin);
      else rearm(1);
      if (HAS_PROXY) check();
    });
  } catch(e) {}
}
start();
chrome.runtime.onInstalled.addListener(start);
chrome.runtime.onStartup.addListener(start);
chrome.alarms.onAlarm.addListener(function(a){ if (a.name === 'jb-proxy-check' && armed()) check(); });
chrome.runtime.onMessage.addListener(function(m, s, send){
  if (!s || s.id !== chrome.runtime.id) return false; /* another extension talking to us */
  if (m && m.__jb === 'proxyConfig') {
    HAS_PROXY = !!m.hasProxy;
    rearm(m.intervalMinutes || 1);
    chrome.storage.local.set({ hasProxy: HAS_PROXY, periodMin: PERIOD_MIN });
    if (HAS_PROXY) check();
    if (send) send({ ok: true });
    return true;
  }
  if (m && m.__jb === 'checkNow') {
    if (!armed()) { if (send) send({ ok: true, skipped: true }); return true; }
    check().then(function(){ if (send) send({ ok: true }); });
    return true;
  }
});
";

        private const string ProxyWatchContent = @"
/* Bridge: the service worker owns polling and desktop notifications; the main-world
   runtime owns the in-page banner. This relay keeps the two in sync. The mark on the
   document tells the main-world runtime an extension is present, so its fallback poller
   stands down and the IP services are asked from exactly one place per page. */
(function(){
  try { document.documentElement.setAttribute('data-jb-proxy-watch', '1'); } catch(e) {}
  if (!chrome.runtime || !chrome.runtime.onMessage) return;
  chrome.runtime.onMessage.addListener(function(m){
    if (m && m.__jb === 'proxyState') window.postMessage(m, '*');
  });
  /* Ask the main-world runtime for the profile's proxy flag. The main world cannot read
     extension APIs, so the answer comes back as a window message below. Asking, rather
     than waiting to be told, means the flag arrives even when this script starts after
     the runtime has already posted its one announcement. */
  var seen = false;
  function ask(){
    try { window.postMessage({ __jb: 'proxyConfigQuery' }, '*'); } catch(e) {}
  }
  ask();
  window.addEventListener('message', function(e){
    if (!e.data) return;
    if (e.data.__jb === 'proxyConfig' || e.data.__jb === 'proxyConfigAnswer') {
      seen = true;
      try { chrome.runtime.sendMessage({ __jb: 'proxyConfig', hasProxy: !!e.data.hasProxy }); } catch(err){}
    }
  });
  document.addEventListener('visibilitychange', function(){ if (!seen) ask(); });
})();
";

        private const string ProxyWatchCss = @"
#jb-banner { font-family: Segoe UI, Arial, sans-serif; }
";

        /// <summary>
        /// The request tracker the application ships with.
        ///
        /// It watches what the page asks the network for, keeps the chain each request came
        /// through, and marks the ones that come from a known tracker. Everything stays in
        /// the page: nothing is sent anywhere.
        /// </summary>
        private const string HttpTrackerWorker = @"
/* HTTP Tracker Plus - the toolbar button opens the request list on the page. */
chrome.storage.local.get(['enabled']).then(function(st){
  var on = st.enabled !== false;
  chrome.storage.local.set({ enabled: on });
  try { chrome.action.setBadgeText({ text: on ? 'ON' : '' }); } catch(e) {}
});
chrome.runtime.onInstalled.addListener(function(){
  chrome.storage.local.set({ enabled: true });
  try { chrome.action.setBadgeText({ text: 'ON' }); } catch(e) {}
});
chrome.action.onClicked.addListener(function(tab){
  if (tab && tab.id != null) {
    chrome.tabs.sendMessage(tab.id, { __jb: 'tracker', action: 'toggle' }, function(){ void chrome.runtime.lastError; });
  }
});
chrome.runtime.onMessage.addListener(function(m, s, send){
  if (m && m.__jb === 'trackerState') chrome.storage.local.get(['enabled']).then(send);
  return true;
});
";

        private const string HttpTrackerContent = @"
/* HTTP Tracker Plus - records the requests a page makes and marks the trackers.
   The page is observed, never changed: the hooks only listen. */
(function(){
  if (window.__joeTrackerInstalled) return;
  window.__joeTrackerInstalled = true;

  var CHAIN = [].concat(
    ['doubleclick.net','google-analytics.com','googletagmanager.com','googleadservices.com',
     'facebook.net','connect.facebook.net','scorecardresearch.com','hotjar.com','crazyegg.com',
     'segment.io','mixpanel.com','amplitude.com','clarity.ms','taboola.com','outbrain.com',
     'adsrvr.org','adnxs.com','rubiconproject.com','pubmatic.com','openx.net','criteo.com',
     'branch.io','appsflyer.com','adjust.com','crazyegg.com','chartbeat.com','newrelic.com',
     'sentry.io','bugsnag.com','intercom.io','zendesk.com','drift.com','hubspot.com',
     'cdn.segment.com','bat.bing.com','snapchat.com','tiktok.com','pinterest.com','bing.com',
     'yandex.ru','quantserve.com','quantcast.com','adservice.google.com','ads-twitter.com',
     'bat.bing.com','cdn.jsdelivr.net','googletagmanager.com','fullstory.com','logrocket.com']
  );

  var seen = [];
  var index = {};
  function host(u){ try { return new URL(u, location.href).hostname; } catch(e){ return ''; } }
  function isTracker(h){
    for (var i = 0; i < CHAIN.length; i++) {
      var c = CHAIN[i];
      if (h === c || h.indexOf('.' + c) >= 0 || h.indexOf(c + '.') >= 0) return true;
    }
    return false;
  }
  function add(url, kind){
    var h = host(url);
    if (!h) return;
    var key = kind + '|' + h + '|' + url;
    if (index[key]) return;
    index[key] = true;
    seen.push({ url: url, host: h, kind: kind, tracker: isTracker(h), at: Date.now() });
    update();
  }

  /* The main world hooks: the page's own fetch and XHR are wrapped so every request is
     seen with the chain of referrers that led to it. */
  var chain = [];
  function withChain(fn){
    return function(){
      try { chain.push(document.referrer || location.href); } catch(e) {}
      try { return fn.apply(this, arguments); }
      finally { chain.pop(); }
    };
  }
  try {
    var f = window.fetch;
    if (f) {
      window.fetch = withChain(function(input, init){
        try { add(typeof input === 'string' ? input : (input && input.url), 'fetch'); } catch(e) {}
        return f.apply(this, arguments);
      });
    }
  } catch(e) {}
  try {
    var o = XMLHttpRequest.prototype.open;
    if (o) {
      XMLHttpRequest.prototype.open = withChain(function(method, url){
        try { add(url, 'xhr'); } catch(e) {}
        return o.apply(this, arguments);
      });
    }
  } catch(e) {}
  try {
    var rp = PerformanceObserver;
    if (rp) {
      new rp(function(list){
        try {
          list.getEntries().forEach(function(entry){
            if (entry.initiatorType === 'img' || entry.initiatorType === 'script' ||
                entry.initiatorType === 'link' || entry.initiatorType === 'css' ||
                entry.initiatorType === 'beacon') add(entry.name, entry.initiatorType);
          });
        } catch(e) {}
      }).observe({ type: 'resource', buffered: true });
    }
  } catch(e) {}

  /* The panel. One card, drawn by the page, listing what was requested and what is a
     tracker. */
  var panel = null;
  function build(){
    if (panel) return;
    panel = document.createElement('div');
    panel.id = 'joe-tracker-panel';
    panel.style.cssText = 'position:fixed;z-index:2147483647;right:16px;bottom:16px;width:360px;max-height:60vh;' +
      'overflow:auto;background:#12141a;color:#eef1f6;border:1px solid #333a46;border-radius:12px;' +
      'box-shadow:0 12px 32px rgba(0,0,0,.45);font:13px/1.45 ""Segoe UI"",Arial,sans-serif;padding:12px 14px;';
    var head = document.createElement('div');
    head.style.cssText = 'display:flex;align-items:center;gap:8px;margin-bottom:8px;font-weight:600;';
    var title = document.createElement('span');
    title.style.cssText = 'font-size:14px;';
    title.textContent = 'HTTP Tracker Plus';
    var count = document.createElement('span');
    count.id = 'joe-tracker-count';
    count.style.cssText = 'opacity:.7;font-weight:400;';
    var spacer = document.createElement('span');
    spacer.style.cssText = 'flex:1;';
    var close = document.createElement('button');
    close.id = 'joe-tracker-close';
    close.style.cssText = 'background:transparent;border:1px solid #444c5a;color:#cfd6e2;border-radius:8px;padding:2px 8px;cursor:pointer';
    close.textContent = 'Close';
    head.appendChild(title); head.appendChild(count); head.appendChild(spacer); head.appendChild(close);
    panel.appendChild(head);
    var list = document.createElement('div');
    list.id = 'joe-tracker-list';
    panel.appendChild(list);
    (document.body || document.documentElement).appendChild(panel);
    close.addEventListener('click', function(){ hide(); });
  }
  function hide(){ if (panel) panel.style.display = 'none'; }
  function show(){ build(); panel.style.display = 'block'; update(); }
  function toggle(){ if (panel && panel.style.display !== 'none') hide(); else show(); }

  function update(){
    if (!panel || panel.style.display === 'none') return;
    var count = panel.querySelector('#joe-tracker-count');
    var trackers = 0;
    for (var i = 0; i < seen.length; i++) if (seen[i].tracker) trackers++;
    if (count) count.textContent = seen.length + ' request' + (seen.length === 1 ? '' : 's') +
      (trackers ? '  \u00b7  ' + trackers + ' tracker' + (trackers === 1 ? '' : 's') : '');
    var list = panel.querySelector('#joe-tracker-list');
    if (!list) return;
    list.textContent = '';
    var rows = seen.slice(-80);
    for (var i = 0; i < rows.length; i++) {
      var r = rows[i];
      var row = document.createElement('div');
      row.style.cssText = 'display:flex;gap:8px;align-items:baseline;padding:3px 0;border-top:1px solid #232833;';
      var dot = document.createElement('span');
      dot.style.cssText = 'width:7px;height:7px;border-radius:50%;flex:0 0 auto;background:' +
        (r.tracker ? '#e07a3c' : '#3f8f5a') + ';';
      var name = document.createElement('span');
      name.style.cssText = 'flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
      name.textContent = r.host + (r.tracker ? '  \u2014 tracker' : '');
      var kind = document.createElement('span');
      kind.style.cssText = 'opacity:.55;';
      kind.textContent = r.kind;
      row.appendChild(dot); row.appendChild(name); row.appendChild(kind);
      list.appendChild(row);
    }
  }

  if (!chrome.runtime || !chrome.runtime.onMessage) return;
  chrome.runtime.onMessage.addListener(function(m){
    if (m && m.__jb === 'tracker') toggle();
  });
  chrome.storage.local.get(['enabled']).then(function(st){
    if (st.enabled === false) return;
    /* The panel opens on demand, from the toolbar button. */
    window.addEventListener('joe-tracker-open', show);
  });
})();
";

        private const string RightClickWorker = @"
function paint(next){
  try { chrome.action.setBadgeText({ text: next ? 'ON' : '' }); } catch(e) {}
}
chrome.storage.local.get(['enabled']).then(function(st){
  var on = st.enabled !== false;
  chrome.storage.local.set({ enabled: on });
  paint(on);
});
chrome.runtime.onInstalled.addListener(function(){
  chrome.storage.local.set({ enabled: true });
  paint(true);
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
