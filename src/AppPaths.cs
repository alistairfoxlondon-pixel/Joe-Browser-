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
        public static string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }

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

        public static void EnsureAll()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Profiles);
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Logs);
        }
    }
}
