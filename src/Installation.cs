using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace JoeBrowser
{
    /// <summary>
    /// Install locations and removal. Shared by the application (which handles the
    /// "--uninstall" switch that Add/Remove Programs invokes) and by the installer, so both
    /// binaries agree on where the product lives.
    /// </summary>
    internal static class Installation
    {
        public const string AppName = "Joe Browser";
        public const string AppVersion = "1.4.7";
        public const string Publisher = "Joe Goldberg";
        public const string AppId = "JoeBrowser.Browser.1.2";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\JoeBrowser";

        /// <summary>Per-user install target: no administrator rights, no UAC prompt.</summary>
        public static string DefaultDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "JoeBrowser");
        }

        public static string StartMenuDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        public static string DesktopDir()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        public static string StartMenuLink() { return Path.Combine(StartMenuDir(), AppName + ".lnk"); }
        public static string DesktopLink() { return Path.Combine(DesktopDir(), AppName + ".lnk"); }

        public static bool IsInstalled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    if (key != null) return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// True when this process IS the installed copy. Running the raw download out of a
        /// Downloads folder is not the same product experience as an installed one: no
        /// shortcuts, no Add/Remove Programs entry, and an update that quietly leaves the old
        /// copy behind. The app therefore insists on being installed first.
        /// </summary>
        public static bool IsRunningInstalled()
        {
            try
            {
                if (!IsInstalled()) return false;
                string installed = Path.Combine(InstalledDir(), "JoeBrowser.exe");
                string running = System.Windows.Forms.Application.ExecutablePath;
                return string.Equals(Path.GetFullPath(installed), Path.GetFullPath(running),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Installs the currently running executable into the per-user program folder and
        /// registers it. Used by the install gate, so a user who opens the raw download ends
        /// up with a properly installed product instead of a stray exe on the desktop.
        /// </summary>
        public static bool SelfInstall(bool desktopShortcut, out string error)
        {
            return SelfInstall(System.Windows.Forms.Application.ExecutablePath, DefaultDir(), desktopShortcut, out error);
        }

        /// <summary>
        /// Same install, from an explicit source. The audit harness uses this to exercise the
        /// gate without driving a mouse, which is the only way to prove the install actually
        /// writes what it claims to.
        /// </summary>
        public static bool SelfInstall(string source, string dir, bool desktopShortcut, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "JoeBrowser.exe");

                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target),
                        StringComparison.OrdinalIgnoreCase))
                {
                    /* An in-place update runs while the previous copy may still be shutting
                       down, and Windows keeps the image locked until it has. Failing the whole
                       install on a transient lock is what made "install" look broken; retry
                       for a few seconds instead. */
                    ReplaceLocked(source, target);
                }
                try { File.SetAttributes(target, FileAttributes.Normal); } catch { }

                CopyFonts(source, dir);

                CreateShortcut(StartMenuLink(), target, dir);
                if (desktopShortcut) CreateShortcut(DesktopLink(), target, dir);
                RegisterUninstall(dir, target);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Copies the payload over the installed one, retrying while Windows holds the target
        /// image open. <paramref name="source"/> may be a running executable, which is fine:
        /// a running image can be read even though it cannot be replaced in place.
        /// </summary>
        internal static void ReplaceLocked(string source, string target)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (File.Exists(target))
                    {
                        try { File.SetAttributes(target, FileAttributes.Normal); } catch { }
                        try { File.Delete(target); } catch { }
                    }
                    File.Copy(source, target, true);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(300);
                }
            }
            // A rename over the old image works even when Delete is refused, e.g. on a
            // machine where the file is mapped by another user's session.
            try
            {
                string staged = target + ".new";
                File.Copy(source, staged, true);
                try
                {
                    File.Replace(staged, target, null, true);
                    return;
                }
                catch
                {
                    File.Delete(target + ".old");
                    File.Move(target, target + ".old");
                    File.Move(staged, target);
                    return;
                }
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "Could not replace " + target + ". Close Joe Browser and try again. " + (last ?? ex).Message);
            }
        }

        /// <summary>
        /// The fonts and other loose files that used to be shipped beside the executable.
        ///
        /// For a while the installer copied only JoeBrowser.exe, so an installed copy had no
        /// <c>fonts</c> folder and drew no icons at all - while the same build run straight
        /// from <c>dist</c>, which had the folder, looked perfect. That is the exact "icons not
        /// visible, but only after installing" report. The build now embeds the faces as well
        /// (see build.ps1 and Theme.InstallEmbeddedFonts), and this copies the folder when a
        /// package does have one.
        /// </summary>
        internal static void CopyFonts(string sourceExe, string targetDir)
        {
            try
            {
                string from = Path.Combine(Path.GetDirectoryName(sourceExe) ?? "", "fonts");
                if (!Directory.Exists(from)) return;
                string to = Path.Combine(targetDir, "fonts");
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from))
                    File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }
            catch { /* the embedded copies are the fallback, so this is never fatal */ }
        }

        internal static void CreateShortcut(string linkPath, string target, string workDir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object link = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { linkPath });
                Type lt = link.GetType();
                lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { target });
                lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { workDir });
                lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, link,
                    new object[] { "Joe Browser anti detect browser workspace" });
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
            }
            catch { }
        }

        internal static void RegisterUninstall(string dir, string exe)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", AppVersion);
                key.SetValue("Publisher", Publisher);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", dir);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --silent");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try
                {
                    long size = 0;
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        size += new FileInfo(f).Length;
                    key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                }
                catch { }
            }
        }

        /// <summary>Where the product is installed, or the default when it is not installed.</summary>
        public static string InstalledDir()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    object loc = key == null ? null : key.GetValue("InstallLocation");
                    if (loc is string && !string.IsNullOrEmpty((string)loc)) return (string)loc;
                }
            }
            catch { }
            return DefaultDir();
        }

        /// <summary>
        /// Removes shortcuts and the Add/Remove Programs entry, then schedules the program
        /// folder for deletion. User data in %APPDATA% is deliberately kept: profiles, proxies
        /// and browser sessions are the user's, not the installer's.
        /// </summary>
        public static void Uninstall(bool silent)
        {
            string dir = InstalledDir();

            Delete(StartMenuLink());
            Delete(DesktopLink());
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            string exe = Path.Combine(dir, "JoeBrowser.exe");
            if (!File.Exists(exe))
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                return;
            }

            /* A running executable cannot delete itself, so a detached shell waits a moment
               and then removes the folder. This is the standard approach for self-removal. */
            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + exe + "\" & rmdir /s /q \"" + dir + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }
        }

        private static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
