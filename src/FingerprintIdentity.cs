using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JoeBrowser
{
    /// <summary>
    /// A fingerprint split into the parts an anti-detect service actually reads.
    ///
    /// AdsPower, GoLogin and Dolphin each show a profile's fingerprint as a list of independent
    /// surfaces - navigator, screen, WebGL, canvas, audio, fonts, timezone, WebRTC, hardware - and
    /// compare two profiles surface by surface. Comparing the whole configuration as one blob
    /// only ever answers "identical or not", which is useless: two profiles that share a GPU and
    /// a timezone are a detectable pair even when everything else differs.
    /// </summary>
    internal static class FpSurface
    {
        public const string UserAgent = "userAgent";
        public const string Navigator = "navigator";
        public const string Screen = "screen";
        public const string Hardware = "hardware";
        public const string WebGl = "webgl";
        public const string Canvas = "canvas";
        public const string Audio = "audio";
        public const string Fonts = "fonts";
        public const string Timezone = "timezone";
        public const string Language = "language";
        public const string WebRtc = "webrtc";
        public const string Platform = "platform";

        /// <summary>Display order, and the order the consistency report uses.</summary>
        public static readonly string[] All =
        {
            UserAgent, Platform, Navigator, Screen, Hardware, WebGl, Canvas, Audio, Fonts,
            Timezone, Language, WebRtc
        };

        public static string Label(string surface)
        {
            switch (surface)
            {
                case UserAgent: return "User agent";
                case Platform: return "OS / platform";
                case Navigator: return "Navigator";
                case Screen: return "Screen";
                case Hardware: return "CPU / memory";
                case WebGl: return "WebGL";
                case Canvas: return "Canvas";
                case Audio: return "Audio";
                case Fonts: return "Fonts";
                case Timezone: return "Timezone";
                case Language: return "Language";
                case WebRtc: return "WebRTC";
                default: return surface;
            }
        }

        /// <summary>
        /// How much telling two profiles apart matters for this surface. A shared user agent is
        /// a mild signal; a shared canvas hash, audio hash or WebGL renderer is the signal
        /// detectors actually group accounts with. These weights drive the similarity score.
        /// </summary>
        public static int Weight(string surface)
        {
            switch (surface)
            {
                case UserAgent: return 4;
                case Platform: return 3;
                case Navigator: return 2;
                case Screen: return 3;
                case Hardware: return 3;
                case WebGl: return 5;       // strongest single signal in the wild
                case Canvas: return 5;
                case Audio: return 4;
                case Fonts: return 4;
                case Timezone: return 4;
                case Language: return 2;
                case WebRtc: return 3;
                default: return 1;
            }
        }

        /// <summary>
        /// Surfaces whose value is <em>derived</em> from another choice rather than chosen.
        ///
        /// A profile's user agent is built from its OS and its browser build, and its timezone
        /// and language are set by the proxy's country. Two profiles on the same provider and the
        /// same browser therefore share them without sharing an identity - describing that as
        /// "linked" would fire on the normal case and teach the user to ignore the audit. These
        /// still count toward the weighted score; they just cannot on their own make a pair
        /// Linked. The surfaces left - WebGL, canvas, audio, fonts, screen, hardware, navigator -
        /// are the ones a generated identity genuinely varies.
        /// </summary>
        public static bool IsDerived(string surface)
        {
            return surface == UserAgent || surface == Timezone || surface == Language;
        }
    }

    /// <summary>One surface of one profile: its short hash and the value that produced it.</summary>
    internal sealed class FpSurfaceValue
    {
        public string Surface;
        public string Hash;
        public string Value;

        public FpSurfaceValue(string surface, string hash, string value)
        {
            Surface = surface;
            Hash = hash;
            Value = value;
        }
    }

    /// <summary>
    /// Builds the surface hashes for a profile and compares profiles against each other.
    ///
    /// Everything here is deterministic from the stored configuration, which is the property the
    /// whole feature depends on: a signature computed in the profile list, in the editor, at
    /// launch and by a background audit must be the same string, or "these two profiles match"
    /// becomes a guess.
    /// </summary>
    internal static class FingerprintIdentity
    {
        // ------------------------------------------------------------------ hashing

        /// <summary>
        /// A short, stable, human-readable digest. FNV-1a rather than SHA so a signature can be
        /// recomputed cheaply inside a paint loop, and formatted as a 10-character token that is
        /// safe to show in a UI chip.
        /// </summary>
        public static string Hash(params string[] parts)
        {
            var sb = new StringBuilder();
            if (parts != null)
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) sb.Append('\u001f');
                    sb.Append(parts[i] ?? "");
                }
            return FnvHex(sb.ToString());
        }

        private static string FnvHex(string text)
        {
            // Two independent 32-bit passes so a 10-char token is not a collision waiting to
            // happen when a workspace holds a few hundred profiles.
            const uint p1 = 16777619u, p2 = 1099511628211u & 0xFFFFFFFFu;
            uint h1 = 2166136261u, h2 = 2166136261u ^ 0x9E3779B9u;
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            foreach (byte b in bytes)
            {
                h1 = (h1 ^ b) * p1;
                h2 = (h2 ^ (uint)(b + 0x9E)) * p2;
            }
            return (h1.ToString("x8") + h2.ToString("x8")).Substring(0, 10);
        }

        // ------------------------------------------------------------------ surfaces

        /// <summary>
        /// The surface map for a profile. Depends only on the stored configuration, so it is
        /// stable across restarts and identical everywhere it is shown.
        /// </summary>
        public static Dictionary<string, FpSurfaceValue> Surfaces(Profile p)
        {
            if (p == null) return new Dictionary<string, FpSurfaceValue>(StringComparer.Ordinal);
            return Surfaces(p.Fp);
        }

        /// <summary>
        /// The surface map for a bare configuration. Needed by every path that compares a
        /// fingerprint that does not belong to a stored profile yet - a duplicate being rolled,
        /// a batch being planned - and it keeps the surface list in exactly one place.
        /// </summary>
        public static Dictionary<string, FpSurfaceValue> Surfaces(FingerprintConfig fp)
        {
            var map = new Dictionary<string, FpSurfaceValue>(StringComparer.Ordinal);
            if (fp == null) return map;

            Add(map, FpSurface.UserAgent, FpSurface.UserAgent,
                string.IsNullOrEmpty(fp.UserAgentOverride) ? "automatic" : fp.UserAgentOverride);

            Add(map, FpSurface.Platform, FpSurface.Platform,
                fp.Os ?? "", fp.DeviceType ?? "", fp.FontSet ?? "auto");

            Add(map, FpSurface.Navigator, FpSurface.Navigator,
                "hw=" + fp.Cores,
                "mem=" + fp.Memory,
                "touch=" + fp.TouchPoints,
                "dnt=" + (fp.DoNotTrack ? 1 : 0),
                "plat=" + (fp.Platform ?? ""),
                "brand=" + (fp.Brand ?? ""));

            Add(map, FpSurface.Screen, FpSurface.Screen,
                fp.ScreenW + "x" + fp.ScreenH,
                "avail=" + fp.AvailW + "x" + fp.AvailH,
                "depth=" + fp.ColorDepth,
                "dpr=" + fp.PixelRatio.ToString("0.###", CultureInfo.InvariantCulture));

            Add(map, FpSurface.Hardware, FpSurface.Hardware,
                "cores=" + fp.Cores,
                "mem=" + fp.Memory,
                "noise=" + (fp.HardwareNoise ? 1 : 0),
                "seed=" + fp.Seed);

            Add(map, FpSurface.WebGl, FpSurface.WebGl,
                fp.WebGlVendor ?? "", fp.WebGlRenderer ?? "");

            /* Canvas, audio and fonts are noise-based: every profile draws the same scene, and the
               seed is what makes the pixels differ. Advertising them as identical because the
               flags match is the mistake that makes a workspace look like one machine. */
            Add(map, FpSurface.Canvas, FpSurface.Canvas,
                fp.CanvasNoise ? "noise:" + fp.Seed : "off");
            Add(map, FpSurface.Audio, FpSurface.Audio,
                fp.AudioNoise ? "noise:" + fp.Seed : "off");
            Add(map, FpSurface.Fonts, FpSurface.Fonts,
                fp.FontsNoise ? "noise:" + fp.Seed : "off:" + (fp.FontSet ?? "auto"));

            Add(map, FpSurface.Timezone, FpSurface.Timezone, fp.Timezone ?? "");
            Add(map, FpSurface.Language, FpSurface.Language,
                fp.Language ?? "", fp.AcceptLanguage ?? "");

            Add(map, FpSurface.WebRtc, FpSurface.WebRtc,
                fp.WebRtcProtect ? "protect:" + (fp.WebRtcMode ?? "proxy") : "off",
                "geo=" + (fp.GeoMode ?? "proxy"));

            return map;
        }

        private static void Add(Dictionary<string, FpSurfaceValue> map, string surface, string kind, params string[] parts)
        {
            string value = string.Join(" \u00b7 ", parts);
            map[surface] = new FpSurfaceValue(surface, Hash(Prepend(kind, parts)), value);
        }

        private static string[] Prepend(string head, string[] rest)
        {
            var list = new List<string> { head };
            if (rest != null) list.AddRange(rest);
            return list.ToArray();
        }

        /// <summary>
        /// One hash for the whole fingerprint, produced by folding the surface hashes in a fixed
        /// order. Two profiles with the same overall token share every surface.
        /// </summary>
        public static string OverallHash(Profile p)
        {
            var map = Surfaces(p);
            var parts = new List<string>();
            foreach (string s in FpSurface.All)
            {
                FpSurfaceValue v;
                parts.Add(map.TryGetValue(s, out v) ? v.Hash : "");
            }
            return Hash(parts.ToArray());
        }

        /// <summary>The same token for a bare configuration, used while a profile is being edited.</summary>
        public static string OverallHash(FingerprintConfig fp)
        {
            return OverallHash(new Profile { Fp = fp });
        }

        // ------------------------------------------------------------------ comparison

        /// <summary>How two profiles relate. Ordered from best to worst.</summary>
        internal enum Relation
        {
            /// <summary>Every surface differs.</summary>
            Distinct,
            /// <summary>They share only low-entropy surfaces such as the language.</summary>
            Remote,
            /// <summary>They share at least one surface a detector groups accounts by.</summary>
            Related,
            /// <summary>They share the strongest signals; a detector would pair the accounts.</summary>
            Linked,
            /// <summary>Same fingerprint in every surface.</summary>
            Identical
        }

        internal sealed class Comparison
        {
            public Profile A;
            public Profile B;
            public Relation Relation;
            public int Score;                        // 0 = unrelated, 100 = identical
            public List<string> SharedSurfaces = new List<string>();
            public List<string> DifferentSurfaces = new List<string>();

            public string Summary
            {
                get
                {
                    switch (Relation)
                    {
                        case Relation.Identical: return "Identical fingerprint";
                        case Relation.Linked: return "Linked \u2014 shares " + Join(SharedSurfaces);
                        case Relation.Related: return "Related \u2014 shares " + Join(SharedSurfaces);
                        case Relation.Remote: return "Barely related \u2014 shares " + Join(SharedSurfaces);
                        default: return "Independent";
                    }
                }
            }

            private static string Join(List<string> list)
            {
                if (list == null || list.Count == 0) return "nothing";
                var names = new List<string>();
                foreach (string s in list) names.Add(FpSurface.Label(s).ToLowerInvariant());
                if (names.Count <= 3) return string.Join(", ", names.ToArray());
                return names[0] + ", " + names[1] + " and " + (names.Count - 2) + " more";
            }
        }

        /// <summary>
        /// Compares two profiles surface by surface and turns the result into a score a human can
        /// act on. Surfaces that a real browser would report identically anyway (the language, the
        /// platform family) are weighted low on purpose: sharing them is not evidence of anything.
        /// </summary>
        public static Comparison Compare(Profile a, Profile b)
        {
            if (a == null || b == null)
                return new Comparison { A = a, B = b, Relation = Relation.Distinct };
            return Compare(a, Surfaces(a), b, Surfaces(b));
        }

        /// <summary>
        /// The real comparison, over two surface maps the caller already has. The workspace audit
        /// compares every pair, which is O(n²) maps if each call rebuilds them; that is 900 map
        /// builds for a 30-profile workspace, and it is the difference between a page that opens
        /// instantly and one that pauses.
        /// </summary>
        public static Comparison Compare(Profile a, Dictionary<string, FpSurfaceValue> ma,
            Profile b, Dictionary<string, FpSurfaceValue> mb)
        {
            var result = new Comparison { A = a, B = b };
            if (a == null || b == null || ma == null || mb == null)
            {
                result.Relation = Relation.Distinct;
                return result;
            }

            int totalWeight = 0, sharedWeight = 0;
            bool sharesAnything = false;

            foreach (string surface in FpSurface.All)
            {
                int w = FpSurface.Weight(surface);
                totalWeight += w;

                FpSurfaceValue va, vb;
                if (!ma.TryGetValue(surface, out va) || !mb.TryGetValue(surface, out vb)) continue;

                if (string.Equals(va.Hash, vb.Hash, StringComparison.Ordinal))
                {
                    sharedWeight += w;
                    result.SharedSurfaces.Add(surface);
                    sharesAnything = true;
                    /* A shared user agent follows from sharing an OS and a browser build; it is
                       reported and weighted, and never on its own grounds for a link. */
                }
                else
                {
                    result.DifferentSurfaces.Add(surface);
                }
            }

            result.Score = totalWeight == 0 ? 0 : (int)Math.Round(100.0 * sharedWeight / totalWeight);

            /* Calibration, because the difference between a useful warning and noise is
               entirely in these numbers.

               Sharing one surface is not evidence of anything. Millions of machines have an RTX
               3060, and a workspace of fifty profiles on thirty-three renderers cannot avoid
               repeating one - so "shares WebGL" alone must never be reported as a link, or the
               audit cries wolf on every large workspace and gets ignored.

               What detectors actually group on is the combination: a pair matching on the
               renderer, the screen and the hardware is a pair even when the canvas noise differs,
               because noise is the cheapest thing to re-roll. So the verdict is driven by the
               weighted overlap, with a count of strong surfaces as a shortcut for the near-miss
               case where the overlap sits just under the threshold. */
            int strongShared = 0;
            foreach (string s2 in result.SharedSurfaces)
                if (FpSurface.Weight(s2) >= 4 && !FpSurface.IsDerived(s2)) strongShared++;

            if (result.SharedSurfaces.Count == FpSurface.All.Length) result.Relation = Relation.Identical;
            else if (result.Score >= 65) result.Relation = Relation.Linked;
            else if (result.Score >= 40 || strongShared >= 3) result.Relation = Relation.Related;
            else if (sharesAnything) result.Relation = Relation.Remote;
            else result.Relation = Relation.Distinct;

            return result;
        }

        /// <summary>
        /// Same answer as <see cref="WorstMatch(Profile, IEnumerable{Profile})"/>, over surface maps
        /// the caller has already built. The uniqueness loop calls this up to twelve times per new
        /// profile, so rebuilding the workspace maps inside it would be the most expensive thing
        /// the creation path does.
        /// </summary>
        public static Comparison WorstMatch(Profile p, Dictionary<string, FpSurfaceValue> map,
            IEnumerable<KeyValuePair<Profile, Dictionary<string, FpSurfaceValue>>> all)
        {
            Comparison worst = null;
            if (p == null || map == null || all == null) return null;
            foreach (var entry in all)
            {
                var other = entry.Key;
                if (other == null) continue;
                if (!string.IsNullOrEmpty(p.Id) && string.Equals(other.Id, p.Id, StringComparison.Ordinal)) continue;
                var c = Compare(p, map, other, entry.Value);
                if (worst == null || Rank(c.Relation) > Rank(worst.Relation) ||
                    (Rank(c.Relation) == Rank(worst.Relation) && c.Score > worst.Score)) worst = c;
            }
            return worst;
        }

        /// <summary>The worst relation between <paramref name="p"/> and any other profile.</summary>
        public static Comparison WorstMatch(Profile p, IEnumerable<Profile> all)
        {
            Comparison worst = null;
            if (p == null || all == null) return null;
            foreach (var other in all)
            {
                if (other == null || ReferenceEquals(other, p)) continue;
                if (!string.IsNullOrEmpty(p.Id) && string.Equals(other.Id, p.Id, StringComparison.Ordinal)) continue;
                var c = Compare(p, other);
                if (worst == null || Rank(c.Relation) > Rank(worst.Relation) ||
                    (Rank(c.Relation) == Rank(worst.Relation) && c.Score > worst.Score)) worst = c;
            }
            return worst;
        }

        private static int Rank(Relation r)
        {
            switch (r)
            {
                case Relation.Identical: return 4;
                case Relation.Linked: return 3;
                case Relation.Related: return 2;
                case Relation.Remote: return 1;
                default: return 0;
            }
        }

        // ------------------------------------------------------------------ uniqueness

        /// <summary>
        /// Regenerates <paramref name="fp"/> until it no longer collides with any profile in
        /// <paramref name="others"/>.
        ///
        /// Regenerate alone is random, and random collides: the Windows GPU pool holds about
        /// thirty entries and the screen pool a dozen, so a workspace of thirty profiles has
        /// several pairs sharing a renderer by chance. This is the loop that turns "probably
        /// different" into "provably different", and it is the one call every creation path goes
        /// through.
        ///
        /// Only the fingerprint is passed, never a <see cref="Profile"/>: every creation path
        /// (new, duplicate, import, regenerate) can call this before the profile it belongs to
        /// has been assembled, which removes the ordering trap where a profile is saved first and
        /// de-duplicated second.
        ///
        /// Returns the number of rolls used. <paramref name="clash"/> is the closest remaining
        /// match when none of the rolls was clean, or the best roll's worst match when one was.
        /// </summary>
        public static int MakeUnique(FingerprintConfig fp, string os, ProxyConfig proxy,
            IEnumerable<Profile> others, out Comparison clash)
        {
            clash = null;
            if (fp == null) return 0;
            var pool = new List<Profile>();
            if (others != null)
                foreach (var o in others) if (o != null && o.Fp != null) pool.Add(o);

            // One probe reused across rolls: Compare only reads the configuration, so allocating a
            // Profile per attempt would only produce twelve short-lived objects per new profile.
            var probe = new Profile { Fp = fp };
            /* The candidate map is reused too. It is rebuilt from fp after each roll, because
               Regenerate writes straight into fp and a cached map would describe the previous
               attempt - the bug that would let a roll look clean while colliding. */
            var probeMap = new Dictionary<string, FpSurfaceValue>(StringComparer.Ordinal);

            // Maps for the workspace are built once, not once per attempt per profile.
            var poolMaps = new List<KeyValuePair<Profile, Dictionary<string, FpSurfaceValue>>>(pool.Count);
            foreach (var o in pool)
                poolMaps.Add(new KeyValuePair<Profile, Dictionary<string, FpSurfaceValue>>(o, Surfaces(o)));

            FingerprintConfig best = null;
            Comparison bestClash = null;

            /* Twelve attempts, escalating: a fresh roll each time, and if the workspace is dense
               enough that a roll keeps landing on a shared surface, the GPU pool is widened for
               the final attempts (see Regenerate's widePool). */
            for (int attempt = 0; attempt < 12; attempt++)
            {
                FingerprintFactory.Regenerate(fp, os, proxy, attempt >= 8);
                probeMap = Surfaces(fp);
                var worst = WorstMatch(probe, probeMap, poolMaps);

                if (worst == null || worst.Relation == Relation.Distinct || worst.Relation == Relation.Remote)
                {
                    clash = worst;
                    return attempt + 1;
                }

                /* On the last third of the loop, stop hoping the roll lands well and move the
                   renderer to one this workspace has never used. That is what makes the
                   guarantee hold at fifty profiles instead of only at five. */
                if (attempt >= 8 && RetargetRenderer(fp, pool))
                {
                    probeMap = Surfaces(fp);
                    var afterSwap = WorstMatch(probe, probeMap, poolMaps);
                    if (afterSwap == null || afterSwap.Relation == Relation.Distinct ||
                        afterSwap.Relation == Relation.Remote)
                    {
                        clash = afterSwap;
                        return attempt + 1;
                    }
                    if (bestClash == null || Rank(afterSwap.Relation) < Rank(bestClash.Relation) ||
                        (Rank(afterSwap.Relation) == Rank(bestClash.Relation) && afterSwap.Score < bestClash.Score))
                    {
                        best = fp.Copy();
                        bestClash = afterSwap;
                    }
                }

                if (bestClash == null || Rank(worst.Relation) < Rank(bestClash.Relation) ||
                    (Rank(worst.Relation) == Rank(bestClash.Relation) && worst.Score < bestClash.Score))
                {
                    // A snapshot, not a reference: fp is mutated by the next roll.
                    best = fp.Copy();
                    bestClash = worst;
                }
            }

            /* Even after twelve rolls every candidate shared something strong - a workspace this
               dense needs a different OS or a different kernel to separate profiles. Restore the
               least-bad roll and report it, so the caller can tell the user rather than pretend. */
            if (best != null) CopyInto(best, fp);
            clash = bestClash;
            return 12;
        }

        /// <summary>
        /// Resolves a workspace that random rolls cannot separate.
        ///
        /// At about thirty profiles the loop stops being enough on its own: the Windows pool has
        /// thirty-three renderers and the screen table a dozen entries, so by then most rolls
        /// land on a surface something else already uses, and twelve attempts do not find a
        /// clean one. Rather than give up and report a clash, this takes the renderer - the single
        /// strongest grouping signal - and moves it to one no other profile in the workspace is
        /// using. Deterministic, so it either succeeds or says it cannot.
        ///
        /// The swap is deliberate and narrow: a real card from the same OS pool, so the profile
        /// stays plausible; the cores and memory already rolled are left alone, because those are
        /// legitimate for almost every card and changing them too would make the profile drift
        /// from what the rest of its surfaces describe.
        /// </summary>
        public static bool RetargetRenderer(FingerprintConfig fp, IEnumerable<Profile> others)
        {
            if (fp == null) return false;
            /* Desktop only. On Android and iOS the renderer is not a free choice: it is the
               silicon the handset behind the viewport actually shipped, and the user agent names
               the same model. Swapping it to dodge a collision would create a phone that was
               never built - a far louder tell than a shared desktop card. */
            if (FingerprintFactory.IsMobileOs(fp.Os)) return false;

            var used = new HashSet<string>(StringComparer.Ordinal);
            if (others != null)
                foreach (var o in others)
                {
                    if (o == null || o.Fp == null) continue;
                    if (string.Equals(o.Fp.Os, fp.Os, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(o.Fp.WebGlRenderer))
                        used.Add(o.Fp.WebGlRenderer);
                }

            var pool = FingerprintFactory.GpusFor(fp.Os);
            if (pool == null || pool.Count == 0) return false;

            /* Start at a position derived from the seed rather than the head of the list, so a
               workspace of fifty profiles spreads across the pool instead of everyone fighting
               over the newest card. */
            int start = Math.Abs(fp.Seed) % pool.Count;
            for (int i = 0; i < pool.Count; i++)
            {
                var candidate = pool[(start + i) % pool.Count];
                if (candidate == null || candidate.Length < 2) continue;
                if (used.Contains(candidate[1])) continue;
                fp.WebGlVendor = candidate[0];
                fp.WebGlRenderer = candidate[1];
                return true;
            }
            return false;
        }

        /// <summary>Copies every malleable field of <paramref name="from"/> onto <paramref name="to"/>.</summary>
        private static void CopyInto(FingerprintConfig from, FingerprintConfig to)
        {
            to.Seed = from.Seed;
            to.Os = from.Os;
            to.DeviceType = from.DeviceType;
            to.Timezone = from.Timezone;
            to.Language = from.Language;
            to.AcceptLanguage = from.AcceptLanguage;
            to.ScreenW = from.ScreenW;
            to.ScreenH = from.ScreenH;
            to.AvailW = from.AvailW;
            to.AvailH = from.AvailH;
            to.ColorDepth = from.ColorDepth;
            to.PixelRatio = from.PixelRatio;
            to.WebGlVendor = from.WebGlVendor;
            to.WebGlRenderer = from.WebGlRenderer;
            to.Cores = from.Cores;
            to.Memory = from.Memory;
            to.TouchPoints = from.TouchPoints;
            to.CanvasNoise = from.CanvasNoise;
            to.AudioNoise = from.AudioNoise;
            to.FontsNoise = from.FontsNoise;
            to.DoNotTrack = from.DoNotTrack;
            to.SpoofBattery = from.SpoofBattery;
            to.SpoofMac = from.SpoofMac;
            to.WebRtcProtect = from.WebRtcProtect;
            to.WebRtcMode = from.WebRtcMode;
            to.GeoMode = from.GeoMode;
            to.AutoMatchProxy = from.AutoMatchProxy;
            to.AutoUserAgent = from.AutoUserAgent;
            to.AndroidVersion = from.AndroidVersion;
            to.IosVersion = from.IosVersion;
            to.FontSet = from.FontSet;
            to.HardwareNoise = from.HardwareNoise;
            to.UserAgent = from.UserAgent;
            to.Platform = from.Platform;
            to.Brand = from.Brand;
            to.UserAgentOverride = from.UserAgentOverride;
        }
    }
}
