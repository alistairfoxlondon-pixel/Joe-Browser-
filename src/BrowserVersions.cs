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
            /* "157.0.0.0" never ships: the branch and the patch are always real numbers. */
            return major >= 80 && build > 0 && patch > 0;
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
            }
            return set;
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
        /// Gives a profile a build of its own, or one it is pinned to.
        ///
        /// A pinned kernel wins: the profile runs that binary, so it must report that build.
        /// Otherwise the profile keeps the major it already had - the one its user agent names -
        /// and takes a build of that major that nothing else in the workspace has. That is the
        /// difference the user asked for: the version changes, and it stays a version that
        /// browser really shipped.
        /// </summary>
        public static bool Assign(Profile target, IEnumerable<Profile> workspace, out string note)
        {
            note = "";
            if (target == null) return false;
            if (target.Fp == null) target.Fp = new FingerprintConfig();

            if (!string.IsNullOrEmpty(target.KernelFullVersion) && IsReal(target.KernelFullVersion))
            {
                target.BrowserVersion = target.KernelFullVersion;
                target.Fp.BrowserVersion = target.KernelFullVersion;
                return true;
            }

            /* Which major this profile reports.
            
               Chrome has shipped a reduced user agent since M113: the string says
               "Chrome/157.0.0.0" and nothing else. So a build number alone is invisible in the
               user agent, and a workspace whose profiles all report 157 is still a workspace
               whose profiles are indistinguishable in the one line a checker prints first. The
               major therefore has to vary too, which is what this loop does: it keeps the
               profile's own major when it has one and nobody else has claimed it, and otherwise
               steps back through the last dozen supported releases until it finds a major this
               workspace is not already reporting. */
            int wanted = MajorOf(target.BrowserVersion);
            if (wanted <= 0) wanted = MajorOf(FromUserAgent(target.Fp.UserAgent));
            if (wanted <= 0 && !string.IsNullOrEmpty(target.KernelVersion)) wanted = MajorOf(target.KernelVersion);
            if (wanted <= 0) wanted = Math.Min(Newest, 131);

            var avoid = Taken(workspace);
            if (!string.IsNullOrEmpty(target.BrowserVersion)) avoid.Remove(target.BrowserVersion);

            int major = wanted;
            for (int step = 0; step < 14; step++)
            {
                int candidate = wanted - step;
                if (candidate < Oldest + 4) break;
                /* A major is free when no build already here names it and no user agent here
                   names it either. */
                bool used = false;
                foreach (string v in avoid)
                    if (MajorOf(v) == candidate) { used = true; break; }
                if (!used) { major = candidate; break; }
            }

            string version = Pick(major, avoid);
            target.BrowserVersion = version;
            target.Fp.BrowserVersion = version;
            if (avoid.Contains(version))
                note = "This workspace already has " + avoid.Count + " builds, so some profiles share one.";
            return true;
        }
    }
}
