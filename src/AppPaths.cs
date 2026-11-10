using System;
using System.IO;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal static class AppPaths
    {
        public static readonly string Root = ResolveRoot();

        public static string Profiles { get { return Path.Combine(Root, "profiles"); } }
        public static string Data { get { return Path.Combine(Root, "data"); } }
        public static string Logs { get { return Path.Combine(Root, "logs"); } }
        public static string Browsers { get { return Path.Combine(Root, "browsers"); } }
        public static string ChromiumDir { get { return Path.Combine(Browsers, "chromium"); } }
        public static string ChromiumExe { get { return Path.Combine(ChromiumDir, "chrome-win", "chrome.exe"); } }
        /// <summary>Pinned browser builds, one folder per engine and version.</summary>
        public static string Kernels { get { return Path.Combine(Browsers, "kernels"); } }
        public static string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }
        /// <summary>Original unpacked extensions, written by Joe Browser itself.</summary>
        public static string Extensions { get { return Path.Combine(Root, "extensions"); } }
        public static string Trash { get { return Path.Combine(Root, "trash"); } }
        public static string Exports { get { return Path.Combine(Root, "exports"); } }

        private static string ResolveRoot()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (exeDir != null && File.Exists(Path.Combine(exeDir, "portable.txt")))
                    return Path.Combine(exeDir, "userdata");
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JoeBrowser");
        }

        public static bool EnsureAll()
        {
            try
            {
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(Profiles);
                Directory.CreateDirectory(Data);
                Directory.CreateDirectory(Logs);
                Directory.CreateDirectory(Extensions);
                Directory.CreateDirectory(Trash);
                Directory.CreateDirectory(Exports);

                /* A .NET file dialog will not open a folder that does not exist, and the
                   "add an extension" and "browse browser" actions start in these two. */
                Directory.CreateDirectory(Browsers);
                Directory.CreateDirectory(Kernels);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("App data folders could not be created", ex);
                return false;
            }
        }

        /// <summary>
        /// True when the data folder is writable. Called once at start-up: without it a
        /// redirected or read-only %APPDATA% produced an app that launched, looked normal and
        /// silently lost every profile and setting on exit.
        /// </summary>
        public static bool IsWritable()
        {
            try
            {
                Directory.CreateDirectory(Root);
                string probe = Path.Combine(Root, ".write-test");
                try
                {
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return true;
                }
                finally
                {
                    try { File.Delete(probe); } catch { }
                }
            }
            catch { return false; }
        }
    }
}
