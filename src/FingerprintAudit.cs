using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>One thing the audit found, ready to be listed and to be fixed.</summary>
    internal sealed class AuditFinding
    {
        /// <summary>environment | ip | kernel</summary>
        public string Kind;
        public string Title;
        public string Detail;
        public FingerprintIdentity.Relation Relation;
        /// <summary>Ids of the profiles involved, in display order.</summary>
        public List<string> Ids = new List<string>();
        public List<string> Names = new List<string>();

        public bool Fixable { get { return Kind == "environment" || Kind == "coherence"; } }

        public Color Tint
        {
            get
            {
                switch (Kind)
                {
                    case "ip": return Color.FromArgb(0xF5, 0x9E, 0x0B);
                    case "kernel": return Color.FromArgb(0x06, 0xB6, 0xD4);
                    case "coherence": return Color.FromArgb(0x8B, 0x5C, 0xF6);
                    default:
                        return Relation == FingerprintIdentity.Relation.Identical ||
                               Relation == FingerprintIdentity.Relation.Linked
                            ? Theme.Accent : Theme.TextDim;
                }
            }
        }

        public string Icon
        {
            get
            {
                switch (Kind)
                {
                    case "ip": return "globe";
                    case "kernel": return "window";
                    case "coherence": return "alert";
                    default:
                        return Relation == FingerprintIdentity.Relation.Identical ? "copy" : "layers";
                }
            }
        }
    }

    /// <summary>
    /// Finds profiles that belong together.
    ///
    /// Two separate questions are answered here, because they have different fixes:
    ///
    ///   * Do these profiles look like <em>one browser</em>? That is the fingerprint comparison
    ///     in FingerprintIdentity, clustered so a workspace of forty profiles produces a handful
    ///     of findings instead of eight hundred pairs.
    ///   * Do these profiles look like <em>one account</em>? The strongest signal for that is not
    ///     the fingerprint at all - it is the network. Two profiles behind one proxy endpoint are
    ///     one identity to every site that logs an IP, whatever their canvas says. That is
    ///     reported separately and cannot be fixed by re-rolling, so the audit says so rather
    ///     than offering a button that would change nothing.
    /// </summary>
    internal static class FingerprintAudit
    {
        public static List<AuditFinding> Run(IEnumerable<Profile> profiles)
        {
            var list = new List<Profile>();
            if (profiles != null)
                foreach (var p in profiles) if (p != null) list.Add(p);

            var findings = new List<AuditFinding>();
            if (list.Count < 2) return findings;

            findings.AddRange(Clusters(list));
            findings.AddRange(SharedAddresses(list));

            var kernel = KernelFinding(list);
            if (kernel != null) findings.Add(kernel);

            var incoherent = CoherenceFinding(list);
            if (incoherent != null) findings.Add(incoherent);

            // Worst first: identical before linked, IP collisions interleaved by severity.
            findings.Sort(delegate (AuditFinding a, AuditFinding b)
            {
                int ra = Severity(a), rb = Severity(b);
                return ra != rb ? rb.CompareTo(ra) : string.CompareOrdinal(a.Title ?? "", b.Title ?? "");
            });
            return findings;
        }

        private static int Severity(AuditFinding f)
        {
            if (f.Kind == "ip") return 8;
            if (f.Kind == "coherence") return 5;
            switch (f.Relation)
            {
                case FingerprintIdentity.Relation.Identical: return 7;
                case FingerprintIdentity.Relation.Linked: return 6;
                case FingerprintIdentity.Relation.Related: return 4;
                case FingerprintIdentity.Relation.Remote: return 2;
                default: return 1;
            }
        }

        // ------------------------------------------------------------------ fingerprint clusters

        /// <summary>
        /// Groups profiles that would be read as the same machine. Union-find, so A~B and B~C
        /// become one finding rather than two, which is how the grouping actually works in
        /// practice: a detector walks the cluster, not the pair.
        /// </summary>
        private static List<AuditFinding> Clusters(List<Profile> list)
        {
            int n = list.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            var pairs = new List<FingerprintIdentity.Comparison>();

            /* Build each surface map once. Every pair needs two of them, so rebuilding per pair
               is n² allocations for a workspace of n profiles - the kind of cost that is invisible
               at five profiles and a visible pause at sixty. */
            var maps = new Dictionary<string, FpSurfaceValue>[n];
            for (int i = 0; i < n; i++) maps[i] = FingerprintIdentity.Surfaces(list[i]);

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var c = FingerprintIdentity.Compare(list[i], maps[i], list[j], maps[j]);
                    if (c.Relation == FingerprintIdentity.Relation.Distinct ||
                        c.Relation == FingerprintIdentity.Relation.Remote) continue;
                    pairs.Add(c);
                    Union(parent, i, j);
                }

            if (pairs.Count == 0) return new List<AuditFinding>();

            var groups = new Dictionary<int, List<Profile>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                List<Profile> bucket;
                if (!groups.TryGetValue(root, out bucket)) { bucket = new List<Profile>(); groups[root] = bucket; }
                bucket.Add(list[i]);
            }

            var findings = new List<AuditFinding>();
            foreach (var kv in groups)
            {
                var members = kv.Value;
                if (members.Count < 2) continue;

                /* The finding's severity is its worst pair, and its detail names the surfaces
                   the whole cluster has in common - which is the part the user can act on. */
                FingerprintIdentity.Comparison worst = null;
                var sharedAll = new HashSet<string>(FpSurface.All);
                /* Pairs are read back through the same union-find that grouped the members, so
                   every pair is visited once and only by the cluster it belongs to. Walk the whole
                   pair list per cluster instead and a workspace with forty profiles that share one
                   renderer does 1600 x 40 comparisons for no extra information. */
                var index = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < n; i++) index[list[i].Id ?? ""] = i;
                int first;
                if (!index.TryGetValue(members[0].Id ?? "", out first)) first = 0;
                int root = Find(parent, first);

                foreach (var c in pairs)
                {
                    if (c.A == null || c.B == null) continue;
                    int ia, ib;
                    if (!index.TryGetValue(c.A.Id ?? "", out ia)) continue;
                    if (!index.TryGetValue(c.B.Id ?? "", out ib)) continue;
                    if (Find(parent, ia) != root || Find(parent, ib) != root) continue;
                    if (worst == null || RankOf(c.Relation) > RankOf(worst.Relation) ||
                        (RankOf(c.Relation) == RankOf(worst.Relation) && c.Score > worst.Score)) worst = c;
                    foreach (string s in FpSurface.All)
                        if (!c.SharedSurfaces.Contains(s)) sharedAll.Remove(s);
                }

                var relation = worst == null ? FingerprintIdentity.Relation.Related : worst.Relation;
                /* "Remote" means they happen to share something minor - an operating system, a
                   language. That is most of any honest workspace, and listing it would bury the
                   pairs that matter under the pairs that do not. */
                if (relation == FingerprintIdentity.Relation.Remote) continue;

                var f = new AuditFinding
                {
                    Kind = "environment",
                    Relation = relation,
                    Title = members.Count + " profiles look like one browser",
                    Detail = DetailFor(sharedAll, worst)
                };
                foreach (var m in members) { f.Ids.Add(m.Id); f.Names.Add(m.Name); }
                findings.Add(f);
            }
            return findings;
        }

        private static string DetailFor(HashSet<string> sharedAll, FingerprintIdentity.Comparison worst)
        {
            if (worst == null) return "These profiles share fingerprint surfaces.";
            /* Common to the whole cluster is the actionable statement; if that is empty, the
               worst pair is what ties the cluster together, so report that instead of "nothing". */
            var names = new List<string>();
            foreach (string s in FpSurface.All) if (sharedAll.Contains(s)) names.Add(FpSurface.Label(s).ToLowerInvariant());
            if (names.Count == 0 && worst.SharedSurfaces.Count > 0)
                foreach (string s in worst.SharedSurfaces) names.Add(FpSurface.Label(s).ToLowerInvariant());

            if (names.Count == 0) return "Same overall fingerprint.";
            string joined = names.Count <= 4
                ? string.Join(", ", names.ToArray())
                : string.Join(", ", names.Take(3).ToArray()) + " and " + (names.Count - 3) + " more";
            return "Shares " + joined + "  \u00b7  " + worst.Score + "% overlap";
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }

        private static int RankOf(FingerprintIdentity.Relation r)
        {
            switch (r)
            {
                case FingerprintIdentity.Relation.Identical: return 4;
                case FingerprintIdentity.Relation.Linked: return 3;
                case FingerprintIdentity.Relation.Related: return 2;
                case FingerprintIdentity.Relation.Remote: return 1;
                default: return 0;
            }
        }

        // ------------------------------------------------------------------ shared network

        /// <summary>
        /// Profiles pointing at the same proxy endpoint, or at the same explicit IP. This is the
        /// account-link signal a fingerprint cannot fix: sites group accounts by address, so two
        /// "unlinked" profiles behind one proxy are one person as far as the server is concerned.
        /// </summary>
        private static List<AuditFinding> SharedAddresses(List<Profile> list)
        {
            var byEndpoint = new Dictionary<string, List<Profile>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in list)
            {
                if (p.Proxy == null || !p.Proxy.Enabled) continue;
                string host = (p.Proxy.Host ?? "").Trim();
                if (host.Length == 0) continue;
                string key = p.Proxy.Type + "|" + host.ToLowerInvariant() + ":" + p.Proxy.Port;
                List<Profile> bucket;
                if (!byEndpoint.TryGetValue(key, out bucket)) { bucket = new List<Profile>(); byEndpoint[key] = bucket; }
                bucket.Add(p);
            }

            var findings = new List<AuditFinding>();
            foreach (var kv in byEndpoint)
            {
                if (kv.Value.Count < 2) continue;
                var f = new AuditFinding
                {
                    Kind = "ip",
                    Title = kv.Value.Count + " profiles share one proxy address",
                    Detail = "Same exit IP links accounts regardless of fingerprint. Give each profile its own proxy."
                };
                foreach (var p in kv.Value) { f.Ids.Add(p.Id); f.Names.Add(p.Name); }
                findings.Add(f);
            }

            /* Profiles with no proxy share this PC's address. That is one sentence in the summary
               rather than a finding per profile: on a fresh install every profile looks like this,
               the user has not had a chance to add a proxy yet, and a stack of red rows on first
               launch teaches people to close the window. */
            LastDirectCount = list.Count(p => p.Proxy == null || !p.Proxy.Enabled);
            return findings;
        }

        /// <summary>Profiles on this PC's own connection, for the summary line.</summary>
        public static int LastDirectCount;

        // ------------------------------------------------------------------ kernel spread

        private static AuditFinding KernelFinding(List<Profile> list)
        {
            int distinct = KernelDiversity.DistinctCount(list);
            if (distinct > 1) return null;

            var installed = KernelDiversity.Available().Count;
            var f = new AuditFinding
            {
                Kind = "kernel",
                Title = "Every profile uses the same browser build",
                Detail = installed <= 1
                    ? "Only one kernel is installed. Install another build on the Browsers page to give profiles their own."
                    : "Kernels are installed but no profile is pinned to them. New profiles now pick a build automatically."
            };
            // A note, not a clickable fix: the fix is a download, and pretending otherwise would
            // produce a button that says "fix" and does nothing.
            foreach (var p in list) { f.Ids.Add(p.Id); f.Names.Add(p.Name); }
            return f;
        }

        // ------------------------------------------------------------------ coherence

        /// <summary>
        /// Profiles whose stored fields describe a machine that cannot exist. Read on a copy, so
        /// merely opening the audit never rewrites anything - the repair happens on load or on
        /// Fix, and this only reports that it was needed.
        /// </summary>
        private static AuditFinding CoherenceFinding(List<Profile> list)
        {
            var bad = new List<Profile>();
            foreach (var p in list)
            {
                if (p == null || p.Fp == null) continue;
                if (FingerprintFactory.Coerce(p.Fp.Copy()) > 0) bad.Add(p);
            }
            if (bad.Count == 0) return null;

            var f = new AuditFinding
            {
                Kind = "coherence",
                Relation = FingerprintIdentity.Relation.Related,
                Title = bad.Count + (bad.Count == 1 ? " profile describes an impossible machine"
                                                    : " profiles describe an impossible machine"),
                Detail = "Mismatched renderer, memory or device fields. These are repaired when the profile opens."
            };
            foreach (var p in bad) { f.Ids.Add(p.Id); f.Names.Add(p.Name); }
            return f;
        }

        // ------------------------------------------------------------------ repair

        /// <summary>
        /// Re-rolls every profile that shares an environment with one already accepted, oldest
        /// profile first so the profiles the user has been using longest keep their identity and
        /// the newer ones move.
        /// </summary>
        public static int Fix(List<Profile> ordered, ProfileStore store, out int failed)
        {
            failed = 0;
            if (ordered == null || ordered.Count == 0) return 0;
            var accepted = new List<Profile>();
            int changed = 0;

            foreach (var p in ordered)
            {
                if (p == null) continue;
                if (p.Fp == null) p.Fp = new FingerprintConfig();

                /* Coherence first: repairing a screen or a renderer changes the fingerprint, so
                   de-duplicating before it would compare a configuration about to change. */
                bool coerced = FingerprintFactory.Coerce(p.Fp) > 0;

                var worst = FingerprintIdentity.WorstMatch(p, accepted);
                if (worst != null && worst.Relation != FingerprintIdentity.Relation.Distinct &&
                    worst.Relation != FingerprintIdentity.Relation.Remote)
                {
                    FingerprintIdentity.Comparison clash;
                    FingerprintIdentity.MakeUnique(p.Fp, p.Fp.Os, p.Proxy, accepted, out clash);
                    /* A re-roll can land on a screen from the right table with the wrong density,
                       or on a renderer whose vendor field was hand-edited; coerce after rolling,
                       not before, so what gets saved is coherent as well as unique. */
                    FingerprintFactory.Coerce(p.Fp);
                    try { store.Save(p); changed++; }
                    catch (Exception ex) { Log.Error("Cannot save re-rolled fingerprint", ex); failed++; }
                }
                else if (coerced)
                {
                    try { store.Save(p); changed++; }
                    catch (Exception ex) { Log.Error("Cannot save repaired fingerprint", ex); failed++; }
                }
                accepted.Add(p);
            }
            return changed;
        }
    }

    /// <summary>The audit window: what collides, why, and one button that fixes what can be fixed.</summary>
    internal sealed class FingerprintAuditForm : JBForm
    {
        private readonly List<Profile> _profiles;
        private readonly ProfileStore _store;
        private readonly List<AuditFinding> _findings;
        private readonly int _directCount;
        private readonly Panel _column = new Panel();
        private readonly JBScrollHost _host;
        private readonly List<AuditRow> _rows = new List<AuditRow>();
        private readonly JBButton _close, _fix;
        private readonly JBIconButton _x;
        private bool _built;

        public int Fixed { get; private set; }

        public FingerprintAuditForm(List<Profile> profiles, ProfileStore store)
        {
            _profiles = profiles ?? new List<Profile>();
            _store = store;
            _findings = FingerprintAudit.Run(_profiles);
            // Copied out of the static immediately: the number belongs to this workspace view,
            // and a second audit opened later must not change what this one says.
            _directCount = FingerprintAudit.LastDirectCount;

            Text = "Account audit";
            Resizable = true;
            DraggableSidebar = false;
            HeaderHit = 76;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            MinimumSize = new Size(Ui.S(520), Ui.S(400));
            Size = Ui.FitToScreen(this, Ui.S(680), Ui.S(560));

            _column.BackColor = Theme.Bg;
            _host = new JBScrollHost(_column);
            Controls.Add(_host);

            foreach (var f in _findings)
            {
                var row = new AuditRow(f);
                _rows.Add(row);
                _column.Controls.Add(row);
            }

            _fix = new JBButton { Kind = BtnKind.Primary, Icon = "refresh", Text = "Fix what can be fixed", Radius = 4, Height = Ui.S(33) };
            _fix.Click += delegate { DoFix(); };
            _fix.Visible = _findings.Count(f => f.Fixable) > 0;
            Controls.Add(_fix);

            _close = new JBButton { Kind = BtnKind.Soft, Text = "Close", Radius = 4, Height = Ui.S(33) };
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            _x = new JBIconButton { Icon = "close", Danger = true, IconSize = 13, Tip = "Close" };
            _x.Click += delegate { Close(); };
            Controls.Add(_x);

            _built = true;
        }

        private void DoFix()
        {
            int failed;
            /* Creation order, oldest first: the profile the user has been running longest keeps
               its identity and the newer copies move, which is the direction they expect. */
            var ordered = _profiles.OrderBy(p => p.Created).ToList();
            int changed = FingerprintAudit.Fix(ordered, _store, out failed);
            Fixed = changed;

            if (failed > 0)
                MessageBox.Show(this, changed + " fingerprints re-rolled, " + failed +
                    " could not be saved.\r\nThe log has the details.", "Account audit",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterToScreen();
            LayoutCore();
        }

        /// <summary>
        /// Esc and Enter both close. This window has one primary action and one dismiss, and a
        /// read-only report that refuses to close on Esc - or swallows it into an accidental
        /// "fix" - is the kind of small rudeness that makes a dialog feel unfinished.
        /// </summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCore();
        }

        private void LayoutCore()
        {
            if (!_built) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(24);
            _x.SetBounds(w - pad - Ui.S(30), Ui.S(14), Ui.S(30), Ui.S(30));

            int top = Ui.S(96);
            _host.SetBounds(pad, top, w - pad * 2, Math.Max(Ui.S(100), h - top - Ui.S(56)));

            int cw = Math.Max(Ui.S(240), _host.Width - Ui.S(10));
            int y = 0;
            foreach (var r in _rows)
            {
                int rh = r.Measure(cw);
                r.SetBounds(0, y, cw, rh);
                y += rh + Ui.S(8);
            }
            _column.SetBounds(0, 0, Math.Max(cw, _host.Width - Ui.S(10)), Math.Max(y, _host.Height));
            _host.SyncExtent();

            int bh = Ui.S(33), by = h - Ui.S(16) - bh;
            int cw2 = _close.PreferredWidth(Ui.S(100));
            _close.SetBounds(w - pad - cw2, by, cw2, bh);
            if (_fix.Visible)
            {
                int fw = _fix.PreferredWidth(Ui.S(190));
                _fix.SetBounds(w - pad - cw2 - Ui.S(8) - fw, by, fw, bh);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Quality(g);
            int pad = Ui.S(24);
            int w = ClientSize.Width;

            Ui.Text(g, "Account audit", Theme.Display, Theme.Text, pad, Ui.S(14), w - pad * 2 - Ui.S(40));

            string sub;
            Color tone = Theme.TextDim;
            if (_profiles.Count < 2)
            {
                sub = "Create a second profile to compare environments.";
            }
            else if (_findings.Count == 0)
            {
                sub = _profiles.Count + " profiles \u00b7 no shared environment found";
                tone = Theme.Success;
            }
            else
            {
                int env = _findings.Count(f => f.Kind == "environment");
                sub = _profiles.Count + " profiles \u00b7 " + env +
                      (env == 1 ? " shared environment" : " shared environments") +
                      " \u00b7 " + KernelDiversity.DistinctCount(_profiles) + " browser builds";
            }
            /* Always worth one sentence, even when nothing else is wrong: an IP is the one thing
               a fingerprint cannot cover, and profiles on this PC's own connection share it. */
            if (_profiles.Count >= 2 && _directCount >= 2)
                sub += "  \u00b7  " + _directCount + " on this PC's IP";
            Ui.Text(g, sub, Theme.Small, tone, pad, Ui.S(44), w - pad * 2 - Ui.S(40));

            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, pad, Ui.S(88), w - pad, Ui.S(88));

            /* An empty scroll area is indistinguishable from a failed one, so say which it is
               instead of leaving a blank rectangle under a heading. */
            if (_rows.Count == 0 && _host != null)
            {
                var box = _host.Bounds;
                float ic2 = Ui.S(26);
                Icons.Draw(g, _profiles.Count < 2 ? "layers" : "check-circle",
                    new RectangleF(box.X + (box.Width - ic2) / 2f, box.Y + box.Height / 2f - Ui.S(42), ic2, ic2),
                    _profiles.Count < 2 ? Theme.TextFaint : Theme.Success);
                Ui.Text(g, _profiles.Count < 2 ? "Nothing to compare yet" : "No shared environment",
                    Theme.H2, Theme.Text, box.X, box.Y + box.Height / 2f - Ui.S(6), box.Width, "center");
                Ui.Text(g, _profiles.Count < 2
                        ? "Profiles are compared once there are two of them."
                        : "No two profiles share a fingerprint, a proxy address or a browser build.",
                    Theme.Caption, Theme.TextDim, box.X, box.Y + box.Height / 2f + Ui.S(16), box.Width, "center", 2);
            }
        }
    }

    /// <summary>One finding: what it is, which profiles, and the surfaces they have in common.</summary>
    internal sealed class AuditRow : Control, IReStyle
    {
        private readonly AuditFinding _finding;
        public int MemberCount { get { return _finding.Names.Count; } }

        public AuditRow(AuditFinding finding)
        {
            _finding = finding;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            Ui.Opaque(this);
        }

        public void ReStyle() { Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.SurfaceColor(this)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Ui.S(7), Theme.Card);
            Ui.Stroke(g, r, Ui.S(7), Theme.Line, 1f);

            float ic = Ui.S(16);
            Icons.Draw(g, _finding.Icon, new RectangleF(Ui.S(14), Ui.S(16), ic, ic), _finding.Tint);

            float x = Ui.S(14) + ic + Ui.S(10);
            float tw = Math.Max(Ui.S(80), Width - x - Ui.S(14));
            Ui.Text(g, _finding.Title, Theme.BodyBold, Theme.Text, x, Ui.S(10), tw);
            Ui.Text(g, _finding.Detail ?? "", Theme.Caption, Theme.TextDim, x, Ui.S(30), tw, "left", 2);

            /* The names are the point of the row: a finding the user cannot map back to a
               profile is a warning they will close without acting on. Three is the ceiling, the
               rest are counted, so the row never grows taller than a screenful of them. */
            string names = NamesLine();
            if (names.Length > 0)
                Ui.Text(g, names, Theme.Caption, Theme.TextFaint, x, Ui.S(66), tw);
        }

        /// <summary>
        /// The height this row needs, measured rather than guessed: "shares webgl, canvas, audio
        /// and 2 more" wraps to two lines in a narrow window and to one in a wide one, and a
        /// fixed height either clips the narrow case or leaves a gap in the wide one.
        /// </summary>
        public int Measure(int width)
        {
            int textW = Math.Max(Ui.S(80), width - Ui.S(14) - Ui.S(16) - Ui.S(10) - Ui.S(14));
            int detailH = MeasureLines(_finding.Detail, Theme.Caption, textW, 2);
            int total = Ui.S(30) + detailH + Ui.S(6);
            if (NamesLine().Length > 0) total += Ui.S(18);
            return total + Ui.S(14);
        }

        private static int MeasureLines(string text, Font font, int width, int maxLines)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return Ui.S(16);
            try
            {
                // 4000 is the same ceiling the rest of the app uses: MeasureText with an
                // int.MaxValue height has no documented behaviour and wraps differently.
                Size proposed = TextRenderer.MeasureText(text, font, new Size(width, 4000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                int lineH = TextRenderer.MeasureText("Xg", font).Height + Ui.S(2);
                int lines = Math.Max(1, Math.Min(maxLines, (int)Math.Ceiling(proposed.Height / (double)lineH)));
                return lines * lineH;
            }
            catch { return Ui.S(16); }
        }

        private string NamesLine()
        {
            if (_finding.Names.Count == 0) return "";
            var shown = new List<string>();
            for (int i = 0; i < _finding.Names.Count && i < 3; i++)
            {
                string n = (_finding.Names[i] ?? "").Trim();
                if (n.Length == 0) n = "Untitled";
                shown.Add(n);
            }
            string line = string.Join(", ", shown.ToArray());
            if (_finding.Names.Count > 3) line += " and " + (_finding.Names.Count - 3) + " more";
            return line;
        }
    }
}
