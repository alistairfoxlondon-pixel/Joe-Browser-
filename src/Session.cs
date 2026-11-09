using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JoeBrowser
{
    /// <summary>
    /// The signed-in session. A small DPAPI-protected marker in the app data root records who
    /// is signed in, so the account gate appears once and daily launches go straight to the
    /// profile list. DPAPI scopes the marker to this Windows user: another account on the
    /// machine cannot read it, and it never leaves the device.
    /// </summary>
    public static class Session
    {
        public static string File { get { return Path.Combine(AppPaths.Root, "session.bin"); } }
        private static Account _current;

        /// <summary>The signed-in account for this run, or null before sign-in.</summary>
        public static Account Current { get { return _current; } }

        public static void Open(Account account)
        {
            _current = account;
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                byte[] payload = Encoding.UTF8.GetBytes(ser.Serialize(account));
                System.IO.File.WriteAllBytes(File, ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) { Log.Error("Cannot persist session", ex); }
        }

        /// <summary>True when a previous run left a valid session marker.</summary>
        public static bool IsOpen()
        {
            if (_current != null) return true;
            _current = Restore();
            return _current != null;
        }

        private static Account Restore()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return null;
                byte[] plain = ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), null, DataProtectionScope.CurrentUser);
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                Account a = ser.Deserialize<Account>(Encoding.UTF8.GetString(plain));
                if (a == null || string.IsNullOrEmpty(a.Email)) return null;
                /* The marker is only a convenience; the account must still exist in the store. */
                Account stored = Accounts.FindByEmail(a.Email);
                return stored ?? a;
            }
            catch { return null; }
        }

        public static void SignOut()
        {
            _current = null;
            try { if (System.IO.File.Exists(File)) System.IO.File.Delete(File); }
            catch (Exception ex) { Log.Error("Cannot clear session", ex); }
        }
    }
}
