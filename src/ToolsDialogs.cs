using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Small themed dialog used by the Tools &amp; data actions. Content is a vertical stack,
    /// so fields never overlap at narrow widths or high DPI. The body scrolls when needed.
    /// </summary>
    internal sealed class ToolDialog : JBForm
    {
        private sealed class Row
        {
            public Control Label;
            public Control Field;
            public int Height;
        }

        private readonly JBLabel _title;
        private readonly JBIconButton _close;
        private readonly Panel _body;
        private readonly List<Row> _rows = new List<Row>();
        private readonly string _primaryText;

        public JBButton Primary { get; private set; }
        public JBButton Secondary { get; private set; }
        public Panel Body { get { return _body; } }
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

            _body = new Panel { BackColor = Theme.Bg, AutoScroll = true };
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

        private void OnPrimary()
        {
            if (Validator != null && !Validator()) return;
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

        public TextBox AddReadOnlyText(string text, int height)
        {
            var t = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
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

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
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
            _body.AutoScrollMinSize = new Size(0, y + Ui.S(6));

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
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
