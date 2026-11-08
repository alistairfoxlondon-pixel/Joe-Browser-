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
    internal enum LabelRole { Title, Body, Dim, Faint, Accent, Strong, Mono }

    /// <summary>Label that remembers its role so a theme flip restores the right contrast.</summary>
    internal sealed class JBLabel : Label, IReStyle
    {
        public LabelRole Role = LabelRole.Body;

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
                case LabelRole.Mono: Font = Theme.MonoBody; ForeColor = Theme.Text; break;
            }
            Invalidate();
        }

        public void ReStyle() { Apply(); }

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

        public static void Attach(Control owner, string text)
        {
            if (owner == null || string.IsNullOrEmpty(text)) return;
            _tip.SetToolTip(owner, text);
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

        public const int WM_SETICON = 0x0080;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const int EM_SETCUEBANNER = 0x1501;
        public const int EM_LINESCROLL = 0x00B6;
        public const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        public const int EM_GETLINECOUNT = 0x00BA;

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
            _onModeChanged = delegate { ReStyle(); };
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
            Ui.Scale = Math.Max(1f, e.DeviceDpiNew / 96f);
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
            using (var path = Ui.Round(new RectangleF(0, 0, strip.Width, strip.Height), Ui.S(8)))
                strip.Region = new Region(path);
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

            // Items carry a glyph name rather than a bitmap, so draw it beside the label.
            var item = e.Item as IIconItem;
            if (item != null && item.Icon != null)
            {
                var g = e.Graphics;
                Ui.Quality(g);
                float s = Ui.S(16);
                float x = e.TextRectangle.Left - s - Ui.S(9);
                float y = e.Item.Height / 2f - s / 2f;
                Icons.Draw(g, item.Icon, new RectangleF(x, y, s, s),
                    !e.Item.Enabled ? Theme.TextFaint : e.Item.Selected ? Theme.Accent : Theme.TextDim, 1.7f);
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            Ui.Quality(e.Graphics);
            if (e.Image != null)
            {
                var r = e.ImageRectangle;
                if (r.Width <= 0 || r.Height <= 0)
                    r = new Rectangle(Ui.S(9), (e.Item.Height - e.Image.Height) / 2, e.Image.Width, e.Image.Height);
                e.Graphics.DrawImage(e.Image, r);
                return;
            }
            var item = e.Item as IIconItem;
            if (item == null || item.Icon == null) return;
            float s = Ui.S(16);
            Color c = e.Item.Selected ? Theme.Accent : Theme.TextFaint;
            Icons.Draw(e.Graphics, item.Icon, new RectangleF(Ui.S(11), e.Item.Height / 2f - s / 2f, s, s), c, 1.7f);
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
                Padding = new Padding(Ui.S(14), Ui.S(7), Ui.S(18), Ui.S(7));
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
            m.ShowImageMargin = true;
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
        public int Radius = 4;
        private string _tip = "";
        public string Tip { get { return _tip; } set { _tip = value ?? ""; ToolTips.Attach(this, _tip); } }
        private bool _hover, _down;

        /// <summary>Width needed for this button's icon and label, so text is never clipped.</summary>
        public int PreferredWidth(int minimum)
        {
            using (var g = CreateGraphics())
            {
                Font f = Font ?? Theme.Body;
                float w = Ui.Measure(g, Text ?? "", f).Width;
                if (!string.IsNullOrEmpty(Icon) || !string.IsNullOrEmpty(BrandIcon)) w += Ui.S(16 + 8);
                return Math.Max(minimum, (int)Math.Ceiling(w) + Ui.S(24));
            }
        }

        public JBButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            ForeColor = Theme.Text;
            Ui.Opaque(this);
            TabStop = true;
            Height = Ui.S(33);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
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
                    bg = !Enabled ? Theme.Panel2 : _down ? Theme.Accent2 : _hover ? Theme.AccentHi : Theme.Accent;
                    border = Ui.Blend(bg, Color.Black, 0.2f); showBorder = true;
                    fg = Enabled ? Theme.OnAccent : Theme.TextFaint;
                    break;

                case BtnKind.Soft:
                    bg = !Enabled ? Theme.Panel2 : _down ? Theme.ActiveBg : _hover ? Theme.PanelHover : Theme.ButtonBg;
                    border = Theme.ButtonBorder; showBorder = true;
                    fg = Enabled ? Theme.Text : Theme.TextFaint;
                    break;

                case BtnKind.Danger:
                    bg = _down ? Ui.Blend(Theme.Danger, Color.Black, 0.16f) : _hover ? Ui.Blend(Theme.Danger, Color.White, 0.10f) : Theme.Danger;
                    border = Ui.Blend(Theme.Danger, Color.Black, 0.2f); showBorder = true;
                    fg = Theme.InkText;
                    break;

                case BtnKind.Nav:
                    bg = Selected || _hover ? Theme.HoverStrong : Color.Transparent;
                    fg = Theme.Text;
                    break;

                default:
                    bg = _down ? Theme.SelOverlay : _hover ? Theme.HoverOverlay : Color.Transparent;
                    fg = Enabled ? Theme.Text : Theme.TextFaint;
                    break;
            }

            if (bg != Color.Transparent) Ui.Fill(g, r, rad, bg);
            if (showBorder) Ui.Stroke(g, r, rad, border, 1f);

            if (Kind == BtnKind.Nav && Selected)
                Ui.Fill(g, new RectangleF(0, (Height - Ui.S(20)) / 2f, Ui.S(3), Ui.S(20)), Ui.S(1.5f), Theme.Accent);

            bool hasText = !string.IsNullOrEmpty(Text);
            bool hasIcon = !string.IsNullOrEmpty(Icon) || !string.IsNullOrEmpty(BrandIcon);

            if (Kind == BtnKind.Nav)
            {
                // windows-ui navbar row: 15px padding, 22px icon, 10px gap, 15px label.
                float navIcon = Ui.S(22);
                if (hasIcon)
                {
                    var box = new RectangleF(Ui.S(15), (Height - navIcon) / 2f, navIcon, navIcon);
                    if (BrandIcon != null) BrandIcons.Draw(g, BrandIcon, box);
                    else Icons.Draw(g, Icon, box, fg);
                }
                Font navFont = Selected ? Theme.BodyBold : Theme.Body;
                Ui.Text(g, Text, navFont, fg, Ui.S(47), (Height - navFont.GetHeight(g)) / 2f, Width - Ui.S(60));
            }
            else
            {
                float iconSize = Ui.S(16);
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
                    if (hasText) Ui.Text(g, Text, Font, fg, x, (Height - Font.GetHeight(g)) / 2f);
                }
            }

            if (Focused && Ui.FocusCues(this))
            {
                /* A text-coloured ring drawn on top of a filled accent button is nearly
                   invisible; the readable equivalent is a ring just OUTSIDE the fill. */
                if (Kind == BtnKind.Primary)
                {
                    var outer = new RectangleF(r.X - Ui.S(2.5f), r.Y - Ui.S(2.5f),
                        r.Width + Ui.S(5f), r.Height + Ui.S(5f));
                    Ui.Stroke(g, outer, rad + Ui.S(2.5f), Ui.Blend(Theme.Accent, Theme.Text, 0.35f), Ui.S(1.5f));
                }
                else Ui.FocusRing(g, r, rad);
            }
        }
    }

    internal class JBIconButton : Control, IReStyle
    {
        public string Icon = "dots";
        public int IconSize = 18;
        private string _tip = "";
        public string Tip { get { return _tip; } set { _tip = value ?? ""; ToolTips.Attach(this, _tip); } }
        public bool Danger;
        /// <summary>Caption buttons use the flat Windows 11 title-bar treatment.</summary>
        public bool Caption;
        private bool _hover, _down;

        public JBIconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Ui.Opaque(this);
            TabStop = true;
            Size = new Size(Ui.S(32), Ui.S(32));
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

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
                else if (_hover || _down) Ui.Fill(g, r, 0f, Theme.HoverStrong);
                ic = hotClose ? Color.White : Theme.Text;
            }
            else
            {
                if (_down) Ui.Fill(g, r, rad, Theme.SelOverlay);
                else if (_hover) Ui.Fill(g, r, rad, Theme.HoverOverlay);
                ic = _hover || Focused ? Theme.Text : Theme.TextDim;
            }

            if (Caption && (Icon == "max" || Icon == "restore"))
            {
                // Windows caption squares are plain rectangles, not the Material fullscreen arrows.
                float s = Ui.S(10);
                float sx = (Width - s) / 2f, sy = (Height - s) / 2f;
                using (var pen = new Pen(ic, 1f))
                {
                    if (Icon == "restore")
                    {
                        s = Ui.S(8);
                        sx = (Width - s) / 2f + Ui.S(1);
                        sy = (Height - s) / 2f + Ui.S(1);
                        g.DrawRectangle(pen, sx, sy, s, s);
                        g.DrawRectangle(pen, sx - Ui.S(2), sy - Ui.S(2), s, s);
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
                    _trailing.IconSize = 15;
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

        public int PadLeft = 10, PadRight = 10, PadTop = 6;

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
            float rad = Ui.S(4);
            Ui.Fill(g, r, rad, Theme.Input);
            Ui.Stroke(g, r, rad, Theme.Line, 1f);

            // windows-ui keeps the 1px frame and lights a 1px accent rule under the field.
            float underlineY = Height - 1.5f;
            using (var pen = new Pen(Tapped ? Theme.Accent : Theme.InputUnderline, 1f))
                g.DrawLine(pen, Ui.S(5), underlineY, Width - Ui.S(5), underlineY);

            if (!string.IsNullOrEmpty(_icon))
            {
                float s = Ui.S(16);
                // A tall multiline field has to keep its icon on the first text row, not
                // floating in the vertical middle of the box.
                float iconY = _multiline ? Ui.S(PadTop) : (Height - s) / 2f;
                Icons.Draw(g, _icon, new RectangleF(Ui.S(10), iconY, s, s),
                    _focused ? Theme.Text : Theme.TextFaint);
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
            // Resume the countdown only if the bar is still up and was paused by the hover.
            if (Visible && !_timer.Enabled)
            {
                _timer.Interval = 1800;
                _timer.Start();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_multiline)
            {
                /* Scroll the field only while it still has somewhere to go; once it is at the
                   top or the bottom the gesture belongs to the page behind it. Swallowing every
                   notch meant the wheel died whenever the pointer happened to be over a notes
                   box - a very common position - even though the page was scrollable. */
                int before = FirstVisibleLine();
                _wheelCarry += e.Delta;
                int lines = _wheelCarry / 120;
                if (lines != 0)
                {
                    _wheelCarry -= lines * 120;
                    Native.SendMessage(Box.Handle, Native.EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
                }
                Invalidate();
                if (FirstVisibleLine() != before) return;   // the field moved: it took the gesture
                _wheelCarry = 0;
            }
            base.OnMouseWheel(e);
        }

        /// <summary>First visible line of a multiline field, or 0 when it cannot be read.</summary>
        private int FirstVisibleLine()
        {
            try
            {
                return (int)Native.SendMessage(Box.Handle, Native.EM_GETFIRSTVISIBLELINE,
                    IntPtr.Zero, IntPtr.Zero).ToInt64();
            }
            catch { return 0; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBox();
        }

        private void LayoutBox()
        {
            if (!IsHandleCreated) return;
            int l = Ui.S(PadLeft) + (string.IsNullOrEmpty(_icon) ? 0 : Ui.S(22));
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
            Height = Ui.S(33);
            TabStop = true;
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
            if (keyData == Keys.F4 && Items.Count > 0)
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
            e.Handled = true;
            base.OnKeyPress(e);
        }

        private void CloseMenu()
        {
            _open = false;
            var m = _menu;
            _menu = null;
            if (m != null) { try { m.Close(); } catch { } }
            Invalidate();
        }

        private void ShowMenu()
        {
            if (Items.Count == 0) return;
            /* Clicking the control while its popup is up used to dispose the strip that was
               still on screen and build a second one on the same tick, so the list flickered and
               could be left ownerless. The open popup owns that gesture: the click closes it. */
            if (_open) { CloseMenu(); return; }
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
            int rows = Math.Max(6, Math.Min(20, (Screen.FromControl(this).WorkingArea.Height - Ui.S(120)) / Ui.S(34)));
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
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Ui.S(4);
            Ui.Fill(g, r, rad, Theme.Input);
            Ui.Stroke(g, r, rad, Theme.Line, 1f);
            using (var pen = new Pen(_open || Focused ? Theme.Accent : Theme.InputUnderline, 1f))
                g.DrawLine(pen, Ui.S(5), Height - 1.5f, Width - Ui.S(5), Height - 1.5f);

            float left = Ui.S(10);
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
            Ui.Text(g, text, Font, fg, left, (Height - Font.GetHeight(g)) / 2f, Width - left - Ui.S(36));

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

            if (_checked) Ui.Fill(g, track, trackH / 2f, Theme.Accent);
            else
            {
                if (_hover) Ui.Fill(g, track, trackH / 2f, Theme.HoverOverlay);
                // Only the off state needs an outline; over the filled track a darker hairline
                // read as a dirty edge rather than a border.
                Ui.Stroke(g, track, trackH / 2f, Theme.TextDim, 1f);
            }

            float knobD = Ui.S(13);
            float ky = track.Y + (trackH - knobD) / 2f;
            float kx = _checked ? track.Right - knobD - Ui.S(3) : track.X + Ui.S(3);
            using (var b = new SolidBrush(_checked ? Theme.OnAccent : Theme.TextDim))
                g.FillEllipse(b, kx, ky, knobD, knobD);

            if (!string.IsNullOrEmpty(Text))
                Ui.Text(g, Text, Font, Enabled ? Theme.Text : Theme.TextFaint, trackW + Ui.S(10), (h - Font.GetHeight(g)) / 2f, Math.Max(0, Width - trackW - Ui.S(10)));

            if (Ui.FocusCues(this))
                Ui.FocusRing(g, track, trackH / 2f);
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

            // Windows 11 pivot tabs: one hairline rule, the selected label carries an accent bar.
            float rule = Height - Ui.S(1);
            using (var b = new SolidBrush(Theme.Line))
                g.FillRectangle(b, 0, rule, Width, Ui.S(1));

            float seg = Width / (float)Items.Count;
            for (int i = 0; i < Items.Count; i++)
            {
                var cell = new RectangleF(i * seg, 0, seg, rule);
                bool sel = i == _selected;
                if (!sel && i == _hover) Ui.Fill(g, cell, 0f, Theme.HoverOverlay);

                Font f = sel ? Theme.BodyBold : Theme.Body;
                Color fg = sel ? Theme.Text : i == _hover ? Theme.Text : Theme.TextDim;
                float ty = (rule - f.GetHeight(g)) / 2f;
                Ui.Text(g, Items[i], f, fg, cell.X, ty, cell.Width, "center");

                if (sel)
                {
                    float tw = Math.Min(cell.Width - Ui.S(8), Ui.Measure(g, Items[i], f).Width + Ui.S(10));
                    float barH = Ui.S(2f);
                    using (var b = new SolidBrush(Theme.Accent))
                        g.FillRectangle(b, cell.X + (cell.Width - tw) / 2f, rule - barH, tw, barH);
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
            _fade = new Timer { Interval = 350 };
            _fade.Tick += delegate
            {
                if (_thumbHover || _dragging) return;
                if ((DateTime.UtcNow - _barShownAt).TotalMilliseconds < 900) return;
                _fade.Stop();
                Invalidate();
            };
        }

        private bool ShowBar
        {
            get
            {
                return MaxOffset > 0 &&
                       (_thumbHover || _dragging || (DateTime.UtcNow - _barShownAt).TotalMilliseconds < 900);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _fade != null) _fade.Dispose();
            base.Dispose(disposing);
        }

        public int Offset { get { return _offset; } }
        public int MaxOffset { get { return Math.Max(0, _content.Height - Height); } }
        public int ContentWidth { get { return _content.Width; } }

        /// <summary>Re-clamps the offset after the content changed size.</summary>
        public void SyncExtent()
        {
            LayoutContent();
            ScrollToQuiet(_offset);
            Invalidate();
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
            Ui.Fill(e.Graphics, bar, Ui.S(2.5f), _dragging || _thumbHover ? Theme.Accent : Theme.Line);
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
            if (h != _thumbHover) { _thumbHover = h; Invalidate(); }
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
    internal sealed class JBGrid : Panel, IReStyle
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

        public int Columns { get; private set; }
        public int Rows { get; private set; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutGrid();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            LayoutGrid();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            LayoutGrid();
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
                        int avail = Math.Max(Ui.S(60), Width - Ui.S(56));
                        Size sz = TextRenderer.MeasureText(g, _text, Font, new Size(avail, 4000),
                            TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                        lines = (int)Math.Round(sz.Height / (double)lineH);
                    }
                }
                catch { lines = 1; }
            }
            _lines = Math.Max(1, Math.Min(3, lines));
            int target = Ui.S(44) + (_lines - 1) * lineH;
            if (Height != target) Height = target;
            if (_bottom > 0) Top = _bottom - Height;
        }

        protected override void OnClick(EventArgs e) { _timer.Stop(); Hide(); base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        public void ReStyle() { Font = Theme.Body; FitHeight(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Ui.S(4);

            // windows-ui alert bar: a 1px tinted surface with a leading status icon.
            Color wash = _kind == ToastKind.Error ? Color.FromArgb(0xE8, 0x47, 0x56)
                : _kind == ToastKind.Warn ? Color.FromArgb(0xF2, 0xA8, 0x1B)
                : _kind == ToastKind.Info ? Color.FromArgb(0x60, 0xCD, 0xFF) : Color.FromArgb(0x57, 0xE8, 0x4A);
            Ui.Fill(g, r, rad, Ui.Blend(Theme.Dark ? Theme.Panel : Theme.Bg, wash, Theme.Dark ? 0.18f : 0.14f));
            Ui.Stroke(g, r, rad, Color.FromArgb(Theme.Dark ? 38 : 26, Theme.Text), 1f);

            string icon = _kind == ToastKind.Error ? "error"
                : _kind == ToastKind.Warn ? "alert"
                : _kind == ToastKind.Info ? "info" : "check-circle";
            Color tint = _kind == ToastKind.Error ? Theme.Danger
                : _kind == ToastKind.Warn ? Color.FromArgb(0xF2, 0xA8, 0x1B)
                : _kind == ToastKind.Info ? Theme.Accent : Theme.Success;
            float ic = Ui.S(16);
            Icons.Draw(g, icon, new RectangleF(Ui.S(14), (Height - ic) / 2f, ic, ic), tint);

            float lineH = Font.GetHeight(g);
            if (_lines <= 1)
                Ui.Text(g, _text, Font, Theme.Text, Ui.S(40), (Height - lineH) / 2f, Width - Ui.S(56));
            else
                Ui.Text(g, _text, Font, Theme.Text, Ui.S(40), (Height - lineH * _lines) / 2f,
                    Width - Ui.S(56), "left", _lines);

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
                using (var pen = new Pen(c, 1f))
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
            if (_hover) Ui.Fill(g, r, Ui.S(4), Theme.HoverStrong);

            float s = Ui.S(22);
            Icons.Draw(g, _checked ? "moon" : "sun", new RectangleF(Ui.S(15), (Height - s) / 2f, s, s), Theme.Text);
            Ui.Text(g, _checked ? NightText : DayText, Theme.Body, Theme.Text,
                Ui.S(47), (Height - Theme.Body.GetHeight(g)) / 2f, Width - Ui.S(58));

            if (Ui.FocusCues(this)) Ui.FocusRing(g, r, Ui.S(4));
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
            BackColor = Theme.Panel;
        }

        protected override void OnMouseEnter(EventArgs e) { if (Hoverable) { _hover = true; Invalidate(); } base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { if (!_hover) { base.OnMouseLeave(e); return; } _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Quality(g);
            g.Clear(Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.Fill(g, r, Theme.RLarge, _hover ? Color.FromArgb(255, Theme.PanelHover) : Theme.Panel);
            Ui.Stroke(g, r, Theme.RLarge, Theme.Line, 1f);
            if (Dividers == null) return;
            using (var pen = new Pen(Theme.Line))
                for (int i = 0; i < Dividers.Length; i++)
                {
                    float dy = (float)Math.Round(Dividers[i]) + 0.5f;
                    g.DrawLine(pen, Ui.S(18), dy, Width - Ui.S(19), dy);
                }
        }

        public void ReStyle() { BackColor = Theme.Panel; Invalidate(); }
    }
}
