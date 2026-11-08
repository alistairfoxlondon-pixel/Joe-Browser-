using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Windows UI Fabric (virtualvivek/windows-ui) design tokens.
    /// Dark: background #111, navbar #202020, cards #303030, accent #60CDFF.
    /// Light: background #FFF, navbar #F3F3F3, cards #FBFBFB, accent #0078D7.
    /// Type is Segoe UI at the framework's 15px base size.
    /// </summary>
    internal static class Theme
    {
        public static bool Dark = true;

        private struct Pal
        {
            public Color Bg, BgSoft, Sidebar, Card, Panel, PanelHover, Panel2, Input, Line, LineSoft;
            public Color Text, TextDim, TextFaint, OnAccent;
            public Color Accent, AccentHover, AccentPressed;
            public Color ButtonBg, ButtonBorder, Success, Danger;
        }

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        private static Pal NewPal(
            Color bg, Color bgSoft, Color sidebar, Color card, Color panel, Color panelHover, Color panel2, Color input,
            Color line, Color lineSoft, Color text, Color textDim, Color textFaint,
            Color accent, Color accentHover, Color accentPressed, Color onAccent,
            Color buttonBg, Color buttonBorder, Color success, Color danger)
        {
            var p = new Pal();
            p.Bg = bg; p.BgSoft = bgSoft; p.Sidebar = sidebar; p.Card = card;
            p.Panel = panel; p.PanelHover = panelHover; p.Panel2 = panel2; p.Input = input;
            p.Line = line; p.LineSoft = lineSoft;
            p.Text = text; p.TextDim = textDim; p.TextFaint = textFaint;
            p.Accent = accent; p.AccentHover = accentHover; p.AccentPressed = accentPressed;
            p.OnAccent = onAccent;
            p.ButtonBg = buttonBg; p.ButtonBorder = buttonBorder;
            p.Success = success; p.Danger = danger;
            return p;
        }

        private static readonly Pal DarkPal = NewPal(
            bg: C(0x11, 0x11, 0x11), bgSoft: C(0x18, 0x18, 0x18), sidebar: C(0x20, 0x20, 0x20),
            card: C(0x30, 0x30, 0x30), panel: C(0x2B, 0x2B, 0x2B), panelHover: C(0x33, 0x33, 0x33),
            panel2: C(0x26, 0x26, 0x26), input: C(0x11, 0x11, 0x11),
            line: C(0x28, 0x28, 0x28), lineSoft: C(0x1F, 0x1F, 0x1F),
            text: C(0xFF, 0xFF, 0xFF), textDim: C(0xD2, 0xD2, 0xD2), textFaint: C(0xAF, 0xAF, 0xAF),
            accent: C(0x60, 0xCD, 0xFF), accentHover: C(0x87, 0xDA, 0xFF), accentPressed: C(0x4C, 0xB8, 0xE6),
            onAccent: C(0x00, 0x00, 0x00),
            buttonBg: C(0x3E, 0x3E, 0x3E), buttonBorder: C(0x3A, 0x3A, 0x3A),
            success: C(0x6A, 0xCB, 0x5D), danger: C(0xFF, 0x99, 0xA3));

        private static readonly Pal LightPal = NewPal(
            bg: C(0xFF, 0xFF, 0xFF), bgSoft: C(0xF7, 0xF7, 0xF7), sidebar: C(0xF3, 0xF3, 0xF3),
            card: C(0xFB, 0xFB, 0xFB), panel: C(0xFB, 0xFB, 0xFB), panelHover: C(0xF0, 0xF0, 0xF0),
            panel2: C(0xE8, 0xE8, 0xE8), input: C(0xFF, 0xFF, 0xFF),
            line: C(0xE0, 0xE0, 0xE0), lineSoft: C(0xED, 0xED, 0xED),
            text: C(0x1A, 0x1A, 0x1A), textDim: C(0x50, 0x50, 0x50), textFaint: C(0x60, 0x60, 0x60),
            accent: C(0x00, 0x78, 0xD7), accentHover: C(0x1A, 0x88, 0xDD), accentPressed: C(0x00, 0x5A, 0x9E),
            onAccent: C(0xFF, 0xFF, 0xFF),
            buttonBg: C(0xFE, 0xFE, 0xFE), buttonBorder: C(0xDF, 0xDF, 0xDF),
            success: C(0x2C, 0x8B, 0x2C), danger: C(0xC5, 0x05, 0x00));

        private static Pal P = DarkPal;

        public static Color Bg { get { return P.Bg; } }
        public static Color BgSoft { get { return P.BgSoft; } }
        public static Color Sidebar { get { return P.Sidebar; } }
        public static Color Card { get { return P.Card; } }
        public static Color Panel { get { return P.Panel; } }
        public static Color PanelHover { get { return P.PanelHover; } }
        public static Color Panel2 { get { return P.Panel2; } }
        public static Color Input { get { return P.Input; } }
        public static Color Line { get { return P.Line; } }
        public static Color LineSoft { get { return P.LineSoft; } }
        public static Color Text { get { return P.Text; } }
        public static Color TextDim { get { return P.TextDim; } }
        public static Color TextFaint { get { return P.TextFaint; } }
        public static Color Accent { get { return P.Accent; } }
        public static Color AccentHi { get { return P.AccentHover; } }
        public static Color Accent2 { get { return P.AccentPressed; } }
        public static Color OnAccent { get { return P.OnAccent; } }
        public static Color ButtonBg { get { return P.ButtonBg; } }
        public static Color ButtonBorder { get { return P.ButtonBorder; } }
        public static Color Success { get { return P.Success; } }
        public static Color Danger { get { return P.Danger; } }

        /// <summary>Danger filled surfaces put black text on the pale dark-theme red.</summary>
        public static Color Ink { get { return P.Danger; } }
        public static Color InkText { get { return Dark ? C(0x00, 0x00, 0x00) : C(0xFF, 0xFF, 0xFF); } }

        /// <summary>The bottom hairline Windows gives every text field.</summary>
        public static Color InputUnderline { get { return Dark ? C(0x77, 0x77, 0x77) : C(0x8A, 0x8A, 0x8A); } }

        public static Color Scrim { get { return Color.FromArgb(Dark ? 140 : 60, 0, 0, 0); } }
        public static Color AccentOnBg { get { return Color.FromArgb(Dark ? 34 : 22, Accent.R, Accent.G, Accent.B); } }
        public static Color AccentBorder { get { return Color.FromArgb(Dark ? 130 : 110, Accent.R, Accent.G, Accent.B); } }
        public static Color AccentText { get { return Dark ? C(0x60, 0xCD, 0xFF) : C(0x00, 0x5A, 0x9E); } }
        public static Color HoverOverlay { get { return Color.FromArgb(Dark ? 20 : 16, Text.R, Text.G, Text.B); } }
        public static Color SelOverlay { get { return Color.FromArgb(Dark ? 34 : 24, Text.R, Text.G, Text.B); } }

        /// <summary>--color-ui-hover-strong, used by navbar rows and cards.</summary>
        public static Color HoverStrong { get { return Dark ? C(0x2D, 0x2D, 0x2D) : C(0xE8, 0xE8, 0xE8); } }
        /// <summary>--color-ui-active-default.</summary>
        public static Color ActiveBg { get { return Dark ? C(0x26, 0x26, 0x26) : C(0xD8, 0xD8, 0xD8); } }

        // windows-ui control metrics
        public const int ControlHeight = 33;
        public const int ButtonRadius = 4;
        public const int CardRadius = 8;
        public const int NavbarWidth = 280;

        public static readonly int Radius = CardRadius;
        public static int RSmall { get { return Ui.S(ButtonRadius); } }
        public static int RMedium { get { return Ui.S(ButtonRadius); } }
        public static int RLarge { get { return Ui.S(CardRadius); } }
        public static int RXLarge { get { return Ui.S(CardRadius); } }

        // ---- typography: Segoe UI, 15px base, exactly like windows-ui ----
        public static string FamilyName = "Segoe UI";

        private static FontFamily _family;
        private static FontFamily _familySemiBold;
        private static FontFamily _familyDisplay;
        private static FontFamily _familyDisplaySemiBold;
        private static PrivateFontCollection _pfc;
        private static FontFamily _iconFamily;
        private static FontFamily _iconFamilySolid;
        private static readonly Dictionary<long, Font> _iconFonts = new Dictionary<long, Font>();
        private static readonly List<string> _installed = new List<string>();
        private static readonly List<string> _privateInstalled = new List<string>();

        /* GDI+ keeps a FontFamily created from a PrivateFontCollection alive only while that
           collection is alive. A collection built in a local variable is collected as soon as
           LoadFonts returns, and every later Font built from the family throws or paints
           nothing - which is exactly "the icons are missing". Every collection therefore has
           to stay reachable for the life of the process. */
        private static readonly List<PrivateFontCollection> _fontCollections = new List<PrivateFontCollection>();

        private static bool _fontsLoaded;
        private static bool _fontsDirResolved;
        private static string _fontsDir;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string lpszFilename, int fl, IntPtr pdv);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RemoveFontResourceEx(string lpszFilename, int fl, IntPtr pdv);
        private const int FR_PRIVATE = 0x10, FR_NOT_ENUM = 0x20;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr res);
        private const int HWND_BROADCAST = 0xffff, WM_FONTCHANGE = 0x001d;

        public static void LoadFonts()
        {
            /* Settings dialogs and the diagnostic tools can ask for this more than once.
               Re-registering every face each time leaks GDI handles and makes the icon cache
               inconsistent, so the whole load is done exactly once per process. */
            if (_fontsLoaded) return;
            _fontsLoaded = true;

            LoadIconFont();

            /* DevToys (and every WinUI 3 app) sets its type in Segoe UI Variable, the optical
               -size family that replaced plain Segoe UI in Windows 11: text sizes get a wider
               spacing, display sizes a tighter one, so headings stop looking like scaled-up
               body copy. It does not exist on Windows 10, where Segoe UI is the same face
               DevToys itself falls back to, so the stack is identical to the reference app on
               both systems. Inter/Open Sans ship as the last resort for stripped installs. */
            FontFamily system = TryFamily("Segoe UI Variable Text");
            if (system != null)
            {
                /* GDI+ cannot address the weight axis of a variable font: asking for Bold on
                   "Segoe UI Variable Text" makes it embolden the Regular outlines
                   algorithmically, which smears every stem and is exactly what "the type looks
                   broken" looks like. Windows exposes the real instances as their own family
                   names, so bind Semibold explicitly and refuse to synthesise. */
                FontFamily semibold = TryFamily("Segoe UI Variable Text Semibold")
                                   ?? TryFamily("Segoe UI Variable Semibold");
                if (semibold != null)
                {
                    _family = system;
                    _familySemiBold = semibold;
                    /* WinUI switches to the Display optical size above 20 px: the letterforms
                       tighten and the spacing closes up, so a heading stops looking like body
                       copy scaled up. */
                    _familyDisplay = TryFamily("Segoe UI Variable Display") ?? system;
                    _familyDisplaySemiBold = TryFamily("Segoe UI Variable Display Semibold") ?? semibold;
                    FamilyName = "Segoe UI Variable Text";
                    return;
                }
                /* No named Semibold instance: use plain Segoe UI, which has one. */
            }

            system = TryFamily("Segoe UI");
            if (system != null)
            {
                _family = system;
                _familySemiBold = TryFamily("Segoe UI Semibold");
                FamilyName = "Segoe UI";
                return;
            }

            var files = BundledFonts();
            PruneFontCache();
            for (int i = 0; i < files.Count; i++) files[i] = Cached(files[i]);
            foreach (string file in files)
            {
                try { AddFontResourceEx(file, 0, IntPtr.Zero); _installed.Add(file); }
                catch { }
            }
            if (files.Count > 0)
            {
                try { IntPtr r; SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, 0, 1000, out r); }
                catch { }
            }

            if (files.Count > 0)
            {
                try
                {
                    _pfc = new PrivateFontCollection();
                    foreach (string file in files) _pfc.AddFontFile(file);
                    foreach (string baseName in new[] { "Open Sans", "Inter" })
                    {
                        if (BindFamily(baseName)) return;
                    }
                }
                catch { }
            }

            foreach (string n in new[] { "Segoe UI Variable Text", "Segoe UI", "Arial" })
            {
                FontFamily f = TryFamily(n);
                if (f != null) { _family = f; FamilyName = n; return; }
            }
            foreach (string n in new[] { "Tahoma" })
            {
                FontFamily f = TryFamily(n);
                if (f != null) { _family = f; FamilyName = n; return; }
            }
        }

        private static bool BindFamily(string baseName)
        {
            _family = Pick(_pfc, baseName);
            if (_family == null) return false;
            _familySemiBold = PickWeight(_pfc, baseName, "SemiBold", "Semibold", "DemiBold");
            FamilyName = baseName;
            return true;
        }

        private static FontFamily PickWeight(PrivateFontCollection pfc, string baseName, params string[] suffixes)
        {
            foreach (string s in suffixes)
            {
                foreach (FontFamily f in pfc.Families)
                {
                    if (f.Name.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) != 0) continue;
                    string rest = f.Name.Substring(baseName.Length);
                    if (rest.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return f;
                }
            }
            return null;
        }

        private static FontFamily Pick(PrivateFontCollection pfc, string name)
        {
            foreach (FontFamily f in pfc.Families)
                if (f.Name == name) return f;
            return null;
        }

        private static void LoadIconFont()
        {
            /* Two faces from the same set. The outlined one matches the light line icons the
               reference UI uses and is right for large surfaces; the solid one is used at
               24 px and below, where an outline stroke resamples into grey mush. */
            _iconFamily = LoadFace("MaterialIconsOutlined-Regular.otf", "MaterialIconsOutlined-Regular.ttf")
                       ?? LoadFace("MaterialIcons-Regular.ttf", "MaterialIcons-Regular.otf")
                       /* A stripped build with no fonts at all still works when the user has
                          Material Icons installed system-wide (a designer's PC, typically). */
                       ?? TryFamily("Material Icons Outlined")
                       ?? TryFamily("Material Icons");
            _iconFamilySolid = LoadFace("MaterialIcons-Regular.ttf", "MaterialIcons-Regular.otf")
                            ?? TryFamily("Material Icons")
                            ?? _iconFamily;

            if (_iconFamily == null)
                Log.Error("The Material icon font could not be loaded from " + (FontsDir() ?? "(no fonts folder)") +
                          ". Icons will be drawn as placeholders.");
        }

        private static FontFamily LoadFace(string first, string second)
        {
            string path = FontFile(first) ?? FontFile(second);
            if (path == null) return null;
            path = Cached(path);
            try
            {
                AddFontResourceEx(path, FR_PRIVATE | FR_NOT_ENUM, IntPtr.Zero);
                _privateInstalled.Add(path);
                var pfc = new PrivateFontCollection();
                pfc.AddFontFile(path);
                if (pfc.Families.Length == 0) return null;
                /* Held for the life of the process: see _fontCollections. */
                _fontCollections.Add(pfc);
                return pfc.Families[0];
            }
            catch (Exception ex)
            {
                Log.Error("Cannot load font " + Path.GetFileName(path), ex);
                return null;
            }
        }

        public static Font IconFont(float px)
        {
            return IconFace(_iconFamily, px);
        }

        /// <summary>Solid icon face, for sizes where a line icon loses its weight.</summary>
        public static Font IconFontSolid(float px)
        {
            return IconFace(_iconFamilySolid ?? _iconFamily, px);
        }

        private static Font IconFace(FontFamily family, float px)
        {
            if (family == null) return null;
            int key = Math.Max(4, (int)Math.Round(px));
            long cacheKey = (long)(uint)key | ((long)family.GetHashCode() << 16);
            Font f;
            if (_iconFonts.TryGetValue(cacheKey, out f)) return f;
            try { f = new Font(family, key, GraphicsUnit.Pixel); }
            catch { return null; }
            _iconFonts[cacheKey] = f;
            return f;
        }

        private static FontFamily TryFamily(string name)
        {
            try
            {
                var f = new FontFamily(name);
                return f.Name == name ? f : null;
            }
            catch { return null; }
        }

        private static List<string> BundledFonts()
        {
            var list = new List<string>();
            string folder = FontsDir();
            if (folder == null) return list;
            foreach (string prefix in new[] { "OpenSans", "Inter" })
            {
                foreach (string weight in new[] { "Regular", "SemiBold", "Bold" })
                {
                    string path = Path.Combine(folder, prefix + "-" + weight + ".ttf");
                    if (File.Exists(path)) list.Add(path);
                }
                if (list.Count > 0) return list;
            }
            return list;
        }

        /// <summary>
        /// Overrides the folder the bundled faces are read from. The app never sets this: it
        /// exists because the audit harness runs out of tools\audit, which has no fonts folder,
        /// and a harness that measures a fallback face is a harness that reports layout that
        /// does not exist in the shipped product.
        /// </summary>
        internal static string FontsOverride = null;

        /// <summary>
        /// Where the bundled faces live. Three places, in order: the harness override, a
        /// <c>fonts</c> folder beside the executable (the portable dist layout), and - because
        /// the installer ships a single executable - the copies extracted from the embedded
        /// resources into the app data folder. The last one is what makes the icons appear in
        /// an installed build; before it existed, an installed Joe Browser had no fonts folder
        /// and drew no icons at all.
        /// </summary>
        private static string FontsDir()
        {
            if (!string.IsNullOrEmpty(FontsOverride) && Directory.Exists(FontsOverride))
                return FontsOverride;
            if (_fontsDirResolved) return _fontsDir;
            _fontsDirResolved = true;

            string dir = null;
            try { dir = Path.GetDirectoryName(Application.ExecutablePath); } catch { }
            if (dir != null)
            {
                foreach (string sub in new[] { "fonts", "" })
                {
                    string folder = Path.Combine(dir, sub);
                    if (HasIconFont(folder)) return _fontsDir = folder;
                }
            }

            _fontsDir = InstallEmbeddedFonts();
            return _fontsDir;
        }

        private static bool HasIconFont(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
                return File.Exists(Path.Combine(folder, "MaterialIcons-Regular.ttf"))
                    || File.Exists(Path.Combine(folder, "MaterialIconsOutlined-Regular.otf"))
                    || File.Exists(Path.Combine(folder, "MaterialIconsOutlined-Regular.ttf"))
                    || File.Exists(Path.Combine(folder, "OpenSans-Regular.ttf"));
            }
            catch { return false; }
        }

        /// <summary>The resource prefix build.ps1 uses when embedding the faces.</summary>
        private const string FontResourcePrefix = "JoeBrowser.fonts.";

        /// <summary>
        /// Writes the fonts carried inside the executable to
        /// <c>%APPDATA%\JoeBrowser\fonts</c> (or the portable data root) and returns that
        /// folder. A file is only rewritten when its length does not match the resource, so a
        /// normal launch costs a few stat calls and no copying.
        /// </summary>
        private static string InstallEmbeddedFonts()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                if (names == null || names.Length == 0) return FindFontsOnDisk();

                string folder = Path.Combine(AppPaths.Root, "fonts");
                bool wrote = false;
                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name) ||
                        name.IndexOf(FontResourcePrefix, StringComparison.Ordinal) != 0) continue;

                    string file = name.Substring(FontResourcePrefix.Length);
                    if (file.IndexOfAny(new[] { '/', '\\' }) >= 0 || file.Length == 0) continue;

                    using (Stream s = asm.GetManifestResourceStream(name))
                    {
                        if (s == null) continue;
                        try
                        {
                            Directory.CreateDirectory(folder);
                            string dest = Path.Combine(folder, file);
                            if (File.Exists(dest) && new FileInfo(dest).Length == s.Length) { wrote = true; continue; }

                            /* Write beside the target and move into place, so a crash halfway
                               through can never leave a truncated font that GDI+ then refuses. */
                            string tmp = dest + "." + ProcessId() + ".tmp";
                            using (var f = File.Create(tmp)) s.CopyTo(f);
                            try { File.Delete(dest); } catch { }
                            File.Move(tmp, dest);
                            wrote = true;
                        }
                        catch (Exception ex) { Log.Error("Cannot extract bundled font " + file, ex); }
                    }
                }
                if (wrote) return folder;
            }
            catch (Exception ex) { Log.Error("Cannot extract bundled fonts", ex); }

            return FindFontsOnDisk();
        }

        /// <summary>Last resort: a fonts folder the user or a previous build left behind.</summary>
        private static string FindFontsOnDisk()
        {
            string[] candidates =
            {
                Path.Combine(AppPaths.Root, "fonts"),
                Path.Combine(Path.GetTempPath(), "JoeBrowser", "fonts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JoeBrowser", "fonts")
            };
            foreach (string c in candidates)
                if (HasIconFont(c)) return c;
            return null;
        }

        private static string FontFile(string name)
        {
            string folder = FontsDir();
            if (folder == null) return null;
            string path = Path.Combine(folder, name);
            return File.Exists(path) ? path : null;
        }

        private static string Cached(string path)
        {
            try
            {
                /* The extracted copies already live in a folder GDI+ may keep open: it is
                   written once per update, never per launch. Copying them to TEMP again would
                   cost a few megabytes of disk churn on every start for no benefit. */
                string root = AppPaths.Root;
                if (!string.IsNullOrEmpty(root) &&
                    path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return path;

                string dir = Path.Combine(Path.GetTempPath(), "JoeBrowser", "fonts", ProcessId());
                Directory.CreateDirectory(dir);
                string dst = Path.Combine(dir, Path.GetFileName(path));
                if (!File.Exists(dst)) File.Copy(path, dst, true);
                return dst;
            }
            catch { return path; }
        }

        private static string ProcessId()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id.ToString(); }
            catch { return "0"; }
        }

        private static void PruneFontCache()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "JoeBrowser", "fonts");
                if (!Directory.Exists(root)) return;
                string keep = ProcessId();
                foreach (string d in Directory.GetDirectories(root))
                {
                    if (Path.GetFileName(d) == keep) continue;
                    try
                    {
                        if (Directory.GetLastWriteTime(d).AddMinutes(30) < DateTime.Now)
                            Directory.Delete(d, true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void UnloadFonts()
        {
            foreach (string path in _installed)
            {
                try { RemoveFontResourceEx(path, 0, IntPtr.Zero); } catch { }
            }
            foreach (string path in _privateInstalled)
            {
                try { RemoveFontResourceEx(path, FR_PRIVATE | FR_NOT_ENUM, IntPtr.Zero); } catch { }
            }
            _installed.Clear();
            _privateInstalled.Clear();
            /* A second LoadFonts (the diagnostic harness does this) must actually reload, and
               must look at the disk again rather than remember the previous answer. */
            _iconFonts.Clear();
            _fontsLoaded = false;
            _fontsDirResolved = false;
            _fontsDir = null;
        }

        private static readonly Dictionary<long, Font> FontCache = new Dictionary<long, Font>();

        public const FontStyle Semi = (FontStyle)128;

        /// <summary>Font from a CSS pixel size, so the app matches windows-ui's scale exactly.</summary>
        public static Font F(double px, FontStyle style = FontStyle.Regular)
        {
            long key = ((long)Math.Round(px * 100) << 8) | (long)(uint)style;
            Font f;
            if (FontCache.TryGetValue(key, out f)) return f;

            float pt = (float)(px * 0.75);
            FontFamily fam = _family;

            try
            {
                if (fam == null) fam = TryFamily(FamilyName) ?? new FontFamily("Tahoma");

                /* Above ~17 px the display optical size is the right face: same family, drawn
                   for larger sizes, with tighter spacing. Only used when the variable stack is
                   actually present. */
                if (px >= 17 && _familyDisplay != null) fam = _familyDisplay;

                if (style == Semi)
                {
                    FontFamily semi = px >= 17 && _familyDisplaySemiBold != null
                        ? _familyDisplaySemiBold
                        : _familySemiBold;
                    // A real Semibold family, never a synthesised bold: emboldened outlines
                    // smear the stems, which reads as broken type.
                    f = semi != null
                        ? new Font(semi, pt, FontStyle.Regular, GraphicsUnit.Point)
                        : new Font(fam, pt, FontStyle.Bold, GraphicsUnit.Point);
                }
                else
                {
                    f = new Font(fam, pt, style, GraphicsUnit.Point);
                }
            }
            catch
            {
                f = new Font("Segoe UI", pt, style == Semi ? FontStyle.Bold : style, GraphicsUnit.Point);
            }
            FontCache[key] = f;
            return f;
        }

        // windows-ui type scale
        public static Font Display { get { return F(20, Semi); } }      // page/dialog title
        public static Font H1 { get { return F(17, Semi); } }           // section heading
        public static Font H2 { get { return F(15, Semi); } }           // card title / button
        public static Font H3 { get { return F(13, Semi); } }           // small heading
        public static Font Body { get { return F(15); } }               // base text
        public static Font BodyBold { get { return F(15, Semi); } }
        public static Font Small { get { return F(13); } }              // secondary
        public static Font SmallBold { get { return F(13, Semi); } }
        public static Font Caption { get { return F(12); } }            // captions, labels
        public static Font Micro { get { return F(11, Semi); } }        // section eyebrow

        /// <summary>
        /// A tabular face for values that are read one character at a time: wallet addresses,
        /// transaction ids, invite codes. In a proportional face "0" and "O" and "l" and "1"
        /// look alike, which is exactly the mistake that sends funds to a stranger.
        /// Consolas ships with Windows; the chain of fallbacks keeps this working on a
        /// stripped install rather than silently substituting a proportional font.
        /// </summary>
        public static Font Mono(double px)
        {
            long key = ((long)Math.Round(px * 100) << 8) | 0x4D;
            Font f;
            if (FontCache.TryGetValue(key, out f)) return f;

            float pt = (float)(px * 0.75);
            FontFamily fam = TryFamily("Consolas") ?? TryFamily("Cascadia Mono") ?? TryFamily("Courier New");
            try { f = fam != null ? new Font(fam, pt, FontStyle.Regular, GraphicsUnit.Point) : F(px); }
            catch { f = F(px); }
            FontCache[key] = f;
            return f;
        }

        public static Font MonoBody { get { return Mono(13); } }
        public static Font MonoSmall { get { return Mono(12); } }

        public static event EventHandler ModeChanged;

        public static void SetMode(bool dark)
        {
            if (Dark == dark) return;
            Dark = dark;
            P = dark ? DarkPal : LightPal;
            var h = ModeChanged;
            if (h != null) h(null, EventArgs.Empty);
        }

        public static void Restyle(Control root)
        {
            if (root == null) return;

            var re = root as IReStyle;
            if (re != null) re.ReStyle();

            RestyleChildren(root);
        }

        public static void RestyleChildren(Control root)
        {
            if (root == null) return;

            var box = root as TextBox;
            if (box != null && box.Parent is JBTextBox == false)
            {
                box.ForeColor = Text;
                box.BackColor = Input;
            }

            var panel = root as Panel;
            if (panel != null && !(root is IReStyle))
                panel.BackColor = panel.Parent != null ? panel.Parent.BackColor : Bg;

            foreach (Control child in root.Controls) Restyle(child);
        }
    }

    internal interface IReStyle
    {
        void ReStyle();
    }

    internal static class Ui
    {
        public static float Scale = 1f;

        public static int S(double v) { return (int)Math.Round(v * Scale); }

        public static int G(double v) { return S(Math.Round(v / 4d) * 4); }

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            // Interpolation stays at the default (NearestNeighbor): bicubic softens 1:1 icon
            // and flag blits, which reads as fuzziness. Icons ask for bicubic themselves.
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        /// <summary>
        /// Give a control a solid background matching whatever it sits on. Transparent WinForms
        /// controls are AlphaBlended over the parent, which smears ClearType subpixels.
        /// </summary>
        public static void Opaque(Control c)
        {
            c.BackColor = SurfaceColor(c);
            c.ParentChanged += delegate { if (!c.IsDisposed) c.BackColor = SurfaceColor(c); };
            EventHandler onMode = delegate { if (!c.IsDisposed) c.BackColor = SurfaceColor(c); };
            Theme.ModeChanged += onMode;
            c.Disposed += delegate { Theme.ModeChanged -= onMode; };
        }

        /// <summary>
        /// The opaque colour a child control must clear its buffer with. GDI+ ClearType is
        /// destroyed when WinForms AlphaBlends a translucent buffer onto the parent, so every
        /// text-bearing control paints a solid surface instead of a transparent one.
        /// </summary>
        public static Color SurfaceColor(Control c)
        {
            if (c == null) return Theme.Bg;
            for (Control p = c.Parent; p != null; p = p.Parent)
            {
                if (p is ProfileCard) return Theme.Card;
                if (p is JBTextBox) return Theme.Input;
                var f = p as JBForm;
                if (f != null)
                {
                    try { return f.SurfaceAt(f.PointToClient(c.PointToScreen(Point.Empty))); }
                    catch { return p.BackColor; }
                }
                /* Everything else paints its own BackColor, whatever that is: a page column is a
                   plain Panel on the page background, not a Theme.Panel card. Guessing from the
                   control type put a grey strip behind every label on the plan page in dark mode. */
                if (p.BackColor.A == 255) return p.BackColor;
            }
            return Theme.Bg;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            if (radius < 0) radius = 0;
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(r);
                path.CloseFigure();
                return path;
            }
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color color)
        {
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color color, float opacity)
        {
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * opacity), color)))
                g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
        {
            using (var p = Round(r, radius))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, p);
        }

        /// <summary>windows-ui focus-visible: a single 1px outline in the text colour.</summary>
        public static void FocusRing(Graphics g, RectangleF r, float radius)
        {
            var ring = new RectangleF(r.X - S(1.5f), r.Y - S(1.5f), r.Width + S(3f), r.Height + S(3f));
            Stroke(g, ring, radius + S(1.5f), Theme.Text, S(1f));
        }

        /// <summary>
        /// True while the user is driving the app with the keyboard, i.e. after a Tab or an
        /// arrow key and until the next mouse click.
        ///
        /// Every custom control used <c>Focused &amp;&amp; ShowFocusCues</c>. Control.ShowFocusCues
        /// is a process-wide flag that Windows reports as FALSE when a touch keyboard is
        /// attached, in tablet posture and in some remote sessions - so on those machines the
        /// app drew no focus ring anywhere and looked unkeyboardable. Keeping our own flag
        /// makes the cue independent of that, and it behaves the way Windows itself does:
        /// visible after a key press, gone as soon as the mouse is used again.
        /// </summary>
        public static bool KeyboardCuesForced;

        /// <summary>Whether a focus indicator should be drawn for this control right now.</summary>
        public static bool FocusCues(Control c)
        {
            if (c == null || !c.Focused) return false;
            if (KeyboardCuesForced) return true;
            /* ShowFocusCues is protected on Control and cannot be read from out here. Asking
               the window with WM_QUERYUISTATE returns the same UI state that property reports,
               so a tablet or remote session that hides focus cues still hides them - and the
               forced flag above still overrides it after a key press. */
            try
            {
                long state = Native.SendMessage(c.Handle, Native.WM_QUERYUISTATE, IntPtr.Zero, IntPtr.Zero).ToInt64();
                return (state & Native.UISF_HIDEFOCUS) == 0;
            }
            catch { return true; }
        }

        /// <summary>Called for the keys that move focus; ignored for ordinary typing.</summary>
        public static void NoteKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Tab || code == Keys.Left || code == Keys.Right ||
                code == Keys.Up || code == Keys.Down || code == Keys.Home || code == Keys.End ||
                code == Keys.PageUp || code == Keys.PageDown || code == Keys.Enter ||
                code == Keys.Space)
                KeyboardCuesForced = true;
        }

        public static void Text(Graphics g, string text, Font font, Color color, float x, float y,
            float width = 0, string align = "left", int maxLines = 1)
        {
            if (string.IsNullOrEmpty(text)) return;

            // GDI text (TextRenderer) is what Windows itself uses: it grid-fits the stems and
            // keeps the ClearType fringes. Graphics.DrawString feathers the same glyphs.
            TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
            if (maxLines <= 1) f |= TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            else f |= TextFormatFlags.WordBreak;
            if (align == "center") f |= TextFormatFlags.HorizontalCenter;
            else if (align == "right") f |= TextFormatFlags.Right;

            int w = width <= 0 ? 4000 : (int)Math.Round(width) + S(2);
            int lineH = (int)Math.Ceiling(font.GetHeight(g));
            int h = maxLines <= 1 ? lineH : maxLines * lineH + S(2);
            TextRenderer.DrawText(g, text, font,
                new Rectangle((int)Math.Round(x), (int)Math.Round(y), w, h), color, f);
        }

        public static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            Size s = TextRenderer.MeasureText(g, text, font, new Size(4000, 4000),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return new SizeF(s.Width, s.Height);
        }

        public static string Clip(Graphics g, string text, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (Measure(g, text, font).Width <= maxWidth) return text;
            int len = text.Length;
            while (len > 1 && Measure(g, text.Substring(0, len) + "…", font).Width > maxWidth) len--;
            return text.Substring(0, Math.Max(1, len)) + "…";
        }

        /// <summary>
        /// Height of a word-wrapped block, capped at <paramref name="maxLines"/>. Used to centre a
        /// title-and-description block inside a tile instead of pinning the description to a fixed
        /// offset, which left a different amount of dead space for every string length.
        /// </summary>
        public static float MeasureWrapped(Graphics g, string text, Font font, float width, int maxLines)
        {
            if (string.IsNullOrEmpty(text) || width <= 0 || maxLines <= 0) return 0f;
            Size s = TextRenderer.MeasureText(g, text, font, new Size((int)Math.Round(width), 4000),
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            float lineH = (float)Math.Ceiling(font.GetHeight(g));
            int lines = Math.Max(1, (int)Math.Round(s.Height / lineH));
            return Math.Min(lines, maxLines) * lineH;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static readonly Color[] AvatarPalette =
        {
            Color.FromArgb(0, 120, 215), Color.FromArgb(0, 90, 158), Color.FromArgb(43, 140, 232),
            Color.FromArgb(30, 96, 158), Color.FromArgb(74, 158, 232)
        };

        public static Color AvatarColor(string seed)
        {
            if (string.IsNullOrEmpty(seed)) return AvatarPalette[0];
            int h = 0;
            for (int i = 0; i < seed.Length; i++) h = unchecked(h * 31 + seed[i]);
            return AvatarPalette[Math.Abs(h) % AvatarPalette.Length];
        }

        public static void Shadow(Graphics g, RectangleF r, float radius, int level, bool dark = true)
        {
            int spread = level <= 1 ? 4 : (level == 2 ? 8 : 14);
            int alpha = level <= 1 ? 60 : (level == 2 ? 90 : 130);
            int offset = level <= 1 ? 1 : (level == 2 ? 2 : 4);
            Color tint = dark ? Color.FromArgb(0, 0, 0) : Color.FromArgb(30, 41, 59);
            for (int i = spread; i >= 1; i--)
            {
                var rr = new RectangleF(r.X - i, r.Y - i + offset, r.Width + i * 2, r.Height + i * 2);
                using (var p = Round(rr, radius + i))
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(1, alpha / spread), tint.R, tint.G, tint.B)))
                    g.FillPath(b, p);
            }
        }

        public static Size FitToScreen(Control c, int w, int h)
        {
            try
            {
                var area = Screen.FromControl(c).WorkingArea;
                return new Size(Math.Min(w, area.Width - S(24)), Math.Min(h, area.Height - S(24)));
            }
            catch { return new Size(w, h); }
        }
    }
}
