using System;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Account security settings: the profile-lock PIN and authenticator-app 2FA with a
    /// scannable QR code. Both are optional; the dialog also removes either one.
    /// </summary>
    internal sealed class SecurityDialog : JBForm
    {
        private readonly string _email;
        private readonly JBLabel _pinStatus, _totpStatus, _error;
        private readonly JBTextBox _pin, _totp;
        private readonly PictureBox _qr;
        private JBButton _totpRemove;

        public SecurityDialog(string email)
        {
            _email = email;
            Text = "Security, Joe Browser";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(Ui.S(560), Ui.S(560));

            int left = Ui.S(28);

            var pinHead = new JBLabel { Text = "Profile lock PIN", Font = Theme.H2, AutoSize = true, Location = new Point(left, Ui.S(20)) };
            _pinStatus = new JBLabel { AutoSize = false, Size = new Size(Ui.S(280), Ui.S(20)), Location = new Point(left, Ui.S(48)), ForeColor = Theme.TextFaint };

            _pin = new JBTextBox { Location = new Point(left, Ui.S(74)), Size = new Size(Ui.S(190), Ui.S(34)) };
            _pin.Placeholder = "4+ digits, empty to remove";

            var pinApply = new JBButton { Text = "Save PIN", Kind = BtnKind.Soft, Location = new Point(left + Ui.S(200), Ui.S(74)), Size = new Size(Ui.S(110), Ui.S(34)) };
            pinApply.Click += delegate { SavePin(); };

            var totpHead = new JBLabel { Text = "Two-factor authentication (authenticator app)", Font = Theme.H2, AutoSize = true, Location = new Point(left, Ui.S(126)) };
            _totpStatus = new JBLabel { AutoSize = false, Size = new Size(Ui.S(500), Ui.S(20)), Location = new Point(left, Ui.S(154)), ForeColor = Theme.TextFaint };

            _qr = new PictureBox { Location = new Point(left, Ui.S(182)), Size = new Size(Ui.S(180), Ui.S(180)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };

            var secretLabel = new JBLabel
            {
                Text = "Scan the code with Google Authenticator, Authy or any TOTP app, or enter the key by hand:",
                AutoSize = false,
                Size = new Size(Ui.S(320), Ui.S(40)),
                Location = new Point(left + Ui.S(196), Ui.S(186)),
                ForeColor = Theme.TextFaint
            };
            var secret = new JBTextBox { Location = new Point(left + Ui.S(196), Ui.S(230)), Size = new Size(Ui.S(320), Ui.S(34)) };
            secret.Box.ReadOnly = true;

            var copySecret = new JBButton { Text = "Copy key", Kind = BtnKind.Soft, Location = new Point(left + Ui.S(196), Ui.S(272)), Size = new Size(Ui.S(100), Ui.S(32)) };
            copySecret.Click += delegate
            {
                string s = secret.Box.Text;
                if (!string.IsNullOrEmpty(s)) { try { Clipboard.SetText(s); } catch { } }
            };

            _totp = new JBTextBox { Location = new Point(left + Ui.S(196), Ui.S(314)), Size = new Size(Ui.S(180), Ui.S(34)) };
            _totp.Placeholder = "6-digit code";

            var totpApply = new JBButton { Text = "Enable 2FA", Kind = BtnKind.Primary, Location = new Point(left + Ui.S(386), Ui.S(314)), Size = new Size(Ui.S(130), Ui.S(34)) };
            totpApply.Click += delegate { EnableTotp(secret.Box.Text); };

            var totpRemove = new JBButton { Text = "Turn off 2FA", Kind = BtnKind.Ghost, Location = new Point(left + Ui.S(196), Ui.S(356)), Size = new Size(Ui.S(130), Ui.S(30)) };
            totpRemove.Click += delegate { DisableTotp(); };
            _totpRemove = totpRemove;

            _error = new JBLabel { ForeColor = Theme.Danger, AutoSize = false, Size = new Size(Ui.S(500), Ui.S(20)), Location = new Point(left, Ui.S(396)) };

            var close = new JBButton { Text = "Done", Kind = BtnKind.Soft, Location = new Point(left, Ui.S(430)), Size = new Size(Ui.S(120), Ui.S(34)) };
            close.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                pinHead, _pinStatus, _pin, pinApply,
                totpHead, _totpStatus, _qr, secretLabel, secret, copySecret,
                _totp, totpApply, totpRemove, _error, close
            });

            RefreshState();
            secret.Box.Text = CurrentSecretOrNew();
            ShowQr(secret.Box.Text);
            Theme.Restyle(this);
        }

        private string CurrentSecretOrNew()
        {
            string existing = Accounts.GetTotpSecret(_email);
            if (!string.IsNullOrEmpty(existing)) return existing;
            /* A fresh candidate secret every time the dialog opens. It only becomes active
               when the user proves possession by typing a valid code, so scanning a QR that
               is never confirmed leaves no half-enabled 2FA behind. */
            return Totp.NewSecret();
        }

        private void ShowQr(string secret)
        {
            try
            {
                string uri = Totp.OtpAuthUri(secret, _email, "Joe Browser");
                QrCode code = QrCode.Encode(uri);
                if (code == null)
                {
                    /* A blank QR box with no word reads as a broken app; the key beside it is
                       the fallback, so say so. */
                    Fail("The code could not be generated for this key - enter it by hand instead.");
                    return;
                }
                var old = _qr.Image;
                _qr.Image = code.ToBitmap(6, 4);
                if (old != null) old.Dispose();
            }
            catch (Exception ex) { Log.Error("QR render", ex); }
        }

        /// <summary>The QR bitmap is a GDI object; the PictureBox does not dispose it on close.</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try
            {
                var img = _qr.Image;
                _qr.Image = null;
                if (img != null) img.Dispose();
            }
            catch { }
        }

        private void Fail(string m) { _error.Text = m; _error.Visible = true; }

        /* Accounts.SetPin/SetTotpSecret throw when the store refuses the write; an uncaught
           throw here went straight to the crash dialog. */
        private void SavePin()
        {
            _error.Visible = false;
            try
            {
                string err = Accounts.SetPin(_email, _pin.Box.Text);
                if (err != null) { Fail(err); return; }
                _pin.Box.Text = "";
                RefreshState();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void EnableTotp(string candidate)
        {
            _error.Visible = false;
            try
            {
                string code = _totp.Box.Text.Trim();
                if (string.IsNullOrEmpty(candidate)) { Fail("No setup key to confirm."); return; }
                if (!Totp.Verify(candidate, code)) { Fail("That code did not match. Check the app and try again."); return; }
                Accounts.SetTotpSecret(_email, candidate);
                _totp.Box.Text = "";
                RefreshState();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void DisableTotp()
        {
            _error.Visible = false;
            /* Turning the second factor off is the single most valuable thing a thief at an
               unlocked desk can do, and it used to be one unconfirmed click away. The app asks,
               plainly, before removing a protection. */
            if (!ConfirmForm.Ask(this, "Turn off two-step code?",
                "Sign-in will no longer ask for the 6-digit code from your authenticator.",
                "You can turn it back on here at any time.", "Turn off", BtnKind.Danger)) return;
            try
            {
                Accounts.SetTotpSecret(_email, null);
                RefreshState();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void RefreshState()
        {
            _pinStatus.Text = Accounts.PinEnabled(_email)
                ? "A PIN is set. Opening this app asks for it."
                : "No PIN set. Anyone at this desk can open the app.";
            bool two = !string.IsNullOrEmpty(Accounts.GetTotpSecret(_email));
            _totpStatus.Text = two
                ? "2FA is ON: sign-in asks for a 6-digit code from your authenticator."
                : "2FA is OFF. Enable it to require a rotating code at sign-in.";
            /* Turning 2FA off when it is off walks the user through a confirm dialog to change
               nothing. */
            if (_totpRemove != null) _totpRemove.Enabled = two;
        }
    }
}
