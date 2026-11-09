using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace JoeBrowser
{
    /// <summary>
    /// The application icon, in the sizes Windows actually asks for.
    ///
    /// A WinForms form that never assigns <see cref="System.Windows.Forms.Form.Icon"/> falls
    /// back to the generic Windows application icon, which is why the taskbar button showed a
    /// blank placeholder. The .ico is embedded as a managed resource so every window can load
    /// the real artwork - and so the artwork survives an installer replacing the shell icon.
    /// </summary>
    internal static class AppIcon
    {
        private const string Resource = "JoeBrowser.app.ico";

        private static Icon _window;    // 32 px - title bar and Alt-Tab
        private static Icon _small;     // 16 px - taskbar and window menu
        private static Icon _large;     // 256 px - explorer previews
        private static Icon _tray;

        /// <summary>32 px icon: what a window shows in the title bar and in Alt-Tab.</summary>
        public static Icon Window { get { return Load(ref _window, 32); } }

        /// <summary>16 px icon: the taskbar button and the window's system menu.</summary>
        public static Icon Small { get { return Load(ref _small, 16); } }

        /// <summary>256 px icon, for the About page and any large surface.</summary>
        public static Icon Large { get { return Load(ref _large, 256); } }

        /// <summary>16 px icon suitable for a notification-area balloon.</summary>
        public static Icon Tray { get { return Load(ref _tray, 16); } }

        private static Icon Load(ref Icon cache, int size)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(Resource))
                {
                    if (s != null)
                    {
                        cache = new Icon(s, size, size);
                        return cache;
                    }
                }
            }
            catch { }

            /* Fall back to the icon embedded in the executable, so a build that forgot the
               managed resource still shows something better than the generic placeholder. */
            try
            {
                string exe = System.Windows.Forms.Application.ExecutablePath;
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null) cache = new Icon(extracted, size, size);
            }
            catch { }

            if (cache == null) cache = SystemIcons.Application;
            return cache;
        }

        /// <summary>Converts the icon to a bitmap at a given size, for painting in-app.</summary>
        public static Bitmap Bitmap(int size)
        {
            try
            {
                /* The cached handle must not travel inside the using: disposing the temporary
                   would take the cache entry with it and every later call would be painting a
                   dead handle. */
                Icon source = Load(ref _large, 256);
                if (source == null) return null;
                using (var scaled = new Icon(source, Math.Max(1, size), Math.Max(1, size)))
                    return scaled.ToBitmap();
            }
            catch { return null; }
        }
    }
}
