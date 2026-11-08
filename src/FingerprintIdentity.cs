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
            var map = new Dictionary<string, FpSurfaceValue>(StringComparer.Ordinal);
            if (p == null) return map;
            var fp = p.Fp ?? new FingerprintConfig();

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
            var result = new Comparison { A = a, B = b };
            if (a == null || b == null) { result.Relation = Relation.Distinct; return result; }

            var ma = Surfaces(a);
            var mb = Surfaces(b);

            int totalWeight = 0, sharedWeight = 0;
            bool sharesStrong = false, sharesAnything = false;

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
                    if (w >= 4) sharesStrong = true;
                }
                else
                {
                    result.DifferentSurfaces.Add(surface);
                }
            }

            result.Score = totalWeight == 0 ? 0 : (int)Math.Round(100.0 * sharedWeight / totalWeight);

            if (result.SharedSurfaces.Count == FpSurface.All.Length) result.Relation = Relation.Identical;
            else if (sharesStrong) result.Relation = Relation.Linked;
            else if (sharesAnything && result.Score >= 35) result.Relation = Relation.Related;
            else if (sharesAnything) result.Relation = Relation.Remote;
            else result.Relation = Relation.Distinct;

            return result;
        }

        /// <summary>True when two profiles would be reported as the same machine by a detector.</summary>
        public static bool TooSimilar(Profile a, Profile b)
        {
            var c = Compare(a, b);
            return c.Relation == Relation.Identical || c.Relation == Relation.Linked;
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
            FingerprintConfig best = null;
            Comparison bestClash = null;

            /* Twelve attempts, escalating: a fresh roll each time, and if the workspace is dense
               enough that a roll keeps landing on a shared surface, the GPU pool is widened for
               the final attempts (see Regenerate's widePool). */
            for (int attempt = 0; attempt < 12; attempt++)
            {
                FingerprintFactory.Regenerate(fp, os, proxy, attempt >= 8);
                var worst = WorstMatch(probe, pool);

                if (worst == null || worst.Relation == Relation.Distinct || worst.Relation == Relation.Remote)
                {
                    clash = worst;
                    return attempt + 1;
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
        /// Makes an already stored workspace unique: walks the profiles in creation order and
        /// re-rolls any profile that collides with one already accepted. Used by the repair action
        /// and after a bulk import, so the guarantee holds for profiles that predate it.
        /// Returns the number of profiles that had to be re-rolled.
        /// </summary>
        public static int RepairAll(IList<Profile> all, Func<Profile, ProxyConfig> proxyFor)
        {
            if (all == null || all.Count == 0) return 0;
            var accepted = new List<Profile>();
            int repaired = 0;

            foreach (var p in all)
            {
                if (p == null) continue;
                if (p.Fp == null) p.Fp = new FingerprintConfig();

                Comparison clash;
                var worst = WorstMatch(p, accepted);
                if (worst != null && (worst.Relation == Relation.Identical || worst.Relation == Relation.Linked ||
                                      worst.Relation == Relation.Related))
                {
                    ProxyConfig proxy = proxyFor != null ? proxyFor(p) : null;
                    MakeUnique(p.Fp, p.Fp.Os, proxy, accepted, out clash);
                    repaired++;
                }
                accepted.Add(p);
            }
            return repaired;
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
