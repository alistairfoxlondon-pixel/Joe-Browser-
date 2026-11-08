using System;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// The guided Google setup. The OAuth client id is the one piece of configuration that
    /// cannot ship inside the binary, so a "Continue with Google" button that has no id has to
    /// do something better than print a sentence pointing at a Settings card the user cannot
    /// reach from the first-run window. This dialog explains the three console clicks, opens
    /// the console, accepts whatever form the id was copied in, and saves it.
    /// </summary>
    internal sealed class GoogleSetupDialog : ToolDialog
    {
        private readonly JBTextBox _id;
        private readonly JBTextBox _secret;
        private readonly JBLabel _status;

        private GoogleSetupDialog(string currentId)
            : base("Set up Google sign-in", "Save and continue", "Cancel", 600, 700)
        {
            AddNote("Joe Browser signs in through Google's official OAuth flow. The consent page opens in your own "
                  + "browser and only the name, email and photo Google returns are kept, on this PC. No password "
                  + "ever reaches the app.");

            AddSection("1.  CREATE AN OAUTH CLIENT");
            AddNote("Sign in to the Google Cloud console, create a project (any name), then APIs & Services > "
                  + "Credentials > Create credentials > OAuth client ID > Desktop app. A Desktop app client "
                  + "accepts the local callback automatically, so no redirect has to be registered.");
            AddAction("Open Google Cloud console", "external",
                delegate { GoogleSignIn.OpenUrl(GoogleSignIn.ConsoleCredentialsUrl); });

            AddSection("2.  PASTE THE CLIENT ID");

            string prefill = GoogleSignIn.NormalizeClientId(currentId);
            if (!GoogleSignIn.LooksValid(prefill)) prefill = FromClipboard() ?? "";

            _id = AddField("CLIENT ID", prefill, "1234567890-abc123.apps.googleusercontent.com",
                "key", false, 38);
            _secret = AddField("CLIENT SECRET  (OPTIONAL)", GoogleSignIn.SavedClientSecret(),
                "GOCSPX-...", "lock", false, 38);

            _status = AddNote("The secret is optional. Joe Browser also sends a PKCE challenge, which Google "
                            + "accepts on its own, so leave the field empty if the console did not show one.");
            _status.ForeColor = Theme.TextFaint;

            Validator = Save;
        }

        /// <summary>Shows the form. True when a valid client id was saved.</summary>
        public static bool Ensure(IWin32Window owner, string currentId)
        {
            try
            {
                using (var dlg = new GoogleSetupDialog(currentId))
                    return dlg.ShowDialog(owner) == DialogResult.OK;
            }
            catch (Exception ex)
            {
                Log.Error("Google setup dialog failed", ex);
                return false;
            }
        }

        /// <summary>
        /// A client id that is already on the clipboard is what people have right after copying
        /// it out of the console, so offer it instead of making them paste it twice.
        /// </summary>
        private static string FromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText()) return null;
                string text = GoogleSignIn.NormalizeClientId(Clipboard.GetText());
                return GoogleSignIn.LooksValid(text) ? text : null;
            }
            catch { return null; }
        }

        private bool Save()
        {
            string id = GoogleSignIn.NormalizeClientId(_id.Value);
            if (id.Length == 0)
            {
                Warn("Paste the client id from the console. It ends with .apps.googleusercontent.com.");
                _id.Box.Focus();
                return false;
            }
            if (!GoogleSignIn.LooksValid(id))
            {
                Warn("That does not look like a client id. Copy the value labelled \"Client ID\", "
                   + "not the project id and not the secret.");
                _id.Box.Focus();
                _id.Box.SelectAll();
                return false;
            }

            try
            {
                var store = new ProfileStore();
                AppSettings settings = store.LoadSettings();
                settings.GoogleClientId = id;
                settings.GoogleClientSecret = (_secret.Value ?? "").Trim();
                store.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                Log.Error("Cannot save the Google client id", ex);
                Warn("The settings could not be saved: " + ex.Message);
                return false;
            }

            Log.Info("Google client id saved (" + id.Substring(0, Math.Min(24, id.Length)) + "...)");
            return true;
        }

        private void Warn(string message)
        {
            _status.Text = message;
            _status.ForeColor = Theme.Danger;
            _status.Invalidate();
        }
    }
}
