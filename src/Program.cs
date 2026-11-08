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
            /* Recognised arguments. Anything else is ignored here and forwarded meaningfully:
               Chromium is launched by this app, never by passing its own switches to it, so an
               unknown switch means the user mistyped something and should be told rather than
               have the app start and silently ignore it. */
            bool uninstall = false, silent = false, portable = false, help = false, unknown = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                    else if (a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                    else if (a.Equals("--portable", StringComparison.OrdinalIgnoreCase)) portable = true;
                    else if (a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("/?", StringComparison.OrdinalIgnoreCase)) help = true;
                    else if (a.Length > 0 && (a[0] == '-' || a[0] == '/')) unknown = true;
                }
            }
            if (help)
            {
                MessageBox.Show(
                    "Joe Browser\r\n\r\n"
                    + "  --portable     run from this folder without installing\r\n"
                    + "  --uninstall    remove Joe Browser (profiles are kept)\r\n"
                    + "  --silent       no prompts, for the uninstaller\r\n\r\n"
                    + "Without an argument Joe Browser starts normally.",
                    "Joe Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
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
            //
            // The wait matters for the installer hand-off: it starts the installed copy while the
            // copy that was just installed still holds this mutex, so the new process used to see
            // "already running", focus a window that was about to close, and exit. The install
            // looked like it did nothing. Waiting a few seconds lets the old process release the
            // mutex and the real one start.
            Mutex mutex = AcquireInstance(0);
            if (mutex == null)
            {
                FocusRunningWindow();
                mutex = AcquireInstance(6000);
                if (mutex == null) return;
            }

            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }

            /* Deliberately no SetCurrentProcessExplicitAppUserModelID here. An explicit id
               only helps when every shortcut carries the same id; when it does not, Windows
               cannot resolve the identity and falls back to a generic taskbar icon. Without
               one, Windows groups by executable path and uses the window icon, which is now
               the real Joe mark (see AppIcon / JBForm). */

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            /* Registered before the first window exists. The install gate used to run with no
               handler at all, so a failure while installing showed the raw .NET crash dialog -
               the "unexpected error" users reported when the very first thing they did was
               install the app. */
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Error("UI thread exception", e.Exception);
                ShowFatal(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            };

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

            /* A data folder that cannot be written is the quiet cause of a whole family of
               reports ("my profiles vanish", "settings never stick", "sign-in works but the
               account is gone next launch"). Say it once, clearly, instead of failing later in
               a dozen unrelated places. */
            if (!AppPaths.IsWritable())
            {
                Log.Error("The data folder is not writable: " + AppPaths.Root);
                try
                {
                    MessageBox.Show(
                        "Joe Browser cannot write to its data folder:\n\n" + AppPaths.Root +
                        "\n\nProfiles and settings cannot be saved until this is fixed. Check folder " +
                        "permissions, or move the folder somewhere writable (for example out of OneDrive " +
                        "or a redirected AppData).",
                        "Joe Browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
            }

            /* Refuse to run as a loose file. Opening the download directly skipped the
               shortcuts, the Add/Remove Programs entry and in-place updates, and left a second
               copy of the product in Downloads. --portable is the deliberate escape hatch for
               the test harness and for support work; --help lists the switching options. */
            if (unknown && !silent)
            {
                /* Say it once, then carry on: a stray switch should not stop the app from
                   opening, but it should not be invisible either. */
                Log.Info("Ignored unrecognised command line arguments.");
            }

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

        /// <summary>
        /// Takes the single-instance mutex, retrying for up to <paramref name="waitMs"/>. Returns
        /// null when another copy of Joe Browser still owns it.
        /// </summary>
        private static Mutex AcquireInstance(int waitMs)
        {
            const string name = "Local\\JoeBrowser.SingleInstance";
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, waitMs));
            while (true)
            {
                bool created;
                Mutex m = null;
                try
                {
                    m = new Mutex(true, name, out created);
                    if (created) return m;
                }
                catch (Exception ex)
                {
                    /* A mutex that cannot be created at all (a locked-down policy profile)
                       should not stop the app from starting. The placeholder is not owned, so
                       the ReleaseMutex in the caller's finally just throws and is ignored. */
                    Log.Error("Single-instance mutex unavailable", ex);
                    try { return new Mutex(); } catch { return null; }
                }
                try { m.Dispose(); } catch { }
                if (DateTime.UtcNow >= deadline) return null;
                System.Threading.Thread.Sleep(200);
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

        /// <summary>
        /// The last-resort report. It is calm rather than alarming: the crash is already in the
        /// log, the details are already on the clipboard, and the user is offered one action.
        ///
        /// Two things used to make this worse than the bug it reported. It asked "report it on
        /// Telegram now?" before the user could copy anything or even read the message, and it
        /// was rendered with the raw WinForms MessageBox, so the one dialog people are most
        /// likely to screenshot was the only one that ignored the app's design.
        /// </summary>
        private static void ShowFatal(Exception ex)
        {
            try
            {
                Log.Error("Fatal error offered to the user", ex);

                string detail = ex == null ? "" : (ex.Message ?? "").Trim();
                if (ex != null && ex.StackTrace != null)
                    detail += "\n" + ex.StackTrace.Split('\n')[0].Trim();

                /* Details on the clipboard before anything is asked, so a user who closes the
                   dialog still has something to send. */
                try { Report.CopyDetails(ex == null ? "" : ex.ToString()); } catch { }

                string logFile = Log.CurrentFile();
                string body = (string.IsNullOrEmpty(detail)
                        ? "" : "Details (already copied to the clipboard):\n" + detail + "\n\n")
                    + (logFile == null
                        ? "The full report is in %APPDATA%\\JoeBrowser\\Logs."
                        : "The full report is in " + logFile + ".");

                bool send;
                try
                {
                    send = ConfirmForm.Ask(null, "Joe Browser ran into a problem",
                        "Something went wrong, and the window it happened in had to close. "
                        + "Your profiles and settings are safe.",
                        body, "Send a report", BtnKind.Primary);
                }
                catch
                {
                    /* The themed dialog needs the fonts and theme tokens; if the failure
                       happened before those were ready there still has to be a dialog. */
                    send = MessageBox.Show(
                        "Joe Browser ran into a problem.\n\n" + body + "\n\nSend a report now?",
                        "Joe Browser", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                }

                if (send)
                {
                    try
                    {
                        Report.OpenForm(null, detail);
                    }
                    catch (Exception reportError)
                    {
                        Log.Error("Could not open the report window", reportError);
                    }
                }
            }
            catch
            {
                /* A crash dialog that crashes would leave the process unable to say anything
                   at all, so this path is deliberately silent. */
            }
        }
    }
}
