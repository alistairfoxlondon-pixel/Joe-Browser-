using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// The path a user takes when something looks wrong: describe it in one box, press one
    /// button, and Telegram opens on a chat with the developer with the message already typed.
    /// Technical details never leave the machine by themselves - they are placed on the
    /// clipboard so the user can paste them if they choose to.
    /// </summary>
    internal static class Report
    {
        public const string TelegramUser = "joegoldberg2025";

        /// <summary>Short facts that make a report answerable, in the user's words plus one line.</summary>
        public static string SummaryLine()
        {
            var sb = new StringBuilder();
            sb.Append("Joe Browser " + VersionText());
            try
            {
                var os = Environment.OSVersion;
                sb.Append(" \u00b7 Windows " + os.Version.Major + "." + os.Version.Minor);
                sb.Append(Environment.Is64BitOperatingSystem ? " x64" : " x86");
            }
            catch { }
            return sb.ToString();
        }

        public static string VersionText()
        {
            try { return Application.ProductVersion; }
            catch { return "unknown"; }
        }

        /// <summary>Everything a developer needs: build, browsers found, kernels, log tail.</summary>
        public static string Details()
        {
            var sb = new StringBuilder();
            sb.AppendLine(SummaryLine());
            try { sb.AppendLine("App data: " + AppPaths.Root); } catch { }
            try
            {
                long free;
                var drive = new DriveInfo(Path.GetPathRoot(AppPaths.Root) ?? "C:\\");
                free = drive.IsReady ? drive.AvailableFreeSpace : 0;
                sb.AppendLine("Disk free: " + (free / 1048576L) + " MB");
            }
            catch { }
            try { sb.AppendLine("Account: " + (Session.Current != null ? Session.Current.Email : "(signed out)")); } catch { }
            try
            {
                var browsers = BrowserLocator.Detect("auto", "");
                sb.AppendLine("Default browser: " + (browsers != null && browsers.Found ? browsers.Name + " " + browsers.Version : "(none found)"));
            }
            catch { }
            try
            {
                var kernels = KernelStore.Installed();
                if (kernels.Count > 0)
                {
                    var names = kernels.Select(k => k.Engine + " " + k.Version).ToArray();
                    sb.AppendLine("Kernels: " + string.Join(", ", names));
                }
            }
            catch { }
            try
            {
                int profiles = new ProfileStore().LoadAll().Count;
                sb.AppendLine("Profiles: " + profiles);
            }
            catch { }
            sb.AppendLine();
            string tail = LogTail();
            if (!string.IsNullOrEmpty(tail))
            {
                sb.AppendLine("--- last log lines ---");
                sb.AppendLine(tail);
            }
            return sb.ToString();
        }

        /// <summary>The last few lines of the newest log, so the report has real evidence.</summary>
        public static string LogTail()
        {
            try
            {
                string dir = AppPaths.Logs;
                if (!Directory.Exists(dir)) return "";
                var newest = new DirectoryInfo(dir).GetFiles("*.log")
                    .OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (newest == null) return "";
                var lines = new List<string>();
                using (var sr = new StreamReader(newest.FullName, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        lines.Add(line);
                        if (lines.Count > 400) lines.RemoveAt(0);
                    }
                }
                // Keep enough context to be useful, few enough that it stays pasteable.
                if (lines.Count > 40) lines.RemoveRange(0, lines.Count - 40);
                return string.Join(Environment.NewLine, lines.ToArray());
            }
            catch { return ""; }
        }

        public static void CopyDetails(string userNote)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(Details());
                if (!string.IsNullOrWhiteSpace(userNote))
                {
                    sb.AppendLine("--- what the user reported ---");
                    sb.AppendLine(userNote.Trim());
                }
                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }

        /// <summary>Opens the Telegram chat with the message pre-typed. Returns false if no
        /// handler could start it, in which case the caller can say so in a toast.</summary>
        public static bool OpenTelegram(string note)
        {
            try
            {
                string text = (note ?? "").Trim();
                if (text.Length > 900) text = text.Substring(0, 900);
                string url = "https://t.me/" + TelegramUser + "?text=" + Uri.EscapeDataString(text);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        public static void OpenForm(IWin32Window owner, string preset)
        {
            try
            {
                using (var dlg = new ReportForm(preset)) dlg.ShowDialog(owner);
            }
            catch { }
        }
    }

    /// <summary>One box, one button: the shortest path from "something broke" to a report.</summary>
    internal sealed class ReportForm : JBForm
    {
        private readonly JBTextBox _what;
        private readonly JBButton _send, _copy;
        private readonly JBLabel _status;
        private readonly JBIconButton _x;
        private bool _built;

        public ReportForm(string preset)
        {
            Text = "Report a problem";
            Resizable = true;
            DraggableSidebar = false;
            HeaderHit = 96;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            MinimumSize = new Size(Ui.S(520), Ui.S(360));
            Size = Ui.FitToScreen(this, Ui.S(620), Ui.S(430));

            _what = new JBTextBox(true) { Height = Ui.S(96) };
            if (!string.IsNullOrEmpty(preset)) _what.Value = preset;
            Controls.Add(_what);

            _send = new JBButton { Kind = BtnKind.Primary, Icon = "arrow-right", Text = "Open Telegram", Radius = Theme.ButtonRadius, Height = Theme.Field };
            _send.Click += delegate { Send(); };
            Controls.Add(_send);

            _copy = new JBButton { Kind = BtnKind.Soft, Icon = "copy", Text = "Copy details", Radius = Theme.ButtonRadius, Height = Theme.Field, Tip = "App version, browser, kernels and the last log lines" };
            _copy.Click += delegate
            {
                Report.CopyDetails(_what.Value);
                _status.Text = "Details copied to the clipboard";
            };
            Controls.Add(_copy);

            _status = new JBLabel(LabelRole.Faint, "");
            Controls.Add(_status);

            _x = new JBIconButton { Icon = "close", Danger = true, IconSize = 16, Tip = "Close" };
            _x.Click += delegate { Close(); };
            Controls.Add(_x);

            _built = true;
        }

        private void Send()
        {
            Report.CopyDetails(_what.Value);
            if (Report.OpenTelegram(_what.Value))
                Close();
            else
                _status.Text = "Telegram could not be opened, so the details were copied instead";
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterToScreen();
            LayoutCore();
            _what.Focus();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCore();
        }

        private void LayoutCore()
        {
            if (!_built) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(24);
            _x.SetBounds(w - pad - Ui.S(30), Ui.S(16), Ui.S(30), Ui.S(30));

            int top = Ui.S(100);
            /* The bottom strip is one row: the status note on the left, the two buttons on the
               right, both on the same baseline. The note used to sit above the buttons with the
               two rectangles crossing - a status line drawn over a button it did not own. */
            int buttonH = Ui.S(Theme.ControlHeight);
            int bottom = h - pad;
            int buttonsY = bottom - buttonH;
            _what.SetBounds(pad, top, w - pad * 2, Math.Max(Ui.S(80), buttonsY - top - Ui.S(16)));

            int sw = _send.PreferredWidth(Ui.S(150));
            int cw = _copy.PreferredWidth(Ui.S(120));
            /* The pair is measured before either is placed: at a large scale they were wider
               than the window and the left button was pushed under the window edge. */
            Ui.FitPair(ref cw, ref sw, Ui.S(8), Math.Max(Ui.S(200), w - pad * 2), Ui.S(72), Ui.S(72));
            _send.SetBounds(w - pad - sw, buttonsY, sw, buttonH);
            _copy.SetBounds(w - pad - sw - Ui.S(8) - cw, buttonsY, cw, buttonH);

            int statusY = buttonsY + (buttonH - Ui.S(18)) / 2;
            int statusW = Math.Max(Ui.S(80), (w - pad - sw - Ui.S(8) - cw) - pad - Ui.S(8));
            _status.SetBounds(pad, statusY, statusW, Ui.S(18));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Quality(g);
            int pad = Ui.S(24);
            int w = ClientSize.Width;
            Ui.Text(g, "Report a problem", Theme.H1, Theme.Text, pad, Ui.S(16), w - pad * 2 - Ui.S(40));
            /* One line each, three fixed rows, no wrapping. The layout used to let the
               subtitle wrap into the caption row, so the two painted over each other on a
               dialog that said what a user reports with. */
            Ui.Text(g, "Your message opens in Telegram, already typed.",
                Theme.Small, Theme.TextDim, pad, Ui.S(48), w - pad * 2 - Ui.S(40), "left", 1);
            Ui.Text(g, "Opens Telegram: @" + Report.TelegramUser + " — details on the clipboard.",
                Theme.Caption, Theme.TextFaint, pad, Ui.S(70), w - pad * 2 - Ui.S(40), "left", 1);
            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, pad, Ui.S(96), w - pad, Ui.S(96));
        }
    }
}
