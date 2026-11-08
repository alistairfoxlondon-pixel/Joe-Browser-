using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [STAThread]
        private static void Main(string[] args)
        {
            /* Add/Remove Programs invokes the installed executable with --uninstall. This has
               to run before the single-instance mutex, because a running copy would otherwise
               just bring its window forward instead of removing anything. */
            bool uninstall = false, silent = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                    if (a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                }
            }
            if (uninstall)
            {
                if (!silent && MessageBox.Show(
                        "Remove Joe Browser from this PC?\r\n\r\nYour profiles, proxies and browser "
                        + "sessions in %APPDATA%\\JoeBrowser are kept.",
                        "Joe Browser", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                Installation.Uninstall(silent);
                if (!silent)
                    MessageBox.Show("Joe Browser has been removed.", "Joe Browser",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // A second copy would hold the same profile store and settings at once, so bring the
            // running window forward instead. The mutex lives for the life of the process.
            bool created;
            var mutex = new Mutex(true, "Local\\JoeBrowser.SingleInstance", out created);
            if (!created)
            {
                FocusRunningWindow();
                return;
            }

            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }

            /* Deliberately no SetCurrentProcessExplicitAppUserModelID here. An explicit id
               only helps when every shortcut carries the same id; when it does not, Windows
               cannot resolve the identity and falls back to a generic taskbar icon. Without
               one, Windows groups by executable path and uses the window icon, which is now
               the real Joe mark (see AppIcon / JBForm). */

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            /* The display scale has to be known BEFORE any window is constructed. Every control
               position and size is computed with Ui.S() while the form is being built, but fonts
               are in points and therefore already scale with the display. Building at 1.0 on a
               150% screen puts 1.5x text into 1.0x boxes: clipped labels, overlapping rows and
               text spilling out of its cards - the whole app reads as broken. Only MainForm
               ever recalculated this, and only after it had already laid itself out, so every
               dialog (including the first-run sign-in window) was affected. */
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    Ui.Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { Ui.Scale = 1f; }

            Theme.LoadFonts();

            /* Refuse to run as a loose file. Opening the download directly skipped the
               shortcuts, the Add/Remove Programs entry and in-place updates, and left a second
               copy of the product in Downloads. --portable is the deliberate escape hatch for
               the test harness and for support work. */
            bool portable = false;
            if (args != null)
                foreach (string a in args)
                    if (a.Equals("--portable", StringComparison.OrdinalIgnoreCase)) portable = true;

            if (!portable && !Installation.IsRunningInstalled())
            {
                try
                {
                    using (var gate = new InstallGate())
                    {
                        Application.Run(gate);
                    }
                }
                finally
                {
                    Theme.UnloadFonts();
                    try { mutex.ReleaseMutex(); } catch { }
                    try { mutex.Dispose(); } catch { }
                }
                return;
            }

            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Error("UI thread exception", e.Exception);
                ShowFatal(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            };

            try
            {
                AppPaths.EnsureAll();
                /* Small drives fill up quietly: browser caches are the one thing the app owns
                   that is always safe to drop, so a low volume triggers a background clean of
                   every closed profile's cache. Cookies and logins are never touched. */
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { DiskSpace.AutoCleanCaches(); });
                /* Self-repair the original built-in extensions on every start, so an app
                   update never leaves stale manifests or scripts behind. Writes only files
                   whose content actually changed. */
                ExtensionManager.EnsureAll();
                /* The account gate only appears until the first account exists. After that the
                   app remembers the sign-in for this Windows user (DPAPI-protected session
                   marker), so daily use stays a double-click. */
                if (!Session.IsOpen())
                {
                    using (var gate = new AccountGate())
                    {
                        gate.ShowDialog();
                        /* The signed-in account is the only reliable signal: the dialog result
                           is Cancel whenever the window is closed by any other path. */
                        if (gate.SignedIn == null) return;
                        Session.Open(gate.SignedIn);
                    }
                }
                else if (Session.Current != null && Accounts.PinEnabled(Session.Current.Email))
                {
                    /* The PIN protects the app on a shared desk: the session marker remembers
                       who is signed in, but the PIN still has to be typed each launch. */
                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        using (var ask = new AskDialog("Joe Browser is locked",
                                   "Enter your PIN to unlock Joe Browser.", "PIN", true))
                        {
                            if (ask.ShowDialog() != DialogResult.OK) return;
                            ok = Accounts.VerifyPin(Session.Current.Email, ask.Value);
                        }
                    }
                    if (!ok) return;
                }
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Log.Error("Fatal startup error", ex);
                ShowFatal(ex);
            }
            finally
            {
                Theme.UnloadFonts();
                try { mutex.ReleaseMutex(); } catch { }
                try { mutex.Dispose(); } catch { }
            }
        }

        private static void FocusRunningWindow()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcessesByName("JoeBrowser"))
                {
                    try
                    {
                        if (p.Id == me) continue;
                        p.Refresh();
                        IntPtr h = p.MainWindowHandle;
                        if (h == IntPtr.Zero) continue;
                        ShowWindow(h, 9); // SW_RESTORE
                        SetForegroundWindow(h);
                        return;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                Log.Error("Fatal error offered to the user", ex);
                var choice = MessageBox.Show(
                    "Joe Browser hit an unexpected error.\r\n\r\n" +
                    (ex == null ? "" : ex.Message + "\r\n\r\n") +
                    "Report it to the developer on Telegram now?",
                    "Joe Browser",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error);
                if (choice == DialogResult.Yes)
                {
                    // Details ride on the clipboard; the user only adds their own words.
                    Report.CopyDetails(ex == null ? "" : ex.ToString());
                    Report.OpenForm(null, ex == null ? "" :
                        (ex.Message ?? "").Trim() + (ex.StackTrace == null ? "" : "\r\nat " + ex.StackTrace.Split('\n')[0].Trim()));
                }
            }
            catch { }
        }
    }
}
