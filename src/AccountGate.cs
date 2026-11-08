using System;
using System.Drawing;
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
            Text = "Joe Browser - Sign in";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(Ui.S(440), Ui.S(430));

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
                Size = new Size(Ui.S(360), Ui.S(22)),
                Location = new Point(Ui.S(40), Ui.S(196)),
                Visible = false
            };

            _signIn = new JBButton { Text = "Sign in", Kind = BtnKind.Primary, Size = new Size(Ui.S(360), Ui.S(38)), Location = new Point(Ui.S(40), Ui.S(226)) };
            _signIn.Click += delegate { DoEmail(); };
            _signUp = new JBButton { Text = "Create account", Kind = BtnKind.Primary, Size = new Size(Ui.S(360), Ui.S(38)), Location = new Point(Ui.S(40), Ui.S(226)), Visible = false };
            _signUp.Click += delegate { DoEmail(); };

            _google = new JBButton
            {
                Text = "Continue with Google",
                Kind = BtnKind.Ghost,
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
        }

        private void ToggleMode()
        {
            _registering = !_registering;
            _signIn.Visible = !_registering;
            _signUp.Visible = _registering;
            _name.Visible = _registering;
            _password.Location = new Point(Ui.S(40), Ui.S(_registering ? 192 : 150));
            _error.Location = new Point(Ui.S(40), Ui.S(_registering ? 236 : 196));
            int top = _registering ? 266 : 226;
            _signIn.Location = new Point(Ui.S(40), Ui.S(top));
            _signUp.Location = new Point(Ui.S(40), Ui.S(top));
            _google.Location = new Point(Ui.S(40), Ui.S(top + 46));
            _toggle.Location = new Point(Ui.S(40), Ui.S(top + 96));
            _toggle.Text = _registering ? "Already have an account? Sign in" : "New here? Create an account";
            Text = _registering ? "Joe Browser - Create account" : "Joe Browser - Sign in";
            _title.Text = _registering ? "Create your Joe Browser account" : "Welcome to Joe Browser";
            _subtitle.Text = _registering
                ? "An email address and a password are all that is required."
                : "Your profiles, proxies and settings stay on this device.";
            _error.Visible = false;
        }

        private void Fail(string message)
        {
            _error.Text = message;
            _error.Visible = true;
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

        private void DoGoogle()
        {
            /* Real Google sign-in runs through the browser with OAuth. The client id is the
               one piece of configuration that cannot ship inside the app - it belongs to the
               publisher's Google Cloud project. Until it is configured the button explains
               exactly that instead of failing silently. */
            string clientId = GoogleSignIn.ClientId();
            if (!GoogleSignIn.LooksValid(clientId))
            {
                Fail(clientId.Length == 0
                    ? "Add a Google client id in Settings \u203a Account."
                    : "That is not a Google client id. It ends with .apps.googleusercontent.com.");
                return;
            }
            var result = GoogleSignIn.SignIn(clientId);
            if (!result.Ok) { Fail(result.Message); return; }
            SignedIn = Accounts.SignInGoogle(result.Identity.Id, result.Identity.Email,
                result.Identity.Name, result.Identity.Picture);
            DialogResult = DialogResult.OK;
            Close();
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
