using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Downloads the official Chromium snapshot when no Chromium-based browser is installed.
    /// PowerShell (Expand-Archive) is only available on newer Windows builds, so we use
    /// System.IO.Compression which exists in .NET Framework 4.7.2 out of the box.
    /// </summary>
    internal static class ChromiumDownloader
    {
        private const string Base = "https://storage.googleapis.com/chromium-browser-snapshots/Win_x64/";

        public static bool IsChromiumPresent()
        {
            return BrowserLocator.FindAll().Count > 0;
        }

        public static async Task<string> DownloadLatestAsync(Action<int, string> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(AppPaths.ChromiumDir);

            /* The archive is ~350 MB and expands to roughly double that, so refuse up front
               instead of failing halfway with an I/O error nobody can interpret. */
            DiskSpace.Require(AppPaths.ChromiumDir, 1600L * 1024 * 1024, "download Chromium");

            string rev = await GetStringAsync(Base + "LAST_CHANGE").ConfigureAwait(false);
            rev = rev.Trim();
            if (rev.Length == 0 || rev.Length > 30) throw new Exception("Could not determine the latest Chromium revision.");

            string zip = Path.Combine(AppPaths.ChromiumDir, "chrome-win.zip");
            string url = Base + rev + "/chrome-win.zip";
            try
            {
                await DownloadFileAsync(url, zip, (pct, msg) => { if (progress != null) progress(pct, msg); }, "Chromium " + rev, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (DiskSpace.IsOutOfSpace(ex))
                    throw new InvalidOperationException(
                        "The drive filled up while downloading Chromium (" + DiskSpace.Human(DiskSpace.Free(AppPaths.ChromiumDir)) +
                        " left). Free some space and try again.", ex);
                throw;
            }

            progress?.Invoke(100, "Extracting\u2026");

            string dest = AppPaths.ChromiumDir;
            string tmp = Path.Combine(AppPaths.ChromiumDir, "extract-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string exe = AppPaths.ChromiumExe;
            try
            {
                Directory.CreateDirectory(tmp);
                ZipFile.ExtractToDirectory(zip, tmp);

                string src = Path.Combine(tmp, "chrome-win");
                if (File.Exists(exe)) { try { File.Delete(exe); } catch { } }
                if (Directory.Exists(Path.Combine(dest, "chrome-win")))
                    Directory.Delete(Path.Combine(dest, "chrome-win"), true);
                Directory.Move(src, Path.Combine(dest, "chrome-win"));
            }
            catch (Exception ex)
            {
                if (DiskSpace.IsOutOfSpace(ex))
                    throw new InvalidOperationException(
                        "The drive filled up while unpacking Chromium (" + DiskSpace.Human(DiskSpace.Free(AppPaths.ChromiumDir)) +
                        " left). Free some space and try again.", ex);
                throw;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
                try { File.Delete(zip); } catch { }
            }

            if (!File.Exists(exe)) throw new Exception("Downloaded Chromium could not be verified.");

            return exe;
        }

        private static async Task<string> GetStringAsync(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 30000;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);
            using (var resp = await req.GetResponseAsync().ConfigureAwait(false))
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.ASCII))
                return await sr.ReadToEndAsync().ConfigureAwait(false);
        }

        internal static async Task DownloadFileAsync(string url, string dest, Action<int, string> progress, string label, CancellationToken ct)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 120000;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);

            using (var resp = await req.GetResponseAsync().ConfigureAwait(false))
            {
                long total = resp.ContentLength;
                using (var inStream = resp.GetResponseStream())
                using (var ms = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    byte[] buf = new byte[81920];
                    long got = 0;
                    int lastPct = -1;
                    int n;
                    while ((n = await inStream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await ms.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                        got += n;
                        if (total > 0)
                        {
                            int pct = (int)(got * 100 / total);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                progress?.Invoke(Math.Min(pct, 99), "Downloading " + label + "\u2026  " +
                                    (got / 1048576.0).ToString("F1") + " MB / " + (total / 1048576.0).ToString("F1") + " MB");
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets a browser the profile asked for but the PC does not have yet, the way AdsPower hands
    /// out its kernels. Chromium comes down as a portable zip into the app folder; Firefox and Brave
    /// run their own official installer, which is the only supported way to place them on Windows.
    /// </summary>
    internal static class BrowserFetcher
    {
        public static string Label(string engine)
        {
            if (engine == "firefox") return "Firefox";
            if (engine == "brave") return "Brave";
            if (engine == "edge") return "Microsoft Edge";
            if (engine == "chromium") return "Chromium";
            if (engine == "chrome") return "Google Chrome";
            return "browser";
        }

        /// <summary>True when Joe Browser can fetch this engine itself.</summary>
        public static bool Supports(string engine)
        {
            return engine == "chromium" || engine == "firefox" || engine == "brave" || engine == "chrome";
        }

        /// <summary>Official download page, for engines we deliberately do not install for the user.</summary>
        public static string HomePage(string engine)
        {
            switch (engine)
            {
                case "chrome": return "https://www.google.com/chrome/";
                case "edge": return "https://www.microsoft.com/edge";
                case "brave": return "https://brave.com/download/";
                case "firefox": return "https://www.mozilla.org/firefox/new/";
                default: return "https://www.chromium.org/getting-involved/download-chromium/";
            }
        }

        public static bool IsInstalled(string engine)
        {
            if (string.IsNullOrEmpty(engine) || engine == "auto" || engine == "custom") return true;
            foreach (var b in BrowserLocator.FindAll())
                if (Matches(engine, b)) return true;
            return false;
        }

        private static bool Matches(string engine, BrowserInfo b)
        {
            switch (engine)
            {
                case "chrome": return b.Name == "Chrome" && !b.IsEdge && !b.IsBrave && !b.IsFirefox && !IsPortable(b);
                case "edge": return b.IsEdge;
                case "brave": return b.IsBrave;
                case "firefox": return b.IsFirefox;
                case "chromium": return !b.IsEdge && !b.IsBrave && !b.IsFirefox && (b.Name != "Chrome" || IsPortable(b));
                default: return false;
            }
        }

        private static bool IsPortable(BrowserInfo b)
        {
            return b.Path != null && b.Path.StartsWith(AppPaths.Browsers + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<string> InstallAsync(string engine, Action<int, string> progress, CancellationToken ct)
        {
            if (engine == "chromium") return await ChromiumDownloader.DownloadLatestAsync(progress, ct).ConfigureAwait(false);
            if (engine == "chrome")
            {
                /* Two official routes, in order of reliability. The offline enterprise MSI is one
                   download and one msiexec call; the stub installer additionally fetches its
                   payload while it runs, which fails behind a proxy or a flaky link. Whichever
                   works is fine - the point is that one of them does. */
                Exception first = null;
                try
                {
                    return await RunMsiAsync(engine,
                        "https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi",
                        progress, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    first = ex;
                    Log.Error("Chrome MSI install failed; trying the stub installer", ex);
                }
                try
                {
                    return await RunInstallerAsync(engine,
                        "https://dl.google.com/chrome/install/latest/chrome_installer.exe",
                        "/silent /install", progress, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Google Chrome could not be installed automatically. " + ex.Message +
                        " Download it from " + HomePage("chrome") + " and Joe Browser will pick it up.", first);
                }
            }
            if (engine == "firefox")
                return await RunInstallerAsync(engine,
                    "https://download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=en-US", "/s", progress, ct).ConfigureAwait(false);
            if (engine == "brave")
                /* No --system-level: a per-user install needs no elevation, so the app never has to
                   ask Windows for administrator rights just to add an engine. */
                return await RunInstallerAsync(engine,
                    "https://laptop-updates.brave.com/latest/winx64", "-s", progress, ct).ConfigureAwait(false);
            throw new Exception("Joe Browser does not download " + Label(engine) + " automatically. " +
                "Use the official download page instead.");
        }

        /// <summary>
        /// Chrome ships a real offline MSI for enterprise deployment. Running it through msiexec
        /// gives an exit code that means something, and installs without a second download.
        /// </summary>
        private static async Task<string> RunMsiAsync(string engine, string url,
            Action<int, string> progress, CancellationToken ct)
        {
            string dir = Path.Combine(AppPaths.Browsers, engine);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, Label(engine) + ".msi");

            /* 160 MB down, roughly 500 MB installed. */
            DiskSpace.Require(dir, 900L * 1024 * 1024, "install " + Label(engine));

            await ChromiumDownloader.DownloadFileAsync(url, file, progress, Label(engine), ct).ConfigureAwait(false);

            progress?.Invoke(100, "Installing " + Label(engine) + "\u2026");
            try
            {
                int exit = await Task.Run(delegate()
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = "/i \"" + file + "\" /qn /norestart",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) throw new Exception("Windows Installer did not start.");
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }, ct).ConfigureAwait(false);

                string found = await WaitForInstallAsync(engine, progress, ct).ConfigureAwait(false);
                if (found != null) return found;

                /* 1603 = fatal error during installation, 1625 = blocked by policy. */
                string why = exit == 1603 ? "Windows Installer reported a fatal error (1603)."
                    : exit == 1625 ? "A group policy blocked the installation (1625)."
                    : exit == 1619 ? "The downloaded package could not be opened."
                    : "Windows Installer returned " + exit + ".";
                throw new Exception(Label(engine) + " was not installed. " + why +
                    " You can install it from " + HomePage(engine) + " instead.");
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        private static async Task<string> RunInstallerAsync(string engine, string url, string args,
            Action<int, string> progress, CancellationToken ct)
        {
            string dir = Path.Combine(AppPaths.Browsers, engine);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, Label(engine) + "Setup.exe");

            await ChromiumDownloader.DownloadFileAsync(url, file, progress, Label(engine), ct).ConfigureAwait(false);

            progress?.Invoke(100, "Installing " + Label(engine) + "\u2026");
            try
            {
                int exit = await Task.Run(delegate()
                {
                    var psi = new System.Diagnostics.ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = true };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) throw new Exception("The " + Label(engine) + " installer did not start.");
                        p.WaitForExit();
                        return p.HasExited ? p.ExitCode : 0;
                    }
                }, ct).ConfigureAwait(false);

                string found = await WaitForInstallAsync(engine, progress, ct).ConfigureAwait(false);
                if (found == null)
                    throw new Exception(Label(engine) + " finished with code " + exit + " but no browser was found. " +
                        "Windows may have blocked the installer, or it still needs to finish in the background.");
                return found;
            }
            finally
            {
                /* Keep the installer only while it is still running; a locked file simply stays. */
                try { File.Delete(file); } catch { }
            }
        }

        /// <summary>
        /// Installers keep writing after the process returns, so the lookup is retried. Chrome's
        /// stub downloads the payload after it starts, which is why this waits a full minute.
        /// </summary>
        private static async Task<string> WaitForInstallAsync(string engine, Action<int, string> progress, CancellationToken ct)
        {
            for (int i = 0; i < 120; i++)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var b in BrowserLocator.FindAll())
                    if (Matches(engine, b)) return b.Path;
                if (i == 6) progress?.Invoke(100, "Waiting for " + Label(engine) + " to finish installing\u2026");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            return null;
        }
    }

    /// <summary>Frameless windows-ui progress dialog used while a browser engine is fetched.</summary>
    internal class DownloadDialog : JBForm
    {
        private float _pct;
        private string _status = "Connecting\u2026";
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private JBButton _cancel;
        private string _title = "Get Chromium";

        public CancellationToken Token { get { return _cts.Token; } }

        public DownloadDialog() { Init(); }

        public DownloadDialog(string title) { _title = title; Init(); }

        private void Init()
        {
            Resizable = false;
            AllowMaximize = false;
            DraggableSidebar = false;
            HeaderHit = 52;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Theme.Bg;
            ClientSize = new Size(Ui.S(460), Ui.S(158));

            _cancel = new JBButton { Kind = BtnKind.Soft, Text = "Cancel", Height = Ui.S(Theme.ControlHeight) };
            _cancel.Click += delegate { _cts.Cancel(); _status = "Cancelling\u2026"; _cancel.Enabled = false; Invalidate(); };
            Controls.Add(_cancel);
            int cw = _cancel.PreferredWidth(Ui.S(88));
            _cancel.SetBounds(ClientSize.Width - Ui.S(24) - cw, ClientSize.Height - Ui.S(24) - Ui.S(Theme.ControlHeight), cw, Ui.S(Theme.ControlHeight));

            /* Esc cancels, and says so: closing the dialog with the download still running
               would leave a half-fetched engine on disk with nothing to explain it. */
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Escape || !_cancel.Enabled) return;
                _cts.Cancel();
                _status = "Cancelling\u2026";
                _cancel.Enabled = false;
                Invalidate();
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                // The window's own close button must also stop the download, not orphan it.
                if (e.CloseReason == CloseReason.UserClosing) { try { _cts.Cancel(); } catch { } }
            };
        }

        /// <summary>
        /// Sits the progress card over the middle of the window that asked for it. FormStartPosition
        /// alone put it in the top-left corner over a maximised owner.
        /// </summary>
        public void CenterOn(Form owner)
        {
            if (owner == null) return;
            StartPosition = FormStartPosition.Manual;
            var screen = owner.WindowState == FormWindowState.Maximized
                ? Screen.FromControl(owner).WorkingArea
                : owner.Bounds;
            Location = new Point(screen.Left + (screen.Width - Width) / 2,
                                 screen.Top + (screen.Height - Height) / 2);
        }

        public void UpdateProgress(int pct, string status)
        {
            if (IsDisposed || Disposing) return;
            if (!IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action<int, string>(UpdateProgress), pct, status); return; }
            _pct = Math.Max(_pct, pct);
            _status = status ?? _status;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Quality(g);
            int w = ClientSize.Width;
            Ui.Text(g, _title, Theme.BodyBold, Theme.Text, Ui.S(24), Ui.S(20), w - Ui.S(48), "left");
            /* Long status lines used to be clipped mid-word ("Waiting for Chrome to finish inst…")
               because the width stopped short of the percentage. The text now ends where the
               percentage actually begins, minus a gap. */
            float pctX = w - Ui.S(76);
            Ui.Text(g, _status, Theme.Small, Theme.TextDim, Ui.S(24), Ui.S(46),
                Math.Max(Ui.S(80), pctX - Ui.S(24) - Ui.S(10)), "left");
            Ui.Text(g, (int)_pct + "%", Theme.SmallBold, Theme.Text, w - Ui.S(76), Ui.S(46), Ui.S(52), "right");

            var barRect = new RectangleF(Ui.S(24), Ui.S(74), w - Ui.S(48), Ui.S(4));
            Ui.Fill(g, barRect, Ui.S(2), Theme.Panel2);
            if (_pct > 0)
                Ui.Fill(g, new RectangleF(barRect.X, barRect.Y, (float)barRect.Width * (_pct / 100f), barRect.Height), Ui.S(2), Theme.Accent);
        }
    }
}
