using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Small themed dialog used by the Tools &amp; data actions. Content is a vertical stack,
    /// so fields never overlap at narrow widths or high DPI. The body scrolls when needed.
    /// Not sealed: GoogleSetupDialog extends it to add the guided OAuth-client setup, which is
    /// a form of the same shape (notes, sections, a primary action) rather than a new one.
    /// </summary>
    internal class ToolDialog : JBForm
    {
        private sealed class Row
        {
            public Control Label;
            public Control Field;
            public int Height;
        }

        private readonly JBLabel _title;
        private readonly JBIconButton _close;
        private readonly Panel _content;
        private readonly JBScrollHost _body;
        private readonly List<Row> _rows = new List<Row>();
        private readonly string _primaryText;

        public JBButton Primary { get; private set; }
        public JBButton Secondary { get; private set; }
        /// <summary>The panel whose children are laid out in rows: add controls to this.</summary>
        public Panel Body { get { return _content; } }
        public bool Confirmed { get; private set; }

        public ToolDialog(string title, string primaryText, string secondaryText, int width, int height)
        {
            _primaryText = primaryText ?? "OK";
            Text = title;
            Resizable = true;
            DraggableSidebar = false;
            HeaderHit = 56;
            KeyPreview = true;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MinimumSize = new Size(Ui.S(430), Ui.S(320));
            ClientSize = new Size(Ui.S(width), Ui.S(height));

            _title = new JBLabel(LabelRole.Title, title);
            Controls.Add(_title);

            _close = new JBIconButton { Icon = "close", Danger = true, IconSize = 13, Tip = "Close" };
            _close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_close);

            /* The body scrolls through JBScrollHost, the app's own overlay scroller, rather than
               through a Panel with AutoScroll: that drew the system scrollbar - a grey native
               strip with arrow buttons - inside a window that is otherwise drawn end to end.
               Nothing in this application may show a native scrollbar. */
            _content = new Panel { BackColor = Theme.Bg, AutoSize = false };
            _body = new JBScrollHost(_content) { BackColor = Theme.Bg };
            Controls.Add(_body);

            Primary = new JBButton { Kind = BtnKind.Primary, Text = _primaryText, Radius = 4, Height = Ui.S(33) };
            Primary.Click += delegate { OnPrimary(); };
            Controls.Add(Primary);

            Secondary = new JBButton { Kind = BtnKind.Ghost, Text = secondaryText ?? "Cancel", Radius = 4, Height = Ui.S(33) };
            Secondary.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(Secondary);
        }

        /// <summary>Set to validate before the dialog closes. Return false to keep it open.</summary>
        public Func<bool> Validator;

        /* Guards against a second commit from the same gesture: a double-click on the primary
           button, or Enter reaching both the command-key pass and KeyPreview. Running the
           validator twice showed two toasts and, for anything that writes, did the work twice. */
        private bool _submitted;

        private void OnPrimary()
        {
            if (_submitted) return;
            if (Validator != null && !Validator()) return;
            _submitted = true;
            Confirmed = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        public JBLabel AddSection(string text)
        {
            var l = new JBLabel(LabelRole.Accent, text);
            AddRow(null, l, Ui.S(18));
            return l;
        }

        public JBLabel AddNote(string text)
        {
            var l = new JBLabel(LabelRole.Faint, text);
            l.Wrap = true;
            l.TextAlign = ContentAlignment.TopLeft;
            AddRow(null, l, Ui.S(34));
            return l;
        }

        public JBTextBox AddField(string label, string value, string placeholder, string icon,
            bool multiline, int height)
        {
            if (!string.IsNullOrEmpty(label)) AddRow(new JBLabel(LabelRole.Accent, label), null, Ui.S(18));
            var t = new JBTextBox(multiline);
            t.Value = value ?? "";
            t.Placeholder = placeholder ?? "";
            t.Icon = icon;
            if (multiline) t.PadTop = 8;   // unscaled; paint applies Ui.S once
            AddRow(null, t, Ui.S(height));
            return t;
        }

        public JBSelect AddSelect(string label, IEnumerable<string> items, int selected, string icon)
        {
            if (!string.IsNullOrEmpty(label)) AddRow(new JBLabel(LabelRole.Accent, label), null, Ui.S(18));
            var s = new JBSelect { Icon = icon };
            var list = new List<string>();
            if (items != null) list.AddRange(items);
            s.SetItems(list, selected);
            AddRow(null, s, Ui.S(33));
            return s;
        }

        public JBToggle AddToggle(string text, bool value)
        {
            var t = new JBToggle { Text = text, Checked = value };
            AddRow(null, t, Ui.S(28));
            return t;
        }

        /// <summary>
        /// A themed single-column chooser: the selection control the app uses where a WinForms
        /// ListBox would have been dropped in. A ListBox paints every row with the system theme
        /// and scrolls with the system scrollbar, both of which this application does not allow.
        /// </summary>
        public JBList AddList(int height)
        {
            var list = new JBList { BackColor = Theme.Input, Font = Theme.Body };
            AddRow(null, list, Ui.S(height));
            return list;
        }

        public TextBox AddReadOnlyText(string text, int height)
        {
            var t = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                /* No native scrollbars anywhere in the application, including here: the field
                   shows JBTextBox's own thin indicator instead. */
                ScrollBars = ScrollBars.None,
                WordWrap = false,
                Text = text ?? "",
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = true
            };
            AddRow(null, t, Ui.S(height));
            return t;
        }

        public JBButton AddAction(string text, string icon, EventHandler click)
        {
            var b = new JBButton { Kind = BtnKind.Soft, Text = text, Icon = icon, Radius = 4, Height = Ui.S(33) };
            b.Click += click;
            AddRow(null, b, Ui.S(33));
            return b;
        }

        public void AddRow(Control label, Control field, int height)
        {
            if (label != null) _body.Controls.Add(label);
            if (field != null) _body.Controls.Add(field);
            _rows.Add(new Row { Label = label, Field = field, Height = height });
        }

        /// <summary>Call after adding every row if you added controls directly to Body.</summary>
        public void RefreshLayout() { LayoutCore(); }

        /* The value each field had when the dialog opened. Esc needs this to know whether there
           is anything to undo: "the box has text in it" is not the same question, and every
           one of these dialogs opens with its fields already filled in. */
        private readonly Dictionary<JBTextBox, string> _initial = new Dictionary<JBTextBox, string>();

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _initial.Clear();
            foreach (Control c in _body.Controls)
            {
                var box = c as JBTextBox;
                if (box != null) _initial[box] = box.Value ?? "";
            }
            LayoutCore();
            CenterToParent();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_body != null) LayoutCore();
        }

        public override void ReStyle()
        {
            base.ReStyle();
            _body.BackColor = Theme.Bg;
            foreach (Control c in _body.Controls)
            {
                if (c is TextBox) { c.BackColor = Theme.Input; c.ForeColor = Theme.Text; }
            }
            Invalidate(true);
        }

        private void LayoutCore()
        {
            if (_body == null) return;
            SuspendLayout();
            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = Ui.S(24);

            _title.SetBounds(pad, Ui.S(14), Math.Max(Ui.S(100), w - pad * 2 - Ui.S(40)), Ui.S(32));
            _close.SetBounds(w - pad - Ui.S(30), Ui.S(14), Ui.S(30), Ui.S(30));

            int footerH = Ui.S(56);
            _body.SetBounds(pad, Ui.S(56), Math.Max(Ui.S(100), w - pad * 2), Math.Max(Ui.S(60), h - Ui.S(56) - footerH));

            /* The overlay bar is inset into the host's right edge, so the rows keep a little
               room from it; nothing is subtracted for a native scrollbar any more, because the
               host hides its bar over the content instead of beside it. */
            int innerW = Math.Max(Ui.S(100), _body.Width - Ui.S(24));
            int y = Ui.S(6);
            foreach (var r in _rows)
            {
                if (r.Label != null && r.Field == null)
                    r.Label.SetBounds(0, y, innerW, r.Height);
                else if (r.Field != null)
                    r.Field.SetBounds(0, y, innerW, r.Height);
                y += r.Height + Ui.S(9);
            }
            _content.SetBounds(0, 0, Math.Max(Ui.S(100), _body.Width), y + Ui.S(6));
            _body.SyncExtent();

            int pw = Primary.PreferredWidth(Ui.S(120));
            int sw = Secondary.Visible ? Secondary.PreferredWidth(Ui.S(90)) : 0;
            Primary.SetBounds(w - pad - pw, h - Ui.S(16) - Ui.S(33), pw, Ui.S(33));
            if (Secondary.Visible)
                Secondary.SetBounds(Primary.Left - Ui.S(8) - sw, h - Ui.S(16) - Ui.S(33), sw, Ui.S(33));
            ResumeLayout(false);
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                /* Esc used to close the dialog and throw away everything typed into it. Now the
                   first Esc undoes the field the caret is in, if it differs from what the dialog
                   opened with, and a second Esc closes. Nothing is lost by pressing Esc once. */
                JBTextBox field = FocusedField();
                if (field != null)
                {
                    string original;
                    string typed = field.Value ?? "";
                    if (_initial.TryGetValue(field, out original) && !string.Equals(original, typed, StringComparison.Ordinal))
                    {
                        field.Value = original;
                        field.Box.SelectAll();
                        _escHinted = true;
                        Secondary.Text = "Press Esc again to close";
                        Secondary.Invalidate();
                        return true;
                    }
                }
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            /* Enter submits, from any single-line field. JBTextBox deliberately swallows Enter
               (so a dialog cannot submit while the user is still typing a value), which also
               meant the AcceptButton never saw it and every one of these dialogs had to be
               finished with the mouse. */
            if ((keyData == Keys.Enter || keyData == Keys.Return) && Primary.Enabled && Primary.Visible)
            {
                JBTextBox field = FocusedField();
                /* Enter in a multi-line field inserts a newline and stays there; the dialog is
                   submitted from the buttons or from any single-line field. A field can also be
                   the body of a commit (a receipt, a store link) - those are single-line. */
                if (field == null || !field.Multiline)
                {
                    OnPrimary();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>The text field that owns the caret, if any, unwrapping the nested edit.</summary>
        private JBTextBox FocusedField()
        {
            Control c = ActiveControl;
            while (c is ContainerControl && ((ContainerControl)c).ActiveControl != null)
                c = ((ContainerControl)c).ActiveControl;
            var box = c as TextBox;
            // JBTextBox hosts a real TextBox, and that is what actually has focus.
            if (box != null) return box.Parent as JBTextBox;
            return c as JBTextBox;
        }

        private bool _escHinted;
    }
}
