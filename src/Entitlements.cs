using System;

namespace JoeBrowser
{
    /// <summary>
    /// The single place that decides what the free plan may do.
    ///
    /// Every count and every paywall lives here rather than at the call sites: a limit that is
    /// checked in six places is a limit that will be missing from the seventh. Callers ask a
    /// question and get a straight answer - no feature decides its own policy.
    ///
    /// The free plan is deliberately usable rather than crippled: three profiles, one browser
    /// at a time, and every maintenance tool except the batch proxy tester. What it must not be
    /// is unenforced, which is what it was before this class existed - batch create happily made
    /// a hundred profiles on the free plan because nothing on that path asked.
    /// </summary>
    internal static class Entitlements
    {
        public const int FreeProfiles = 3;
        public const int FreeConcurrent = 1;

        /// <summary>Testing a whole proxy list at once.</summary>
        public const string BatchProxyTest = "proxytest";

        public static bool IsPro { get { return Plan.IsPro; } }

        public static int ProfileLimit { get { return IsPro ? int.MaxValue : FreeProfiles; } }
        public static int ConcurrentLimit { get { return IsPro ? int.MaxValue : FreeConcurrent; } }

        /// <summary>How many more profiles this plan may hold, given what is already stored.</summary>
        public static int ProfileSlotsLeft(int current)
        {
            if (IsPro) return int.MaxValue;
            int left = FreeProfiles - Math.Max(0, current);
            return left < 0 ? 0 : left;
        }

        /// <summary>How many more browsers may be open at once.</summary>
        public static int ConcurrentSlotsLeft(int running)
        {
            if (IsPro) return int.MaxValue;
            int left = FreeConcurrent - Math.Max(0, running);
            return left < 0 ? 0 : left;
        }

        /// <summary>True when the feature exists only on the paid plan.</summary>
        public static bool IsProOnly(string feature)
        {
            return string.Equals(feature, BatchProxyTest, StringComparison.Ordinal);
        }

        public static bool Allows(string feature)
        {
            return !IsProOnly(feature) || IsPro;
        }

        /// <summary>The sentence shown when a Pro-only feature is used on the free plan.</summary>
        public static string Reason(string feature)
        {
            if (string.Equals(feature, BatchProxyTest, StringComparison.Ordinal))
                return "Testing a whole proxy list is part of Pro.";
            return "That feature is part of Pro.";
        }

        /// <summary>The sentence shown when the profile cap is reached.</summary>
        public static string ProfileReason(int current)
        {
            if (current <= 0) return "The free plan holds " + FreeProfiles + " profiles. Unlimited profiles are $1 a month.";
            return "The free plan holds " + FreeProfiles + " profiles and you have " + current +
                   ". Unlimited profiles are $1 a month.";
        }

        /// <summary>The sentence shown when the concurrency cap is reached.</summary>
        public static string ConcurrentReason()
        {
            return "The free plan opens one profile at a time. Running several at once is $1 a month.";
        }
    }
}
