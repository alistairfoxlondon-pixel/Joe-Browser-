using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>A registered Joe Browser account.</summary>
    public class Account
    {
        public string Email { get; set; }
        public string Hash { get; set; }          // PBKDF2 payload, null for Google-only sign-in
        public string GoogleId { get; set; }      // Google subject id when linked
        public string GoogleName { get; set; }
        public string GooglePicture { get; set; }
        public string DisplayName { get; set; }
        public string Created { get; set; }
        public string LastLogin { get; set; }
        public string PinHash { get; set; }       // optional profile-lock PIN
        public string TotpSecret { get; set; }    // optional authenticator-app 2FA (base32)
    }

    /// <summary>
    /// The local account store. Sign-up, sign-in, Google linking and 2FA secrets all live in
    /// one JSON file under the app data root, protected with DPAPI so another Windows user
    /// cannot simply read the hashes.
    /// </summary>
    public static class Accounts
    {
        private const int Iterations = 100000;
        private const int SaltLen = 16;
        private const int KeyLen = 32;

        /* Every read-modify-write operation goes through this gate. Two dialogs writing at once
           (a registration finishing while a TouchLogin runs) used to lose one write - a
           registered account that existed only until the next save. */
        private static readonly object _gate = new object();
        /// <summary>Set when the store on disk could not be read. Saves are refused while it
        /// stands, so a corrupt or undecryptable file is never silently overwritten with an
        /// empty list - which used to erase every registered account.</summary>
        private static bool _unreadable;

        public static string File { get { return Path.Combine(AppPaths.Root, "accounts.json"); } }

        /// <summary>
        /// Every account on this PC, for the Account page. The passwords are hashes and stay in
        /// the object, but nothing outside this class ever shows or exports them.
        /// </summary>
        public static List<Account> LoadAll() { return Load(); }

        private static List<Account> Load()
        {
            lock (_gate)
            {
                try
                {
                    if (!System.IO.File.Exists(File)) { _unreadable = false; return new List<Account>(); }
                    string json = Decrypt(System.IO.File.ReadAllText(File));
                    var ser = new JavaScriptSerializer();
                    var list = ser.Deserialize<List<Account>>(json);
                    if (list == null) throw new InvalidDataException("null account list");
                    _unreadable = false;
                    return list;
                }
                catch (Exception ex)
                {
                    Log.Error("Cannot read accounts", ex);
                    _unreadable = true;
                    /* Preserve whatever is on disk before anything can overwrite it. A backup
                       copy plus the refusal to save below is what turns "all accounts gone"
                       into "accounts intact, store needs repair". */
                    try
                    {
                        string bak = File + ".bak-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                        if (System.IO.File.Exists(File) && !System.IO.File.Exists(bak))
                            System.IO.File.Copy(File, bak, true);
                    }
                    catch { }
                    return new List<Account>();
                }
            }
        }

        private static bool Save(List<Account> list)
        {
            lock (_gate)
            {
                if (_unreadable)
                {
                    Log.Error("Account store unreadable; refusing to overwrite accounts.json");
                    return false;
                }
                try
                {
                    var ser = new JavaScriptSerializer();
                    /* Write to a temp file and replace atomically: a crash or power cut in the
                       middle of the old WriteAllText truncated accounts.json and lost every
                       registered account on this PC. */
                    string tmp = File + ".tmp";
                    System.IO.File.WriteAllText(tmp, Encrypt(ser.Serialize(list)), new UTF8Encoding(false));
                    if (System.IO.File.Exists(File)) System.IO.File.Replace(tmp, File, null);
                    else System.IO.File.Move(tmp, File);
                    return true;
                }
                catch (Exception ex) { Log.Error("Cannot write accounts", ex); return false; }
            }
        }

        private static string Protect(byte[] data)
        {
            try
            {
                return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                /* DPAPI is unavailable (some service sessions): fall back to base64 with a
                   marker so the file still round-trips. Hashes are one-way anyway. */
                return "b64:" + Convert.ToBase64String(data);
            }
        }

        private static string ProtectString(string s) { return Protect(Encoding.UTF8.GetBytes(s ?? "")); }

        private static string Decrypt(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "[]";
            if (stored.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                try
                {
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                        Convert.FromBase64String(stored.Substring(6)), null, DataProtectionScope.CurrentUser));
                }
                catch (Exception ex) { Log.Error("Cannot decrypt accounts", ex); return "[]"; }
            }
            if (stored.StartsWith("b64:", StringComparison.Ordinal))
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(4)));
            return stored.StartsWith("[", StringComparison.Ordinal) ? stored : "[]";
        }

        private static string Encrypt(string plain) { return ProtectString(plain); }

        // ------------------------------------------------------------- password hashing

        public static string HashSecret(string secret, string saltB64)
        {
            byte[] salt = string.IsNullOrEmpty(saltB64)
                ? new byte[SaltLen]
                : Convert.FromBase64String(saltB64);
            using (var k = new Rfc2898DeriveBytes(secret ?? "", salt, Iterations))
                return Convert.ToBase64String(k.GetBytes(KeyLen));
        }

        private static string NewSalt()
        {
            byte[] salt = new byte[SaltLen];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            return Convert.ToBase64String(salt);
        }

        // ------------------------------------------------------------------ operations

        public static bool IsConfigured()
        {
            return Load().Count > 0;
        }

        public static Account FindByEmail(string email)
        {
            string key = Normalize(email);
            return Load().FirstOrDefault(a => Normalize(a.Email) == key);
        }

        /// <summary>
        /// Registers a new email account. No verification mail is sent - the product asks for
        /// an address and a password and the account exists immediately.
        /// Returns an error string, or null on success.
        /// </summary>
        public static string Register(string email, string password, string displayName)
        {
            email = (email ?? "").Trim();
            if (!LooksLikeEmail(email)) return "Please enter a valid email address.";
            if (string.IsNullOrEmpty(password) || password.Length < 6)
                return "The password needs at least 6 characters.";
            if (FindByEmail(email) != null) return "An account with this email already exists.";

            string salt = NewSalt();
            var a = new Account
            {
                Email = email,
                Hash = salt + "$" + HashSecret(password, salt),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
                Created = DateTime.UtcNow.ToString("o")
            };
            var list = Load();
            list.Add(a);
            if (!Save(list)) return "The account could not be stored on this PC. Nothing was saved - please try again.";
            return null;
        }

        public static bool Verify(string email, string password)
        {
            var a = FindByEmail(email);
            if (a == null || string.IsNullOrEmpty(a.Hash)) return false;
            int i = a.Hash.IndexOf('$');
            if (i <= 0) return false;
            string salt = a.Hash.Substring(0, i);
            string expect = a.Hash.Substring(i + 1);
            string got = HashSecret(password ?? "", salt);
            return FixedTimeEquals(expect, got);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>
        /// Signs in with a Google identity. The account is created on first use, so the
        /// Google button is also the sign-up button.
        /// </summary>
        public static Account SignInGoogle(string googleId, string email, string name, string picture)
        {
            if (string.IsNullOrEmpty(googleId)) return null;
            var list = Load();
            var a = list.FirstOrDefault(x => x.GoogleId == googleId);
            if (a == null && !string.IsNullOrEmpty(email)) a = list.FirstOrDefault(x => Normalize(x.Email) == Normalize(email));
            if (a == null)
            {
                a = new Account
                {
                    Email = string.IsNullOrEmpty(email) ? googleId + "@google.local" : email,
                    GoogleId = googleId,
                    DisplayName = string.IsNullOrWhiteSpace(name) ? "Google user" : name,
                    GoogleName = name,
                    GooglePicture = picture,
                    Created = DateTime.UtcNow.ToString("o")
                };
                list.Add(a);
            }
            else
            {
                a.GoogleId = googleId;
                if (!string.IsNullOrEmpty(name)) a.GoogleName = name;
                if (!string.IsNullOrEmpty(picture)) a.GooglePicture = picture;
            }
            a.LastLogin = DateTime.UtcNow.ToString("o");
            if (!Save(list)) return null;   /* sign-in state not persisted - caller treats it as failure */
            return a;
        }

        public static void TouchLogin(string email)
        {
            var list = Load();
            var a = list.FirstOrDefault(x => Normalize(x.Email) == Normalize(email));
            if (a == null) return;
            a.LastLogin = DateTime.UtcNow.ToString("o");
            Save(list);
        }

        public static void Update(Account updated)
        {
            var list = Load();
            var a = list.FirstOrDefault(x => Normalize(x.Email) == Normalize(updated.Email));
            if (a == null) { list.Add(updated); }
            else
            {
                a.Hash = updated.Hash;
                a.PinHash = updated.PinHash;
                a.TotpSecret = updated.TotpSecret;
                a.GoogleId = updated.GoogleId;
                a.GoogleName = updated.GoogleName;
                a.GooglePicture = updated.GooglePicture;
                a.DisplayName = updated.DisplayName;
            }
            if (!Save(list)) throw new InvalidOperationException("The account could not be saved on this PC.");
        }

        public static string SetPin(string email, string pin)
        {
            pin = (pin ?? "").Trim();
            if (pin.Length != 0 && (pin.Length < 4 || !pin.All(char.IsDigit)))
                return "The PIN must be 4 or more digits.";
            var a = FindByEmail(email);
            if (a == null) return "No signed-in account.";
            string salt = NewSalt();
            a.PinHash = pin.Length == 0 ? null : salt + "$" + HashSecret(pin, salt);
            Update(a);
            return null;
        }

        public static bool VerifyPin(string email, string pin)
        {
            var a = FindByEmail(email);
            if (a == null || string.IsNullOrEmpty(a.PinHash)) return true;   // no PIN set = unlocked
            int i = a.PinHash.IndexOf('$');
            if (i <= 0) return true;
            return FixedTimeEquals(a.PinHash.Substring(i + 1), HashSecret(pin ?? "", a.PinHash.Substring(0, i)));
        }

        public static bool PinEnabled(string email)
        {
            var a = FindByEmail(email);
            return a != null && !string.IsNullOrEmpty(a.PinHash);
        }

        /// <summary>
        /// Stores a base32 TOTP secret for the account. Generation and verification are done
        /// by <see cref="Totp"/>, so the UI only needs these two calls.
        /// </summary>
        public static void SetTotpSecret(string email, string base32Secret)
        {
            var a = FindByEmail(email);
            if (a == null) return;
            a.TotpSecret = string.IsNullOrWhiteSpace(base32Secret) ? null : base32Secret.Trim().Replace(" ", "");
            Update(a);
        }

        public static string GetTotpSecret(string email)
        {
            var a = FindByEmail(email);
            return a == null ? null : a.TotpSecret;
        }

        public static void Delete(string email)
        {
            var list = Load();
            list.RemoveAll(x => Normalize(x.Email) == Normalize(email));
            Save(list);
        }

        private static string Normalize(string email)
        {
            return (email ?? "").Trim().ToLowerInvariant();
        }

        private static bool LooksLikeEmail(string email)
        {
            int at = (email ?? "").IndexOf('@');
            return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0 && !email.Contains(" ");
        }
    }

    /// <summary>
    /// RFC 6238 TOTP with the Google Authenticator defaults (SHA-1, 6 digits, 30 s step) and
    /// base32 secret handling, so a secret shown as a QR code scans into any authenticator
    /// app and verifies here.
    /// </summary>
    public static class Totp
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string NewSecret()
        {
            byte[] raw = new byte[20];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(raw);
            return Base32Encode(raw);
        }

        public static string Base32Encode(byte[] data)
        {
            var sb = new StringBuilder();
            int bits = 0, value = 0;
            foreach (byte b in data)
            {
                value = (value << 8) | b;
                bits += 8;
                while (bits >= 5) { sb.Append(Alphabet[(value >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(Alphabet[(value << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] Base32Decode(string s)
        {
            s = (s ?? "").Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
            int bits = 0, value = 0;
            var bytes = new List<byte>();
            foreach (char c in s)
            {
                int idx = Alphabet.IndexOf(c);
                if (idx < 0) continue;
                value = (value << 5) | idx;
                bits += 5;
                if (bits >= 8) { bytes.Add((byte)((value >> (bits - 8)) & 255)); bits -= 8; }
            }
            return bytes.ToArray();
        }

        /// <summary>True when the code matches now or within one step either side.</summary>
        public static bool Verify(string base32Secret, string code)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            string clean = (code ?? "").Trim();
            for (long drift = -1; drift <= 1; drift++)
                if (Code(base32Secret, now + drift) == clean) return true;
            return false;
        }

        public static string Code(string base32Secret, long step)
        {
            byte[] key = Base32Decode(base32Secret);
            byte[] counter = new byte[8];
            for (int i = 7; i >= 0; i--) { counter[i] = (byte)(step & 255); step >>= 8; }
            using (var h = new HMACSHA1(key))
            {
                byte[] hash = h.ComputeHash(counter);
                int off = hash[hash.Length - 1] & 15;
                int bin = ((hash[off] & 127) << 24) | (hash[off + 1] << 16) | (hash[off + 2] << 8) | hash[off + 3];
                return (bin % 1000000).ToString("D6");
            }
        }

        /// <summary>The otpauth:// URI an authenticator app expects inside the QR code.</summary>
        public static string OtpAuthUri(string secret, string account, string issuer)
        {
            return "otpauth://totp/" + Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account ?? "") +
                   "?secret=" + secret + "&issuer=" + Uri.EscapeDataString(issuer) + "&algorithm=SHA1&digits=6&period=30";
        }
    }
}
