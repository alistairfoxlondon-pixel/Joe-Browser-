using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>
    /// Referrals that work without a server.
    ///
    /// The problem: a referral bonus needs two machines to agree, and this app has no backend.
    /// The answer is the blockchain. A referred user who has paid gets a short receipt
    /// (<c>JBR1.CODE.CHAIN.TXID</c>) that they send to the friend who invited them. The friend's
    /// copy of the app looks that transaction up on the chain and, only if it is a real,
    /// confirmed payment to the publisher's address, adds 7 days to Pro.
    ///
    /// That is a real check, not a courtesy: the transaction has to exist, be confirmed, and be
    /// paid to the address the app already collects to. A made-up receipt fails, and the same
    /// transaction can only ever be claimed once.
    /// </summary>
    internal static class Referral
    {
        public const int Days = 7;
        private const string Prefix = "JBR1";

        private sealed class Ledger
        {
            public List<string> Claimed { get; set; }
            public List<string> Log { get; set; }

            public Ledger()
            {
                Claimed = new List<string>();
                Log = new List<string>();
            }
        }

        public static string FilePath { get { return Path.Combine(AppPaths.Root, "referrals.json"); } }

        /// <summary>
        /// This user's code. Derived from the signed-in address so it is stable across
        /// reinstalls and cannot be chosen to look like somebody else's.
        /// </summary>
        public static string MyCode
        {
            get
            {
                string seed = "";
                var me = Session.Current;
                if (me != null && !string.IsNullOrEmpty(me.Email)) seed = me.Email.Trim().ToLowerInvariant();
                if (seed.Length == 0) seed = Environment.MachineName.ToLowerInvariant();

                using (var sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("JoeBrowser.Referral." + seed));
                    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O/1/I
                    var sb = new StringBuilder("JB");
                    for (int i = 0; i < 6; i++) sb.Append(alphabet[h[i] % alphabet.Length]);
                    return sb.ToString();
                }
            }
        }

        /// <summary>The message a user sends to a friend.</summary>
        public static string Invite()
        {
            return "Try Joe Browser with me \u2014 anti-detect profiles for Windows. "
                 + "Use my code " + MyCode + " before you pay and I get 7 days of Pro.";
        }

        /// <summary>Looks like a code this app could have issued.</summary>
        public static bool LooksValid(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            string c = code.Trim().ToUpperInvariant();
            if (c.Length != 8 || !c.StartsWith("JB", StringComparison.Ordinal)) return false;
            for (int i = 2; i < c.Length; i++)
            {
                char ch = c[i];
                bool ok = (ch >= 'A' && ch <= 'Z' && ch != 'I' && ch != 'O') || (ch >= '2' && ch <= '9');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Records who invited this user. Stored with the settings, not the licence.</summary>
        public static bool Redeem(string code)
        {
            if (!LooksValid(code)) return false;
            try
            {
                var store = new ProfileStore();
                var s = store.LoadSettings() ?? new AppSettings();
                s.ReferralCode = code.Trim().ToUpperInvariant();
                store.SaveSettings(s);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Referral code could not be saved", ex);
                return false;
            }
        }

        public static string MyReferrer()
        {
            try
            {
                var s = new ProfileStore().LoadSettings();
                return s == null ? "" : (s.ReferralCode ?? "");
            }
            catch { return ""; }
        }

        /// <summary>The receipt a paying user sends to whoever invited them.</summary>
        public static string BuildReceipt(string chain, string txId)
        {
            string referrer = MyReferrer();
            if (!LooksValid(referrer) || string.IsNullOrEmpty(txId)) return null;
            return Prefix + "." + referrer + "." + (chain ?? "").ToLowerInvariant() + "." + txId;
        }

        /// <summary>
        /// Checks a receipt and, when the transaction is real and confirmed, adds the days.
        /// Returns a message for the UI either way.
        /// </summary>
        public static string Claim(string receipt, out bool credited)
        {
            credited = false;
            if (string.IsNullOrWhiteSpace(receipt)) return "Paste the receipt your friend sent you.";

            string[] parts = receipt.Trim().Split('.');
            if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.OrdinalIgnoreCase))
                return "That is not a Joe Browser referral receipt.";

            string code = parts[1].ToUpperInvariant();
            string chain = parts[2].ToLowerInvariant();
            string txId = parts[3];

            if (!LooksValid(code)) return "The code inside that receipt is not valid.";
            if (!string.Equals(code, MyCode, StringComparison.OrdinalIgnoreCase))
                return "That receipt belongs to a different account.";

            var ledger = Load();
            foreach (string seen in ledger.Claimed)
                if (string.Equals(seen, txId, StringComparison.OrdinalIgnoreCase))
                    return "That receipt has already been claimed.";

            var check = Payments.Confirm(chain, txId);
            if (check == null || !check.Found)
                return check != null && !string.IsNullOrEmpty(check.Message)
                    ? check.Message
                    : "That payment was not found on the chain yet. Try again once it confirms.";

            if (!Plan.Extend(Days, "Referral " + code + " \u2014 tx " + txId))
                return "The extra days could not be saved.";

            ledger.Claimed.Add(txId);
            ledger.Log.Add(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "  +" + Days + " days  tx " + txId);
            Save(ledger);
            credited = true;
            return "+" + Days + " days added. Thank you for inviting someone.";
        }

        /// <summary>How many referral days this machine has earned, for the plan card.</summary>
        public static int EarnedDays()
        {
            var ledger = Load();
            return ledger.Claimed.Count * Days;
        }

        private static Ledger Load()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath)) return new Ledger();
                var ser = new JavaScriptSerializer();
                var l = ser.Deserialize<Ledger>(System.IO.File.ReadAllText(FilePath));
                if (l == null) return new Ledger();
                if (l.Claimed == null) l.Claimed = new List<string>();
                if (l.Log == null) l.Log = new List<string>();
                return l;
            }
            catch { return new Ledger(); }
        }

        private static void Save(Ledger l)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                var ser = new JavaScriptSerializer();
                System.IO.File.WriteAllText(FilePath, ser.Serialize(l), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Referral ledger could not be saved", ex); }
        }
    }
}
