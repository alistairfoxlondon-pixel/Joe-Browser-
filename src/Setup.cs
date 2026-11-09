using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace JoeBrowser.Setup
{
    /// <summary>
    /// Joe Browser setup. A self-contained installer: it carries the application as an
    /// embedded payload, installs per user (so no elevation prompt and nothing for Windows
    /// to flag as an elevated installer), creates Start menu and desktop shortcuts carrying
    /// the application's AppUserModelID, and registers a proper Add/Remove Programs entry.
    /// </summary>
    internal static class Program
    {
        internal const string AppId = "JoeBrowser.Browser.1.2";
        internal const string AppName = "Joe Browser";
        internal const string Version = "1.5.26";
        internal const string Publisher = "Joe Goldberg";

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = false, uninstall = false, desktop = true, selfTest = false;
            foreach (string a in args)
            {
                if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase) || a.Equals("/S", StringComparison.OrdinalIgnoreCase)) silent = true;
                if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                if (a.Equals("/nodesktop", StringComparison.OrdinalIgnoreCase)) desktop = false;
                if (a.Equals("--selftest", StringComparison.OrdinalIgnoreCase) || a.Equals("/selftest", StringComparison.OrdinalIgnoreCase)) selfTest = true;
            }

            /* The release build runs the installer with --selftest: it opens no window, but it
               does read the embedded application payload, so a build that lost its resource -
               the one defect that produces an installer which fails on the user's machine and
               nowhere else - fails the build instead of shipping. */
            if (selfTest)
            {
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("JoeBrowser.payload.exe"))
                    {
                        if (s == null) { Console.Error.WriteLine("selftest: the embedded payload is missing"); return 2; }
                        if (s.Length < 1024 * 100) { Console.Error.WriteLine("selftest: the embedded payload is only " + s.Length + " bytes"); return 3; }
                        Console.WriteLine("selftest ok, payload " + s.Length + " bytes");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("selftest failed: " + ex.GetType().Name + ": " + ex.Message);
                    return 4;
                }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (uninstall)
            {
                bool quiet = silent;
                if (!quiet && MessageBox.Show("Remove " + AppName + " from this PC?", AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return 1;
                Installer.Uninstall(silent);
                if (!quiet)
                    MessageBox.Show(AppName + " has been removed. Your profiles and settings in %APPDATA% were kept.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            if (silent)
            {
                var r = Installer.Install(Installer.DefaultDir(), desktop, false);
                return r.Ok ? 0 : 2;
            }

            using (var form = new SetupForm())
            {
                Application.Run(form);
                return form.ExitCode;
            }
        }
    }

    internal sealed class InstallResult
    {
        public bool Ok;
        public string Error;
        public string Dir;
    }

    internal static class Installer
    {
        public static string DefaultDir() { return JoeBrowser.Installation.DefaultDir(); }

        public static InstallResult Install(string dir, bool desktopShortcut, bool launch)
        {
            var result = new InstallResult { Dir = dir };
            try
            {
                Directory.CreateDirectory(dir);
                string exe = Path.Combine(dir, "JoeBrowser.exe");

                /* Replace the payload. The previous copy cannot be overwritten while it is
                   running, so a running instance is closed first - the installer is the one
                   place where that is the correct behaviour. */
                CloseRunningInstances(exe);

                /* Written beside the target and moved into place. The old code deleted the
                   installed executable first and then streamed the payload over it: a payload
                   that could not be read, or a crash mid-copy, left the user with no Joe
                   Browser at all instead of the previous version. */
                string staged = exe + ".new";
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("JoeBrowser.payload.exe"))
                {
                    if (s == null) throw new InvalidOperationException("The installer payload is missing.");
                    if (File.Exists(staged)) File.Delete(staged);
                    using (var fs = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
                        s.CopyTo(fs);
                }
                try { File.SetAttributes(staged, FileAttributes.Normal); } catch { }

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(exe))
                        {
                            try { File.SetAttributes(exe, FileAttributes.Normal); } catch { }
                            try { File.Delete(exe); } catch { }
                        }
                        File.Move(staged, exe);
                        break;
                    }
                    catch (Exception moveError)
                    {
                        if (attempt >= 9)
                        {
                            try { File.Delete(staged); } catch { }
                            throw new IOException("Could not write " + exe + ". " + moveError.Message);
                        }
                        System.Threading.Thread.Sleep(300);
                    }
                }
                try { File.SetAttributes(exe, FileAttributes.Normal); } catch { }

                /* The application's configuration travels inside the installer for the same
                   reason the fonts do: the installed copy is a lone executable, and .NET reads
                   JoeBrowser.exe.config from beside it. Without this file an installed Joe
                   Browser ran without per-monitor DPI awareness and without WinForms' own DPI
                   rescaling, which is what made the installed build look blurry on a scaled
                   display while the portable one did not. */
                WriteConfig(dir, exe);

                /* The payload carries its fonts inside itself now; this covers a package that
                   was built with a side-by-side folder as well. */
                CopyFonts(dir);

                CreateShortcut(Path.Combine(StartMenuDir(), Program.AppName + ".lnk"), exe, dir);
                if (desktopShortcut)
                    CreateShortcut(Path.Combine(DesktopDir(), Program.AppName + ".lnk"), exe, dir);

                RegisterUninstall(dir, exe);
                result.Ok = true;

                if (launch)
                {
                    /* Launched through the shortcut this install just wrote, so the new image is
                       started by the shell rather than by the process that created it. */
                    try
                    {
                        string link = Path.Combine(StartMenuDir(), Program.AppName + ".lnk");
                        string start = File.Exists(link) ? link : exe;
                        Process.Start(new ProcessStartInfo(start) { UseShellExecute = true, WorkingDirectory = dir });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
            }
            return result;
        }

        /// <summary>
        /// Writes the application's config file beside the installed executable, replaced
        /// atomically so an interrupted install cannot leave a half-written one behind.
        /// </summary>
        private static void WriteConfig(string dir, string exe)
        {
            const string resource = "JoeBrowser.payload.exe.config";
            string target = exe + ".config";
            string staged = target + ".new";
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (s == null) return;
                    if (File.Exists(staged)) File.Delete(staged);
                    using (var fs = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
                        s.CopyTo(fs);
                }
                if (File.Exists(target)) File.Delete(target);
                File.Move(staged, target);
            }
            catch
            {
                /* A missing config file degrades the look of the window; failing the whole
                   install over it would be worse. */
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
            }
        }

        /// <summary>
        /// Asks any running copy to exit before its image is replaced. Without this the
        /// installer sat on "Delete" retries until it gave up, and the user was told the
        /// install failed for no reason they could see.
        /// </summary>
        private static void CloseRunningInstances(string exe)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(exe);
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        p.CloseMainWindow();
                        if (!p.WaitForExit(4000))
                        {
                            p.Kill();
                            p.WaitForExit(2000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Copies a side-by-side fonts folder when the package has one.</summary>
        private static void CopyFonts(string dir)
        {
            try
            {
                string from = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "fonts");
                if (!Directory.Exists(from)) return;
                string to = Path.Combine(dir, "fonts");
                Directory.CreateDirectory(to);
                foreach (string f in Directory.GetFiles(from))
                    File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
            catch { }
        }

        public static void Uninstall(bool silent)
        {
            JoeBrowser.Installation.Uninstall(silent);
        }

        private static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        internal static string StartMenuDir() { return JoeBrowser.Installation.StartMenuDir(); }

        internal static string DesktopDir() { return JoeBrowser.Installation.DesktopDir(); }

        /* Shortcuts and the Add/Remove Programs entry live in Installation, which the
           application also compiles: the installer and the install gate must produce exactly
           the same result, and two copies of this logic would drift. */
        private static void CreateShortcut(string linkPath, string target, string workDir)
        {
            JoeBrowser.Installation.CreateShortcut(linkPath, target, workDir);
        }

        private static void RegisterUninstall(string dir, string exe)
        {
            JoeBrowser.Installation.RegisterUninstall(dir, exe);
        }
    }

    /// <summary>Compact installer window: pick a folder, choose shortcuts, install.</summary>
    internal sealed class SetupForm : Form
    {
        private readonly TextBox _path;
        private readonly CheckBox _desktop;
        private readonly CheckBox _launch;
        private readonly Button _install;
        private readonly Button _cancel;
        private readonly Button _browse;
        private readonly Label _title;
        private readonly Label _sub;
        private readonly Label _pathLabel;
        private readonly Label _status;
        private readonly ProgressBar _progress;

        /* Scale for this window, and every metric derived from it.
           The installer was laid out in fixed pixels with fonts asked for in points, which is
           the one combination that cannot be right at more than one display scale: the controls
           stayed 560 px wide while the text they carried grew by 50%, so at 150% the buttons
           overlapped, the path box ran under Browse, and the description was cut mid sentence.
           Everything here is now multiplied by the monitor's scale, and the fonts are asked for
           in device pixels for the same reason the main window does it: the size in the code is
           the size on the glass. This file is compiled alone, so it carries its own copy. */
        private static float _scale = 1f;
        private static int S(int v) { return (int)Math.Round(v * _scale); }

        private Font _fTitle, _fBody, _fSmall, _fButton;
        private Rectangle _iconBox;

        public int ExitCode = 1;

        private static readonly Color Bg = Color.FromArgb(0x16, 0x17, 0x1B);
        private static readonly Color Panel = Color.FromArgb(0x1E, 0x20, 0x26);
        private static readonly Color Line = Color.FromArgb(0x2E, 0x31, 0x39);
        private static readonly Color Ink = Color.FromArgb(0xEC, 0xEE, 0xF2);
        private static readonly Color Faint = Color.FromArgb(0x9A, 0xA1, 0xAE);
        private static readonly Color Accent = Color.FromArgb(0x4C, 0x8D, 0xFF);

        /// <summary>The display scale of the monitor this window opens on, in quarter steps.</summary>
        private static float DetectScale()
        {
            float dpi = 96f;
            /* The desktop's device context already reports the system scale, which is the scale
               the installer's StartPosition will centre it on. DetectScale is static because it
               runs before the window exists, so it cannot use Control.CreateGraphics. */
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            }
            catch { }
            float scale = dpi / 96f;
            if (scale < 1f) scale = 1f;
            if (scale > 3f) scale = 3f;
            return (float)(Math.Round(scale * 4) / 4);
        }

        /// <summary>A font whose em size is device pixels, so it scales with the window.</summary>
        private static Font MakeFont(float px, bool semibold)
        {
            string name = semibold ? "Segoe UI Semibold" : "Segoe UI";
            try { return new Font(name, Math.Max(1f, px * _scale), FontStyle.Regular, GraphicsUnit.Pixel); }
            catch
            {
                return new Font("Tahoma", Math.Max(1f, px * _scale),
                    semibold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            }
        }

        public SetupForm()
        {
            _scale = DetectScale();

            Text = "Joe Browser Setup";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            DoubleBuffered = true;
            /* The manifest declares PerMonitorV2, so the process is DPI aware and WinForms must
               not rescale this form on top of that - it would double-scale the layout on a
               125% or 150% display and clip the buttons. AutoScaleMode.None matches JBForm. */
            AutoScaleMode = AutoScaleMode.None;
            /* Without this the installer window wore the generic WinForms icon while its own
               executable carried the real one - the first thing a new user sees looking wrong. */
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _title = new Label
            {
                Text = "Joe Browser " + Program.Version,
                ForeColor = Ink,
                AutoSize = false,
                BackColor = Color.Transparent
            };
            _sub = new Label
            {
                Text = "Anti detect browser workspace. Installs for this Windows account only. "
                     + "No administrator rights required.",
                ForeColor = Faint,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _pathLabel = new Label { Text = "Install location", ForeColor = Faint, AutoSize = true, BackColor = Color.Transparent };
            _path = new TextBox
            {
                Text = Installer.DefaultDir(),
                BackColor = Panel,
                ForeColor = Ink,
                BorderStyle = BorderStyle.FixedSingle
            };
            _browse = new Button
            {
                Text = "Browse...",
                FlatStyle = FlatStyle.Flat,
                BackColor = Panel,
                ForeColor = Ink
            };
            _browse.FlatAppearance.BorderColor = Line;
            _browse.Click += delegate
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Choose where Joe Browser should be installed";
                    if (Directory.Exists(_path.Text)) dlg.SelectedPath = _path.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
                }
            };

            _desktop = new CheckBox { Text = "Create a desktop shortcut", ForeColor = Ink, AutoSize = true, Checked = true, BackColor = Color.Transparent };
            /* Unchecked by default. Writing an executable and starting it a moment later, from
               the same unsigned process, is the exact shape a reputation engine scores as a
               dropper, and it is the behaviour behind most false positives on installers like
               this one. Starting the app from the shortcut it just created is the same one
               click for the user and does not put that sequence in the installer. */
            _launch = new CheckBox { Text = "Start Joe Browser after installation", ForeColor = Ink, AutoSize = true, Checked = false, BackColor = Color.Transparent };

            _progress = new ProgressBar { Style = ProgressBarStyle.Continuous, Visible = false };
            _status = new Label { ForeColor = Faint, AutoSize = false };

            _install = new Button
            {
                Text = "Install",
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = Color.White
            };
            _install.FlatAppearance.BorderSize = 0;
            _install.Click += delegate { DoInstall(); };

            _cancel = new Button
            {
                Text = "Cancel",
                FlatStyle = FlatStyle.Flat,
                BackColor = Panel,
                ForeColor = Ink
            };
            _cancel.FlatAppearance.BorderColor = Line;
            _cancel.Click += delegate { Close(); };

            Controls.AddRange(new Control[] { _title, _sub, _pathLabel, _path, _browse,
                _desktop, _launch, _progress, _status, _install, _cancel });

            /* Enter installs, Esc cancels - without these two the window ignored the keyboard
               entirely, which reads as an unresponsive installer. */
            AcceptButton = _install;
            CancelButton = _cancel;

            ApplyLayout();
        }

        /// <summary>
        /// Places every control from the current scale, and sizes the window to what the text
        /// actually needs. Called on construction and again if the window is dragged to a
        /// monitor with a different scale.
        /// </summary>
        private void ApplyLayout()
        {
            SuspendLayout();
            try
            {
                foreach (var f in new[] { _fTitle, _fBody, _fSmall, _fButton })
                { if (f != null) { try { f.Dispose(); } catch { } } }
                _fTitle = MakeFont(17f, true);
                _fBody = MakeFont(13.5f, false);
                _fSmall = MakeFont(13f, false);
                _fButton = MakeFont(13.5f, true);

                Font = _fBody;
                _title.Font = _fTitle;
                _sub.Font = _fBody;
                _pathLabel.Font = _fSmall;
                _path.Font = _fBody;
                _browse.Font = _fSmall;
                _desktop.Font = _fBody;
                _launch.Font = _fBody;
                _status.Font = _fSmall;
                _install.Font = _fButton;
                _cancel.Font = _fSmall;

                int pad = S(26);
                int w = S(560);
                int inner = w - pad * 2;
                int y = S(24);

                /* The title sits beside the icon, which OnPaint draws from the same rectangle. */
                int icon = S(46);
                _iconBox = new Rectangle(pad, y, icon, icon);
                _title.SetBounds(pad + icon + S(14), y, inner - icon - S(14), S(26));
                y += Math.Max(S(26), icon) + S(16);

                _sub.MaximumSize = new Size(inner, 0);
                Size subNeeded = _sub.GetPreferredSize(new Size(inner, 0));
                _sub.SetBounds(pad, y, inner, subNeeded.Height);
                y += subNeeded.Height + S(22);

                _pathLabel.SetBounds(pad, y, inner, S(18));
                y += S(24);
                int fieldH = S(30);
                int browseW = S(84);
                _path.SetBounds(pad, y, inner - browseW - S(10), fieldH);
                _browse.SetBounds(pad + inner - browseW, y, browseW, fieldH);
                y += fieldH + S(20);

                _desktop.SetBounds(pad, y, inner, S(24));
                y += S(28);
                _launch.SetBounds(pad, y, inner, S(24));
                y += S(30);

                _progress.SetBounds(pad, y, inner, Math.Max(4, S(6)));
                y += Math.Max(4, S(6)) + S(12);
                _status.SetBounds(pad, y, inner, S(38));
                y += S(38) + S(14);

                int btnH = S(38);
                int installW = S(132);
                int cancelW = S(96);
                _install.SetBounds(pad + inner - installW, y, installW, btnH);
                _cancel.SetBounds(pad + inner - installW - S(10) - cancelW, y, cancelW, btnH);
                y += btnH + S(24);

                ClientSize = new Size(w, y);
            }
            finally { ResumeLayout(true); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            /* The mark, at the size the header was laid out for. The installer used to be a plain
               grey Windows dialog with a Segoe heading; this is the first window a buyer sees, so
               it carries the same mark and the same hairline language as the app behind it. */
            try
            {
                if (_iconBox.Width > 0)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    {
                        if (icon != null)
                        {
                            using (var bmp = icon.ToBitmap())
                                g.DrawImage(bmp, _iconBox);
                        }
                    }
                }
            }
            catch { }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            /* Moving the window to a monitor at a different scale used to leave it at the old
               one: the manifest is PerMonitorV2, so no one else rescales it either. */
            float scale = e.DeviceDpiNew / 96f;
            if (scale < 1f) scale = 1f;
            if (scale > 3f) scale = 3f;
            _scale = (float)(Math.Round(scale * 4) / 4);
            ApplyLayout();
            Invalidate();
        }

        /// <summary>
        /// True once the install finished. The button changes to "Close" at that point, and
        /// because the original Click handler was never removed, pressing it ran a second full
        /// install before closing - so the success path re-installed the app from itself.
        /// </summary>
        private bool _done;

        private async void DoInstall()
        {
            if (_done) { Close(); return; }
            string dir = _path.Text.Trim();
            if (dir.Length == 0)
            {
                _status.ForeColor = Color.FromArgb(0xE8, 0x47, 0x56);
                _status.Text = "Choose an install location.";
                return;
            }
            try
            {
                string full = Path.GetFullPath(dir);
                string root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady && drive.AvailableFreeSpace < 60L * 1024 * 1024)
                    {
                        _status.ForeColor = Color.FromArgb(0xF2, 0xA8, 0x1B);
                        _status.Text = "Not enough free space on " + root + " (needs about 60 MB).";
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _status.ForeColor = Color.FromArgb(0xE8, 0x47, 0x56);
                _status.Text = "That folder cannot be used: " + ex.Message;
                return;
            }

            _install.Enabled = false;
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Marquee;
            _status.ForeColor = Faint;
            _status.Text = "Installing...";
            Application.DoEvents();

            InstallResult result;
            try
            {
                /* Off the UI thread: the copy plus the shortcut and registry work used to block
                   the message loop behind a single DoEvents(), so the window showed "not
                   responding" for the whole install - which on a first run reads as a hang. */
                bool desk = _desktop.Checked, launch = _launch.Checked;
                result = await System.Threading.Tasks.Task.Run(new Func<InstallResult>(
                    delegate { return Installer.Install(dir, desk, launch); }));
            }
            catch (Exception ex)
            {
                /* Install already catches its own failures; this is the belt-and-braces case
                   so a surprise never escapes as an unhandled-exception dialog during setup. */
                result = new InstallResult { Ok = false, Error = ex.Message, Dir = dir };
            }
            if (IsDisposed) return;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;

            if (result.Ok)
            {
                _status.ForeColor = Color.FromArgb(0x57, 0xE8, 0x4A);
                _status.Text = "Installed to " + result.Dir;
                /* The button says Close, so it has to work as Close and the form must not close
                   itself while the user is still reading where things went. */
                _install.Text = "Close";
                _install.Enabled = true;
                _done = true;   // the existing handler now closes instead of reinstalling
                ExitCode = 0;
            }
            else
            {
                _status.ForeColor = Color.FromArgb(0xE8, 0x47, 0x56);
                _status.Text = "Installation failed: " + result.Error;
                _install.Enabled = true;
            }
        }
    }
}
