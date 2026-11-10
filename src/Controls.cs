using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal enum BtnKind { Primary, Ghost, Soft, Danger, Nav }
    internal enum ToastKind { Success, Info, Warn, Error }
    internal enum LabelRole { Title, Body, Dim, Faint, Accent, Strong, Mono, Section, Caption }

    /// <summary>
    /// A tiny shared tween engine. The interface used to answer every interaction with an
    /// instant colour swap - technically responsive, but it reads as a flip chart, not as an
    /// app: nothing moves, so nothing feels alive. Every hover, toggle, toast and scrollbar
    /// now eases between its states through this one clock: one Timer drives all the running
    /// animations, each target gets a 0-1 progress with an ease-out curve, and an animation
    /// that finishes removes itself, so the idle app costs literally nothing.
    /// </summary>
    internal static class UiAnim
    {
        private sealed class Item
        {
            public object Key;
            public Action<float> Step;
            public float T;
            public float Speed;
        }

        private static readonly List<Item> _items = new List<Item>();
        private static Timer _timer;
        private static readonly object _gate = new object();

        private static Timer Clock
        {
            get
            {
                if (_timer == null)
                {
                    _timer = new Timer { Interval = 16 };
                    _timer.Tick += Tick;
                }
                return _timer;
            }
        }

        /// <summary>
        /// Runs <paramref name="step"/> once per frame with an eased 0-1 progress. Re-animating
        /// the same <paramref name="key"/> replaces the running animation instead of stacking
        /// two; the key is whatever the caller owns (a control, an AnimValue instance).
        /// </summary>
        public static void Animate(object key, Action<float> step, float speed)
        {
            if (key == null) key = new object();
            lock (_gate)
            {
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].Key == key) { _items.RemoveAt(i); break; }
                _items.Add(new Item { Key = key, Step = step, T = 0f, Speed = Math.Max(0.02f, speed) });
                Clock.Start();
            }
        }

        /// <summary>Cancels every animation registered under <paramref name="key"/>.</summary>
        public static void Cancel(object key)
        {
            if (key == null) return;
            lock (_gate)
            {
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].Key == key) { _items.RemoveAt(i); break; }
            }
        }

        private static void Tick(object sender, EventArgs e)
        {
            List<Item> run;
            lock (_gate)
            {
                if (_items.Count == 0) { _timer.Stop(); return; }
                run = new List<Item>(_items);
            }
            bool alive = false;
            foreach (var it in run)
            {
                it.T += it.Speed;
                float t = Math.Min(1f, it.T);
                /* Ease-out cubic: fast at the start where the eye wants the answer, gentle at
                   the end where it reads as "settled" rather than "slammed". */
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                try { it.Step(eased); }
                catch
                {
                    /* A step that throws every frame used to stay in the list forever: the
                       catch skipped the removal, the 16 ms clock never stopped, and the app
                       burned idle CPU on an exception loop. One failure and the animation is
                       out. */
                    lock (_gate) { _items.Remove(it); }
                    continue;
                }
                if (t < 1f) alive = true;
                else lock (_gate) { _items.Remove(it); }
            }
            if (!alive)
            {
                lock (_gate)
                {
                    if (_items.Count == 0 && _timer != null) _timer.Stop();
                }
            }
        }
    }

    /// <summary>A 0-1 animated value a control can blend its colours against.</summary>
    internal sealed class AnimValue
    {
        private readonly Control _owner;
        private float _value;
        private readonly object _key;

        public float Value { get { return _value; } }

        public AnimValue(Control owner)
        {
            _owner = owner;
            _value = 0f;
            _key = new object();
        }

        /// <summary>Drives the value toward 1 (shown) or 0 (hidden) with an ease-out.</summary>
        public void Toward(bool shown, float speed)
        {
            if (_owner == null || _owner.IsDisposed) { _value = shown ? 1f : 0f; return; }
            if (shown && _value >= 1f) { _value = 1f; return; }
            if (!shown && _value <= 0f) { _value = 0f; return; }
            float from = _value;
            var owner = _owner;
            var key = _key;
            UiAnim.Animate(key, t =>
            {
                _value = from + (shown ? 1f - from : -from) * t;
                try { owner.Invalidate(); } catch { }
            }, speed);
        }

        /// <summary>Sets the value instantly, no animation. Used when a control is rebuilt.</summary>
        public void Set(float v) { _value = Math.Max(0f, Math.Min(1f, v)); }
    }

    /// <summary>Label that remembers its role so a theme flip restores the right contrast.</summary>
    internal sealed class JBLabel : Label, IReStyle
    {
        public LabelRole Role = LabelRole.Body;

        /// <summary>
        /// A label whose text can be selected and copied. A plain WinForms label cannot: the
        /// user can see an error message or a path and has no way to get it out of the window,
        /// which is exactly when they need it - to paste into a report. Turning the label into a
        /// read only single line text box would lose the ellipsis and the themed font, so the
        /// paint is done here instead: the same native text drawing, plus a selection that
        /// follows a mouse drag and a Ctrl+C that copies it. Ctrl+A selects everything.
        /// </summary>
        private bool _selectable;

        public bool Selectable
        {
            get { return _selectable; }
            set
            {
                _selectable = value;
                /* A label is not focusable by default, and a key handler only runs on the
                   focused control, so Ctrl+C needs this to work at all. */
                SetStyle(ControlStyles.Selectable, value);
                TabStop = value;
                if (value) Cursor = Cursors.IBeam;
            }
        }

        private int _selStart = -1, _selEnd = -1;
        private bool _dragging;
        private string _dragText;
        private Rectangle _dragBounds;

        public string SelectedText
        {
            get
            {
                if (_selStart < 0 || _selEnd < 0 || Text == null) return "";
                int a = Math.Min(_selStart, _selEnd), b = Math.Max(_selStart, _selEnd);
                if (a >= Text.Length) return "";
                if (b > Text.Length) b = Text.Length;
                return Text.Substring(a, b - a);
            }
        }

        public void ClearSelection() { _selStart = _selEnd = -1; Invalidate(); }

        public JBLabel()
        {
            AutoSize = false;
            AutoEllipsis = true;
            Ui.Opaque(this);
            Apply();
        }

        public JBLabel(LabelRole role, string text) : this()
        {
            Role = role;
            Text = text;
            Apply();
        }

        public void Apply()
        {
            switch (Role)
            {
                case LabelRole.Title: Font = Theme.Display; ForeColor = Theme.Text; break;
                case LabelRole.Strong: Font = Theme.BodyBold; ForeColor = Theme.Text; break;
                case LabelRole.Body: Font = Theme.Small; ForeColor = Theme.TextDim; break;
                case LabelRole.Dim: Font = Theme.Small; ForeColor = Theme.TextFaint; break;
                case LabelRole.Faint: Font = Theme.Caption; ForeColor = Theme.TextFaint; break;
                case LabelRole.Accent: Font = Theme.SmallBold; ForeColor = Theme.TextDim; break;
                /* Row titles and the eyebrow above a group are two roles the pages need and
                   the list had no name for, so every page painted them by hand with its own
                   size. Named here they cannot drift apart. */
                case LabelRole.Section: Font = Theme.H2; ForeColor = Theme.Text; break;
                case LabelRole.Caption: Font = Theme.Caption; ForeColor = Theme.TextFaint; break;
                case LabelRole.Mono: Font = Theme.MonoBody; ForeColor = Theme.Text; break;
            }
            Invalidate();
        }

        public void ReStyle() { Apply(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Selectable) return;
            _dragging = true;
            _dragText = Text;
            _dragBounds = ClientRectangle;
            _selStart = _selEnd = IndexAt(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Selectable || !_dragging) return;
            int i = IndexAt(e.X);
            if (i != _selEnd) { _selEnd = i; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Selectable) return;
            _dragging = false;
            if (_selStart == _selEnd) { _selStart = _selEnd = -1; Invalidate(); }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (Selectable && (keyData == Keys.Left || keyData == Keys.Right)) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Selectable && e.Control && e.KeyCode == Keys.A)
            {
                _selStart = 0; _selEnd = Text == null ? 0 : Text.Length;
                Invalidate(); e.Handled = true; return;
            }
            if (Selectable && e.Control && e.KeyCode == Keys.C)
            {
                string t = SelectedText;
                if (!string.IsNullOrEmpty(t)) { try { Clipboard.SetText(t); } catch { } }
                e.Handled = true; return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Selectable || Text == null || _selStart < 0 || _selEnd < 0) return;
            int a = Math.Min(_selStart, _selEnd), b = Math.Max(_selStart, _selEnd);
            if (a == b) return;
            /* The highlight is measured with the same font the label draws with, so it lands on
               the glyphs rather than near them. AutoEllipsis means the visible tail may be cut;
               the selection is clamped to what is drawn. */
            string shown = Text;
            Size proposed = TextRenderer.MeasureText(shown, Font, new Size(int.MaxValue, Height),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (proposed.Width > Width && Width > 0)
            {
                while (shown.Length > 0 && TextRenderer.MeasureText(shown + "…", Font).Width > Width)
                    shown = shown.Substring(0, shown.Length - 1);
                if (b > shown.Length) b = shown.Length;
                if (a >= b) return;
            }
            int x1 = TextRenderer.MeasureText(shown.Substring(0, Math.Min(a, shown.Length)), Font,
                new Size(int.MaxValue, Height), TextFormatFlags.NoPadding).Width;
            int x2 = TextRenderer.MeasureText(shown.Substring(0, Math.Min(b, shown.Length)), Font,
                new Size(int.MaxValue, Height), TextFormatFlags.NoPadding).Width;
            int y = (Height - Font.Height) / 2;
            var fill = new Rectangle(x1 + Padding.Left, y, Math.Max(1, x2 - x1), Font.Height);
            using (var b2 = new SolidBrush(Theme.SelOverlay)) e.Graphics.FillRectangle(b2, fill);
        }

        /// <summary>Character index nearest a client X, for turning a drag into a range.</summary>
        private int IndexAt(int x)
        {
            if (Text == null) return 0;
            int best = 0, bestDist = int.MaxValue;
            for (int i = 0; i <= Text.Length; i++)
            {
                int w = TextRenderer.MeasureText(Text.Substring(0, i), Font,
                    new Size(int.MaxValue, Height), TextFormatFlags.NoPadding).Width;
                int d = Math.Abs(w - x);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>Wrap to several lines instead of trimming the tail with an ellipsis.</summary>
        public bool Wrap
        {
            set { AutoEllipsis = !value; Invalidate(); }
        }
    }

    /// <summary>Shared tooltip so every hover hint costs one component, not one per control.</summary>
    internal static class ToolTips
    {
        private static readonly ToolTip _tip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 420,
            ReshowDelay = 120,
            AutoPopDelay = 7000
        };

        /// <summary>
        /// Tooltips redraw in the system's own colours, so in Night Mode they appeared as a pale
        /// yellow rectangle with black text - the one surface in the whole app that ignored the
        /// theme, and the thing that looked most broken about a dark window. Drawing them here
        /// keeps them in the palette: a card surface, the hairline, and the app's own type.
        /// </summary>
        static ToolTips()
        {
            _tip.OwnerDraw = true;
            _tip.Popup += delegate(object sender, PopupEventArgs e)
            {
                /* A tooltip must never be able to stop the app: this runs on the UI thread while
                   the pointer is moving, and an exception here would surface as a crash while
                   hovering a button. */
                try
                {
                    if (e.AssociatedControl == null || !e.AssociatedControl.IsHandleCreated) return;
                    string text = _tip.GetToolTip(e.AssociatedControl);
                    using (var g = e.AssociatedControl.CreateGraphics())
                    {
                        Size size = TextRenderer.MeasureText(g, text ?? "", Theme.Body,
                            new Size(Ui.S(360), int.MaxValue),
                            TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                        e.ToolTipSize = new Size(size.Width + Ui.S(20), size.Height + Ui.S(14));
                    }
                }
                catch { }
            };
            _tip.Draw += delegate(object sender, DrawToolTipEventArgs e)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                var box = new RectangleF(0.5f, 0.5f, e.Bounds.Width - 1.5f, e.Bounds.Height - 1.5f);
                Ui.Fill(g, box, Ui.S(Theme.CardRadius), Theme.Card);
                Ui.Stroke(g, box, Ui.S(Theme.CardRadius), Theme.Line);
                TextRenderer.DrawText(g, e.ToolTipText ?? "", Theme.Body,
                    new Rectangle(e.Bounds.X + Ui.S(10), e.Bounds.Y + Ui.S(7),
                        e.Bounds.Width - Ui.S(20), e.Bounds.Height - Ui.S(14)),
                    Theme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.Left);
            };
        }

        public static void Attach(Control owner, string text)
        {
            if (owner == null || string.IsNullOrEmpty(text)) return;
            _tip.SetToolTip(owner, text);
        }

        /// <summary>
        /// Removes a hint. Needed when the same control is reused for a different meaning - a
        /// profile card that no longer collides must stop claiming it does, and a stale tooltip
        /// is worse than none because it is the one a user reads while deciding what to delete.
        /// </summary>
        public static void Clear(Control owner)
        {
            if (owner == null) return;
            _tip.SetToolTip(owner, string.Empty);
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// One-call full compose: invalidate everything (children included), then paint the
        /// whole tree synchronously before the call returns. Used after WM_SETREDRAW unfreezes
        /// a page switch, so the first frame the user sees is the finished one - not the old
        /// header over the new page for a message-loop turn, which is the reported flash.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_NOERASE = 0x0020;
        public const uint RDW_ALLCHILDREN = 0x0080;
        public const uint RDW_UPDATENOW = 0x0100;

        public static void ComposeNow(IntPtr hWnd)
        {
            try
            {
                RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_NOERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
            catch { }
        }


        public const int WM_SETICON = 0x0080;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const int EM_SETCUEBANNER = 0x1501;
        public const int EM_LINESCROLL = 0x00B6;
        public const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        public const int EM_GETLINECOUNT = 0x00BA;
        public const int WM_QUERYUISTATE = 0x0129;
        // Set in the answer to WM_QUERYUISTATE when the keyboard cues are hidden, which is
        // exactly when Control.ShowFocusCues reports false.
        public const int UISF_HIDEFOCUS = 0x0001;

        public static void RoundedCorners(IntPtr hwnd, int radius)
        {
            // Win11 rounds borderless windows only when asked; older builds ignore the call.
            try
            {
                int pref = radius > 0 ? 2 : 1;
                DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
            }
            catch { }
        }

        public static void WindowBorderColor(IntPtr hwnd, Color c)
        {
            try
            {
                int rgb = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, 34, ref rgb, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// Asks DWM to draw the non-client area dark. Without this a themed dialog keeps the
        /// default white caption bar above a dark client area, which is the single most
        /// "this app is broken" looking thing in the whole product.
        /// </summary>
        public static void ImmersiveDarkMode(IntPtr hwnd, bool dark)
        {
            try
            {
                int on = dark ? 1 : 0;
                // 20 on Windows 10 2004 and later; 19 on the builds before the attribute moved.
                if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
            catch { }
        }

        public static void DarkScroll(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }
    }

    /// <summary>Borderless window with rounded corners, edge resizing and caption dragging.</summary>
    internal class JBForm : Form, IReStyle
    {
        public bool Resizable = true;
        public bool AllowMaximize = true;
        public bool DraggableSidebar = false;
        public int HeaderHit = 48;

        private bool _maximized;
        private Rectangle _restoreBounds = Rectangle.Empty;

        private readonly EventHandler _onModeChanged;

        /// <summary>
        /// Applies a theme change to this window in one step: repainting is stopped over the
        /// whole window, every control is re-styled, the layout is redone and one paint follows.
        /// Controls that invalidate themselves during the walk are therefore not painted against
        /// a palette the next control is about to change - which is what the flash was.
        /// </summary>
        private void ApplyModeChange()
        {
            bool frozen = false;
            try
            {
                if (IsHandleCreated)
                {
                    try { Native.SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); frozen = true; } catch { }
                }
                ReStyle();
                Theme.Restyle(this);
            }
            finally
            {
                if (frozen && IsHandleCreated)
                {
                    try { Native.SendMessage(Handle, 0x000B, (IntPtr)1, IntPtr.Zero); } catch { }
                }
                /* The caption bar and the scrollbars of a themed dialog are Windows' own, so the
                   DWM attributes have to be re-applied on a mode change or the window keeps a dark
                   border and a light body until it is reopened. */
                if (IsHandleCreated)
                {
                    try { Native.WindowBorderColor(Handle, Theme.Line); } catch { }
                    try { Native.ImmersiveDarkMode(Handle, Theme.Dark); } catch { }
                }
                /* ComposeNow is Update() plus the children: RDW_ALLCHILDREN | RDW_UPDATENOW
                   paints the whole tree synchronously, so the first frame after the switch is
                   the finished one. Invalidate(true) + Update() only synchronized the form's
                   own surface - child windows kept their old-palette invalid regions and
                   painted them on the next message-loop turns, which was the flicker. */
                if (IsHandleCreated) Native.ComposeNow(Handle);
            }
        }

        public JBForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            /* Without this every window inherits the generic Windows application icon, so the
               taskbar button and Alt-Tab show a blank placeholder instead of the Joe mark. */
            Icon = AppIcon.Window;
            ShowIcon = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _onModeChanged = delegate { ApplyModeChange(); };
            Theme.ModeChanged += _onModeChanged;
            Disposed += delegate { Theme.ModeChanged -= _onModeChanged; };
        }

        public bool Maximized { get { return _maximized; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundedCorners(Handle, Theme.Radius);
            Native.WindowBorderColor(Handle, Theme.Line);
            Native.ImmersiveDarkMode(Handle, Theme.Dark);
            ApplyWindowIcons();
        }

        /// <summary>
        /// WinForms sets one icon and lets Windows shrink it for every surface, so the 16 px
        /// taskbar button was a resampled 32 px frame - soft, and on some machines washed out
        /// to almost nothing. Handing the window the exact 16 px and 32 px frames keeps both
        /// crisp. The icons are the app's own cached instances, so nothing is disposed here.
        /// </summary>
        private void ApplyWindowIcons()
        {
            try
            {
                if (Icon != null) Native.SendMessage(Handle, Native.WM_SETICON, (IntPtr)Native.ICON_BIG, Icon.Handle);
                Icon small = AppIcon.Small;
                if (small != null) Native.SendMessage(Handle, Native.WM_SETICON, (IntPtr)Native.ICON_SMALL, small.Handle);
            }
            catch { }
        }

        public void ToggleMaximize()
        {
            if (!AllowMaximize) return;
            if (_maximized)
            {
                if (_restoreBounds.IsEmpty) _restoreBounds = new Rectangle(0, 0, Ui.S(1240), Ui.S(800));
                Bounds = _restoreBounds;
                _maximized = false;
            }
            else
            {
                Maximize();
            }
            OnSurfaceChanged();
            Invalidate();
        }

        /// <summary>Fill the working area, remembering the current bounds as the restore size.</summary>
        public void Maximize()
        {
            if (_maximized) return;
            if (!Bounds.IsEmpty) _restoreBounds = Bounds;
            Bounds = Screen.FromControl(this).WorkingArea;
            _maximized = true;
        }

        /// <summary>Return to the remembered window bounds, or a large default when none exist.</summary>
        public void RestoreTo(Rectangle bounds)
        {
            _restoreBounds = bounds;
            if (_maximized)
            {
                Bounds = bounds;
                _maximized = false;
            }
        }

        /// <summary>The size the window returns to when the maximize button is pressed again.</summary>
        /// <summary>Bounds the window had before it was filled; WindowState never leaves Normal here.</summary>
        public Rectangle LastNormalBounds { get { return _restoreBounds; } }

        /// <summary>Keeps the window inside the monitor it is on, at any DPI.</summary>
        protected void FitToWorkArea()
        {
            var area = Screen.FromControl(this).WorkingArea;
            Size max = new Size(area.Width - Ui.S(16), area.Height - Ui.S(16));
            Size want = new Size(Math.Min(ClientSize.Width, max.Width), Math.Min(ClientSize.Height, max.Height));
            if (want != ClientSize) ClientSize = want;
            if (Left < area.Left) Left = area.Left;
            if (Top < area.Top) Top = area.Top;
        }

        protected virtual void OnSurfaceChanged() { }

        /// <summary>
        /// The colour a child control should clear itself with at this client point. A shell that
        /// paints regions the form's BackColor does not cover (a tinted navbar) overrides this,
        /// otherwise children over those regions punch opaque rectangles into them.
        /// </summary>
        public virtual Color SurfaceAt(Point clientLocation) { return BackColor; }

        /// <summary>
        /// Every borderless window re-runs its layout when it moves to a different-DPI monitor.
        /// Without this, fonts grow through point units while Ui.Scale-derived bounds do not.
        /// </summary>
        protected virtual void OnScaleChanged() { }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            float scale = Math.Max(1f, e.DeviceDpiNew / 96f);
            /* Text is now measured in device units (see Theme.F), so a window that moved to a
               different monitor has to be laid out again with its new scale or every measured
               width would still describe the old screen. */
            scale = (float)(Math.Round(scale * 4f) / 4f);
            if (Math.Abs(scale - Ui.Scale) > 0.001f)
            {
                Theme.ClearFontCache();
                Ui.Scale = scale;
            }
            FitToWorkArea();
            OnScaleChanged();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!_suppressSurface) OnSurfaceChanged();
        }

        private bool _suppressSurface;
        protected void SuppressSurfaceEvents(Action a)
        {
            _suppressSurface = true;
            try { a(); } finally { _suppressSurface = false; }
        }

        /// <summary>
        /// Tracks whether the user is on the keyboard, so a focus indicator is drawn even on
        /// systems whose ShowFocusCues flag is stuck false (touch keyboard, tablet posture,
        /// some remote sessions). See Ui.FocusCues. Every window derives from JBForm, and
        /// ProcessCmdKey bubbles up from the focused control, so this one hook covers all of
        /// them without touching each dialog.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Ui.NoteKey(keyData);
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            /* Pointer input means the standard Windows behaviour: no focus rectangles until a
               key is used again. */
            Ui.KeyboardCuesForced = false;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDBLCLK = 0xA3;
            const int HTCLIENT = 1, HTCAPTION = 2;
            const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                      HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_NCLBUTTONDBLCLK && AllowMaximize) { ToggleMaximize(); return; }

            if (m.Msg == WM_NCHITTEST && Resizable && !_maximized)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int lp = m.LParam.ToInt32();
                    var screen = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
                    var p = PointToClient(screen);
                    int edge = Math.Max(5, Ui.S(7));
                    bool left = p.X <= edge, right = p.X >= ClientSize.Width - edge;
                    bool top = p.Y <= edge, bottom = p.Y >= ClientSize.Height - edge;

                    if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                    else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                    else if (InDragBand(p) && IsQuietAt(p)) m.Result = (IntPtr)HTCAPTION;
                    return;
                }
                return;
            }
            base.WndProc(ref m);
        }

        private bool InDragBand(Point p)
        {
            return p.Y < Ui.S(HeaderHit) || (DraggableSidebar && p.X < Ui.S(244));
        }

        /// <summary>
        /// Drags may only start on inert surfaces. A button, field or card that happens to sit
        /// inside the header band must still receive its mouse clicks.
        /// </summary>
        private bool IsQuietAt(Point p)
        {
            Control c;
            try { c = GetChildAtPoint(p, GetChildAtPointSkip.Invisible); }
            catch { return false; }

            while (c != null && !ReferenceEquals(c, this))
            {
                if (!(c is JBPanel || c is JBLabel || c is JBScrollHost)) return false;
                c = c.Parent;
            }
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Ui.Quality(e.Graphics);
            using (var pen = new Pen(Theme.Line))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        public virtual void ReStyle()
        {
            BackColor = Theme.Bg;
            Native.WindowBorderColor(Handle, Theme.Line);
            Theme.RestyleChildren(this);
            Invalidate();
        }
    }

    /// <summary>Menu with accent-blue highlights and optional leading icons.</summary>
    internal sealed class JBMenu : ToolStripProfessionalRenderer
    {
        public JBMenu() : base(new Surface()) { RoundedEdges = false; }

        private sealed class Surface : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
            public override Color MenuItemSelected { get { return Theme.Panel2; } }
            public override Color MenuItemBorder { get { return Theme.Line; } }
            public override Color MenuBorder { get { return Theme.Line; } }
            public override Color SeparatorDark { get { return Theme.LineSoft; } }
            public override Color SeparatorLight { get { return Theme.LineSoft; } }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.Panel);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Line))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        /// <summary>Rounds the dropdown window itself, the way a Windows 11 flyout looks.</summary>
        private static void Round(ToolStrip strip)
        {
            if (strip.Width <= 0 || strip.Height <= 0) return;
            /* Square, like every other surface. A window region is also the one place a mistake is
               visible for a whole second: while a context menu is still a plain rectangle the
               default grey border shows at its edge, which is the pale line that flashed when a
               menu opened before the region was applied. The old region is disposed before it is
               replaced - WinForms does not do it, and every resize churned one native region. */
            using (var path = Ui.Round(new RectangleF(0, 0, strip.Width, strip.Height), Ui.S(Theme.ButtonRadius)))
            using (var region = new Region(path))
            {
                var old = strip.Region;
                strip.Region = region;
                if (old != null) old.Dispose();
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(Ui.S(5), Ui.S(3), e.Item.Width - Ui.S(10), e.Item.Height - Ui.S(6));
            if (e.Item.Selected) Ui.Fill(g, r, Theme.RSmall, Theme.HoverOverlay);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            e.TextFont = e.Item.Font ?? Theme.Body;

            /* Items carry a glyph name rather than a bitmap. The glyph is drawn here, from the
               text rectangle, and nowhere else: it used to be painted a second time from
               OnRenderItemImage at a fixed x, which with no image margin put the icon straight
               through the first two characters of the label. */
            var item = e.Item as IIconItem;
            if (item != null && item.Icon != null)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                float s = Ui.S(16);
                var lane = new RectangleF(e.TextRectangle.Left - s - Ui.S(9), e.Item.Height / 2f - s / 2f, s, s);
                if (lane.Left >= 0)
                    Icons.Draw(g, item.Icon, lane,
                        !e.Item.Enabled ? Theme.TextFaint : e.Item.Selected ? Theme.Accent : Theme.TextDim, 1.7f);
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            Ui.Quality(e.Graphics);
            if (e.Image != null)
            {
                /* The menu turns the framework image lane off by default (glyphs are drawn as
                   text), so a caller that does supply a bitmap - the country picker's flag chip -
                   gets no rectangle from the framework and is placed here instead. */
                var r = e.ImageRectangle;
                if (r.Width <= 0 || r.Height <= 0)
                    r = new Rectangle(Ui.S(9), (e.Item.Height - e.Image.Height) / 2, e.Image.Width, e.Image.Height);
                e.Graphics.DrawImage(e.Image, r);
                return;
            }
            /* Glyph icons are painted by OnRenderItemText, in the lane reserved by Item's left
               padding. Drawing them here as well put two glyphs in two different places. */
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Ui.Quality(e.Graphics);
            float s = Ui.S(14);
            Icons.Draw(e.Graphics, "check", new RectangleF(Ui.S(10), e.Item.Height / 2f - s / 2f, s, s), Theme.Accent, 2f);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.LineSoft))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, Ui.S(12), y, e.Item.Width - Ui.S(12), y);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        public interface IIconItem { string Icon { get; } }

        internal sealed class Item : ToolStripMenuItem, IIconItem
        {
            public string Icon { get; private set; }
            public Item(string text, string icon) : base(text)
            {
                Icon = icon;
                Font = Theme.Body;
                ForeColor = Theme.Text;
                /* Room for the 16 px glyph plus its 9 px gap, so the label never sits on it.
                   Only items with a glyph need the room; an item with a bitmap gets its own
                   image margin from the framework. */
                int left = string.IsNullOrEmpty(icon) ? Ui.S(14) : Ui.S(14 + 16 + 9);
                Padding = new Padding(left, Ui.S(7), Ui.S(18), Ui.S(7));
                AutoSize = true;
            }
        }

        public static ContextMenuStrip Create()
        {
            var m = new ContextMenuStrip();
            m.Renderer = new JBMenu();
            m.BackColor = Theme.Panel;
            m.ForeColor = Theme.Text;
            m.Font = Theme.Body;
            /* Items carry glyph names, not bitmaps, so the framework's image lane would be an
               empty gutter on every menu. It is switched on by callers that do insert flag
               bitmaps into their items (the country picker). */
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            m.Padding = new Padding(Ui.S(4), Ui.S(6), Ui.S(4), Ui.S(6));
            m.GripStyle = ToolStripGripStyle.Hidden;
            m.MinimumSize = new Size(Ui.S(196), 0);
            m.AutoClose = true;
            m.Opened += delegate { Round(m); };
            m.SizeChanged += delegate { Round(m); };
            return m;
        }

        public static Item Add(ContextMenuStrip menu, string text, string icon, EventHandler click, bool enabled = true)
        {
            var item = new Item(text, icon);
            item.Enabled = enabled;
            if (click != null) item.Click += click;
            menu.Items.Add(item);
            return item;
        }

        /// <summary>Shows the menu so it always stays on screen, flipping up when it would run off the bottom.</summary>
        public static void ShowNear(ContextMenuStrip menu, Control owner, Point relativeLocation)
        {
            menu.Show(owner, Clamp(menu, owner, relativeLocation));
        }

        private static Point Clamp(ContextMenuStrip menu, Control owner, Point p)
        {
            try
            {
                Point screen = owner.PointToScreen(p);
                var bounds = Screen.FromControl(owner).Bounds;
                Size need = menu.GetPreferredSize(new Size(0, 0));
                // A menu taller than the display cannot be made to fit: let the framework add its
                // scroll arrows instead of pinning the top edge and clipping the last entries.
                if (need.Height > bounds.Height - Ui.S(12)) return p;
                int x = Math.Min(screen.X, bounds.Right - need.Width - 4);
                int y = screen.Y;
                if (y + need.Height > bounds.Bottom - 4) y = screen.Y - need.Height - Ui.S(8);
                if (y < bounds.Top + 4) y = bounds.Top + 4;
                return owner.PointToClient(new Point(Math.Max(bounds.Left + 4, x), y));
            }
            catch { return p; }
        }
    }

    internal class JBButton : Control, IReStyle
    {
        public BtnKind Kind = BtnKind.Ghost;
        public string Icon;
        public string BrandIcon;
        public bool Selected;
        /// <summary>Corners are square throughout; the field is kept so existing callers still
        /// compile and a future shape can be introduced in one place.</summary>
        public int Radius = Theme.ButtonRadius;
        private string _tip = "";
        public string Tip { get { return _tip; } set { _tip = value ?? ""; _tipShown = null; ToolTips.Attach(this, _tip); } }
        private string _tipShown;
        private bool _hover, _down;

        /// <summary>Width needed for this button's icon and label, so text is never clipped.</summary>
        public int PreferredWidth(int minimum)
        {
            /* Measured on the shared ruler: CreateGraphics() on every button in a footer is a
               device context per button, per layout pass. The measurement is the same. */
            Font f = Font ?? Theme.Body;
            float w = Ui.Measure(Ui.Ruler, Text ?? "", f).Width;
            if (!string.IsNullOrEmpty(Icon) || !string.IsNullOrEmpty(BrandIcon)) w += Ui.S(16 + 8);
            return Math.Max(minimum, (int)Math.Ceiling(w) + Ui.S(24));
        }

        /// <summary>Hover and press are tweens, not flips: the fill eases into its new colour
        /// over about 120 ms, which is the difference between an interface that reacts and one
        /// that flicks. The booleans stay the source of truth; the values are what is painted.</summary>
        private readonly AnimValue _hoverT, _downT;

        public JBButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            ForeColor = Theme.Text;
            Ui.Opaque(this);
            TabStop = true;
            Height = Ui.S(36);
            _hoverT = new AnimValue(this);
            _downT = new AnimValue(this);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; _hoverT.Toward(true, 0.28f); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; _hoverT.Toward(false, 0.18f); _downT.Toward(false, 0.25f); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); _down = true; _downT.Toward(true, 0.4f); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; _downT.Toward(false, 0.3f); Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public void ReStyle() { Font = Theme.Body; ForeColor = Theme.Text; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Ui.S(Radius);

            Color bg = Color.Transparent, border = Color.Transparent, fg = Theme.Text;
            bool showBorder = false;

            switch (Kind)
            {
                case BtnKind.Primary:
                    bg = !Enabled ? Theme.Panel2 : Theme.Accent;
                    bg = Ui.Blend(bg, Theme.AccentHi, _hoverT.Value);
                    bg = Ui.Blend(bg, Theme.Accent2, _downT.Value);
                    border = Ui.Blend(bg, Color.Black, 0.2f); showBorder = true;
                    fg = Enabled ? Theme.OnAccent : Theme.TextFaint;
                    break;

                case BtnKind.Soft:
                    bg = !Enabled ? Theme.Panel2 : Theme.ButtonBg;
                    bg = Ui.Blend(bg, Theme.PanelHover, _hoverT.Value);
                    bg = Ui.Blend(bg, Theme.ActiveBg, _downT.Value);
                    border = Theme.ButtonBorder; showBorder = true;
                    fg = Enabled ? Theme.Text : Theme.TextFaint;
                    break;

                case BtnKind.Danger:
                    bg = Ui.Blend(Theme.Danger, Color.White, 0.10f * _hoverT.Value);
                    bg = Ui.Blend(bg, Color.Black, 0.16f * _downT.Value);
                    border = Ui.Blend(Theme.Danger, Color.Black, 0.2f); showBorder = true;
                    fg = Theme.InkText;
                    break;

                case BtnKind.Nav:
                    bg = Selected ? Theme.AccentOnBg : Ui.Blend(Color.Transparent, Theme.HoverOverlay, _hoverT.Value);
                    bg = Ui.Blend(bg, Theme.ActiveBg, _downT.Value * 0.6f);
                    fg = Selected ? Theme.Text : _hover ? Theme.Text : Theme.TextDim;
                    break;

                default:
                    bg = Ui.Blend(Color.Transparent, Theme.SelOverlay, _downT.Value);
                    bg = Ui.Blend(bg, Theme.HoverOverlay, Math.Max(_hoverT.Value, _downT.Value) * (1f - _downT.Value * 0.5f));
                    fg = Enabled ? Theme.Text : Theme.TextFaint;
                    break;
            }

            if (bg != Color.Transparent) Ui.Fill(g, r, rad, bg);
            if (showBorder) Ui.Stroke(g, r, rad, border, 1f);

            bool hasText = !string.IsNullOrEmpty(Text);
            bool hasIcon = !string.IsNullOrEmpty(Icon) || !string.IsNullOrEmpty(BrandIcon);

            if (Kind == BtnKind.Nav)
            {
                /* A navigation row is a tall rounded row with a 20 px icon and its label, and the
                   selected one carries a soft accent fill plus a short accent bar on the left.
                   Drawn as a bare text line on the rail it read as a link in a list rather than
                   the page you are on, which is the one piece of state a shell must show. */
                float navIcon = Ui.S(22);
                float iconX = Ui.S(14);
                if (hasIcon)
                {
                    var box = new RectangleF(iconX, (Height - navIcon) / 2f, navIcon, navIcon);
                    if (BrandIcon != null) BrandIcons.Draw(g, BrandIcon, box);
                    else Icons.Draw(g, Icon, box, Selected ? Theme.AccentText : fg);
                }
                Font navFont = Selected ? Theme.BodyBold : Theme.Body;
                Color navFg = Selected ? Theme.Text : Theme.TextDim;
                float labelX = iconX + navIcon + Ui.S(12);
                Ui.Text(g, Text, navFont, navFg, labelX,
                    (Height - navFont.GetHeight(g)) / 2f, Math.Max(Ui.S(20), Width - labelX - Ui.S(12)));
                if (Selected)
                {
                    float barH = Height - Ui.S(16);
                    Ui.Fill(g, new RectangleF(0, Ui.S(Theme.ButtonRadius), Ui.S(3f), barH), Ui.S(1.5f), Theme.Accent);
                }
            }
            else
            {
                float iconSize = Ui.S(18);
                float gap = Ui.S(8);
                float textW = hasText ? Ui.Measure(g, Text, Font).Width : 0;
                float total = (hasIcon ? iconSize + (hasText ? gap : 0) : 0) + textW;

                if (total > 0)
                {
                    float x = Math.Max(Ui.S(10), (Width - total) / 2f);
                    if (hasIcon)
                    {
                        var box = new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize);
                        if (BrandIcon != null) BrandIcons.Draw(g, BrandIcon, box);
                        else Icons.Draw(g, Icon, box, fg);
                        x += iconSize + (hasText ? gap : 0);
                    }
                    if (hasText)
                    {
                        /* The label gets the room that is actually left inside the fill. Drawn
                           at its measured width it could run under the border and be cut by the
                           rounded corner, which reads as a clipped word rather than a shortened
                           one. */
                        float room = Math.Max(Ui.S(8), Width - x - Ui.S(10));
                        Ui.Text(g, Text, Font, fg, x, (Height - Font.GetHeight(g)) / 2f, room);
                        /* Once per text change, not once per paint: the old per-paint Attach
                           permanently overwrote whatever Tip the caller had set. When the label
                           fits again the caller's tip is restored. */
                        string wantTip = textW > room + 1f ? Text : _tip;
                        if (!string.Equals(_tipShown, wantTip, StringComparison.Ordinal))
                        {
                            _tipShown = wantTip;
                            ToolTips.Attach(this, wantTip);
                        }
                    }
                }
            }

            /* A button that is wider than its label does not centre it: the label sits at the
               left padding and the rest of the button is empty, which is what a "broken layout"
               looks like at a glance. Only the icon and label pair are centred, and only when
               they fit; otherwise the text is clipped with an ellipsis instead of overflowing
               the fill and being cut by the border. */
            if (Focused && Ui.FocusCues(this))
            {
                /* A text-coloured ring drawn on top of a filled accent button is nearly
                   invisible; the readable equivalent is a ring just OUTSIDE the fill. */
                if (Kind == BtnKind.Primary)
                {
                    var outer = new RectangleF(r.X - Ui.S(2.5f), r.Y - Ui.S(2.5f),
                        r.Width + Ui.S(5f), r.Height + Ui.S(5f));
                    Ui.Stroke(g, outer, rad + Ui.S(2.5f), Ui.Blend(Theme.Accent, Theme.Text, 0.35f), Ui.S(1.4f));
                }
                else Ui.FocusRing(g, r, rad);
            }
        }
    }

    internal class JBIconButton : Control, IReStyle
    {
        public string Icon = "dots";
        public int IconSize = 22;
        private string _tip = "";
        public string Tip { get { return _tip; } set { _tip = value ?? ""; ToolTips.Attach(this, _tip); } }
        public bool Danger;
        /// <summary>Caption buttons use the flat Windows 11 title-bar treatment.</summary>
        public bool Caption;
        private bool _hover, _down;
        private readonly AnimValue _hoverT, _downT;

        public JBIconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Ui.Opaque(this);
            TabStop = true;
            Size = new Size(Ui.S(32), Ui.S(32));
            _hoverT = new AnimValue(this);
            _downT = new AnimValue(this);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; _hoverT.Toward(true, 0.3f); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; _hoverT.Toward(false, 0.2f); _downT.Toward(false, 0.3f); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Focus(); _downT.Toward(true, 0.4f); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; _downT.Toward(false, 0.3f); Invalidate(); base.OnMouseUp(e); }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public void ReStyle() { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0, 0, Width, Height);
            float rad = Caption ? 0f : Theme.RMedium;

            Color ic;
            if (Caption)
            {
                bool hotClose = Danger && (_hover || _down);
                if (hotClose) Ui.Fill(g, r, 0f, Ui.Blend(Color.FromArgb(0xC4, 0x2B, 0x1C), Color.Black, _down ? 0.12f : 0f));
                else if (_hoverT.Value > 0.01f) Ui.Fill(g, r, 0f, Ui.Blend(Color.Transparent, Theme.HoverStrong, _hoverT.Value));
                ic = hotClose ? Color.White : Theme.Text;
            }
            else
            {
                Color fill = Ui.Blend(Color.Transparent, Theme.HoverOverlay, Math.Max(_hoverT.Value, _downT.Value));
                fill = Ui.Blend(fill, Theme.SelOverlay, _downT.Value);
                if (fill.A > 0) Ui.Fill(g, r, rad, fill);
                ic = _hover || Focused ? Theme.Text : Theme.TextDim;
            }

            if (Caption && (Icon == "max" || Icon == "restore"))
            {
                // Windows caption squares are plain rectangles, not the Material fullscreen arrows.
                float s = Ui.S(10);
                float sx = (Width - s) / 2f, sy = (Height - s) / 2f;
                using (var pen = new Pen(ic, Ui.Hair))
                {
                    if (Icon == "restore")
                    {
                        s = Ui.S(8);
                        sx = (Width - s) / 2f + Ui.S(1);
                        sy = (Height - s) / 2f + Ui.S(1);
                        /* Back square first and filled: drawn afterwards, its strokes cut
                           straight through the front square. */
                        using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, sx - Ui.S(2), sy - Ui.S(2), s, s);
                        g.DrawRectangle(pen, sx - Ui.S(2), sy - Ui.S(2), s, s);
                        g.DrawRectangle(pen, sx, sy, s, s);
                    }
                    else g.DrawRectangle(pen, sx, sy, s, s);
                }
                return;
            }

            float s2 = Ui.S(IconSize);
            Icons.Draw(g, Icon, new RectangleF((Width - s2) / 2f, (Height - s2) / 2f, s2, s2), ic);
            if (!Caption && Ui.FocusCues(this)) Ui.FocusRing(g, r, rad);
        }
    }

    /// <summary>Text field with optional leading icon, trailing action and its own thin scrollbar.</summary>
    internal class JBTextBox : Panel, IReStyle
    {
        /* A native Edit sits inside this panel, so WinForms resolves Tab against the panel -
           which only contains that one edit - and focus never leaves the field. Intercepting
           the dialog key here hands Tab back to the form so it walks the real tab order. */
        private sealed class FieldBox : TextBox
        {
            public Func<Keys, bool> DialogKey;

            protected override bool ProcessDialogKey(Keys keyData)
            {
                if ((keyData & Keys.KeyCode) == Keys.Tab && DialogKey != null && DialogKey(keyData))
                    return true;
                return base.ProcessDialogKey(keyData);
            }
        }

        public readonly TextBox Box;
        private bool _focused;
        private string _placeholder = "";
        private string _icon;
        private JBIconButton _trailing;
        private readonly bool _multiline;
        private readonly Label _cue;
        private int _wheelCarry;

        public JBTextBox(bool multiline = false)
        {
            _multiline = multiline;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            /* Panel disables the Selectable style, so WinForms treats a field wrapper as an
               unreachable container: SelectNextControl skips it and Tab jumps straight past
               every text box. Re-enabling it makes the wrapper a real tab stop; focus is then
               forwarded to the inner edit by OnGotFocus. */
            SetStyle(ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Box = new FieldBox();
            ((FieldBox)Box).DialogKey = FieldTab;
            TabStop = true;   // makes the field a real stop in the form's tab order
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Input;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Body;
            Box.Multiline = multiline;
            if (multiline) Box.ScrollBars = ScrollBars.None;
            Box.GotFocus += delegate { _focused = true; Invalidate(); };
            Box.LostFocus += delegate { _focused = false; Invalidate(); };
            Box.TextChanged += delegate { Invalidate(); SyncCue(); };
            Box.KeyDown += BoxKeyDown;
            Controls.Add(Box);

            /* EM_SETCUEBANNER is ignored by multiline edits. A small opaque label sitting
               above the edit shows the hint without ever becoming part of Box.Text. It must
               be opaque: a transparent label would ask the parent to repaint its surface
               colour over the edit, leaving a visible rectangle behind the hint. */
            if (multiline)
            {
                _cue = new Label
                {
                    AutoSize = false,
                    AutoEllipsis = true,   // never paint past the right edge of the field
                    BackColor = Theme.Input,
                    ForeColor = Theme.TextFaint,
                    Font = Theme.Body,
                    TextAlign = ContentAlignment.TopLeft,
                    Padding = new Padding(2, 1, 0, 0),
                    Cursor = Cursors.IBeam,
                    Visible = false
                };
                _cue.MouseDown += delegate
                {
                    Box.Focus();
                    Box.SelectionStart = 0;
                    Box.SelectionLength = 0;
                };
                Controls.Add(_cue);
                _cue.BringToFront();
            }
            TabStop = false;
        }

        private void SyncCue()
        {
            if (_cue == null) return;
            /* The hint is drawn on a single, unwrapped label row. Placeholders here are long
               ("JBR1.JB7K2M4Q.bsc.0x\u2026"), so an unclipped label ran past the right edge of
               the field and over whatever was beside it. Clip to the field instead. */
            string first = _placeholder ?? "";
            int nl = first.IndexOf('\n');
            if (nl >= 0) first = first.Substring(0, nl);
            _cue.Text = first;
            _cue.Visible = Box.Text.Length == 0 && !string.IsNullOrEmpty(first);
        }

        public string Icon
        {
            get { return _icon; }
            set { _icon = value; LayoutBox(); Invalidate(); }
        }

        public JBIconButton Trailing
        {
            get { return _trailing; }
            set
            {
                if (_trailing != null) { Controls.Remove(_trailing); _trailing.Dispose(); }
                _trailing = value;
                if (_trailing != null)
                {
                    _trailing.IconSize = 18;
                    _trailing.Size = new Size(Ui.S(26), Ui.S(26));
                    Controls.Add(_trailing);
                }
                LayoutBox();
                Invalidate();
            }
        }

        public bool UsePasswordChar
        {
            get { return Box.UseSystemPasswordChar; }
            set { Box.UseSystemPasswordChar = value; Invalidate(); }
        }

        /// <summary>
        /// A multi-line field edits notes, not a value: Enter must insert a newline there. The
        /// dialogs ask this to decide whether Enter should submit the form instead.
        /// </summary>
        public bool Multiline { get { return _multiline; } }

        public int PadLeft = 12, PadRight = 12, PadTop = 7;

        public string Value
        {
            get { return Box.Text; }
            set { Box.Text = value ?? ""; SyncCue(); }
        }

        public string Placeholder
        {
            get { return _placeholder; }
            set
            {
                _placeholder = value ?? "";
                if (Box.IsHandleCreated)
                    // wParam TRUE keeps the cue visible while the field has focus, so a form
                    // that opens with the first field focused never shows a blank box.
                    Native.SendMessage(Box.Handle, Native.EM_SETCUEBANNER, new IntPtr(1), _placeholder);
                SyncCue();
            }
        }

        /* Raised only for the Enter key. It must NOT be wired straight to Box.KeyDown:
           doing that fires the handler on every keystroke, which made the sign-in form
           submit as the user typed and the PIN prompt accept a one-character code. */
        public event KeyEventHandler EnterPressed;

        private void BoxKeyDown(object sender, KeyEventArgs e)
        {
            if (!_multiline && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                OnEnterKey();
            }
            else if (e.KeyCode == Keys.Escape && _focused) Invalidate();
        }

        protected virtual void OnEnterKey()
        {
            var handler = EnterPressed;
            if (handler != null) handler(this, new KeyEventArgs(Keys.Enter));
        }

        /// <summary>Moves focus to the next/previous field in the form's tab order.</summary>
        private bool FieldTab(Keys keyData)
        {
            var form = FindForm();
            if (form == null) return false;
            bool back = (keyData & Keys.Shift) == Keys.Shift;
            /* nested must be false: with nested on, the search descends back into this panel
               and simply re-selects the edit inside it, so focus never appears to move.
               tabStopOnly is false because the wrapper is a Panel and panels are not tab stops
               by default; labels and hidden controls are still skipped because they report
               CanSelect == false. */
            bool moved = form.SelectNextControl(this, !back, false, false, true);
            if (!moved) moved = form.SelectNextControl(form, !back, false, false, true);
            return moved;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Box.Focus(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!string.IsNullOrEmpty(_placeholder))
                Native.SendMessage(Box.Handle, Native.EM_SETCUEBANNER, new IntPtr(1), _placeholder);
            LayoutBox();
            SyncCue();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Box.Focus();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(SurfaceColor());
        }

        /// <summary>The colour behind this field, so the rounded corners never reveal stale pixels.</summary>
        private Color SurfaceColor()
        {
            return Ui.SurfaceColor(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Ui.S(Theme.ButtonRadius);
            /* A field is a rounded surface with one hairline, and focus thickens that hairline to
               the accent colour. The old field carried BOTH a full frame and a second rule under
               it, which is two outlines for one control - the reason every form looked busy. */
            Ui.Fill(g, r, rad, Theme.Input);
            if (Tapped)
            {
                /* A real stroke width - the old call passed a radius constant (0), which only
                   rendered because Stroke maps any width below 1.2 to the hairline. */
                Ui.Stroke(g, r, rad, Theme.Accent, Ui.S(1.6f));
            }
            else
            {
                Ui.Stroke(g, r, rad, _hover ? Theme.HoverStrong : Theme.Line);
            }

            if (!string.IsNullOrEmpty(_icon))
            {
                float s = Ui.S(16);
                // A tall multiline field has to keep its icon on the first text row, not
                // floating in the vertical middle of the box.
                float iconY = _multiline ? Ui.S(PadTop) : (Height - s) / 2f;
                Icons.Draw(g, _icon, new RectangleF(Ui.S(12), iconY, s, s),
                    Tapped ? Theme.AccentText : Theme.TextFaint);
            }

            if (_multiline) PaintScroll(g);
        }

        /// <summary>True while the field owns the keyboard, including its inner edit control.</summary>
        private bool Tapped { get { return _focused || (Box != null && Box.Focused); } }

        private void PaintScroll(Graphics g)
        {
            int total = Lines(Box, Native.EM_GETLINECOUNT);
            int first = (int)Native.SendMessage(Box.Handle, Native.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt64();
            int visible = Math.Max(1, Box.Height / Math.Max(1, Box.Font.Height));
            if (total <= visible) return;

            float track = Height - Ui.S(10);
            float frac = Math.Min(1f, visible / (float)total);
            float thumbH = Math.Max(Ui.S(24), track * frac);
            float maxTop = track - thumbH;
            float pos = Ui.S(5) + maxTop * MathHelper01(first / (float)Math.Max(1, total - visible));

            var r = new RectangleF(Width - Ui.S(9), pos, Ui.S(4), thumbH);
            Ui.Fill(g, r, 0f, _hover ? Theme.Accent : Theme.Line);
        }

        private bool _hover;

        private static int Lines(TextBox box, int msg)
        {
            try { return (int)Native.SendMessage(box.Handle, msg, IntPtr.Zero, IntPtr.Zero).ToInt64(); }
            catch { return 0; }
        }

        private static float MathHelper01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBox();
        }

        private void LayoutBox()
        {
            if (!IsHandleCreated) return;
            int l = Ui.S(PadLeft) + (string.IsNullOrEmpty(_icon) ? 0 : Ui.S(24));
            int t = Ui.S(PadTop);
            int rr = Ui.S(PadRight) + (_trailing != null ? Ui.S(26) : 0);
            if (_multiline) rr += Ui.S(6);
            int w = Math.Max(10, Width - l - rr);
            int h = Math.Max(12, Height - t - Ui.S(PadTop));
            Box.SetBounds(l, t, w, h);
            if (_cue != null)
            {
                _cue.SetBounds(l, t, Math.Max(10, w), Theme.Body.Height + Ui.S(4));
                SyncCue();
            }
            if (_trailing != null) _trailing.SetBounds(Width - Ui.S(PadRight) - Ui.S(26), (Height - Ui.S(26)) / 2, Ui.S(26), Ui.S(26));
        }

        public void ReStyle()
        {
            Box.BackColor = Theme.Input;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Body;
            if (_cue != null)
            {
                _cue.BackColor = Theme.Input;
                _cue.ForeColor = Theme.TextFaint;
                _cue.Font = Theme.Body;
            }
            Invalidate();
        }
    }

    internal class JBSelect : Control, IReStyle
    {
        public readonly List<string> Items = new List<string>();
        private int _selected = -1;
        private int _pageStart;
        private bool _pagingNav;
        private bool _hover, _open;
        private ContextMenuStrip _menu;
        private string _icon;

        public event EventHandler SelectedIndexChanged;

        /// <summary>Optional per item country code, drawn as a circular chip.</summary>
        public Func<int, string> Badge { get; set; }

        /// <summary>Optional readable label per item; the stored Value stays the raw key.</summary>
        public Func<int, string> Display { get; set; }

        private string LabelAt(int i)
        {
            if (Display != null)
            {
                try { string s = Display(i); if (!string.IsNullOrEmpty(s)) return s; } catch { }
            }
            return i >= 0 && i < Items.Count ? Items[i] : "";
        }

        public JBSelect()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            Ui.Opaque(this);
            Height = Ui.S(36);
            TabStop = true;
            /* The popup outlives a close (see CloseMenu), so the control has to be the last
               owner: replacing it while an old strip is still alive leaked a top-level window. */
            Disposed += delegate { if (_menu != null) { try { _menu.Dispose(); } catch { } _menu = null; } };
        }

        public string Icon { get { return _icon; } set { _icon = value; Invalidate(); } }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < -1 || value >= Items.Count) value = -1;
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string Value { get { return _selected >= 0 && _selected < Items.Count ? Items[_selected] : ""; } }

        public void SetItems(IEnumerable<string> items, int selected)
        {
            Items.Clear();
            Items.AddRange(items);
            _selected = (selected >= 0 && selected < Items.Count) ? selected : -1;
            _pageStart = 0;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { Focus(); ShowMenu(); Ui.KeyboardCuesForced = false; }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Space || keyData == Keys.Enter || keyData == Keys.Down ||
                   keyData == Keys.Up || keyData == Keys.Home || keyData == Keys.End ||
                   base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                int next = _selected + (e.KeyCode == Keys.Down ? 1 : -1);
                SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, next));
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Home) { SelectedIndex = Items.Count > 0 ? 0 : -1; e.Handled = true; }
            else if (e.KeyCode == Keys.End) { SelectedIndex = Items.Count - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                if (_open) CloseMenu();
                else ShowMenu();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        /// <summary>
        /// F4 and Alt+Down are the shortcut every Windows combo box honours; both are command
        /// keys, so OnKeyDown never saw them.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData == Keys.F4 || keyData == (Keys.Alt | Keys.Down)) && Items.Count > 0)
            {
                if (_open) CloseMenu(); else ShowMenu();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /* Type-ahead: in a 47-country list, jumping to "Germany" by pressing G is the whole
           difference between a usable control and a scroll hunt. */
        private string _typed = "";
        private DateTime _typedAt;

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar) || Items.Count == 0) { base.OnKeyPress(e); return; }
            if ((DateTime.UtcNow - _typedAt).TotalMilliseconds > 800) _typed = "";
            _typedAt = DateTime.UtcNow;
            _typed += char.ToLowerInvariant(e.KeyChar);

            // Prefer a match after the current position so repeated presses cycle the list.
            int start = (_selected + 1) % Items.Count;
            int found = -1;
            for (int n = 0; n < Items.Count; n++)
            {
                int i = (start + n) % Items.Count;
                string label = LabelAt(i) ?? "";
                if (label.StartsWith(_typed, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
            }
            if (found >= 0) SelectedIndex = found;
            else
            {
                /* A letter that matches nothing used to stay in the buffer and block every
                   further match until the 800 ms idle reset - "G, E, R" after a typo made the
                   control go deaf. */
                _typed = "";
            }
            e.Handled = true;
            base.OnKeyPress(e);
        }

        /// <summary>
        /// Closes the popup and releases it. Closing alone is not enough: a strip built for a
        /// paged list holds flag bitmaps that are disposed on Dispose, so a menu that was only
        /// Closed leaked its images until the control itself went away.
        /// </summary>
        private void CloseMenu()
        {
            _open = false;
            var m = _menu;
            _menu = null;
            if (m != null)
            {
                /* Close first so the window is off screen, then dispose once the click that
                   asked for this has unwound. */
                try { m.Close(); } catch { }
                try { m.BeginInvoke(new Action(delegate { try { m.Dispose(); } catch { } })); }
                catch { try { m.Dispose(); } catch { } }
            }
            Invalidate();
        }

        private void ShowMenu()
        {
            if (Items.Count == 0) return;
            /* Clicking the control while its popup is up used to dispose the strip that was
               still on screen and build a second one on the same tick, so the list flickered and
               could be left ownerless. The open popup owns that gesture: the click closes it. */
            if (_open) { CloseMenu(); return; }
            /* A menu closed by the last gesture is disposed on a posted message, so _menu may
               still be live here. Disposing it again is safe (the flag is already set) and keeps
               the field from naming a window that no longer exists. */
            if (_menu != null) { try { _menu.Dispose(); } catch { } _menu = null; }
            _menu = JBMenu.Create();
            _menu.Font = Font;

            bool chips = Badge != null;
            _menu.ShowCheckMargin = !chips;
            _menu.ShowImageMargin = chips;
            _menu.MinimumSize = new Size(Width, 0);

            var made = new List<Bitmap>();
            if (chips) _menu.Disposed += delegate { foreach (var b in made) { try { b.Dispose(); } catch { } } };

            // A 47-zone list is taller than the display. Page it instead of letting Windows
            // clamp the popup to the top edge, which hides the tail with no way to scroll.
            /* Row height is the menu's own padding plus the text height, not a constant: at
               150% scaling a hard-coded 34 px fitted more rows than the popup could show, and
               the surplus was clamped off the bottom with no scrollbar. */
            int rowH = Math.Max(Ui.S(28), Theme.Body.Height + Ui.S(14));
            int rows = Math.Max(6, Math.Min(20, (Screen.FromControl(this).WorkingArea.Height - Ui.S(120)) / rowH));
            int pageSize = Math.Max(4, rows - 2);
            bool paged = Items.Count > rows;

            if (!_pagingNav && paged && _selected >= 0 && (_selected < _pageStart || _selected >= _pageStart + pageSize))
                _pageStart = (_selected / pageSize) * pageSize;
            _pagingNav = false;
            if (_pageStart > Items.Count - 1) _pageStart = Math.Max(0, ((Items.Count - 1) / pageSize) * pageSize);
            if (_pageStart < 0) _pageStart = 0;

            int start = paged ? _pageStart : 0;
            int end = paged ? Math.Min(Items.Count, start + pageSize) : Items.Count;

            if (paged && start > 0)
            {
                var up = new JBMenu.Item("Previous", "chevron-up");
                up.Click += delegate { _pagingNav = true; _pageStart = Math.Max(0, start - pageSize); ReopenMenu(); };
                _menu.Items.Add(up);
            }

            for (int i = start; i < end; i++)
            {
                int idx = i;
                var item = new JBMenu.Item(LabelAt(i), null);
                item.Checked = !chips && i == _selected;
                if (chips)
                {
                    string code = null;
                    try { code = Badge(i); } catch { }
                    if (!string.IsNullOrEmpty(code))
                    {
                        var bmp = Flags.NewBitmap(code, Ui.S(19));
                        made.Add(bmp);
                        item.Image = bmp;
                        item.ImageScaling = ToolStripItemImageScaling.None;
                    }
                    if (i == _selected) item.Font = Theme.BodyBold;
                }
                item.Click += delegate { SelectedIndex = idx; };
                _menu.Items.Add(item);
            }

            if (paged && end < Items.Count)
            {
                var more = new JBMenu.Item("More…  (" + (Items.Count - end) + ")", "chevron-down");
                more.Click += delegate { _pagingNav = true; _pageStart = end; ReopenMenu(); };
                _menu.Items.Add(more);
            }

            _open = true;
            Invalidate();
            var opened = _menu;
            opened.Closed += delegate { if (ReferenceEquals(_menu, opened)) { _open = false; Invalidate(); } };
            JBMenu.ShowNear(opened, this, new Point(0, Height + Ui.S(4)));
        }

        private void ReopenMenu()
        {
            // Clicking a page item closes the menu; reopen after the click has unwound.
            try { BeginInvoke(new Action(ShowMenu)); } catch { }
        }

        public void ReStyle() { Font = Theme.Body; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            /* The dropdown is a field, so it is drawn like one: the same radius, the same
               hairline, and the same accent on focus. The extra underline it used to draw made a
               select look different from the text box beside it for no reason a user could read. */
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Ui.S(Theme.ButtonRadius);
            Ui.Fill(g, r, rad, Theme.Input);
            Ui.Stroke(g, r, rad, _open || Focused ? Theme.Accent : _hover ? Theme.HoverStrong : Theme.Line,
                _open || Focused ? Ui.S(1.5f) : 1f);

            float left = Ui.S(12);
            string chip = null;
            if (Badge != null && _selected >= 0)
            {
                try { chip = Badge(_selected); } catch { chip = null; }
            }

            if (!string.IsNullOrEmpty(chip))
            {
                float d = Ui.S(18);
                Flags.Draw(g, chip, new RectangleF(Ui.S(9), (Height - d) / 2f, d, d));
                left = Ui.S(9) + d + Ui.S(8);
            }
            else if (!string.IsNullOrEmpty(_icon))
            {
                float s = Ui.S(16);
                Icons.Draw(g, _icon, new RectangleF(Ui.S(10), (Height - s) / 2f, s, s), Theme.TextFaint);
                left = Ui.S(10) + Ui.S(22);
            }

            string text = LabelAt(_selected);
            Color fg = string.IsNullOrEmpty(text) ? Theme.TextFaint : Theme.Text;
            /* A dropdown narrower than its own chevron and padding would ask for a negative
               width here, and GDI draws nothing for that - the selected value vanished instead of
               being trimmed. The last 24 px are kept for the chevron and no more. */
            Ui.Text(g, text, Font, fg, left, (Height - Font.GetHeight(g)) / 2f,
                Math.Max(Ui.S(16), Width - left - Ui.S(36)));

            float c = Ui.S(16);
            Icons.Draw(g, _open ? "chevron-up" : "chevron-down",
                new RectangleF(Width - c - Ui.S(10), (Height - c) / 2f, c, c),
                _open || _hover ? Theme.Text : Theme.TextDim);

            if (Ui.FocusCues(this)) Ui.FocusRing(g, r, rad);
        }
    }

    internal class JBToggle : Control, IReStyle
    {
        private bool _checked, _hover;
        private readonly AnimValue _hoverT;
        /// <summary>0 = off, 1 = on. The knob slides between the two instead of teleporting,
        /// which is the one motion a switch is supposed to have.</summary>
        private readonly AnimValue _knobT;

        public event EventHandler CheckedChanged;

        public JBToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            ForeColor = Theme.Text;
            Ui.Opaque(this);
            Height = Ui.S(26);
            Width = Ui.S(210);
            TabStop = true;
            _hoverT = new AnimValue(this);
            _knobT = new AnimValue(this);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _knobT.Toward(value, 0.22f);
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; _hoverT.Toward(true, 0.3f); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverT.Toward(false, 0.2f); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        public void ReStyle() { Font = Theme.Body; ForeColor = Theme.Text; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            float h = Height;
            // windows-ui switch: 42x21 track, 13px thumb, 1px outline when off.
            float trackW = Ui.S(42), trackH = Ui.S(21);
            float ty = (h - trackH) / 2f;
            var track = new RectangleF(0.5f, ty, trackW, trackH);

            /* Square, like everything else on the surface: the track is the shape of the app,
               and the knob is a square that slides. A pill here would be the one rounded thing
               left on a page of square cards, which is exactly the inconsistency the radius
               sweep is for. */
            float rad = Ui.S(Theme.ButtonRadius);
            /* The track and the knob both read their tween: the fill eases between the empty
               and the accent track, and the knob travels the distance the switch covers. */
            float onT = _knobT.Value;
            if (onT > 0.01f)
                Ui.Fill(g, track, rad, Ui.Blend(_hover ? Theme.HoverOverlay : Color.FromArgb(0, 0, 0, 0), Theme.Accent, onT));
            if (!_checked)
            {
                if (_hoverT.Value > 0.01f && onT <= 0.01f) Ui.Fill(g, track, rad, Ui.Blend(Color.Transparent, Theme.HoverOverlay, _hoverT.Value));
                // Only the off state needs an outline; over the filled track a darker hairline
                // read as a dirty edge rather than a border.
                Ui.Stroke(g, track, rad, Ui.Blend(Theme.TextDim, Theme.Accent, onT), 1f);
            }

            float knobD = Ui.S(15);
            float ky = track.Y + (trackH - knobD) / 2f;
            float kxOff = track.X + Ui.S(3), kxOn = track.Right - knobD - Ui.S(3);
            float kx = kxOff + (kxOn - kxOff) * onT;
            using (var b = new SolidBrush(onT > 0.5f ? Theme.OnAccent : Theme.TextDim))
                g.FillRectangle(b, kx, ky, knobD, knobD);

            if (!string.IsNullOrEmpty(Text))
                Ui.Text(g, Text, Font, Enabled ? Theme.Text : Theme.TextFaint, trackW + Ui.S(10), (h - Font.GetHeight(g)) / 2f, Math.Max(0, Width - trackW - Ui.S(10)));

            if (Ui.FocusCues(this))
                Ui.FocusRing(g, track, Ui.S(Theme.ButtonRadius));
        }
    }

    internal class JBSegment : Control, IReStyle
    {
        public readonly List<string> Items = new List<string>();
        private int _selected;
        private int _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public JBSegment()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            Ui.Opaque(this);
            Height = Ui.S(38);
            TabStop = true;
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= Items.Count) return;
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetItems(IEnumerable<string> items, int selected)
        {
            Items.Clear();
            Items.AddRange(items);
            _selected = Math.Max(0, Math.Min(selected, Items.Count - 1));
            Invalidate();
        }

        /// <summary>
        /// The width this row of names wants before any of them has to shrink. Measuring the
        /// names rather than guessing keeps the five editor tabs from drawing over each other in
        /// a narrow window: past 125% scale "Fingerprint" and "Privacy" used to sit on top of one
        /// another, and the label that was clicked was not the label that was read.
        /// </summary>
        public int PreferredWidth(int minCell)
        {
            if (Items.Count == 0) return 0;
            int widest = 0;
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    foreach (var item in Items)
                        widest = Math.Max(widest, (int)Math.Ceiling(Ui.Measure(g, item, Theme.BodyBold).Width));
                }
            }
            catch { }
            int cell = Math.Max(minCell, widest + Ui.S(28));
            return cell * Items.Count;
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { SelectedIndex = Math.Min(Items.Count - 1, _selected + 1); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = Hit(e.X);
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int idx = Hit(e.X);
            if (idx >= 0) SelectedIndex = idx;
        }

        private int Hit(int x)
        {
            if (Items.Count == 0 || Width <= 0) return -1;
            int i = x * Items.Count / Width;
            return Math.Max(0, Math.Min(Items.Count - 1, i));
        }

        public void ReStyle() { Font = Theme.Body; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            if (Items.Count == 0) return;

            /* A pivot bar: one hairline along the full width, a soft hover pill per segment, and
               an accent bar under the selected one that spans its name rather than the whole
               cell. The bar is drawn on the device pixel grid so it is two crisp rows and not a
               three row blur. */
            float rule = Height - Ui.S(1);
            using (var b = new SolidBrush(Theme.Line))
                g.FillRectangle(b, 0, rule, Width, Ui.S(1));

            float seg = Width / (float)Items.Count;
            for (int i = 0; i < Items.Count; i++)
            {
                var cell = new RectangleF(i * seg, 0, seg, rule);
                bool sel = i == _selected;
                if (!sel && i == _hover)
                {
                    var pill = new RectangleF(cell.X + Ui.S(4), Ui.S(4), cell.Width - Ui.S(8), rule - Ui.S(8));
                    Ui.Fill(g, pill, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay);
                }

                Font f = sel ? Theme.BodyBold : Theme.Body;
                Color fg = sel ? Theme.Text : i == _hover ? Theme.Text : Theme.TextDim;
                float ty = (rule - f.GetHeight(g)) / 2f - Ui.S(1);
                Ui.Text(g, Items[i], f, fg, cell.X, ty, cell.Width, "center");

                if (sel)
                {
                    float tw = Math.Min(cell.Width - Ui.S(16), Ui.Measure(g, Items[i], f).Width + Ui.S(12));
                    float barH = Ui.S(3f);
                    var bar = new RectangleF(cell.X + (cell.Width - tw) / 2f,
                        Math.Max(0f, rule - barH - Ui.S(1)), tw, barH);
                    Ui.Fill(g, bar, barH / 2f, Theme.Accent);
                }
            }

            /* This control is a real tab stop and handled arrow keys, but never showed that it
               had focus: a keyboard user tabbing through the editor could not tell where they
               were. */
            if (Ui.FocusCues(this))
                Ui.FocusRing(g, new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, rule - 0.5f)), 0f);
        }
    }

    /// <summary>Scrollable host with a thin, self-drawn scrollbar that only appears when needed.</summary>
    internal class JBScrollHost : Panel, IReStyle
    {
        private readonly Control _content;
        private int _offset;
        private bool _dragging, _thumbHover;
        private Point _dragStart;
        private int _dragOffsetStart;
        private DateTime _barShownAt = DateTime.MinValue;
        private readonly Timer _fade;
        /// <summary>0 = hidden, 1 = fully shown. The bar eases in on a scroll and eases back
        /// to a quiet line when the page stops moving, so the rail never shouts over the
        /// content it is measuring - which is what "avoid the vertical scrollbar" means in
        /// practice: it is there when needed, and out of the way when not.</summary>
        private readonly AnimValue _barT;

        public JBScrollHost(Control content)
        {
            _content = content;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            TabStop = false;
            /* Panel clears ControlStyles.Selectable, so the host can never take focus and its
               keyboard scrolling could not be reached at all. It is deliberately NOT a tab stop:
               a focusable container in the tab order makes every field inside it need two Tabs.
               Instead it takes focus when the pointer enters it - which is what the user is
               pointing at - and the page itself passes Up/Down to it. */
            SetStyle(ControlStyles.Selectable, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            content.Parent = this;
            Controls.Add(content);

            /* The bar is an overlay that appears while scrolling and then leaves. A permanent
               vertical strip on every page is visual noise, and this content is read top-down
               rather than scrolled constantly. */
            /* Content is often laid out after the host has already been resized (a page built
               row by row, a list that grows when the user acts on it). Without this the host
               looked at a content height of zero, reported no overflow, and the page simply
               could not be scrolled until something else happened to trigger a layout - the
               "the list is cut off and there is no scrollbar" bug. Watching the content's own
               layout is the only signal that arrives in every one of those cases. */
            content.SizeChanged += delegate { SyncExtent(); };
            content.Layout += delegate { SyncExtent(); };

            _barT = new AnimValue(this);
            _barT.Set(0f);   /* quiet until the first real scroll reveals it */
            _fade = new Timer { Interval = 350 };
            _fade.Tick += delegate
            {
                if (_thumbHover || _dragging) return;
                if ((DateTime.UtcNow - _barShownAt).TotalMilliseconds < 900) return;
                _fade.Stop();
                _barT.Toward(false, 0.06f);
                Invalidate();
            };
        }

        /// <summary>
        /// True whenever the content is taller than the viewport - and that is all. The bar used
        /// to appear only while the user was scrolling and fade a second later, so a page that
        /// needed scrolling looked exactly like a page that did not, and a cut-off list read as
        /// "the app is truncating my data" rather than "there is more below". A persistent bar is
        /// also what makes the page feel responsive: it answers on the first frame.
        /// </summary>
        private bool ShowBar { get { return MaxOffset > 0; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _fade != null) _fade.Dispose();
            base.Dispose(disposing);
        }

        public int Offset { get { return _offset; } }
        public int MaxOffset { get { return Math.Max(0, _content.Height - Height); } }
        public int ContentWidth { get { return _content.Width; } }

        /// <summary>Re-clamps the offset after the content changed size.</summary>
        private bool _syncing;

        private int _syncPasses;

        public void SyncExtent()
        {
            /* LayoutContent resizes the content, and the content's own Layout event calls back
               in here: without the guard that is unbounded recursion the first time a page adds
               a row from inside its layout pass. */
            if (_syncing) return;
            _syncing = true;
            try
            {
                LayoutContent();
                ScrollToQuiet(_offset);
                /* A page that computes its column width from ContentWidth only settles on the
                   second pass: the first runs against the previous width, which is what left a
                   page laid out for the size it had before the resize. One more pass once the
                   width is final fixes that, and the counter keeps a page from recursing. */
                if (_syncPasses < 1)
                {
                    _syncPasses++;
                    try
                    {
                        /* The page owns the layout method, not the column it scrolls: ask the
                           nearest ancestor that knows how to lay this column out. */
                        Control owner = _content.Parent;
                        while (owner != null)
                        {
                            var sync = owner as ISyncLayout;
                            if (sync != null) { sync.LayoutAtWidth(); break; }
                            owner = owner.Parent;
                        }
                    }
                    catch { }
                }
                Invalidate();
            }
            finally { _syncPasses = 0; _syncing = false; }
        }

        public void ScrollTo(int value)
        {
            Apply(value, true);
        }

        /// <summary>Layout changes must not flash the bar; only a real scroll reveals it.</summary>
        private void ScrollToQuiet(int value)
        {
            Apply(value, false);
        }

        private void Apply(int value, bool reveal)
        {
            int max = MaxOffset;
            _offset = Math.Max(0, Math.Min(max, value));
            _content.Top = -_offset;
            if (reveal)
            {
                _barShownAt = DateTime.UtcNow;
                _barT.Toward(true, 0.3f);
                if (!_fade.Enabled) _fade.Start();
            }
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutContent();
            ScrollToQuiet(_offset);
        }

        private void LayoutContent()
        {
            _content.Width = Math.Max(1, Width - Ui.S(10));
            _content.Left = 0;
            _content.Top = -_offset;
            /* A content panel shorter than the host would leave the page under-filled, which
               reads as a rendering glitch on a tall window. It only ever grows to the host
               height; a page that really is taller keeps its own height. */
            if (_content.Height < Height) _content.Height = Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            if (!ShowBar) return;
            Ui.Quality(e.Graphics);
            var hit = ThumbRect();
            if (hit.IsEmpty) return;
            // The visible bar is inset inside the grab zone so the two never disagree.
            var bar = new RectangleF(Width - Ui.S(9), hit.Y, Ui.S(5), hit.Height);
            /* Quiet while idle, loud while moving: the resting bar is a low-contrast rail so a
               page that scrolls still answers the first frame without a grey stripe down the
               window, and the moment it moves - or the pointer reaches for it - it eases into
               the accent colour that says "this is being dragged". */
            float t = Math.Max(_barT.Value, _thumbHover ? 1f : 0f);
            Color c = _dragging || _thumbHover ? Theme.Accent : Ui.Blend(Theme.LineSoft, Theme.Accent, t);
            int alpha = _dragging || _thumbHover ? 255 : (int)(150 + 105 * t);
            if (alpha > 255) alpha = 255;
            using (var b = new SolidBrush(Color.FromArgb(alpha, c)))
                e.Graphics.FillRectangle(b, bar);
        }

        /// <summary>Thumb geometry, shared by painting and hit-testing so the grab zone matches the bar.</summary>
        private RectangleF ThumbRect()
        {
            if (MaxOffset <= 0) return RectangleF.Empty;
            float track = Height - Ui.S(8);
            float frac = Math.Min(1f, Height / (float)_content.Height);
            float thumbH = Math.Max(Ui.S(30), track * frac);
            float pos = Ui.S(4) + (track - thumbH) * (_offset / (float)MaxOffset);
            return new RectangleF(Width - Ui.S(13), pos, Ui.S(12), thumbH);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            /* Focus on the surface, but never steal it from a text field the user is typing in:
               clicking empty page space should not eat the caret. */
            if (!ContainsFocus) { try { Focus(); } catch { } }
            if (e.Button == MouseButtons.Left && ThumbRect().Contains(e.Location))
            {
                _dragging = true;
                _dragStart = e.Location;
                _dragOffsetStart = _offset;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                float track = Height - Ui.S(8);
                float frac = Math.Min(1f, Height / (float)_content.Height);
                float thumbH = Math.Max(Ui.S(30), track * frac);
                float perPx = MaxOffset / Math.Max(1f, track - thumbH);
                ScrollTo(_dragOffsetStart + (int)Math.Round((e.Y - _dragStart.Y) * perPx));
                return;
            }
            bool h = ThumbRect().Contains(e.Location);
            if (h != _thumbHover) { _thumbHover = h; if (h) { _barShownAt = DateTime.UtcNow; _barT.Toward(true, 0.3f); } Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging) { _dragging = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _thumbHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Wheel(e.Delta);
        }

        /// <summary>
        /// One notch of the wheel. Kept separate so a descendant that handles the wheel itself
        /// (a multiline field, the grid) can hand the rest of the gesture up to the page.
        /// </summary>
        public void Wheel(int delta)
        {
            if (delta == 0 || MaxOffset == 0) return;
            ScrollTo(_offset - (delta / 120) * Ui.S(64));
        }

        /// <summary>
        /// Windows delivers WM_MOUSEWHEEL to the focused window, not to the one under the
        /// pointer, and only forwards it up the parent chain while nothing consumes it. Inside a
        /// page the focused window is usually a profile card or a chip - a control that neither
        /// scrolls nor wants the gesture - so the notches that should have moved the page were
        /// lost on the way down. Claiming the message here, at the host that owns the scrolling,
        /// catches every descendant that did not take it for itself (a multiline field still
        /// does, and hands back only what it cannot use).
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = (short)(((long)m.WParam.ToInt64() >> 16) & 0xFFFF);
                if (delta != 0 && MaxOffset > 0) { Wheel(delta); return; }
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// Home, End and the page keys are command keys for WinForms, not input keys: for a
        /// Panel they are resolved by the dialog-key pass BEFORE OnKeyDown ever runs, so the
        /// old OnKeyDown handler never saw them and only the arrows worked - and even those
        /// only once the panel had focus, which no amount of tabbing reliably gave it. All of
        /// them are routed here instead, from the message loop, which is the only place that
        /// can claim the whole set.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Down: ScrollTo(_offset + Ui.S(64)); return true;
                case Keys.Up: ScrollTo(_offset - Ui.S(64)); return true;
                case Keys.PageDown: ScrollTo(_offset + Height - Ui.S(64)); return true;
                case Keys.PageUp: ScrollTo(_offset - Height + Ui.S(64)); return true;
                case Keys.Home: ScrollTo(0); return true;
                case Keys.End: ScrollTo(MaxOffset); return true;
                case Keys.Control | Keys.Home: ScrollTo(0); return true;
                case Keys.Control | Keys.End: ScrollTo(MaxOffset); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void ReStyle() { BackColor = Theme.Bg; Invalidate(); }
    }

    /// <summary>Responsive card grid: columns are chosen from the available width, cells stretch to fill.</summary>
    internal sealed class JBGrid : Panel, IReStyle, ISyncLayout
    {
        public int MinCell = 360;
        public int CellHeight = 200;
        public int Gap = 16;

        public JBGrid()
        {
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void ReStyle() { BackColor = Theme.Bg; Invalidate(); }

        /// <summary>One more pass once the scroll host has fixed the width it scrolls.</summary>
        public void LayoutAtWidth() { LayoutGrid(); }

        public int Columns { get; private set; }
        public int Rows { get; private set; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutGrid();
        }

        /* A batch keeps adding and removing cards from laying the grid out once per card.
           Rebuilding a workspace of sixty profiles added sixty cards, and each one ran the
           whole layout over every card added so far: sixty passes of sixty SetBounds calls
           while the window was opening. One pass at the end is the same result for a fraction
           of the work, and it is what made opening a large workspace feel slow. */
        private int _batch;

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (_batch == 0) LayoutGrid();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            if (_batch == 0) LayoutGrid();
        }

        /// <summary>Runs a block that changes many children, then lays the grid out once.</summary>
        public void Batch(Action changes)
        {
            _batch++;
            try
            {
                SuspendLayout();
                try { changes(); }
                finally { ResumeLayout(false); }
            }
            finally
            {
                _batch--;
                if (_batch == 0) LayoutGrid();
            }
        }

        /// <summary>
        /// Columns are chosen so each row always spans the full width: a wide window gets more
        /// cards per row instead of a capped grid with dead space on the right.
        /// </summary>
        private bool _layingOut;

        public void LayoutGrid()
        {
            /* Setting Height below raises OnResize, which calls straight back in here. The
               second pass computes the same numbers, so it is harmless today - but it re-enters
               this method in the middle of the control loop, and the guard makes that
               impossible rather than merely unlikely. */
            if (_layingOut) return;
            _layingOut = true;
            try { LayoutGridCore(); }
            finally { _layingOut = false; }
        }

        private void LayoutGridCore()
        {
            int gap = Ui.S(Gap);
            int min = Ui.S(MinCell);
            int cellH = Ui.S(CellHeight);
            SuspendLayout();

            int avail = Math.Max(min, Width);
            int cols = Math.Max(1, (avail + gap) / (min + gap));
            int cellW = (avail - gap * (cols - 1)) / cols;
            Columns = cols;

            int i = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int row = i / cols, col = i % cols;
                c.SetBounds(col * (cellW + gap), row * (cellH + gap), cellW, cellH);
                i++;
            }
            Rows = Math.Max(1, (int)Math.Ceiling(i / (double)cols));
            Height = i > 0 ? Rows * (cellH + gap) + Ui.S(8) : Ui.S(40);

            ResumeLayout(false);
            Invalidate();
        }
    }

    internal sealed class JBToast : Control, IReStyle
    {
        private readonly Timer _timer = new Timer();
        private string _text = "";
        private ToastKind _kind = ToastKind.Success;
        private bool _hover;
        private int _lines = 1;
        private int _bottom;      // anchored bottom edge, set by Place()

        public JBToast()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Visible = false;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            _timer.Interval = 3400;
            _timer.Tick += delegate
            {
                /* A long warning takes more than three seconds to read, and the pointer resting
                   on the bar is the user saying "I am reading this". The countdown restarts when
                   they move away instead of deleting the message under their eyes. */
                if (_hover) { _timer.Stop(); return; }
                Hide();
            };
        }

        /// <summary>
        /// Anchors the toast to a bottom edge and lets it grow upward for long messages.
        /// A one-line message keeps the standard 44px bar; longer ones wrap to at most three
        /// lines so nothing is silently clipped with an ellipsis.
        /// </summary>
        public void Place(int x, int bottom, int width)
        {
            _bottom = bottom;
            Width = Math.Max(Ui.S(200), width);
            Left = x;
            FitHeight();
        }

        public void Show(string text, ToastKind kind)
        {
            _text = text ?? "";
            _kind = kind;
            Visible = true;
            BringToFront();
            FitHeight();
            Invalidate();
            _timer.Stop();
            // Warnings and errors carry instructions the user has to read; keep them up longer.
            _timer.Interval = (kind == ToastKind.Warn || kind == ToastKind.Error) ? 6500 : 3400;
            _timer.Start();
            /* Enter like something arriving, not like something appearing: the bar rises a few
               pixels and eases into place. Exiting stays immediate - dismissing feedback
               quickly is as much a part of the feel as showing it smoothly. */
            int finalTop = Top;
            UiAnim.Animate(this, t =>
            {
                try { Top = finalTop + (int)Math.Round(Ui.S(14) * (1f - t)); } catch { }
            }, 0.28f);
            Top = finalTop + Ui.S(14);
        }

        private void FitHeight()
        {
            int lineH = Math.Max(1, (int)Math.Ceiling(Font.GetHeight()));
            int lines = 1;
            if (!string.IsNullOrEmpty(_text) && Width > Ui.S(60))
            {
                try
                {
                    using (var g = CreateGraphics())
                    {
                        int avail = Math.Max(Ui.S(60), Width - Ui.S(56) - Ui.S(40));
                        Size sz = TextRenderer.MeasureText(g, _text, Font, new Size(avail, 4000),
                            TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                        lines = (int)Math.Round(sz.Height / (double)lineH);
                    }
                }
                catch { lines = 1; }
            }
            /* Three lines was the cap, and a message longer than that was simply cut off: the
               paint asks for WordBreak without EndEllipsis, so the tail vanished mid sentence
               with nothing to say there was more. Five lines is enough for every message the app
               shows, and the timer still dismisses it. */
            _lines = Math.Max(1, Math.Min(5, lines));
            int target = Ui.S(52) + (_lines - 1) * lineH;
            if (Height != target) Height = target;
            if (_bottom > 0) Top = _bottom - Height;
        }

        protected override void OnClick(EventArgs e) { _timer.Stop(); Hide(); base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        /* Hovering the toast stops the countdown, so leaving must start it again - the old code
           only cleared the flag and the message stayed open forever. */
        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            if (Visible) { _timer.Interval = 1800; _timer.Start(); }
            base.OnMouseLeave(e);
        }

        public void ReStyle() { Font = Theme.Body; FitHeight(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            /* A floating panel, not a tinted strip. The bar is a solid raised surface with a
               hairline, a round icon chip in the status colour and the message beside it, so a
               warning for one second reads as a warning and not as a coloured background with
               text on it. The 4 px radius was the giveaway that it was a rectangle. */
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Ui.S(Theme.ButtonRadius);
            Ui.Shadow(g, r, rad, 2, Theme.Dark);
            Ui.Fill(g, r, rad, Theme.Dark ? Theme.Panel : Theme.Card);
            Ui.Stroke(g, r, rad, Theme.Line);

            Color tint = _kind == ToastKind.Error ? Theme.Danger
                : _kind == ToastKind.Warn ? Theme.Warn
                : _kind == ToastKind.Info ? Theme.Accent : Theme.Success;
            string icon = _kind == ToastKind.Error ? "error"
                : _kind == ToastKind.Warn ? "alert"
                : _kind == ToastKind.Info ? "info" : "check-circle";
            float chip = Ui.S(30);
            var cr = new RectangleF(Ui.S(12), (Height - chip) / 2f, chip, chip);
            Ui.Fill(g, cr, chip / 2f, Color.FromArgb(Theme.Dark ? 40 : 28, tint));
            float ic = Ui.S(17);
            Icons.Draw(g, icon, new RectangleF(cr.X + (chip - ic) / 2f, cr.Y + (chip - ic) / 2f, ic, ic), tint);

            float textX = cr.Right + Ui.S(12);
            float lineH = Font.GetHeight(g);
            if (_lines <= 1)
                Ui.Text(g, _text, Font, Theme.Text, textX, (Height - lineH) / 2f, Width - textX - Ui.S(32));
            else
                /* WordBreak without EndEllipsis draws nothing for the part that does not fit, so
                   a message one line longer than the box silently loses its tail. EndEllipsis
                   cannot be combined with WordBreak, so the width is what is trusted here and the
                   line count above is measured from the same font and width. */
                Ui.Text(g, _text, Font, Theme.Text, textX, (Height - lineH * _lines) / 2f,
                    Math.Max(Ui.S(80), Width - textX - Ui.S(32)), "left", _lines);

            float close = Ui.S(16);
            Icons.Draw(g, "close", new RectangleF(Width - Ui.S(14) - close, (Height - close) / 2f, close, close),
                _hover ? Theme.Text : Theme.TextFaint);
        }
    }

    /// <summary>Inline text link with hover feedback.</summary>
    internal sealed class JBLink : Control, IReStyle
    {
        private bool _hover;

        public JBLink()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Ui.Opaque(this);
            Cursor = Cursors.Hand;
            Font = Theme.Small;
            ForeColor = Theme.AccentText;
            TabStop = true;
            AutoSize = false;
            /* A link with no explicit size would be a zero-width control - invisible and
               unclickable. Measuring from Text keeps every call site correct without
               remembering to call Fit(). */
            Height = Ui.S(22);
        }

        public override string Text
        {
            get { return base.Text; }
            set { base.Text = value; Fit(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public void Fit()
        {
            // Measure without CreateGraphics(): this runs from the Text setter, which can fire
            // before the control has a window handle. TextRenderer's static overload uses the
            // screen DC and is safe at any point in the lifetime.
            if (string.IsNullOrEmpty(Text)) return;
            Size sz = TextRenderer.MeasureText(Text, Font, new Size(4000, 4000),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Size = new Size(sz.Width + Ui.S(6), Ui.S(22));
        }

        public void ReStyle() { ForeColor = Theme.AccentText; Fit(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            bool focused = Ui.FocusCues(this);
            Color c = _hover || focused ? Theme.AccentHi : Theme.AccentText;
            Ui.Text(g, Text, Font, c, Ui.S(2), (Height - Font.GetHeight(g)) / 2f, Width - Ui.S(4));
            if (_hover || focused)
            {
                using (var pen = new Pen(c, Ui.Hair))
                {
                    float y = Height - Ui.S(4);
                    float w = Ui.Measure(g, Text, Font).Width;
                    g.DrawLine(pen, Ui.S(2), y, Ui.S(2) + w, y);
                }
            }
        }
    }

    /// <summary>
    /// windows-ui navbar theme switch: a sun/moon glyph with the current mode beside it.
    /// Clicking the row flips the theme.
    /// </summary>
    internal sealed class JBThemeSwitch : Control, IReStyle
    {
        private bool _checked, _hover;

        /// <summary>Localisable caption for each state.</summary>
        public string NightText = "Night Mode";
        public string DayText = "Day Mode";

        public event EventHandler CheckedChanged;

        public JBThemeSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            Ui.Opaque(this);
            Height = Ui.S(42);
            TabStop = true;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        public void ReStyle() { Font = Theme.Body; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0, 0, Width, Height);
            if (_hover) Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), Theme.HoverStrong);

            float s = Ui.S(22);
            Icons.Draw(g, _checked ? "moon" : "sun", new RectangleF(Ui.S(15), (Height - s) / 2f, s, s), Theme.Text);
            Ui.Text(g, _checked ? NightText : DayText, Theme.Body, Theme.Text,
                Ui.S(47), (Height - Theme.Body.GetHeight(g)) / 2f, Width - Ui.S(58));

            if (Ui.FocusCues(this)) Ui.FocusRing(g, r, Ui.S(Theme.ButtonRadius));
        }
    }

    /// <summary>Painted card surface used to group settings and dialog sections.</summary>
    internal sealed class JBPanel : Panel, IReStyle
    {
        private bool _hover;

        /// <summary>Y offsets of the hairlines that separate rows inside the card.</summary>
        public float[] Dividers;

        /// <summary>Full-bleed containers such as settings sections must not light up as one block.</summary>
        public bool Hoverable = true;

        public JBPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
        }

        protected override void OnMouseEnter(EventArgs e) { if (Hoverable) { _hover = true; Invalidate(); } base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { if (!_hover) { base.OnMouseLeave(e); return; } _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            /* The rounded corners have to show the surface BEHIND the card, not a hard-coded
               Theme.Bg: a card nested in a page or dialog with a different backdrop had four
               bright corner pixels that read as a rendering artefact. */
            g.Clear(Ui.SurfaceColor(this));
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            /* A settings section is a card on the page, so it uses the card surface and the card
               radius. It used Theme.Panel, which is the INPUT surface: on the new ramp a panel is
               a shade above a card, and every section then looked like a field. */
            Ui.Fill(g, r, Theme.RLarge, _hover ? Color.FromArgb(255, Theme.PanelHover) : Theme.Card);
            Ui.Stroke(g, r, Theme.RLarge, _hover ? Theme.HoverStrong : Theme.Line);
            if (Dividers == null) return;
            /* Dividers stop short of the rounded corners and are drawn on a half pixel, so each
               is exactly one device row rather than a two row grey band. */
            using (var pen = new Pen(Theme.LineSoft, Ui.Hair))
                for (int i = 0; i < Dividers.Length; i++)
                {
                    float dy = (float)Math.Round((double)Dividers[i]) + 0.5f;
                    g.DrawLine(pen, Ui.S(20), dy, Width - Ui.S(21), dy);
                }
        }

        public void ReStyle() { BackColor = Theme.Card; Invalidate(); }
    }
    /// <summary>
    /// Themed single-column chooser. Replaces System.Windows.Forms.ListBox wherever one would
    /// have been used: a ListBox paints every row in the system theme and scrolls with the
    /// system scrollbar, and this application allows neither. Selection, keyboard handling and
    /// the overlay scroll indicator are all drawn here.
    /// </summary>
    internal sealed class JBList : Control, IReStyle
    {
        public sealed class Item
        {
            public string Text;
            public string Detail;
            public object Tag;
            public Item(string text, string detail, object tag) { Text = text; Detail = detail; Tag = tag; }
        }

        private readonly List<Item> _items = new List<Item>();
        private int _selected = -1;
        private int _hoverIndex = -1;
        private int _scroll;
        private bool _thumbHover, _dragging;
        private Point _dragStart;
        private int _dragScrollStart;
        private DateTime _barShownAt = DateTime.MinValue;

        public event EventHandler SelectedIndexChanged;

        public JBList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Input;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = (value >= 0 && value < _items.Count) ? value : -1;
                if (_selected == v) return;
                _selected = v;
                EnsureVisible(v);
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public Item SelectedItem { get { return _selected >= 0 && _selected < _items.Count ? _items[_selected] : null; } }

        public void SetItems(IEnumerable<Item> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _selected = -1;
            _scroll = 0;
            Invalidate();
        }

        private int RowH { get { return Ui.S(46); } }
        private int MaxScroll { get { return Math.Max(0, _items.Count * RowH - Height); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_scroll > MaxScroll) _scroll = MaxScroll;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            g.Clear(Theme.Input);

            if (_items.Count == 0)
            {
                Ui.Text(g, L10n.T("list.empty"), Theme.Body, Theme.TextFaint,
                    0, (Height - Theme.Body.GetHeight(g)) / 2f, Width, "center");
                return;
            }

            int rowH = RowH;
            int innerW = Math.Max(Ui.S(40), Width - Ui.S(10));
            for (int i = 0; i < _items.Count; i++)
            {
                float y = i * rowH - _scroll;
                if (y + rowH < 0 || y > Height) continue;
                var r = new RectangleF(Ui.S(4), y + 1f, Width - Ui.S(8), rowH - 2f);
                bool sel = i == _selected;
                bool hov = i == _hoverIndex;
                if (sel) Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), Theme.AccentOnBg);
                else if (hov) Ui.Fill(g, r, Ui.S(Theme.ButtonRadius), Theme.HoverOverlay);

                var it = _items[i];
                float textLeft = r.X + Ui.S(10);
                float textW = innerW - Ui.S(16);
                float titleY = y + (it.Detail == null ? (rowH - Theme.Body.GetHeight(g)) / 2f : Ui.S(6));
                Ui.Text(g, it.Text ?? "", Theme.Body, sel ? Theme.AccentText : Theme.Text, textLeft, titleY, textW);
                if (it.Detail != null)
                    Ui.Text(g, it.Detail, Theme.Caption, Theme.TextFaint,
                        textLeft, y + rowH - Theme.Caption.GetHeight(g) - Ui.S(6), textW);
            }

            /* Always shown when the list is longer than the box. A hint that appears only while
               scrolling cannot tell a user that a list is long before they try to scroll it, and
               a cut-off list with no bar is read as lost data. */
            bool showBar = MaxScroll > 0;
            if (showBar)
            {
                var hit = ThumbRect();
                if (!hit.IsEmpty)
                {
                    var bar = new RectangleF(Width - Ui.S(9), hit.Y, Ui.S(5), hit.Height);
                    Ui.Fill(g, bar, Ui.S(Theme.ButtonRadius), _dragging || _thumbHover ? Theme.Accent : Theme.Line);
                }
            }

            if (Focused && Ui.FocusCues(this))
                Ui.FocusRing(g, new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), Ui.S(Theme.ButtonRadius));
        }

        private RectangleF ThumbRect()
        {
            if (MaxScroll <= 0) return RectangleF.Empty;
            float track = Height - Ui.S(8);
            float frac = Math.Min(1f, Height / (float)(_items.Count * RowH));
            float thumbH = Math.Max(Ui.S(28), track * frac);
            float pos = Ui.S(4) + (track - thumbH) * (_scroll / (float)MaxScroll);
            return new RectangleF(Width - Ui.S(13), pos, Ui.S(12), thumbH);
        }

        private void EnsureVisible(int index)
        {
            if (index < 0) return;
            int top = index * RowH;
            int bottom = top + RowH;
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + Height) _scroll = bottom - Height;
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll));
            _barShownAt = DateTime.UtcNow;
        }

        private void ScrollTo(int value, bool reveal)
        {
            int v = Math.Max(0, Math.Min(MaxScroll, value));
            if (v == _scroll) return;
            _scroll = v;
            if (reveal) _barShownAt = DateTime.UtcNow;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            Ui.KeyboardCuesForced = false;
            if (ThumbRect().Contains(e.Location))
            {
                _dragging = true;
                _dragStart = e.Location;
                _dragScrollStart = _scroll;
                Invalidate();
                return;
            }
            int index = (_scroll + e.Y) / RowH;
            if (index >= 0 && index < _items.Count) SelectedIndex = index;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                var hit = ThumbRect();
                float track = Height - Ui.S(8);
                float perPx = MaxScroll / Math.Max(1f, track - hit.Height);
                ScrollTo(_dragScrollStart + (int)Math.Round((e.Y - _dragStart.Y) * perPx), true);
                return;
            }
            bool thumb = ThumbRect().Contains(e.Location);
            if (thumb != _thumbHover) { _thumbHover = thumb; Invalidate(); }
            int index = (_scroll + e.Y) / RowH;
            if (index >= _items.Count) index = -1;
            if (index != _hoverIndex) { _hoverIndex = index; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging) { _dragging = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _thumbHover = false;
            _hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(_scroll - (e.Delta / 120) * (RowH / 2), true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Home ||
                   keyData == Keys.End || keyData == Keys.PageUp || keyData == Keys.PageDown ||
                   base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int page = Math.Max(1, Height / RowH);
            switch (e.KeyCode)
            {
                case Keys.Up: SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; break;
                case Keys.Down: SelectedIndex = Math.Min(_items.Count - 1, _selected + 1); e.Handled = true; break;
                case Keys.Home: SelectedIndex = 0; e.Handled = true; break;
                case Keys.End: SelectedIndex = _items.Count - 1; e.Handled = true; break;
                case Keys.PageUp: SelectedIndex = Math.Max(0, _selected - page); e.Handled = true; break;
                case Keys.PageDown: SelectedIndex = Math.Min(_items.Count - 1, _selected + page); e.Handled = true; break;
            }
            if (!e.Handled) base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        public void ReStyle() { BackColor = Theme.Input; Invalidate(); }
    }

}
