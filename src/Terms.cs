using System;
using System.Collections.Generic;

namespace JoeBrowser
{
    /// <summary>
    /// The payment terms. They are shown once, before the first payment, and the acceptance is
    /// remembered per Windows user. The version is stored with it, so changing these lines
    /// asks again instead of silently binding someone to text they never saw.
    ///
    /// The wording is deliberately short and concrete: this is a $1 product paid in
    /// irreversible crypto, and the things a buyer must understand are the network, the
    /// amount and the fact that a wrong send cannot be undone.
    /// </summary>
    internal static class Terms
    {
        public const string Version = "1";

        public sealed class Section
        {
            public string Title;
            public string Body;
            public Section(string title, string body) { Title = title; Body = body; }
        }

        public static List<Section> Sections()
        {
            return new List<Section>
            {
                new Section("What you are buying",
                    "30 days of Joe Browser Pro on this PC, for $1.00. It does not renew by itself and "
                    + "no card is stored. When the 30 days end, the app returns to the free plan until "
                    + "you pay again."),

                new Section("The network matters",
                    "Each payment option names one network. Send the coin on that exact network. "
                    + "USDT sent on BNB Chain to a Tron address, or the reverse, is lost and cannot be "
                    + "recovered by us or by anyone else."),

                new Section("The amount and the rate",
                    "The amount is fixed when the session starts and is shown to you before you send. "
                    + "The rate comes from a public price feed and is locked for that session. Send the "
                    + "amount shown, or very slightly more; a transfer well below it will not unlock Pro."),

                new Section("How a payment is confirmed",
                    "Joe Browser reads the blockchain directly. A payment counts only when the transfer "
                    + "is on the right network, to the address shown, for at least the amount shown, and "
                    + "has enough confirmations. Only then is Pro activated. A transfer that arrived "
                    + "before your session started is ignored, and a transaction can never be used twice."),

                new Section("Your licence",
                    "Pro is tied to your Windows user account on this PC and is stored encrypted. "
                    + "Reinstalling while signed in to the same Windows account restores it. It is not "
                    + "transferable to another person or another computer."),

                new Section("Refunds",
                    "Crypto transfers are final. If a payment is confirmed on-chain and Pro does not "
                    + "activate, contact @joegoldberg2025 on Telegram with the transaction id and it "
                    + "will be resolved or refunded."),

                new Section("Acceptable use",
                    "Use Joe Browser only for lawful purposes and only with accounts you are allowed "
                    + "to use. The software is provided as-is, without warranty, under the MIT licence.")
            };
        }

        /// <summary>True when this Windows user has accepted the current version.</summary>
        public static bool Accepted
        {
            get
            {
                try
                {
                    var s = new ProfileStore().LoadSettings();
                    return s != null && string.Equals(s.TermsAccepted, Version, StringComparison.Ordinal);
                }
                catch { return false; }
            }
        }

        /// <summary>Records the acceptance. Returns false when it could not be saved.</summary>
        public static bool Accept()
        {
            try
            {
                var store = new ProfileStore();
                var s = store.LoadSettings() ?? new AppSettings();
                s.TermsAccepted = Version;
                store.SaveSettings(s);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Terms acceptance could not be saved", ex);
                return false;
            }
        }
    }
}
