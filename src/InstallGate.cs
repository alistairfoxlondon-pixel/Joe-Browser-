using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Shown when the executable is started from somewhere other than its install folder.
    ///
    /// Joe Browser is a distributed application, not a portable file: it keeps profiles,
    /// proxies and browser engines in a fixed location, updates itself in place, and has to be
    /// removable from Settings. Opening the raw download used to skip all of that and leave a
    /// second copy of the product living in Downloads. This window makes installing the only
    /// way in, and does the install in one click instead of sending the user hunting for
    /// another file.
    /// </summary>
    internal sealed class InstallGate : JBForm
    {
        private readonly JBButton _install;
        private readonly JBButton _cancel;
        private readonly JBLabel _status;
        private readonly CheckBox _desktop;
        private bool _working;

        public InstallGate()
        {
            Text = "Joe Browser - Install";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(Ui.S(480), Ui.S(420));
            BackColor = Theme.Bg;

            var title = new JBLabel
            {
                Text = "Install Joe Browser",
                Font = Theme.H1,
                AutoSize = false,
                Size = new Size(Ui.S(400), Ui.S(30)),
                Location = new Point(Ui.S(40), Ui.S(92))
            };

            var body = new JBLabel
            {
                Text = "Joe Browser has to be installed before it can run. Installing takes a "
                     + "moment and puts everything where Windows expects to find it.",
                ForeColor = Theme.TextFaint,
                AutoSize = false,
                Size = new Size(Ui.S(400), Ui.S(56)),
                Location = new Point(Ui.S(40), Ui.S(126))
            };
            body.Wrap = true;

            _desktop = new CheckBox
            {
                Text = "Create a desktop shortcut",
                Checked = true,
                AutoSize = false,
                Size = new Size(Ui.S(300), Ui.S(24)),
                Location = new Point(Ui.S(42), Ui.S(282)),
                ForeColor = Theme.Text
            };

            _status = new JBLabel
            {
                Text = "",
                ForeColor = Theme.TextFaint,
                AutoSize = false,
                Size = new Size(Ui.S(400), Ui.S(20)),
                Location = new Point(Ui.S(40), Ui.S(314)),
                Visible = false
            };

            _install = new JBButton
            {
                Kind = BtnKind.Primary,
                Text = "Install now",
                Radius = 4,
                Size = new Size(Ui.S(180), Ui.S(38)),
                Location = new Point(Ui.S(40), Ui.S(344))
            };
            _install.Click += delegate { DoInstall(); };

            _cancel = new JBButton
            {
                Kind = BtnKind.Ghost,
                Text = "Not now",
                Radius = 4,
                Size = new Size(Ui.S(120), Ui.S(38)),
                Location = new Point(Ui.S(232), Ui.S(344))
            };
            _cancel.Click += delegate { Close(); };

            Controls.AddRange(new Control[] { title, body, _desktop, _status, _install, _cancel });
            Theme.Restyle(this);

            /* The window is a dead end without a keyboard escape, and a borderless-themed form
               has no close button of its own. */
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && !_working) Close();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Quality(g);
            Logo.Draw(g, new RectangleF(Ui.S(40), Ui.S(28), Ui.S(44), Ui.S(44)));

            float x = Ui.S(40), y = Ui.S(190);
            string[] rows =
            {
                "Start menu and desktop shortcuts",
                "An entry in Settings > Apps, so it can be removed cleanly",
                "Updates replace the old copy instead of piling up"
            };
            foreach (string row in rows)
            {
                Icons.Draw(g, "check", new RectangleF(x, y + Ui.S(2), Ui.S(15), Ui.S(15)), Theme.Success);
                Ui.Text(g, row, Theme.Small, Theme.TextDim, x + Ui.S(23), y, Ui.S(400));
                y += Ui.S(24);
            }
        }

        private void DoInstall()
        {
            if (_working) return;
            _working = true;
            _install.Enabled = false;
            _cancel.Enabled = false;
            _install.Text = "Installing\u2026";
            _status.Visible = true;
            _status.Text = "Copying to " + Installation.DefaultDir();
            Application.DoEvents();

            string error;
            bool ok = Installation.SelfInstall(_desktop.Checked, out error);
            if (!ok)
            {
                _working = false;
                _install.Enabled = true;
                _cancel.Enabled = true;
                _install.Text = "Install now";
                _status.ForeColor = Theme.Danger;
                _status.Text = "Install failed: " + error;
                return;
            }

            _status.ForeColor = Theme.Success;
            _status.Text = "Installed. Starting Joe Browser\u2026";
            Application.DoEvents();

            try
            {
                string exe = Path.Combine(Installation.InstalledDir(), "JoeBrowser.exe");
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Installation.InstalledDir()
                });
            }
            catch { }

            Close();
        }
    }
}
