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

        public static void Add(string profileId, string profileName, string url, string title, bool hop)
        {
            if (!Enabled || !Interesting(url)) return;
            try
            {
                lock (Gate)
                {
                    string file = FileFor(profileId);
                    var entry = new Entry
                    {
                        Url = url.Trim(),
                        Title = (title ?? "").Trim(),
                        At = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                        Hop = hop,
                        ProfileId = profileId,
                        ProfileName = profileName
                    };
                    var ser = new JavaScriptSerializer();
                    using (var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                        w.WriteLine(ser.Serialize(entry));
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
                }
            }
            catch (Exception ex) { Log.Error("History clear failed", ex); return 0; }
            return n;
        }

        /// <summary>Drops every profile's history, for the settings page.</summary>
        public static void ClearAll()
        {
            try
            {
                lock (Gate)
                {
                    string d = Dir;
                    if (!Directory.Exists(d)) return;
                    foreach (string f in Directory.GetFiles(d, "*.jsonl")) { try { File.Delete(f); } catch { } }
                }
            }
            catch (Exception ex) { Log.Error("History clear-all failed", ex); }
        }

        /// <summary>Removes a profile's history when the profile itself is deleted for good.</summary>
        public static void Forget(string profileId)
        {
            Clear(profileId);
        }
    }
}
