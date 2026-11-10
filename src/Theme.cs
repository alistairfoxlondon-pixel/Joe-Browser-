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

        /* A four step neutral ramp, the way a designed dark theme is built: the window sits on
           the lowest step, a card one above it, an input above that. The previous palette had a
           black window and a #303030 card with a #282828 line between them, so the card edge was
           darker than the card and the whole page read as stacked boxes. These steps are 6 to 9
           values apart, which is the smallest difference that still separates two surfaces on a
           cheap panel. */
        private static readonly Pal DarkPal = NewPal(
            bg: C(0x17, 0x18, 0x1B), bgSoft: C(0x1C, 0x1D, 0x21), sidebar: C(0x14, 0x15, 0x18),
            card: C(0x23, 0x25, 0x29), panel: C(0x2A, 0x2C, 0x31), panelHover: C(0x31, 0x33, 0x39),
            panel2: C(0x2D, 0x2F, 0x34), input: C(0x1B, 0x1C, 0x20),
            line: C(0x33, 0x35, 0x3B), lineSoft: C(0x28, 0x2A, 0x2F),
            text: C(0xF2, 0xF3, 0xF5), textDim: C(0xC3, 0xC6, 0xCC), textFaint: C(0x9A, 0x9E, 0xA6),
            accent: C(0x5B, 0x9B, 0xF7), accentHover: C(0x74, 0xAD, 0xFF), accentPressed: C(0x44, 0x85, 0xE0),
            onAccent: C(0xFF, 0xFF, 0xFF),
            buttonBg: C(0x2E, 0x30, 0x36), buttonBorder: C(0x3B, 0x3E, 0x45),
            success: C(0x4E, 0xC3, 0x8A), danger: C(0xF2, 0x6D, 0x76));

        private static readonly Pal LightPal = NewPal(
            bg: C(0xF7, 0xF8, 0xFA), bgSoft: C(0xFF, 0xFF, 0xFF), sidebar: C(0xF1, 0xF3, 0xF7),
            card: C(0xFF, 0xFF, 0xFF), panel: C(0xFF, 0xFF, 0xFF), panelHover: C(0xF2, 0xF4, 0xF8),
            panel2: C(0xEC, 0xEE, 0xF3), input: C(0xFF, 0xFF, 0xFF),
            line: C(0xDF, 0xE2, 0xE8), lineSoft: C(0xEB, 0xED, 0xF2),
            text: C(0x16, 0x18, 0x1D), textDim: C(0x4A, 0x4E, 0x57), textFaint: C(0x6B, 0x70, 0x7A),
            accent: C(0x2C, 0x6B, 0xD6), accentHover: C(0x3D, 0x7B, 0xE6), accentPressed: C(0x20, 0x55, 0xB0),
            onAccent: C(0xFF, 0xFF, 0xFF),
            buttonBg: C(0xFF, 0xFF, 0xFF), buttonBorder: C(0xDA, 0xDD, 0xE4),
            success: C(0x1F, 0x8A, 0x5F), danger: C(0xC3, 0x37, 0x44));

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
        /// <summary>Amber, the same value the warning toast uses, for "attention, not broken".</summary>
        public static Color Warn { get { return C(0xF2, 0xA8, 0x1B); } }
        public static Color Danger { get { return P.Danger; } }

        /// <summary>Danger filled surfaces put black text on the pale dark-theme red.</summary>
        public static Color Ink { get { return P.Danger; } }
        public static Color InkText { get { return Dark ? C(0x00, 0x00, 0x00) : C(0xFF, 0xFF, 0xFF); } }

        /// <summary>The bottom hairline Windows gives every text field.</summary>
        public static Color InputUnderline { get { return Dark ? C(0x77, 0x77, 0x77) : C(0x8A, 0x8A, 0x8A); } }

        public static Color Scrim { get { return Color.FromArgb(Dark ? 140 : 60, 0, 0, 0); } }
        public static Color AccentOnBg { get { return Color.FromArgb(Dark ? 34 : 22, Accent.R, Accent.G, Accent.B); } }
        public static Color AccentBorder { get { return Color.FromArgb(Dark ? 130 : 110, Accent.R, Accent.G, Accent.B); } }
        public static Color AccentText { get { return Dark ? C(0x8C, 0xBA, 0xFF) : C(0x1C, 0x51, 0xA8); } }
        public static Color HoverOverlay { get { return Color.FromArgb(Dark ? 20 : 16, Text.R, Text.G, Text.B); } }
        public static Color SelOverlay { get { return Color.FromArgb(Dark ? 34 : 24, Text.R, Text.G, Text.B); } }

        /// <summary>--color-ui-hover-strong, used by navbar rows and cards.</summary>
        public static Color HoverStrong { get { return Dark ? C(0x2D, 0x2D, 0x2D) : C(0xE8, 0xE8, 0xE8); } }
        /// <summary>--color-ui-active-default.</summary>
        public static Color ActiveBg { get { return Dark ? C(0x26, 0x26, 0x26) : C(0xD8, 0xD8, 0xD8); } }

        // windows-ui control metrics
        public const int ControlHeight = 40;
        /// <summary>
        /// The app draws square corners. Every surface used to be a rounded rectangle - 6 px on
        /// a control, 8 px on a card - and asking for "no border radius" cannot be honoured by
        /// editing call sites one at a time: there were sixty of them, and the next control
        /// added would have been round again. Every rounded rectangle in the app goes through
        /// the helpers below, so the corner lives here, in one number.
        /// </summary>
        public const int ButtonRadius = 0;
        public const int CardRadius = 0;
        public const int NavbarWidth = 280;

        public static readonly int Radius = CardRadius;
        public static int RSmall { get { return Ui.S(ButtonRadius); } }
        public static int RMedium { get { return Ui.S(ButtonRadius); } }
        public static int RLarge { get { return Ui.S(CardRadius); } }
        public static int RXLarge { get { return Ui.S(CardRadius); } }

        // ---- typography: Telenor (the app's requested brand face), Segoe UI fallback ----
        public static string FamilyName = "Telenor";

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

            /* The app is set in Telenor, the brand face the product is named for. The three
               faces ship inside the executable (Telenor Regular/Bold/Light, extracted to the
               data folder on first run), so the app looks the same on a stripped Windows
             install as on a fully stocked one. Segoe UI Variable, then plain Segoe UI, then
               Open Sans/Inter remain the fallback chain for a machine where the bundled
               faces cannot be registered - and for the non-Latin interface languages the
               Telenor face has no glyphs for (see FamilyForText). */
            if (LoadTelenor()) return;

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

        /// <summary>
        /// Binds the bundled Telenor faces. Telenor Regular and Telenor Bold share one family
        /// name, so GDI+ hands back a single FontFamily carrying both styles: asking for Bold
        /// selects the real bold outlines instead of smearing the regular ones. Telenor has no
        /// Semibold or Display instance, so those slots stay null - F() already falls back to
        /// the family's own Bold for Semi, which is how the brand uses emphasis anyway.
        /// Returns false when the faces are not on disk, so the Segoe chain takes over.
        /// </summary>
        private static bool LoadTelenor()
        {
            var files = new List<string>();
            bool regular = false;
            foreach (string name in new[] { "Telenor-Regular.ttf", "Telenor-Bold.ttf", "Telenor-Light.ttf" })
            {
                string path = FontFile(name);
                if (path == null) continue;
                if (name == "Telenor-Regular.ttf") regular = true;
                files.Add(Cached(path));
            }
            if (!regular) return false;

            try
            {
                var pfc = new PrivateFontCollection();
                foreach (string file in files)
                {
                    try { AddFontResourceEx(file, FR_PRIVATE | FR_NOT_ENUM, IntPtr.Zero); _privateInstalled.Add(file); } catch { }
                    pfc.AddFontFile(file);
                }
                _fontCollections.Add(pfc);
                FontFamily fam = Pick(pfc, "Telenor");
                if (fam == null) return false;
                _family = fam;
                _familySemiBold = null;
                _familyDisplay = null;
                _familyDisplaySemiBold = null;
                FamilyName = "Telenor";
                _telenorFamily = fam;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not load the bundled Telenor faces", ex);
                return false;
            }
        }

        private static FontFamily _telenorFamily;

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
            foreach (string prefix in new[] { "Telenor", "OpenSans", "Inter" })
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
                    || File.Exists(Path.Combine(folder, "Telenor-Regular.ttf"))
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

        /// <summary>
        /// Throws the font instances away so the next request builds them for the current
        /// display scale. Called when the window moves to a monitor with a different DPI; the
        /// cache key includes the scale, so this also keeps the cache from growing without
        /// bound when a laptop is docked and undocked all day.
        /// </summary>
        public static void ClearFontCache()
        {
            foreach (var f in FontCache.Values) { try { f.Dispose(); } catch { } }
            FontCache.Clear();
            /* The heights were measured from those very fonts, so they go with them: a stale
               entry would hand a page the line height of a disposed font after a theme flip. */
            _lineHeights.Clear();
        }

        public const FontStyle Semi = (FontStyle)128;

        /// <summary>
        /// A font for a CSS pixel size, measured in DEVICE pixels.
        ///
        /// This is the single most important line in the whole interface. A Font created in
        /// points is already scaled by the display DPI when it is drawn, while every rectangle
        /// in this app is computed with Ui.S() at 96 dpi multiplied by the display scale - so a
        /// 15 px font asked for in points becomes 15 * scale DEVICE pixels inside a box that is
        /// 15 * scale big, but the font's line height grows by MORE than the scale (a Segoe UI
        /// line is about 1.33 em, not 1 em) and the two stopped agreeing. At 125% and 150% the
        /// result was text that was a pixel or two too tall for every row, clipped descenders,
        /// and baselines that did not line up between a label and the value beside it. Asking
        /// for the size in pixels removes the font from the DPI transform entirely: the size in
        /// the code is the size on the glass, and measuring it reports the same box that is
        /// drawn.
        /// </summary>
        public static Font F(double px, FontStyle style = FontStyle.Regular)
        {
            long key = ((long)Math.Round(px * 100) << 8) | (long)(uint)style;
            Font f;
            if (FontCache.TryGetValue(key, out f)) return f;

            /* Device pixels: the size asked for is the size drawn at every scale. The old
               code multiplied by 0.75 - the pixel-to-point factor - and then handed the
               number to a GraphicsUnit.Pixel constructor, so every "16 px" face was actually
               drawn as a 12 px em and the whole interface read as one size too small. The
               constant is gone: what a row asks for is what the glass shows. */
            float pt = (float)(px * Ui.Scale);
            FontFamily fam = FamilyForText();

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
                        ? new Font(semi, pt, FontStyle.Regular, GraphicsUnit.Pixel)
                        : new Font(fam, pt, FontStyle.Bold, GraphicsUnit.Pixel);
                }
                else
                {
                    f = new Font(fam, pt, style, GraphicsUnit.Pixel);
                }
            }
            catch
            {
                f = new Font("Segoe UI", pt, style == Semi ? FontStyle.Bold : style, GraphicsUnit.Pixel);
            }
            FontCache[key] = f;
            return f;
        }

        /// <summary>
        /// The family the interface text is drawn in right now. Telenor when it loaded and the
        /// interface language is one the face can actually render; the system stack otherwise.
        /// The check runs on the first font request (by then the language has been set) and is
        /// re-run when the language changes, because switching to Russian or Chinese halfway
        /// through a session must not leave the new strings in a face that has no glyphs.
        /// </summary>
        private static FontFamily FamilyForText()
        {
            if (!_coverageChecked)
            {
                _coverageChecked = true;
                _latinOk = LanguageCovered();
            }
            if (!_latinOk)
            {
                /* Same chain LoadFonts uses, minus the bundled fallback: a language outside
                   Telenor's coverage is by definition not Latin, and Open Sans cannot fill it
                   either. The system face does, via GDI's own font linking. */
                if (_systemFamily == null)
                    _systemFamily = TryFamily("Segoe UI") ?? TryFamily("Tahoma") ?? _family;
                if (_systemFamily != null) return _systemFamily;
            }
            return _family;
        }

        private static FontFamily _systemFamily;
        private static bool _coverageChecked, _latinOk = true;

        /// <summary>True when the active interface language fits inside the bundled face.</summary>
        public static bool LanguageCovered()
        {
            try
            {
                string sample = L10n.CoverageSample();
                if (string.IsNullOrEmpty(sample)) return true;
                foreach (char c in sample)
                {
                    if (c <= '\u024F') continue;                      // Latin, Latin-1, Extended-A/B
                    if (c >= '\u2000' && c <= '\u2122') continue;     // punctuation, currency, marks
                    if (char.IsWhiteSpace(c) || char.IsDigit(c)) continue;
                    if (char.IsPunctuation(c) || char.IsSymbol(c)) continue;
                    return false;
                }
            }
            catch { }
            return true;
        }

        /// <summary>
        /// Called when the interface language changes at runtime: the coverage decision is
        /// re-made, and the font cache - whose entries were built for the previous family -
        /// is dropped together with every window's styled controls, so nothing keeps painting
        /// with a face the new language cannot show.
        /// </summary>
        public static void OnLanguageChanged()
        {
            _coverageChecked = true;
            bool ok = LanguageCovered();
            if (ok == _latinOk) return;
            _latinOk = ok;
            ClearFontCache();
            foreach (Form f in Application.OpenForms)
            {
                try { Restyle(f); f.Invalidate(true); } catch { }
            }
        }

        /* The type scale.
        
           Read at arm's length, on a 100% display, in a window that is often not maximised:
           the previous scale started at 12 px for an eyebrow and 13 px for a field label, and
           the complaint that came back was simply "the font is too small". Every step below is
           one or two pixels larger, and the small end - the labels and captions people read
           most often - is moved furthest. The rhythm stays the same shape (Display > H1 > H2 >
           H3 > body > small > micro), so a page that was composed reads the same way, larger.
          
           Nothing here is in device pixels: Ui.S applies the user's scale, and F() applies the
           system DPI, so the increase compounds nothing and a 200% display still gets 200%. */
        /* The sizes themselves, named, because they are now a product promise: the base text is
           at least 16 px and the smallest text anywhere is 14 px, at 100%. The self test asserts
           those two floors against these constants, so a future "it looks a bit large" edit has
           to be a deliberate change to the promise rather than a number typed into a font call. */
        public const double PxDisplay = 30;
        public const double PxH1 = 24;
        public const double PxH2 = 19;
        public const double PxH3 = 17;
        public const double PxBody = 16;
        public const double PxSmall = 15;
        public const double PxCaption = 15;
        public const double PxMicro = 14;

        public static Font Display { get { return F(PxDisplay, Semi); } }   // page/dialog title
        public static Font H1 { get { return F(PxH1, Semi); } }             // section heading
        public static Font H2 { get { return F(PxH2, Semi); } }             // card title
        public static Font H3 { get { return F(PxH3, Semi); } }             // row title / button
        public static Font Body { get { return F(PxBody); } }               // base text
        public static Font BodyBold { get { return F(PxBody, Semi); } }
        public static Font Small { get { return F(PxSmall); } }             // secondary, labels
        public static Font SmallBold { get { return F(PxSmall, Semi); } }
        public static Font Caption { get { return F(PxCaption); } }         // captions, hints
        public static Font Micro { get { return F(PxMicro, Semi); } }       // section eyebrow

        /// <summary>Line height of a face, in device pixels, rounded up.</summary>
        /* Line heights are asked for on every row of every page and of every card, and each
           answer is the same for a given font at a given scale. Caching them turns a device
           context per row into a dictionary lookup, which matters when a page builds sixty
           rows while the window is being resized. */
        private static readonly Dictionary<Font, int> _lineHeights = new Dictionary<Font, int>();

        public static int LineHeight(Font f)
        {
            if (f == null) return Ui.S(18);
            int cached;
            if (_lineHeights.TryGetValue(f, out cached)) return cached;
            int height;
            try
            {
                height = Math.Max(Ui.S(12), (int)Math.Ceiling((double)f.GetHeight(Ui.Ruler ?? Graphics.FromHwnd(IntPtr.Zero))));
            }
            catch { height = Math.Max(Ui.S(12), (int)Math.Ceiling((double)f.Height)); }
            _lineHeights[f] = height;
            return height;
        }

        /* The vertical rhythm every page, card and dialog is built from. A controls library
           that hard codes 18 for a label row and 33 for a field gets 1 px of clipping the day
           the font gets a pixel taller; these are derived from the fonts instead. */
        public static int Row { get { return LineHeight(Small) + Ui.S(4); } }
        public static int Field { get { return Ui.S(40); } }
        public static int Gap { get { return Ui.S(8); } }

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

            float pt = (float)(px * Ui.Scale);
            FontFamily fam = TryFamily("Consolas") ?? TryFamily("Cascadia Mono") ?? TryFamily("Courier New");
            try { f = fam != null ? new Font(fam, pt, FontStyle.Regular, GraphicsUnit.Pixel) : F(px); }
            catch { f = F(px); }
            FontCache[key] = f;
            return f;
        }

        public static Font MonoBody { get { return Mono(15); } }
        public static Font MonoSmall { get { return Mono(14); } }

        public static event EventHandler ModeChanged;

        /// <summary>True while a theme change is being applied, so painted controls can hold.</summary>
        public static bool Restyling { get; private set; }

        public static void SetMode(bool dark)
        {
            if (Dark == dark) return;
            Dark = dark;
            P = dark ? DarkPal : LightPal;
            var h = ModeChanged;
            if (h == null) return;
            /* One window, one repaint.
            
               Every control on the page used to repaint itself the moment it heard the mode
               changed - a hundred Invalidate calls, each painting against a palette the next one
               was about to contradict - and that is the flash and the half-styled frame reported
               when switching between light and dark. The listeners now run with repainting
               suppressed, and the invalidation happens once, at the end, from the top of each
               window. A single paint cannot show a mixture of two themes. */
            Restyling = true;
            try { h(null, EventArgs.Empty); }
            finally { Restyling = false; }
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

    /// <summary>
    /// A page that lays its column out from the host's content width. Implementing this lets
    /// JBScrollHost ask for one more pass once that width is final, which is what makes a resize
    /// land correctly on the first paint instead of after the next event.
    /// </summary>
    internal interface ISyncLayout
    {
        void LayoutAtWidth();
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
            /* Text is drawn with TextRenderer (GDI), which does its own ClearType and ignores
               this hint. It matters for the few places that fall back to Graphics.DrawString,
               and there the difference is visible: AntiAlias feathered the glyphs grey, which
               was the "unclear text" this app showed next to native Windows text on the same
               screen. ClearTypeGridFit matches what GDI does on the rest of the window. */
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            // Interpolation stays at the default (NearestNeighbor): bicubic softens 1:1 icon
            // and flag blits, which reads as fuzziness. Icons ask for bicubic themselves.
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        /// <summary>
        /// Pixel size for the screen's frames per inch. A one pixel line on a 4K panel at 200%
        /// covers two device pixels and reads as a grey smear unless it is drawn 2 units wide.
        /// </summary>
        public static float Hair { get { return Ui.Scale >= 1.5f ? 1.6f : 1f; } }

        /// <summary>
        /// A card edge that stays a crisp line at any scale. Drawn at half pixel offsets with
        /// AntiAlias it lands on exactly one device pixel row, so borders look drawn rather than
        /// smudged - which is the difference between a flat design and a low quality one.
        /// </summary>
        public static void Card(Graphics g, RectangleF r, float radius, Color fill, Color border)
        {
            Fill(g, r, radius, fill);
            Stroke(g, r, radius, border, Ui.Hair);
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

        /// <summary>
        /// Places a row of buttons left to right, wrapping onto another line when the next one
        /// would cross <paramref name="width"/>. Returns the y of the line after the last one, so
        /// the caller starts the next block below every button it placed.
        ///
        /// Three pages used to measure and place their buttons by hand, each with the same
        /// defect: the position was computed from the right edge one button at a time, so a
        /// button could be placed on top of the one before it, and a narrow window pushed the
        /// group past the edge of the page. One implementation means one fix.
        /// </summary>
        /// <summary>
        /// Two measured widths that have to share one row. When the pair is wider than the room
        /// it gets, both shrink in proportion instead of one being pushed off the edge - which is
        /// what happened to the left hand button of every footer built this way, at a large text
        /// scale or in a window someone had narrowed.
        /// </summary>
        public static void FitPair(ref int left, ref int right, int gap, int room, int minLeft, int minRight)
        {
            if (left < 0) left = 0;
            if (right < 0) right = 0;
            if (left + gap + right <= room) return;
            int spare = Math.Max(minLeft + minRight, room - gap);
            int total = Math.Max(1, left + right);
            int a = spare * left / total;
            left = Math.Max(Math.Min(minLeft, spare), a);
            right = Math.Max(Math.Min(minRight, spare - left), spare - left);
        }

        public static int FlowButtons(JBButton[] buttons, int left, int top, int width, int gap, int rowHeight)
        {
            if (buttons == null || buttons.Length == 0) return top;
            int x = left, y = top, used = 0;
            foreach (var b in buttons)
            {
                if (b == null || !b.Visible) continue;
                int bw = b.PreferredWidth(S(84));
                if (bw > width) bw = width;
                if (used > 0 && x + bw > left + width) { x = left; y += rowHeight + gap; used = 0; }
                b.SetBounds(x, y, bw, rowHeight);
                x += bw + gap;
                used++;
            }
            return y + rowHeight;
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
            /* A square surface is filled as a rectangle, not as a path: a path edge that lands
               on a fractional coordinate is antialiased, and a card whose four edges are half
               covered rows of grey reads as a soft, slightly blurry box next to crisp text. */
            if (radius <= 0.5f)
            {
                using (var b = new SolidBrush(color))
                    g.FillRectangle(b, new RectangleF(r.X, r.Y, r.Width, r.Height));
                return;
            }
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color color, float opacity)
        {
            if (radius <= 0.5f)
            {
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * opacity), color)))
                    g.FillRectangle(b, new RectangleF(r.X, r.Y, r.Width, r.Height));
                return;
            }
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * opacity), color)))
                g.FillPath(b, p);
        }

        /// <summary>
        /// An outline. A width of one means one DEVICE pixel, not one logical pixel: at 150%
        /// the old code drew a 1 unit pen, which the DPI transform turned into 1.5 device
        /// pixels and antialiased into two half-covered rows of grey. Every card edge on a
        /// scaled screen looked soft for that reason. A pen of exactly one device pixel drawn
        /// on a half pixel boundary is the crisp line a designer means by "a hairline".
        /// </summary>
        public static void Stroke(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
        {
            using (var pen = new Pen(color, width < 1.2f ? Hair : width))
            {
                if (radius <= 0.5f) { g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height); return; }
                using (var p = Round(r, radius))
                    g.DrawPath(pen, p);
            }
        }

        /// <summary>windows-ui focus-visible: a single 1px outline in the text colour.
        /// Drawn just INSIDE the rect: the ring used to expand outward by 1.5 px, but a
        /// control's own paint is clipped to its bounds, so on buttons and selects the ring
        /// landed exactly where nothing could draw it - keyboard focus was invisible.</summary>
        public static void FocusRing(Graphics g, RectangleF r, float radius)
        {
            var ring = new RectangleF(r.X + S(1.5f), r.Y + S(1.5f), r.Width - S(3f), r.Height - S(3f));
            Stroke(g, ring, radius, Theme.Text, S(1f));
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
            /* A caller that measured a negative budget - a chip wider than its card - used to
               get a 4000 px line that ran over everything beside it. A negative budget means
               "there is no room": drawing nothing is the honest answer, and the tooltip that
               carries the full value is the way to read it. */
            if (width <= 0) return;
            int lineH = (int)Math.Ceiling(font.GetHeight(g));
            int h = maxLines <= 1 ? lineH : maxLines * lineH + S(2);
            TextRenderer.DrawText(g, text, font,
                new Rectangle((int)Math.Round(x), (int)Math.Round(y), w, h), color, f);
        }

        /// <summary>
        /// Splits text into lines that fit the width, the way the drawing code will lay them out.
        /// A centred label that is too narrow for its sentence needs its own height: drawn on one
        /// line it was cut off with an ellipsis, and the words it dropped were the end of the
        /// sentence the user needed to read. Wrapping is separate from MeasureText's WordBreak,
        /// which reports a height for a single batch of text and cannot be asked for the lines.
        /// </summary>
        public static List<string> WrapLines(Graphics g, string text, Font font, float maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            if (maxWidth <= 0) { lines.Add(text); return lines; }

            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                var words = para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) { lines.Add(""); continue; }
                var line = words[0];
                for (int i = 1; i < words.Length; i++)
                {
                    var candidate = line + " " + words[i];
                    // The two pixel allowance matches Text's own width padding, so a line that
                    // measures as exactly the width is not reported as one that does not fit.
                    if (Measure(g, candidate, font).Width <= maxWidth + S(2)) line = candidate;
                    else { lines.Add(line); line = words[i]; }
                }
                lines.Add(line);
            }
            return lines;
        }

        private static Graphics _ruler;

        /// <summary>
        /// A measuring surface that is reused instead of one per call.
        ///
        /// Ui.Measure was called with whatever Graphics a caller happened to have; on a control
        /// that meant CreateGraphics(), which allocates a device context and releases it on every
        /// label. A layout pass with sixty labels then spent its time in GDI handle churn, which is
        /// exactly the hitch a user feels when a page opens. One process wide surface does the
        /// same measurement with no allocation.
        /// </summary>
        public static Graphics Ruler
        {
            get
            {
                if (_ruler == null)
                {
                    try { _ruler = Graphics.FromHwnd(IntPtr.Zero); } catch { }
                }
                return _ruler;
            }
        }

        public static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            if (g == null) g = Ruler;
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
            /* TextRenderer works without a Graphics; passing null measures on the desktop
               context, which is what a caller without one has. */
            Size s = g == null
                ? TextRenderer.MeasureText(text, font, new Size((int)Math.Round(width), 4000),
                    TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak)
                : TextRenderer.MeasureText(g, text, font, new Size((int)Math.Round(width), 4000),
                    TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            float lineH = (float)Math.Ceiling(g == null ? font.GetHeight() : font.GetHeight(g));
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
