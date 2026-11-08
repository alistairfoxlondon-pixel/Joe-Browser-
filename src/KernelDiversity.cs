using System;
using System.Collections.Generic;

namespace JoeBrowser
{
    /// <summary>
    /// Keeps the engine build - the "kernel" - as much a part of a profile's identity as its
    /// fingerprint.
    ///
    /// The request that produced this file was blunt: every new profile has to be different from
    /// the original, "including a different kernel version". That is the harder half of the
    /// problem, because a fingerprint is generated from a seed and can be re-rolled at will,
    /// while a kernel only exists if a build was downloaded. So the rule here is graded:
    ///
    ///   * A pinned kernel is copied from the source profile when a new one is made, never
    ///     inherited silently - a duplicate that inherits both the fingerprint settings and the
    ///     build is, to a detector, one browser opened twice.
    ///   * When builds are installed, the new profile takes a different build than every profile
    ///     it could be confused with. Engine matters as much as version: Chrome 141 and Brave 141
    ///     on the same machine report different user agents and different binary properties.
    ///   * When only one build exists - or none, which is the default install - the profile keeps
    ///     the "installed browser" sentinel and the caller reports honestly that kernel diversity
    ///     is not available rather than pinning a build that is not there. A kernel that is
    ///     pinned but missing falls back to the installed browser at launch, so pretending would
    ///     produce a profile that looks pinned in the UI and is not in reality.
    /// </summary>
    internal static class KernelDiversity
    {
        /// <summary>Human label for a pinned build, or the installed-browser sentinel.</summary>
        public static string Label(Profile p)
        {
            if (p == null) return "";
            return Label(p.KernelEngine, p.KernelVersion);
        }

        public static string Label(string engine, string version)
        {
            if (string.IsNullOrEmpty(engine) || string.IsNullOrEmpty(version)) return "Installed browser";
            string name = PrettyEngine(engine);
            return name + " " + version;
        }

        public static string PrettyEngine(string engine)
        {
            if (string.IsNullOrEmpty(engine)) return "";
            switch (engine.ToLowerInvariant())
            {
                case "chrome": return "Chrome";
                case "brave": return "Brave";
                case "chromium": return "Chromium";
                case "firefox": return "Firefox";
                case "edge": return "Edge";
                default: return engine.Length <= 1 ? engine.ToUpperInvariant()
                    : char.ToUpperInvariant(engine[0]) + engine.Substring(1);
            }
        }

        /// <summary>
        /// The key that identifies one build. Empty means "whatever is installed", which is a
        /// shared resource: two profiles on the sentinel are on the same build whichever way the
        /// machine is configured.
        /// </summary>
        public static string Key(Profile p)
        {
            if (p == null) return "";
            return Key(p.KernelEngine, p.KernelVersion);
        }

        public static string Key(string engine, string version)
        {
            if (string.IsNullOrEmpty(engine) || string.IsNullOrEmpty(version)) return "";
            return engine.ToLowerInvariant() + "|" + version;
        }

        public static bool IsPinned(Profile p)
        {
            return p != null && !string.IsNullOrEmpty(p.KernelEngine) && !string.IsNullOrEmpty(p.KernelVersion);
        }

        /* A batch of 100 profiles asks for the inventory 100 times, and each ask walks the
           kernels folder and reads file versions off disk. Cached for the duration of a burst;
           every mutation path (install, delete) calls Invalidate() so the cache cannot go stale
           behind the user's back. */
        private static List<KernelInstall> _cache;

        /// <summary>Drops the cached inventory. Call after installing or removing a build.</summary>
        public static void Invalidate() { _cache = null; }

        /// <summary>Every build this machine can actually launch, newest first within an engine.</summary>
        public static List<KernelInstall> Available()
        {
            if (_cache != null) return _cache;
            var list = new List<KernelInstall>();
            try
            {
                var installed = KernelStore.Installed();
                foreach (string engine in KernelStore.Engines)
                {
                    var forEngine = new List<KernelInstall>();
                    foreach (var k in installed)
                        if (k != null && k.Ready &&
                            string.Equals(k.Engine, engine, StringComparison.OrdinalIgnoreCase)) forEngine.Add(k);
                    // Newest first so a fresh profile lands on current hardware-adjacent builds;
                    // the tail is what gives a large workspace its spread.
                    forEngine.Sort(delegate (KernelInstall a, KernelInstall b)
                    {
                        return string.CompareOrdinal(VersionSortKey(b), VersionSortKey(a));
                    });
                    list.AddRange(forEngine);
                }
            }
            catch (Exception ex) { Log.Error("Kernel inventory failed", ex); }
            _cache = list;
            return list;
        }

        private static string VersionSortKey(KernelInstall k)
        {
            string v = string.IsNullOrEmpty(k.FileVersion) ? k.Version : k.FileVersion;
            string[] parts = (v ?? "").Split('.');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                int n = 0;
                if (i < parts.Length) int.TryParse(parts[i], out n);
                sb.Append(n.ToString("D6"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Picks the build a brand new profile should use, given the profiles that already exist
        /// and the ones this profile must not resemble.
        ///
        /// Returns true when a build was assigned. When it returns false the profile is left on
        /// the installed-browser sentinel and <paramref name="reason"/> explains why - the caller
        /// shows that, because "no other kernel is installed" is useful information and silent
        /// failure is not.
        /// </summary>
        public static bool Assign(Profile target, IEnumerable<Profile> existing, out string reason)
        {
            reason = "";
            if (target == null) return false;

            var available = Available();
            if (available.Count == 0)
            {
                reason = "Only one browser build is installed, so this profile uses it. " +
                         "Install another kernel to give each profile its own build.";
                return false;
            }

            // Builds already in use, and how often, so the least-used one wins outright.
            var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
            {
                foreach (var p in existing)
                {
                    if (p == null) continue;
                    string k = Key(p);
                    if (k.Length == 0) continue;
                    int n;
                    usage.TryGetValue(k, out n);
                    usage[k] = n + 1;
                }
            }

            KernelInstall best = null;
            int bestUse = int.MaxValue;
            foreach (var k in available)
            {
                string key = Key(k.Engine, k.Version);
                int n;
                usage.TryGetValue(key, out n);
                if (n < bestUse) { bestUse = n; best = k; }
            }

            if (best == null)
            {
                reason = "No usable kernel build was found.";
                return false;
            }

            /* Only pin when pinning buys something. A single installed build shared by every
               profile is not diversity, and pinning it would make the profile break the day that
               build is deleted, so the sentinel stays. The test is on how many builds exist, not
               on whether this one is unused: one build nobody has picked yet is still one build. */
            bool addsDiversity = available.Count > 1;
            if (!addsDiversity)
            {
                reason = "Only one browser build is installed, so this profile uses it. " +
                         "Install another kernel to give each profile its own build.";
                return false;
            }

            target.KernelEngine = best.Engine;
            target.KernelVersion = best.Version;
            if (bestUse > 0)
                reason = PrettyEngine(best.Engine) + " " + best.Version + " is shared with " +
                         (bestUse == 1 ? "one other profile" : bestUse + " other profiles") +
                         ". Install more kernels to spread them out.";
            return true;
        }

        /// <summary>
        /// The same choice for a duplicate: the copy must not land on the build its source is
        /// using, or the pair shares the one surface the fingerprint cannot compensate for.
        /// </summary>
        public static bool AssignDifferent(Profile target, Profile source, IEnumerable<Profile> existing, out string reason)
        {
            reason = "";
            if (target == null) return false;
            string avoid = Key(source);

            /* The copy starts from the source, so a source on the sentinel is already "the
               installed browser" - there is nothing to separate it from unless the copy can be
               pinned to a real build. */
            var available = Available();
            if (available.Count == 0)
            {
                target.KernelEngine = "";
                target.KernelVersion = "";
                reason = "Only one browser build is installed, so both profiles use it. " +
                         "Install another kernel to separate them.";
                return false;
            }

            var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
                foreach (var p in existing)
                {
                    if (p == null) continue;
                    string k = Key(p);
                    if (k.Length == 0) continue;
                    int n;
                    usage.TryGetValue(k, out n);
                    usage[k] = n + 1;
                }

            KernelInstall best = null;
            int bestUse = int.MaxValue;
            foreach (var k in available)
            {
                string key = Key(k.Engine, k.Version);
                if (string.Equals(key, avoid, StringComparison.OrdinalIgnoreCase)) continue;
                int n;
                usage.TryGetValue(key, out n);
                if (n < bestUse) { bestUse = n; best = k; }
            }

            if (best == null)
            {
                /* Every installed build is the source's build - there is one, and it is taken.
                   Copying the source's pin would leave a copy that is identical down to the
                   binary, so the sentinel (whatever is installed) is the honest answer: it is
                   also what a fresh profile gets, and the fingerprint still changed, which is
                   what keeps the pair apart as far as it can be. */
                target.KernelEngine = "";
                target.KernelVersion = "";
                reason = "Only the source profile's browser build is installed, so the copy uses " +
                         "the installed browser. Install another kernel to give the copy its own.";
                return false;
            }

            target.KernelEngine = best.Engine;
            target.KernelVersion = best.Version;
            return true;
        }

        /// <summary>
        /// True when every profile in the list sits on the same build, which is the common case
        /// after an install with a single kernel. Used by the audit to say so once, instead of
        /// repeating the same finding per profile.
        /// </summary>
        public static bool AllSame(IEnumerable<Profile> profiles)
        {
            string first = null;
            bool any = false;
            foreach (var p in profiles)
            {
                if (p == null) continue;
                string k = Key(p);
                if (!any) { first = k; any = true; continue; }
                if (!string.Equals(first, k, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return any;
        }

        /// <summary>How many distinct builds the workspace is spread across.</summary>
        public static int DistinctCount(IEnumerable<Profile> profiles)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profiles != null)
                foreach (var p in profiles) if (p != null) set.Add(Key(p));
            return set.Count;
        }
    }
}
