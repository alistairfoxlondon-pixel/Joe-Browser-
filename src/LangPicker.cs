using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace JoeBrowser
{
    public sealed class LangItem
    {
        public string Key;
        public string Flag;
        public string Title;
        public string Subtitle;
        public LangItem(string key, string flag, string title, string subtitle)
        {
            Key = key; Flag = flag; Title = title; Subtitle = subtitle;
        }
    }

    /// <summary>
    /// A language picker that shows a circular country flag and the full language name.
    /// Locale codes are never used as the visible label - they only appear as a quiet
    /// secondary line. The popup is searchable, so a long language list stays usable.
    /// </summary>
    internal sealed class JBLangPicker : Control, IReStyle
    {
        private readonly List<LangItem> _items = new List<LangItem>();
        private int _selected = -1;
        private bool _hover, _open;
        private Popup _popup;

        public event EventHandler SelectedIndexChanged;

        public JBLangPicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            Height = Ui.S(36);
            TabStop = true;
            Ui.Opaque(this);
            Disposed += delegate { ClosePopup(); };
        }

        public IList<LangItem> Items { get { return _items; } }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < -1 || value >= _items.Count) value = -1;
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string Value
        {
            get { return _selected >= 0 && _selected < _items.Count ? _items[_selected].Key : ""; }
            set
            {
                for (int i = 0; i < _items.Count; i++)
                    if (string.Equals(_items[i].Key, value, StringComparison.OrdinalIgnoreCase)) { SelectedIndex = i; return; }
                SelectedIndex = -1;
            }
        }

        public void SetItems(IEnumerable<LangItem> items, string selectedKey)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _selected = -1;
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Key, selectedKey, StringComparison.OrdinalIgnoreCase)) { _selected = i; break; }
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Enter || k == Keys.Space || k == Keys.Down || k == Keys.Up || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OpenPopup(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                if (_items.Count > 0)
                    SelectedIndex = Math.Max(0, Math.Min(_items.Count - 1, _selected + (e.KeyCode == Keys.Down ? 1 : -1)));
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { Focus(); if (_open) ClosePopup(); else OpenPopup(); }
        }

        private void OpenPopup()
        {
            if (_open || _items.Count == 0) return;
            _open = true;
            var topLeft = PointToScreen(new Point(0, Height + Ui.S(4)));
            _popup = new Popup(this, topLeft, _selected);
            _popup.FormClosed += delegate { _open = false; _popup = null; Invalidate(); };
            _popup.Show(this);
            Invalidate();
        }

        private void ClosePopup()
        {
            if (_popup == null) return;
            var p = _popup;
            _popup = null;
            try { p.Close(); } catch { }
            try { p.Dispose(); } catch { }
            _open = false;
            Invalidate();
        }

        public void ChooseIndex(int realIndex)
        {
            SelectedIndex = realIndex;
            ClosePopup();
            Focus();
        }

        public void ReStyle() { Font = Theme.Body; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Ui.S(4);
            Ui.Fill(g, r, rad, Theme.Input);
            Ui.Stroke(g, r, rad, Theme.Line, 1f);
            using (var pen = new Pen(_open || Focused ? Theme.Accent : Theme.InputUnderline, Ui.Hair))
                g.DrawLine(pen, Ui.S(5), Height - 1.5f, Width - Ui.S(5), Height - 1.5f);

            LangItem item = _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;
            float x = Ui.S(10);
            float d = Ui.S(20);
            if (item != null)
            {
                Flags.Draw(g, item.Flag, new RectangleF(x, (Height - d) / 2f, d, d));
                x += d + Ui.S(10);
            }
            else
            {
                float ic = Ui.S(16);
                Icons.Draw(g, "globe", new RectangleF(x, (Height - ic) / 2f, ic, ic), Theme.TextFaint);
                x += ic + Ui.S(10);
            }

            string title = item != null ? item.Title : "Select a language";
            Color fg = item != null ? Theme.Text : Theme.TextFaint;
            Ui.Text(g, title, Font, fg, x, (Height - Font.GetHeight(g)) / 2f, Math.Max(Ui.S(20), Width - x - Ui.S(46)));
            if (item != null && !string.IsNullOrEmpty(item.Subtitle))
            {
                float tw;
                using (var tmp = CreateGraphics()) tw = Ui.Measure(tmp, title, Font).Width;
                float remain = Width - x - tw - Ui.S(46);
                if (remain > Ui.S(60))
                    Ui.Text(g, item.Subtitle, Theme.Caption, Theme.TextFaint,
                        x + tw + Ui.S(8), (Height - Theme.Caption.GetHeight(g)) / 2f, remain);
            }

            float c = Ui.S(16);
            Icons.Draw(g, _open ? "chevron-up" : "chevron-down",
                new RectangleF(Width - c - Ui.S(10), (Height - c) / 2f, c, c),
                _open || _hover ? Theme.Text : Theme.TextDim);
            if (Ui.FocusCues(this)) Ui.FocusRing(g, r, rad);
        }

        /// <summary>Searchable owner-drawn popup. Kept as a nested class so the picker owns it.</summary>
        private sealed class Popup : Form
        {
            private readonly JBLangPicker _owner;
            private readonly List<int> _visible = new List<int>();
            private readonly TextBox _search = new TextBox();
            private readonly Panel _list = new Panel();
            private int _hi = -1;
            private int _scroll;

            public Popup(JBLangPicker owner, Point screenPos, int selected)
            {
                _owner = owner;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Theme.Panel;
                DoubleBuffered = true;
                KeyPreview = true;
                MinimumSize = new Size(Ui.S(180), Ui.S(80));
                Font = Theme.Body;

                int rowH = Ui.S(38);
                int width = Math.Max(_owner.Width, Ui.S(300));
                var area = Screen.FromPoint(screenPos).WorkingArea;

                /* Prefer dropping down. A dropdown that flips up covers the settings card
                   above the field, which reads as an overlap even though it is on-screen.
                   Cap the height to the space below instead; the list scrolls internally. */
                int chrome = Ui.S(46) + Ui.S(8);
                int desired = chrome + Math.Min(9, _owner._items.Count) * rowH;
                int below = area.Bottom - screenPos.Y - Ui.S(6);
                int above = screenPos.Y - _owner.Height - Ui.S(8) - area.Top;
                int minimum = chrome + 4 * rowH;

                int height;
                if (below >= minimum)
                    height = Math.Min(desired, below);
                else if (above > below)
                {
                    height = Math.Max(Ui.S(150), Math.Min(desired, above));
                    screenPos.Y = Math.Max(area.Top, screenPos.Y - _owner.Height - Ui.S(8) - height);
                }
                else
                    height = Math.Max(Ui.S(150), below);

                if (screenPos.X + width > area.Right) screenPos.X = Math.Max(area.Left, area.Right - width - Ui.S(6));
                Location = screenPos;
                ClientSize = new Size(width, height);

                _search.SetBounds(Ui.S(10), Ui.S(8), width - Ui.S(20), Ui.S(30));
                _search.Font = Theme.Body;
                _search.BackColor = Theme.Input;
                _search.ForeColor = Theme.Text;
                _search.BorderStyle = BorderStyle.FixedSingle;
                _search.PlaceholderTextCompat("Search language or country\u2026");
                _search.TextChanged += delegate { Filter(); };
                _search.KeyDown += SearchKeyDown;
                Controls.Add(_search);

                _list.SetBounds(Ui.S(4), Ui.S(44), width - Ui.S(8), height - Ui.S(48));
                _list.BackColor = Theme.Panel;
                _list.Paint += ListPaint;
                _list.MouseMove += ListMouseMove;
                _list.MouseDown += ListMouseDown;
                _list.MouseWheel += ListMouseWheel;
                Controls.Add(_list);

                Filter();
                for (int i = 0; i < _visible.Count; i++) if (_visible[i] == selected) { _hi = i; break; }
                if (_hi < 0) _hi = _visible.Count > 0 ? 0 : -1;
                Deactivate += delegate
                {
                    if (!_cancel) ClosePopup();
                };
            }

            private int RowHeight { get { return Ui.S(38); } }

            private void ClosePopup()
            {
                try { if (!IsDisposed) Close(); } catch { }
            }

            /// <summary>Closes and puts the caret back on the field the popup belongs to.</summary>
            private void Cancel()
            {
                _cancel = true;
                ClosePopup();
                try { if (_owner != null && _owner.Visible) _owner.Focus(); } catch { }
            }

            /* Set when the popup is dismissed rather than committed: moving focus back to the
               picker inside Deactivate is what made "click another field" jump back here. */
            private bool _cancel;

            private void Filter()
            {
                string q = _search.Text.Trim().ToLowerInvariant();
                _visible.Clear();
                for (int i = 0; i < _owner._items.Count; i++)
                {
                    var it = _owner._items[i];
                    if (q.Length == 0 ||
                        (it.Title ?? "").ToLowerInvariant().Contains(q) ||
                        (it.Subtitle ?? "").ToLowerInvariant().Contains(q) ||
                        (it.Key ?? "").ToLowerInvariant().Contains(q) ||
                        (it.Flag ?? "").ToLowerInvariant().Contains(q))
                        _visible.Add(i);
                }
                _scroll = 0;
                _hi = _visible.Count > 0 ? 0 : -1;
                _list.Invalidate();
            }

            private void SearchKeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Cancel(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { Pick(_hi); e.Handled = true; }
                else if (e.KeyCode == Keys.Down) { MoveHighlight(1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up) { MoveHighlight(-1); e.Handled = true; }
                else if (e.KeyCode == Keys.PageDown) { MoveHighlight(5); e.Handled = true; }
                else if (e.KeyCode == Keys.PageUp) { MoveHighlight(-5); e.Handled = true; }
            }

            private void MoveHighlight(int delta)
            {
                if (_visible.Count == 0) return;
                _hi = Math.Max(0, Math.Min(_visible.Count - 1, (_hi < 0 ? 0 : _hi) + delta));
                int maxScroll = Math.Max(0, _visible.Count - VisibleRows);
                if (_hi < _scroll) _scroll = _hi;
                if (_hi >= _scroll + VisibleRows) _scroll = Math.Min(maxScroll, _hi - VisibleRows + 1);
                _list.Invalidate();
            }

            private int VisibleRows { get { return Math.Max(1, _list.Height / RowHeight); } }

            private void Pick(int hi)
            {
                if (hi < 0 || hi >= _visible.Count) return;
                _owner.ChooseIndex(_visible[hi]);
            }

            private void ListMouseMove(object sender, MouseEventArgs e)
            {
                int i = _scroll + e.Y / RowHeight;
                if (i >= 0 && i < _visible.Count && i != _hi) { _hi = i; _list.Invalidate(); }
            }

            private void ListMouseDown(object sender, MouseEventArgs e)
            {
                int i = _scroll + e.Y / RowHeight;
                if (i >= 0 && i < _visible.Count) Pick(i);
            }

            private void ListMouseWheel(object sender, MouseEventArgs e)
            {
                int max = Math.Max(0, _visible.Count - VisibleRows);
                _scroll = Math.Max(0, Math.Min(max, _scroll - (e.Delta > 0 ? 2 : -2)));
                _list.Invalidate();
            }

            private void ListPaint(object sender, PaintEventArgs e)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                g.Clear(Theme.Panel);
                int w = _list.Width;
                for (int row = 0; row < VisibleRows; row++)
                {
                    int idx = _scroll + row;
                    if (idx >= _visible.Count) break;
                    var it = _owner._items[_visible[idx]];
                    float y = row * RowHeight;
                    var cell = new RectangleF(Ui.S(3), y + Ui.S(1), w - Ui.S(6), RowHeight - Ui.S(2));
                    if (idx == _hi) Ui.Fill(g, cell, Ui.S(4), Theme.HoverOverlay);

                    float d = Ui.S(22);
                    Flags.Draw(g, it.Flag, new RectangleF(Ui.S(12), y + (RowHeight - d) / 2f, d, d));
                    float tx = Ui.S(12) + d + Ui.S(12);
                    Font f = _visible[idx] == _owner._selected ? Theme.BodyBold : Theme.Body;
                    Ui.Text(g, it.Title, f, Theme.Text, tx, y + Ui.S(4), w - tx - Ui.S(16));
                    /* Never fall back to the raw code ("en", "de-DE"): the user explicitly
                       rejected bare codes. A name with no quiet subtitle is enough. */
                    if (!string.IsNullOrEmpty(it.Subtitle))
                        Ui.Text(g, it.Subtitle, Theme.Caption, Theme.TextFaint, tx, y + Ui.S(21), w - tx - Ui.S(16));
                    if (_visible[idx] == _owner._selected)
                    {
                        float c = Ui.S(14);
                        Icons.Draw(g, "check", new RectangleF(w - c - Ui.S(12), y + (RowHeight - c) / 2f, c, c), Theme.Accent);
                    }
                }
                if (_visible.Count > VisibleRows)
                {
                    float track = _list.Height;
                    float thumb = Math.Max(Ui.S(24), track * VisibleRows / (float)_visible.Count);
                    float pos = (track - thumb) * (_scroll / (float)Math.Max(1, _visible.Count - VisibleRows));
                    Ui.Fill(g, new RectangleF(w - Ui.S(7), pos, Ui.S(4), thumb), Ui.S(2), Theme.Line);
                }

                /* A search that matches nothing used to leave a blank white box with no
                   explanation, which read as a broken picker rather than "no such language". */
                if (_visible.Count == 0)
                {
                    string none = string.IsNullOrWhiteSpace(_search.Text)
                        ? "No languages available."
                        : "No language matches \u201c" + _search.Text.Trim() + "\u201d.";
                    Ui.Text(g, none, Theme.Body, Theme.TextFaint, 0,
                        (_list.Height - Theme.Body.GetHeight(g)) / 2f, w, "center");
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape) { Cancel(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _search.Focus();
            }
        }
    }

    internal static class TextBoxPlaceholderCompat
    {
        /// <summary>EM_SETCUEBANNER works on .NET Framework 4.8 without a custom paint path.</summary>
        public static void PlaceholderTextCompat(this TextBox box, string text)
        {
            try { Native.SendMessage(box.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, text); }
            catch { }
        }
    }
}
