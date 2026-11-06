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
        private static void Main()
        {
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Theme.LoadFonts();

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
                /* Self-repair the original built-in extensions on every start, so an app
                   update never leaves stale manifests or scripts behind. Writes only files
                   whose content actually changed. */
                ExtensionManager.EnsureAll();
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
                MessageBox.Show(
                    "Joe Browser hit an unexpected error and needs to close.\r\n\r\n" +
                    (ex == null ? "" : ex.Message) +
                    "\r\n\r\nDetails were written to:\r\n" + AppPaths.Logs,
                    "Joe Browser",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
