using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// The start-up gate: sign in with an email account, register a new one, or continue
    /// with Google. Registration asks for an address and a password and creates the account
    /// immediately - no verification mail, exactly as the product specifies.
    /// </summary>
    internal sealed class AccountGate : JBForm
    {
        private readonly JBTextBox _email, _password, _name;
        private readonly JBLabel _title, _subtitle, _error;
        private readonly JBButton _signIn, _signUp, _google;
        private readonly JBLink _toggle;
        private bool _registering;

        /// <summary>The signed-in account, or null when the user closed the window.</summary>
        public Account SignedIn;

        public AccountGate()
        {
            Text = "Joe Browser";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(Ui.S(440), Ui.S(448));

            _title = new JBLabel
            {
                Text = "Welcome to Joe Browser",
                Font = Theme.H1,
                AutoSize = false,
                Size = new Size(Ui.S(360), Ui.S(34)),
                Location = new Point(Ui.S(40), Ui.S(28))
            };
            _subtitle = new JBLabel
            {
                Text = "Your profiles, proxies and settings stay on this device.",
                ForeColor = Theme.TextFaint,
                AutoSize = false,
                Size = new Size(Ui.S(360), Ui.S(22)),
                Location = new Point(Ui.S(40), Ui.S(64))
            };

            _email = new JBTextBox { Location = new Point(Ui.S(40), Ui.S(104)), Size = new Size(Ui.S(360), Ui.S(36)) };
            _email.Placeholder = "Email address";

            _name = new JBTextBox { Location = new Point(Ui.S(40), Ui.S(148)), Size = new Size(Ui.S(360), Ui.S(36)) };
            _name.Placeholder = "Display name (optional)";
            _name.Visible = false;

            _password = new JBTextBox { Location = new Point(Ui.S(40), Ui.S(150)), Size = new Size(Ui.S(360), Ui.S(36)) };
            _password.Placeholder = "Password";
            _password.Box.UseSystemPasswordChar = true;

            _error = new JBLabel
            {
                ForeColor = Theme.Danger,
                AutoSize = false,
                /* Two lines, wrapping: a Google failure explains what to do next, and a single
                   22 px line clipped every useful word of it. One line when it only carries the
                   "waiting for Google" status, so it does not sit over the Sign in button. */
                Size = new Size(Ui.S(360), Ui.S(34)),
                Location = new Point(Ui.S(40), Ui.S(186)),
                Visible = false
            };
            _error.Wrap = true;

            _signIn = new JBButton { Text = "Sign in", Kind = BtnKind.Primary, Size = new Size(Ui.S(360), Ui.S(38)), Location = new Point(Ui.S(40), Ui.S(226)) };
            _signIn.Click += delegate { DoEmail(); };
            _signUp = new JBButton { Text = "Create account", Kind = BtnKind.Primary, Size = new Size(Ui.S(360), Ui.S(38)), Location = new Point(Ui.S(40), Ui.S(226)), Visible = false };
            _signUp.Click += delegate { DoEmail(); };

            _google = new JBButton
            {
                Text = "Continue with Google",
                Kind = BtnKind.Ghost,
                BrandIcon = "google",
                Size = new Size(Ui.S(360), Ui.S(38)),
                Location = new Point(Ui.S(40), Ui.S(272))
            };
            _google.Click += delegate { DoGoogle(); };

            _toggle = new JBLink { Text = "New here? Create an account", Location = new Point(Ui.S(40), Ui.S(322)) };
            _toggle.Click += delegate { ToggleMode(); };

            // Enter submits from any field, the way every sign-in form behaves.
            _email.EnterPressed += delegate { DoEmail(); };
            _password.EnterPressed += delegate { DoEmail(); };
            _name.EnterPressed += delegate { DoEmail(); };

            // An old error should not sit under the form while the user fixes the input.
            _email.Box.TextChanged += delegate { _error.Visible = false; };
            _password.Box.TextChanged += delegate { _error.Visible = false; };
            _name.Box.TextChanged += delegate { _error.Visible = false; };

            Controls.AddRange(new Control[] { _title, _subtitle, _email, _name, _password, _error, _signIn, _signUp, _google, _toggle });
            Theme.Restyle(this);
            LayoutForm();

            /* Tab order follows the layout, not the order the controls were created in: the
               display-name field was added last but sits between the email and password boxes,
               so Tab used to jump from the address straight into the buttons. In sign-in mode
               the name field is hidden and the order collapses to email -> password by itself. */
            _email.TabIndex = 0;
            _name.TabIndex = 1;
            _password.TabIndex = 2;
            _signIn.TabIndex = 3;
            _signUp.TabIndex = 3;
            _google.TabIndex = 4;
            _toggle.TabIndex = 5;
            Shown += delegate { try { _email.Box.Focus(); } catch { } };
        }

        /// <summary>
        /// The gate is the only way into the app, so it must not be dismissible by a stray Esc:
        /// closing it quits Joe Browser. Esc clears the focused field instead, and the window
        /// keeps its close button.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                _error.Visible = false;
                if (_password.Box.Focused) _password.Value = "";
                else if (_email.Box.Focused || ActiveControl == _email) _email.Value = "";
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ToggleMode()
        {
            _registering = !_registering;
            _signIn.Visible = !_registering;
            _signUp.Visible = _registering;
            _name.Visible = _registering;
            _toggle.Text = _registering ? "Already have an account? Sign in" : "New here? Create an account";
            Text = "Joe Browser";
            _title.Text = _registering ? "Create your Joe Browser account" : "Welcome to Joe Browser";
            _subtitle.Text = _registering
                ? "An email address and a password are all that is required."
                : "Your profiles, proxies and settings stay on this device.";
            _error.Visible = false;
            LayoutForm();
        }

        /// <summary>
        /// One measured pass over the whole form.
        ///
        /// The window used to be a stack of hand written pixel offsets with two sets of them,
        /// one per mode, and a message box given a fixed 34 px for any text it carried. A Google
        /// failure is four lines, so its tail was drawn underneath the Sign in button, and the
        /// title, which is set in the display face, was given one line whether or not it fitted.
        /// Everything below is placed from what the text actually needs, in one order, and the
        /// window is then sized to the result so nothing can sit outside it.
        /// </summary>
        private void LayoutForm()
        {
            int pad = Ui.S(36);
            int field = Ui.S(40);
            int btn = Ui.S(42);
            int gap = Ui.S(10);

            int inner = Ui.S(440) - pad * 2;

            /* Both titles are set in the display face and both are one line at 440 px; the
               measure is here so a longer translation cannot be clipped at the bottom. */
            int titleH = Math.Max(Ui.S(30), Theme.LineHeight(Theme.H1));
            int subH = Math.Max(Ui.S(20), Theme.LineHeight(Theme.Small));

            int y = Ui.S(30);
            _title.SetBounds(pad, y, inner, titleH);
            y += titleH + Ui.S(6);
            _subtitle.SetBounds(pad, y, inner, subH);
            y += subH + Ui.S(22);

            _email.SetBounds(pad, y, inner, field);
            y += field + gap;
            if (_registering)
            {
                _name.SetBounds(pad, y, inner, field);
                y += field + gap;
            }
            _password.SetBounds(pad, y, inner, field);
            y += field + gap;

            /* The message box takes the height its text needs and nothing is drawn over it: the
               buttons below start under the measured box, whether it is hidden or four lines. */
            if (_error.Visible && !string.IsNullOrEmpty(_error.Text))
            {
                int lines = Math.Max(1, Math.Min(4, CountLines(_error.Text, _error.Font, inner)));
                int h = Math.Max(Ui.S(18), Theme.LineHeight(_error.Font)) * lines + Ui.S(2);
                _error.SetBounds(pad, y, inner, h);
                y += h + Ui.S(6);
            }
            else
            {
                _error.SetBounds(pad, y, inner, 1);
            }

            _signIn.SetBounds(pad, y, inner, btn);
            _signUp.SetBounds(pad, y, inner, btn);
            y += btn + gap;

            _google.SetBounds(pad, y, inner, btn);
            y += btn + Ui.S(16);

            _toggle.SetBounds(pad, y, inner, Ui.S(24));
            y += Ui.S(24) + Ui.S(28);

            ClientSize = new Size(Ui.S(440), Math.Max(y, Ui.S(360)));
        }

        /// <summary>Whole lines a string needs in a given width, using the wrapped measurement.</summary>
        private static int CountLines(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return 1;
            float lineH = Math.Max(1f, (float)Math.Ceiling(font.GetHeight()));
            float used = Ui.MeasureWrapped(null, text, font, width, 8);
            return Math.Max(1, (int)Math.Ceiling(used / lineH));
        }

        private void Fail(string message)
        {
            _error.Text = message;
            _error.Visible = true;
            LayoutForm();
        }

        private void DoEmail()
        {
            _error.Visible = false;
            if (_registering)
            {
                string err = Accounts.Register(_email.Box.Text, _password.Box.Text, _name.Box.Text);
                if (err != null) { Fail(err); return; }
                SignedIn = Accounts.FindByEmail(_email.Box.Text);
            }
            else
            {
                if (!Accounts.Verify(_email.Box.Text, _password.Box.Text)) { Fail("Wrong email or password."); return; }
                /* Second factor, when the account has one. The code is checked against the
                   stored TOTP secret with a one-step window for clock drift. */
                string secret = Accounts.GetTotpSecret(_email.Box.Text);
                if (!string.IsNullOrEmpty(secret))
                {
                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        using (var ask = new AskDialog("Two-factor authentication",
                                   "Enter the 6-digit code from your authenticator app.", "6-digit code", false))
                        {
                            if (ask.ShowDialog(this) != DialogResult.OK) { Fail("Two-factor authentication cancelled."); return; }
                            ok = Totp.Verify(secret, ask.Value);
                        }
                    }
                    if (!ok) { Fail("That code did not match."); return; }
                }
                Accounts.TouchLogin(_email.Box.Text);
                SignedIn = Accounts.FindByEmail(_email.Box.Text);
            }
            /* A modal form returns Cancel unless DialogResult is set explicitly, so the caller
               would treat a successful sign-in as a dismissal and quit. */
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool _busy;

        /// <summary>
        /// Shows that a long step is running and stops the form being clicked again while it
        /// is. Sign-in waits on the browser for up to three minutes, and without this the
        /// window simply froze with no explanation and could not even be closed.
        /// </summary>
        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            _signIn.Enabled = _signUp.Enabled = _google.Enabled = !busy;
            _email.Enabled = _password.Enabled = _name.Enabled = !busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
            UseWaitCursor = busy;
            if (message == null)
            {
                _error.Visible = false;
                _error.ForeColor = Theme.Danger;
                return;
            }
            /* A status is not an error: "Waiting for Google" shown in the error colour made a
               normal step look like a failure. */
            _error.ForeColor = Theme.TextDim;
            _error.Text = message;
            _error.Visible = true;
        }

        /// <summary>
        /// Google sign-up and sign-in are the same flow: a first-time Google account creates a
        /// local account on the spot, which is what "Continue with Google" promises.
        /// </summary>
        private async void DoGoogle()
        {
            if (_busy) return;
            _error.Visible = false;

            try
            {
                /* The client id belongs to the publisher's Google Cloud project and is the one
                   thing the app cannot invent, so when it is missing this opens the guided
                   setup instead of refusing with a message pointing at a Settings card the
                   first-run window cannot even reach - which is why users described sign-up as
                   simply broken. */
                string clientId = GoogleSignIn.ClientId();
                if (!GoogleSignIn.LooksValid(clientId))
                {
                    if (!GoogleSetupDialog.Ensure(this, clientId))
                    {
                        _error.ForeColor = Theme.Danger;
                        Fail("Google sign-in is not set up yet.");
                        return;
                    }
                    clientId = GoogleSignIn.ClientId();
                    if (!GoogleSignIn.LooksValid(clientId))
                    {
                        Fail("Google sign-in is not set up yet.");
                        return;
                    }
                }

                SetBusy(true, "Waiting for Google\u2026 finish in your browser.");
                GoogleResult result;
                try
                {
                    /* On a worker thread: the listener and the token exchange must not block
                       the message loop, or the window cannot repaint the status above. */
                    string id = clientId;
                    result = await Task.Run(new Func<GoogleResult>(delegate { return GoogleSignIn.SignIn(id); }));
                }
                finally
                {
                    SetBusy(false, null);
                }

                if (result == null || !result.Ok)
                {
                    _error.ForeColor = Theme.Danger;
                    string message = result == null ? "Google sign-in failed." : result.Message;
                    if (result != null && GoogleSignIn.IsSetupProblem(result.Code))
                        message += "  Use \u201cContinue with Google\u201d again once the client id is fixed.";
                    Fail(message);
                    return;
                }

                Account account = Accounts.SignInGoogle(result.Identity.Id, result.Identity.Email,
                    result.Identity.Name, result.Identity.Picture);
                if (account == null)
                {
                    _error.ForeColor = Theme.Danger;
                    Fail("Google accepted the sign-in but the account could not be created on this PC. "
                       + "Check that %APPDATA%\\JoeBrowser is writable.");
                    return;
                }

                SignedIn = account;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                /* An async void handler that throws takes the whole process down through the
                   global handler; this keeps a Google problem inside the form. */
                Log.Error("Google sign-in failed", ex);
                SetBusy(false, null);
                _error.ForeColor = Theme.Danger;
                Fail("Google sign-in failed: " + ex.Message);
            }
        }

    }
}

namespace JoeBrowser
{
    /// <summary>
    /// A single-field prompt used for the PIN lock and the authenticator code. It is a real
    /// dialog rather than an inline field so the two paths (app lock and sign-in 2FA) can
    /// share one implementation.
    /// </summary>
    internal sealed class AskDialog : JBForm
    {
        private readonly JBTextBox _field;
        private readonly JBLabel _error;
        public string Value { get { return _field.Box.Text; } }

        public AskDialog(string title, string message, string placeholder, bool password)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(Ui.S(380), Ui.S(190));

            var head = new JBLabel
            {
                Text = message,
                AutoSize = false,
                Size = new Size(Ui.S(320), Ui.S(44)),
                Location = new Point(Ui.S(28), Ui.S(24)),
                ForeColor = Theme.TextFaint
            };
            _field = new JBTextBox { Location = new Point(Ui.S(28), Ui.S(74)), Size = new Size(Ui.S(324), Ui.S(36)) };
            _field.Placeholder = placeholder;
            _field.Box.UseSystemPasswordChar = password;
            _error = new JBLabel
            {
                ForeColor = Theme.Danger,
                AutoSize = false,
                Size = new Size(Ui.S(324), Ui.S(20)),
                Location = new Point(Ui.S(28), Ui.S(114)),
                Visible = false
            };
            var ok = new JBButton { Kind = BtnKind.Primary, Text = "Continue", Size = new Size(Ui.S(324), Ui.S(36)), Location = new Point(Ui.S(28), Ui.S(140)) };
            ok.Click += delegate { Accept(); };
            _field.EnterPressed += delegate { Accept(); };

            Controls.AddRange(new Control[] { head, _field, _error, ok });
            Theme.Restyle(this);
            Shown += delegate { _field.Box.Focus(); };
        }

        private void Accept()
        {
            if (string.IsNullOrEmpty(_field.Box.Text)) { _error.Text = "Enter a value."; _error.Visible = true; return; }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
