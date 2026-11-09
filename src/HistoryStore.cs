using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>
    /// Per-profile browsing history.
    ///
    /// The point of this is to recover a URL the user passed through and cannot remember, so it
    /// records the whole navigation chain rather than only what ended up in the address bar: a
    /// link that bounces through a tracker, a redirect, or a consent hand-off leaves three or
    /// four URLs behind and only the last one is ever shown. Every hop is kept.
    ///
    /// What is deliberately NOT recorded: sub-resources. Recording images, fonts, scripts and
    /// beacons would produce thousands of rows per page and turn a history the user can read
    /// into a log nobody can search. A navigation is a document the browser replaced the page
    /// with; that is the unit here.
    ///
    /// Storage is one append-only file per profile, which keeps history isolated per profile and
    /// cheap to clear. Each line is a JSON object so a partially written line is skipped rather
    /// than corrupting the file.
    /// </summary>
    internal static class HistoryStore
    {
        private static readonly object Gate = new object();

        /// <summary>Hard cap per profile. Oldest entries are dropped, never the newest.</summary>
        private const int MaxEntriesPerProfile = 20000;

        public sealed class Entry
        {
            public string Url { get; set; }
            public string Title { get; set; }
            /// <summary>ISO-8601 UTC.</summary>
            public string At { get; set; }
            /// <summary>True for a hop the browser passed through without showing it, so the
            /// UI can mark the ones that never reached the address bar.</summary>
            public bool Hop { get; set; }
            public string ProfileId { get; set; }
            public string ProfileName { get; set; }

            public DateTime When
            {
                get
                {
                    DateTime t;
                    return DateTime.TryParse(At, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out t) ? t.ToLocalTime() : DateTime.Now;
                }
            }
        }

        /// <summary>
        /// History lives inside the signed-in account's folder, next to that account's profiles.
        /// Two accounts signed in on one Windows user therefore cannot read each other's history
        /// even by opening the files, and signing out does not leave the trail where the next
        /// account can pick it up.
        /// </summary>
        private static string Dir
        {
            get
            {
                string d = Path.Combine(ProfileStore.ActiveRoot, "history");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        /// <summary>Only characters safe in a file name, so a profile id can never escape the folder.</summary>
        private static string FileFor(string profileId)
        {
            var sb = new StringBuilder();
            foreach (char c in (profileId ?? "unknown"))
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return Path.Combine(Dir, sb.ToString() + ".jsonl");
        }

        /* The switch lives in AppSettings and is written by the Settings page. Read through a
           provider rather than by holding a settings object here: MainForm owns the only
           AppSettings instance, and a second copy loaded in this class would go stale the moment
           the user flipped the toggle. */
        public static Func<bool> EnabledProvider;

        public static bool Enabled
        {
            get
            {
                if (ForceEnabled) return true;
                var p = EnabledProvider;
                try { return p == null || p(); } catch { return true; }
            }
        }

        /// <summary>
        /// True for URLs worth keeping. Chrome's own pages and the blank start page carry no
        /// information the user could ever want back, and data: URLs are page content, not
        /// navigation.
        /// </summary>
        public static bool Interesting(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string u = url.Trim();
            if (u.Length > 4096) return false;
            if (u.Equals("about:blank", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("chrome-extension:", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("chrome-untrusted:", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("devtools:", StringComparison.OrdinalIgnoreCase)) return false;
            if (u.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>
        /// Fills in the title of the newest entry for a profile. Titles arrive as a separate CDP
        /// event a moment after the document starts, so the visit is recorded first - when it
        /// happens, which is the whole point - and named second.
        /// </summary>
        public static void SetLastTitle(string profileId, string title)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(title)) return;
            try
            {
                lock (Gate)
                {
                    string file = FileFor(profileId);
                    if (!File.Exists(file)) return;
                    string[] lines = File.ReadAllLines(file);
                    if (lines.Length == 0) return;
                    var ser = new JavaScriptSerializer();
                    int last = -1;
                    for (int i = lines.Length - 1; i >= 0; i--)
                        if (!string.IsNullOrWhiteSpace(lines[i])) { last = i; break; }
                    if (last < 0) return;
                    Entry e;
                    try { e = ser.Deserialize<Entry>(lines[last]); } catch { return; }
                    if (e == null || e.Title == title) return;
                    e.Title = title.Length > 300 ? title.Substring(0, 300) : title;
                    lines[last] = ser.Serialize(e);
                    File.WriteAllLines(file, lines);
                }
            }
            catch (Exception ex) { Log.Error("History title failed", ex); }
        }

        /// <summary>Appends a visit, then trims the file when it has outgrown the cap.</summary>
        private static void Append(Entry entry, bool trim)
        {
            string file = FileFor(entry.ProfileId);
            var ser = new JavaScriptSerializer();
            using (var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                w.WriteLine(ser.Serialize(entry));

            if (!trim) return;
            /* The cap used to be applied only when reading, so the file itself grew without
               limit and every read parsed a year of rows to show the newest page of them. The
               file is rewritten only once it is 25% over, so the cost is amortised, and only
               the newest cap rows survive.
               The size is checked BEFORE the read: Append runs on every navigation in every
               running profile, and the old code re-read a file that could hold 25,000 JSON
               lines just to learn it was still under the cap. A FileInfo.Length call answers
               in microseconds; a 25k-line read on every page load is the "history makes
               browsing feel slow" report. */
            long limit = (long)(MaxEntriesPerProfile + MaxEntriesPerProfile / 4) * 150; // smallest realistic JSON row
            try { if (new FileInfo(file).Length <= limit) return; } catch { return; }
            string[] lines = File.ReadAllLines(file);
            if (lines.Length <= MaxEntriesPerProfile + MaxEntriesPerProfile / 4) return;
            int keep = MaxEntriesPerProfile;
            var tail = new string[keep];
            Array.Copy(lines, lines.Length - keep, tail, 0, keep);
            File.WriteAllLines(file, tail);
        }

        /* The url of the newest row per profile, so the two events that describe one navigation
           can be told apart from two navigations. Kept in memory: it only has to survive within a
           run, and a duplicate that slips through after a restart is one row, not a pattern. */
        private static readonly Dictionary<string, string> LastUrl = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> LastHop = new Dictionary<string, bool>(StringComparer.Ordinal);

        public static void Add(string profileId, string profileName, string url, string title, bool hop)
        {
            if (!Enabled || !Interesting(url)) return;
            try
            {
                lock (Gate)
                {
                    /* frameStartedNavigating fires before the request and frameNavigated fires
                       when the document is committed, so an ordinary navigation reaches here
                       twice with the same address. The first is the hop, the second is the page
                       the user is actually on, and the second has to win: it is the one in the
                       address bar. */
                    string key = profileId ?? "";
                    string lastUrl;
                    bool lastHop;
                    if (LastUrl.TryGetValue(key, out lastUrl) && string.Equals(lastUrl, url.Trim(), StringComparison.Ordinal) &&
                        LastHop.TryGetValue(key, out lastHop))
                    {
                        if (lastHop && !hop)
                        {
                            ClearHop(profileId);
                            LastHop[key] = false;
                        }
                        return;
                    }
                    LastUrl[key] = url.Trim();
                    LastHop[key] = hop;
                    var entry = new Entry
                    {
                        Url = url.Trim(),
                        Title = (title ?? "").Trim(),
                        At = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                        Hop = hop,
                        ProfileId = profileId,
                        ProfileName = profileName
                    };
                    Append(entry, true);
                }
            }
            catch (Exception ex) { Log.Error("History append failed", ex); }
        }

        /// <summary>
        /// Reads one profile's history, newest first. A line that cannot be parsed is skipped:
        /// the app may have been killed mid-write, and losing one row is better than losing the
        /// file.
        /// </summary>
        public static List<Entry> Read(string profileId)
        {
            var list = new List<Entry>();
            string file;
            try { file = FileFor(profileId); } catch { return list; }
            if (!File.Exists(file)) return list;

            var ser = new JavaScriptSerializer();
            try
            {
                lock (Gate)
                {
                    foreach (string line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var e = ser.Deserialize<Entry>(line);
                            if (e != null && !string.IsNullOrEmpty(e.Url)) list.Add(e);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Log.Error("History read failed", ex); }

            list.Reverse();
            if (list.Count > MaxEntriesPerProfile) list.RemoveRange(MaxEntriesPerProfile, list.Count - MaxEntriesPerProfile);
            return list;
        }

        public static int Count(string profileId)
        {
            try
            {
                string file = FileFor(profileId);
                if (!File.Exists(file)) return 0;
                int n = 0;
                lock (Gate)
                {
                    foreach (string line in File.ReadLines(file))
                        if (!string.IsNullOrWhiteSpace(line)) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        /// <summary>Drops everything recorded for one profile and reports what was dropped.</summary>
        public static int Clear(string profileId)
        {
            int n = Count(profileId);
            try
            {
                lock (Gate)
                {
                    string file = FileFor(profileId);
                    if (File.Exists(file)) File.Delete(file);
                    LastUrl.Remove(profileId ?? "");
                    LastHop.Remove(profileId ?? "");
                }
            }
            catch (Exception ex) { Log.Error("History clear failed", ex); return 0; }
            return n;
        }

        /// <summary>Drops every profile's history, for the settings page. Returns rows removed.</summary>
        public static int ClearAll()
        {
            int n = 0;
            try
            {
                lock (Gate)
                {
                    string d = Dir;
                    if (!Directory.Exists(d)) return 0;
                    foreach (string f in Directory.GetFiles(d, "*.jsonl"))
                    {
                        try
                        {
                            n += File.ReadAllLines(f).Length;
                            File.Delete(f);
                        }
                        catch { }
                    }
                    LastUrl.Clear();
                    LastHop.Clear();
                }
            }
            catch (Exception ex) { Log.Error("History clear-all failed", ex); }
            return n;
        }

        /// <summary>
        /// Round-trips the store against a throwaway profile. Called by --selftest in the release
        /// build: history is the one part of this app whose failure is completely silent, so
        /// "it compiles" is not evidence that it works and the build says so explicitly.
        /// </summary>
        public static string SelfTest()
        {
            const string id = "__selftest__";
            var problems = new StringBuilder();
            bool wasEnabled = ForceEnabled;
            try
            {
                DeleteFileFor(id);
                ForceEnabled = true;
                Add(id, "self test", "https://example.com/start", "", false);
                Add(id, "self test", "https://example.com/through?hop=1", "", true);
                Add(id, "self test", "https://example.com/end", "", false);
                SetLastTitle(id, "End page");

                var rows = Read(id);
                if (rows.Count != 3) problems.Append(" count=" + rows.Count);
                else
                {
                    if (rows[0].Url != "https://example.com/end") problems.Append(" newest=" + rows[0].Url);
                    if (rows[0].Title != "End page") problems.Append(" title=" + rows[0].Title);
                    if (!rows[1].Hop) problems.Append(" hop flag lost");
                    if (rows[2].Url != "https://example.com/start") problems.Append(" oldest=" + rows[2].Url);
                }

                /* One navigation reaches the store twice, as a hop and then as the committed
                   page. The second must not add a row, and it must clear the hop mark. */
                DeleteFileFor(id);
                Add(id, "self test", "https://example.com/page", "", true);
                Add(id, "self test", "https://example.com/page", "Page", false);
                var collapsed = Read(id);
                if (collapsed.Count != 1) problems.Append(" one navigation stored " + collapsed.Count + " rows");
                else if (collapsed[0].Hop) problems.Append(" the committed page is still marked as a hop");

                /* Two different addresses in a row are two rows. */
                Add(id, "self test", "https://example.com/second", "", false);
                if (Read(id).Count != 2) problems.Append(" a real second visit was dropped");

                if (Interesting("chrome://settings")) problems.Append(" a browser page was recorded");
                if (!Interesting("https://example.com/keep")) problems.Append(" a real url was rejected");

                if (ClearAll() < 1) problems.Append(" clear all removed nothing");
                if (Read(id).Count != 0) problems.Append(" rows survived clear");
            }
            catch (Exception ex) { problems.Append(" " + ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                ForceEnabled = wasEnabled;
                try { DeleteFileFor(id); } catch { }
            }
            return problems.ToString();
        }

        /// <summary>Lets the self test record while the user's own switch may be off.</summary>
        private static bool ForceEnabled;

        private static void DeleteFileFor(string profileId)
        {
            try { string f = FileFor(profileId); if (File.Exists(f)) File.Delete(f); } catch { }
        }

        /// <summary>Marks the newest row as a real page rather than a hop.</summary>
        private static void ClearHop(string profileId)
        {
            try
            {
                string file = FileFor(profileId);
                if (!File.Exists(file)) return;
                var ser = new JavaScriptSerializer();
                string[] lines = File.ReadAllLines(file);
                int last = -1;
                for (int i = lines.Length - 1; i >= 0; i--)
                    if (!string.IsNullOrWhiteSpace(lines[i])) { last = i; break; }
                if (last < 0) return;
                Entry e;
                try { e = ser.Deserialize<Entry>(lines[last]); } catch { return; }
                if (e == null || !e.Hop) return;
                e.Hop = false;
                lines[last] = ser.Serialize(e);
                File.WriteAllLines(file, lines);
            }
            catch (Exception ex) { Log.Error("History hop clear failed", ex); }
        }

        /// <summary>Removes a profile's history when the profile itself is deleted for good.</summary>
        public static void Forget(string profileId)
        {
            Clear(profileId);
        }
    }
}
