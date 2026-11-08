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
    /// What the user is entitled to. Free is a real, usable product; Pro removes the caps.
    ///
    /// The licence lives in <c>license.dat</c> and is encrypted with DPAPI for the current
    /// Windows user, so it cannot simply be copied to another machine and the JSON is not
    /// sitting there in plain text. That is the honest limit of a local app: a determined
    /// owner of the machine can always patch a binary. It stops casual editing, nothing more.
    /// </summary>
    internal static class Plan
    {
        /* The limits live in Entitlements; these aliases exist so older call sites keep working
           without a second, drifting copy of the numbers. */
        public const int FreeProfiles = Entitlements.FreeProfiles;
        public const int FreeConcurrent = Entitlements.FreeConcurrent;

        /// <summary>
        /// Accounts the publisher has granted Pro to by hand. This is an administrative
        /// allowlist held by the program's owner, not a payment: it is checked against the
        /// signed-in account, so the grant follows the account to any machine it signs in on,
        /// and nobody else is affected when it is edited. It is deliberately NOT a hidden bypass
        /// for the paid plan - the list contains exactly the accounts the publisher named, and
        /// every other account still needs a confirmed payment.
        ///
        /// Being an allowlist of email addresses in the binary is a real trade: anyone who reads
        /// the file learns which accounts are comped (they still cannot sign in as them). That is
        /// a much smaller exposure than a blanket bypass, which is why this is the shape used.
        /// </summary>
        private static readonly string[] ComplimentaryAccounts =
        {
            "alistairfox.london@gmail.com"
        };

        /// <summary>True when the signed-in account has been given a complimentary subscription.</summary>
        public static bool IsComplimentary
        {
            get
            {
                var a = Session.Current;
                return a != null && IsComplimentaryEmail(a.Email);
            }
        }

        public static bool IsComplimentaryEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            string key = email.Trim().ToLowerInvariant();
            foreach (string s in ComplimentaryAccounts)
                if (string.Equals(s, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly object Gate = new object();
        private static PlanData _data;
        private static bool _loaded;

        public static string FilePath { get { return Path.Combine(AppPaths.Root, "license.dat"); } }

        private sealed class PlanData
        {
            public string Plan { get; set; }
            /// <summary>ISO-8601 UTC end of the paid period.</summary>
            public string Until { get; set; }
            /// <summary>Transaction ids already redeemed, so a hash cannot pay twice.</summary>
            public List<string> Spent { get; set; }
            /// <summary>Invoices issued on this machine, for the payment log.</summary>
            public List<string> Log { get; set; }

            /// <summary>The session that was open when the app last closed, so a payment made
            /// just before closing is still honoured on the next start.</summary>
            public string PendingCoin { get; set; }
            public string PendingCreated { get; set; }
            public string PendingAmount { get; set; }

            public PlanData()
            {
                Plan = "free";
                Spent = new List<string>();
                Log = new List<string>();
            }
        }

        public static bool IsPro
        {
            get
            {
                /* A complimentary account never lapses, and never needs a license file. Checked
                   before the file so the answer is the same on a machine that has never paid. */
                if (IsComplimentary) return true;
                var d = Load();
                if (d == null || !"pro".Equals(d.Plan, StringComparison.OrdinalIgnoreCase)) return false;
                DateTime until;
                return TryDate(d.Until, out until) && until > DateTime.UtcNow;
            }
        }

        public static DateTime? Until
        {
            get
            {
                // Complimentary accounts have no end date, and inventing one would put a
                // countdown in the UI for a subscription that does not expire.
                if (IsComplimentary) return null;
                var d = Load();
                DateTime until;
                if (d != null && TryDate(d.Until, out until)) return until;
                return null;
            }
        }

        /// <summary>"Pro until 7 December" or "Free plan".</summary>
        public static string Summary()
        {
            if (IsComplimentary) return "Pro, unlimited";
            if (!IsPro) return "Free plan";
            var u = Until;
            return u == null ? "Pro" : "Pro until " + u.Value.ToLocalTime().ToString("d MMMM", CultureInfo.InvariantCulture);
        }

        public static int ProfileLimit { get { return IsPro ? int.MaxValue : FreeProfiles; } }
        public static int ConcurrentLimit { get { return IsPro ? int.MaxValue : FreeConcurrent; } }

        public static bool HasSpent(string txId)
        {
            if (string.IsNullOrWhiteSpace(txId)) return true;
            var d = Load();
            if (d == null || d.Spent == null) return false;
            foreach (string s in d.Spent)
                if (string.Equals(s, txId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Marks a payment as used and extends Pro by one month.</summary>
        public static void Activate(string txId, string note)
        {
            lock (Gate)
            {
                var d = Load();
                if (d == null) return;

                DateTime baseDate = DateTime.UtcNow;
                DateTime until;
                if (TryDate(d.Until, out until) && until > baseDate) baseDate = until;  // stack renewals
                d.Plan = "pro";
                d.Until = baseDate.AddDays(30).ToString("o", CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(txId) && !HasSpent(txId)) d.Spent.Add(txId);
                if (!string.IsNullOrWhiteSpace(note)) d.Log.Add(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "  " + note);
                Save(d);
            }
        }

        /// <summary>
        /// Remembers the open payment session. A user who sends the transfer and closes the app
        /// before the confirmations land must not lose the payment, so the session - not just
        /// the coins - survives a restart. It expires on its own after two hours.
        /// </summary>
        public static void SavePending(string coinKey, DateTime createdUtc, decimal amount)
        {
            lock (Gate)
            {
                var d = Load();
                if (d == null) return;
                d.PendingCoin = coinKey;
                d.PendingCreated = createdUtc.ToString("o", CultureInfo.InvariantCulture);
                d.PendingAmount = amount.ToString(CultureInfo.InvariantCulture);
                Save(d);
            }
        }

        public static void ClearPending()
        {
            lock (Gate)
            {
                var d = Load();
                if (d == null || string.IsNullOrEmpty(d.PendingCoin)) return;
                d.PendingCoin = null;
                d.PendingCreated = null;
                d.PendingAmount = null;
                Save(d);
            }
        }

        /// <summary>The last open session, if it is still recent enough to be paid into.</summary>
        public static string PendingCoin { get { var d = Load(); return d == null ? null : d.PendingCoin; } }

        public static DateTime? PendingCreated
        {
            get
            {
                var d = Load();
                DateTime v;
                if (d == null || !TryDate(d.PendingCreated, out v)) return null;
                return v;
            }
        }

        public static decimal? PendingAmount
        {
            get
            {
                var d = Load();
                decimal v;
                if (d == null || string.IsNullOrEmpty(d.PendingAmount)) return null;
                return decimal.TryParse(d.PendingAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : (decimal?)null;
            }
        }

        /// <summary>
        /// Adds days to the paid period, stacking on top of whatever is left. Used for referral
        /// credit, which is why it takes a note rather than a transaction id.
        /// </summary>
        public static bool Extend(int days, string note)
        {
            if (days <= 0) return false;
            lock (Gate)
            {
                var d = Load();
                if (d == null) return false;

                DateTime baseDate = DateTime.UtcNow;
                DateTime until;
                if (TryDate(d.Until, out until) && until > baseDate) baseDate = until;
                d.Plan = "pro";
                d.Until = baseDate.AddDays(days).ToString("o", CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(note))
                    d.Log.Add(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "  " + note);
                Save(d);
                return true;
            }
        }

        public static void Downgrade()        {
            lock (Gate)
            {
                var d = Load();
                if (d == null) return;
                d.Plan = "free";
                d.Until = null;
                Save(d);
            }
        }

        public static void AppendLog(string note)
        {
            if (string.IsNullOrWhiteSpace(note)) return;
            lock (Gate)
            {
                var d = Load();
                if (d == null) return;
                d.Log.Add(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "  " + note);
                if (d.Log.Count > 200) d.Log.RemoveRange(0, d.Log.Count - 200);
                Save(d);
            }
        }

        public static List<string> History()
        {
            var d = Load();
            return d == null || d.Log == null ? new List<string>() : new List<string>(d.Log);
        }

        private static bool TryDate(string iso, out DateTime value)
        {
            value = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(iso)) return false;
            /* RoundtripKind cannot be combined with AdjustToUniversal - .NET throws on the
               combination, which would take the whole Plan card down the moment a licence
               exists. The "o" format already carries its own offset. */
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                value = value.ToUniversalTime();
                return true;
            }
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out value);
        }

        private static PlanData Load()
        {
            lock (Gate)
            {
                if (_loaded) return _data;
                _loaded = true;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        byte[] raw = File.ReadAllBytes(FilePath);
                        byte[] plain = ProtectedData.Unprotect(raw, Entropy(), DataProtectionScope.CurrentUser);
                        string json = Encoding.UTF8.GetString(plain);
                        var d = new JavaScriptSerializer().Deserialize<PlanData>(json);
                        if (d != null)
                        {
                            if (d.Spent == null) d.Spent = new List<string>();
                            if (d.Log == null) d.Log = new List<string>();
                            _data = d;
                        }
                    }
                }
                catch (Exception ex)
                {
                    /* A file written by another Windows user, or a corrupted one, reads as free
                       rather than crashing the app. */
                    Log.Error("Licence could not be read", ex);
                }
                if (_data == null) _data = new PlanData();
                return _data;
            }
        }

        private static void Save(PlanData d)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                string json = new JavaScriptSerializer().Serialize(d);
                byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy(), DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, cipher);
            }
            catch (Exception ex) { Log.Error("Licence could not be saved", ex); }
        }

        private static byte[] Entropy()
        {
            return Encoding.UTF8.GetBytes("JoeBrowser.Licence.v1");
        }
    }
}
