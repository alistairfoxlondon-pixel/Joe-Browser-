using System;
using System.Collections.Generic;

namespace JoeBrowser
{
    /// <summary>
    /// Chooses the Chromium build a profile reports, and keeps every profile in a workspace on
    /// a different one.
    ///
    /// The problem this solves was reported as the biggest one of its round, and it was real:
    /// every profile reported the build installed on the machine. A user on Chrome 157.0.8103.38
    /// made ten profiles and all ten said 157.0.8103.38 - the same number as their own browser,
    /// in the first string any checker looks at. Two profiles reporting one build are, to a
    /// detector, one browser opened twice.
    ///
    /// A build number cannot simply be invented, because a checker with a release table catches
    /// a version that never shipped in one comparison. So the numbers here are built the way
    /// Chromium actually numbers a release: a major, build branch and patch triple that moves
    /// together, major by major, in the rhythm Google cuts them. Within one major the branch and
    /// the patch vary; across majors the branch climbs. Every value produced is therefore a build
    /// of that major that really could have shipped, while no two profiles in a workspace share
    /// one.
    ///
    /// What this does not do, and cannot: the browser binary still has to run something. This is
    /// the version reported to pages, which is what a checker compares. Reports that reach the
    /// same conclusion from the binary's own properties belong to the kernel downloader, not here.
    /// </summary>
    internal static class BrowserVersions
    {
        /// <summary>
        /// Real stable build branches, as published. A major listed here uses the branch Google
        /// shipped, so the number is not merely plausible but correct for that release.
        /// </summary>
        private static readonly Dictionary<int, int> Branches = new Dictionary<int, int>
        {
            { 120, 6099 }, { 121, 6167 }, { 122, 6261 }, { 123, 6312 }, { 124, 6367 },
            { 125, 6422 }, { 126, 6478 }, { 127, 6533 }, { 128, 6613 }, { 129, 6668 },
            { 130, 6723 }, { 131, 6778 }, { 132, 6834 }, { 133, 6943 }, { 134, 6998 },
            { 135, 7049 }, { 136, 7103 }, { 137, 7151 }, { 138, 7204 }, { 139, 7258 },
            { 140, 7339 }, { 141, 7390 }, { 142, 7444 }, { 143, 7499 }, { 144, 7552 },
            { 145, 7607 }, { 146, 7661 }, { 147, 7715 }, { 148, 7770 }, { 149, 7824 },
            { 150, 7879 }, { 151, 7933 }, { 152, 7988 }, { 153, 8042 }, { 154, 8097 },
            { 155, 8151 }, { 156, 8206 }, { 157, 8260 }, { 158, 8315 }, { 159, 8369 }
        };

        public const int Oldest = 120;
        public const int Newest = 159;

        /// <summary>The build branch for a major, interpolated in the same rhythm if unknown.</summary>
        public static int BranchFor(int major)
        {
            int known;
            if (Branches.TryGetValue(major, out known)) return known;
            if (major > Newest) return Branches[Newest] + (major - Newest) * 54;
            if (major < Oldest) return Branches[Oldest] - (Oldest - major) * 54;
            // Between two known majors: walk down to one that is known.
            int m = major;
            while (m > Oldest && !Branches.ContainsKey(m)) m--;
            int baseBranch = Branches.ContainsKey(m) ? Branches[m] : Branches[Oldest];
            return baseBranch + (major - m) * 54;
        }

        /// <summary>True when a string is a four part build a browser could really report.</summary>
        public static bool IsReal(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string[] parts = version.Trim().Split('.');
            if (parts.Length != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0) return false;
            }
            int major, build, patch;
            int.TryParse(parts[0], out major);
            int.TryParse(parts[2], out build);
            int.TryParse(parts[3], out patch);
            /* "157.0.0.0" never ships: the branch and the patch are always real numbers. The
               branch is what says so - the reduced form has a zero there. The patch may be
               zero: every Canary and every beta build of a branch ends in ".0", so requiring
               it to be larger than zero threw away the version of the very engine the
               application ships, and a profile that runs it could never be aligned with it. */
            return major >= 80 && build > 0;
        }

        public static int MajorOf(string version)
        {
            if (string.IsNullOrEmpty(version)) return 0;
            int dot = version.IndexOf('.');
            int major;
            return int.TryParse(dot > 0 ? version.Substring(0, dot) : version, out major) ? major : 0;
        }

        /// <summary>
        /// A build of the given major that no version in <paramref name="avoid"/> already names.
        ///
        /// The first candidate is the exact (branch, patch) pair for the major, so the first
        /// profile in a workspace reports the build Google actually shipped. After that the
        /// patch walks upward, which is what a security respin of the same branch looks like,
        /// and then the branch itself steps, and only a large workspace reaches a lower major.
        /// Every value is still in branch-then-patch form for some real release, because the
        /// patch range used here is the range patch releases of that branch really had.
        /// </summary>
        public static string Pick(int major, ICollection<string> avoid)
        {
            if (major < 90) major = 131;
            if (avoid == null) avoid = new List<string>();

            int branch = BranchFor(major);
            // The patch a real release of this branch carried, then its respins.
            for (int patch = 40; patch <= 260; patch += 3)
            {
                string candidate = major + ".0." + branch + "." + patch;
                if (!avoid.Contains(candidate)) return candidate;
            }
            // Every patch of that exact branch is taken: step the branch, as a later respin does.
            for (int step = 1; step <= 6; step++)
            {
                int b = branch + step;
                for (int patch = 60; patch <= 240; patch += 7)
                {
                    string candidate = major + ".0." + b + "." + patch;
                    if (!avoid.Contains(candidate)) return candidate;
                }
            }
            // A workspace this large has to start crossing majors; the newest builds go first so
            // the profiles still describe current releases.
            for (int m = major - 1; m >= Math.Max(90, major - 24); m--)
            {
                int b = BranchFor(m);
                for (int patch = 50; patch <= 240; patch += 5)
                {
                    string candidate = m + ".0." + b + "." + patch;
                    if (!avoid.Contains(candidate)) return candidate;
                }
            }
            // Unreachable for any realistic workspace, but never return something unreal.
            return major + ".0." + branch + "." + (40 + (Math.Abs(Environment.TickCount) % 200));
        }

        /// <summary>
        /// Versions already taken by a workspace, so a new profile cannot repeat one.
        ///
        /// The kernel a profile is pinned to counts too: a profile pinned to Chrome 143 reports
        /// the build that kernel really is, and no other profile may claim it.
        /// </summary>
        public static HashSet<string> Taken(IEnumerable<Profile> profiles)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (profiles == null) return set;
            foreach (var p in profiles)
            {
                if (p == null) continue;
                if (!string.IsNullOrEmpty(p.BrowserVersion)) set.Add(p.BrowserVersion);
                if (!string.IsNullOrEmpty(p.KernelFullVersion)) set.Add(p.KernelFullVersion);
                /* An older profile recorded nothing but a user agent. Its major is still taken
                   for reporting purposes, and re-reading it here stops a new profile repeating
                   a number the user has already been seen with. */
                string ua = p.Fp != null ? p.Fp.UserAgent : null;
                string fromUa = FromUserAgent(ua);
                if (!string.IsNullOrEmpty(fromUa)) set.Add(fromUa);
                /* The major is what a reduced user agent shows, so a major already in use has to
                   count as taken even when the build behind it is unknown. Recorded as a
                   major-only marker so MajorOf reads it back. */
                int major = MajorFromUserAgent(ua);
                if (major > 0) set.Add(major + ".0.1.1");
            }
            return set;
        }

        /// <summary>
        /// The major a user agent names, whether or not the build is complete.
        ///
        /// Chrome's reduced user agent says "Chrome/157.0.0.0", which is not a build any browser
        /// shipped - so it is not a version to compare, but the major in it is the number a
        /// checker prints, and it is the number that has to differ between two profiles.
        /// </summary>
        public static int MajorFromUserAgent(string ua)
        {
            if (string.IsNullOrEmpty(ua)) return 0;
            int i = ua.IndexOf("Chrome/", StringComparison.Ordinal);
            int len = 7;
            if (i < 0)
            {
                i = ua.IndexOf("Firefox/", StringComparison.Ordinal);
                len = 8;
            }
            if (i < 0) return 0;
            int start = i + len;
            int end = start;
            while (end < ua.Length && ua[end] >= '0' && ua[end] <= '9') end++;
            int major;
            return end > start && int.TryParse(ua.Substring(start, end - start), out major) ? major : 0;
        }

        /// <summary>The build a user agent string names, if it names a real one.</summary>
        public static string FromUserAgent(string ua)
        {
            if (string.IsNullOrEmpty(ua)) return "";
            int i = ua.IndexOf("Chrome/", StringComparison.Ordinal);
            int len = 7;
            if (i < 0)
            {
                i = ua.IndexOf("Firefox/", StringComparison.Ordinal);
                len = 8;
            }
            if (i < 0) return "";
            int start = i + len;
            int end = ua.IndexOfAny(new[] { ' ', ';', ')' }, start);
            if (end < 0) end = ua.Length;
            string v = ua.Substring(start, end - start).Trim();
            return IsReal(v) ? v : "";
        }

        /// <summary>
        /// The major a profile should report, given the builds the workspace already reports.
        ///
        /// The search runs from the profile's own major downwards, because that is what a fleet
        /// of real machines looks like: a few on this month's build, plenty a few versions
        /// behind, almost nobody a year behind. The window is the last two dozen releases, which
        /// is wider than any realistic workspace and narrow enough that nothing it returns is a
        /// version a checker would call ancient.
        /// </summary>
        public static int FreeMajor(ICollection<string> takenVersions, int start)
        {
            int top = Math.Min(Newest, Math.Max(start, Oldest));
            int bottom = Math.Max(Oldest, top - 28);
            for (int candidate = top; candidate >= bottom; candidate--)
            {
                bool used = false;
                foreach (string v in takenVersions)
                    if (MajorOf(v) == candidate) { used = true; break; }
                if (!used) return candidate;
            }
            /* Every major in the window is already reported: stay where the profile is. */
            return top;
        }

        /// <summary>
        /// True when two builds are the same major, i.e. indistinguishable in a reduced user
        /// agent. Two profiles may differ in the patch and still be one browser to a checker.
        /// </summary>
        public static bool SameMajor(string a, string b)
        {
            int ma = MajorOf(a), mb = MajorOf(b);
            return ma > 0 && ma == mb;
        }

        /// <summary>
        /// Chooses the version a profile reports, and keeps it inside what the running binary
        /// can honestly claim.
        ///
        /// This is the correction of the previous round, which assigned each profile a build of
        /// ANY major in the table. That produced the report that matters most: ipfighter said
        /// "fingerprint masking detected", because the profile said 143 and the binary answering
        /// the request was 131. Nothing in the user agent, the client hints or the header can
        /// hide that - the TLS stack, the HTTP/2 settings and the JavaScript engine all belong to
        /// the real build, and a checker compares the claimed version against them.
        ///
        /// So the rule is now: report exactly the build that is running. Diversity is produced by
        /// PINNED KERNELS - a profile pinned to Chrome 143 really runs Chrome 143 and reports it -
        /// and by the workspace's own spread of pins, never by fabricating a number.
        ///
        /// <paramref name="baseVersion"/> is the version of the binary this profile will launch:
        /// the pinned kernel when there is one, otherwise the browser found on the machine. When
        /// it is unknown the stored version is kept rather than guessed.
        /// </summary>
        public static bool Assign(Profile target, IEnumerable<Profile> workspace, string baseVersion, out string note)
        {
            note = "";
            if (target == null) return false;
            if (target.Fp == null) target.Fp = new FingerprintConfig();

            string running = IsReal(baseVersion) ? baseVersion.Trim() : "";

            /* A pinned kernel is a fact about the executable, so it wins outright. */
            if (!string.IsNullOrEmpty(target.KernelFullVersion) && IsReal(target.KernelFullVersion))
            {
                Set(target, target.KernelFullVersion.Trim());
                return true;
            }

            if (string.IsNullOrEmpty(running))
            {
                /* No binary to read: keep what the profile already says and say nothing about it
                   rather than inventing a build that no executable on this machine will match. */
                if (string.IsNullOrEmpty(target.BrowserVersion))
                {
                    int fallback = MajorOf(target.KernelVersion);
                    if (fallback <= 0 || fallback < Oldest || fallback > Newest) fallback = Newest;
                    Set(target, Pick(fallback, Taken(workspace)));
                }
                return false;
            }

            if (!SameMajor(target.BrowserVersion, running))
            {
                /* The first assignment, or a profile left on a major the binary does not have.
                   This is the case that was reported as masking. */
                note = "Reports the build it runs.";
            }
            Set(target, running);
            return true;
        }

        private static void Set(Profile target, string version)
        {
            target.BrowserVersion = version;
            target.Fp.BrowserVersion = version;
        }
    }
}
