using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace scrlog.UI
{
    /// <summary>Базовый контрол с двойной буферизацией и фоном родителя в углах.</summary>
    public abstract class PaintedControl : Control
    {
        protected PaintedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        }
    }

    /// <summary>Белая карточка со скруглёнными углами.</summary>
    public class Card : Panel
    {
        public Color FillColor { get; set; } = Theme.Card;
        public Color BorderColor { get; set; } = Theme.CardBorder;
        public int Radius { get; set; } = Theme.S(8);

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public override Color BackColor
        {
            get => FillColor;
            set { }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
            Theme.FillRounded(e.Graphics, ClientRectangle, Radius, FillColor, BorderColor);
        }

        public Label AddTitle(string text)
        {
            var title = Theme.MakeLabel(text, Theme.Semibold(11f), Theme.Text);
            title.Location = new Point(Theme.S(16), Theme.S(14));
            Controls.Add(title);
            return title;
        }
    }

    public class NavButton : PaintedControl
    {
        private bool _selected;
        private bool _hover;

        public string Glyph { get; set; }

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public NavButton(string glyph, string text)
        {
            Glyph = glyph;
            Text = text;
            Cursor = Cursors.Hand;
            Height = Theme.S(56);
            Font = Theme.Font(12f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_selected ? Theme.SidebarSelected : _hover ? Theme.SidebarHover : Theme.Sidebar);

            int icon = Theme.S(28);
            var iconRect = new Rectangle(Theme.S(16), (Height - icon) / 2, icon, icon);
            using (var bg = new SolidBrush(Color.FromArgb(_selected ? 60 : 30, Color.White)))
            using (var path = Theme.RoundRect(iconRect, Theme.S(6)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(bg, path);
            }
            TextRenderer.DrawText(g, Glyph, Theme.Icon(11f), iconRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var textRect = new Rectangle(iconRect.Right + Theme.S(14), 0, Width - iconRect.Right - Theme.S(14), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Крупная цветная кнопка с иконкой.</summary>
    public class ActionButton : PaintedControl
    {
        private bool _hover;
        private bool _pressed;
        private Color _fill = Theme.Accent;
        private string _glyph;

        public Color FillColor
        {
            get => _fill;
            set { _fill = value; Invalidate(); }
        }

        public Color TextColor { get; set; } = Color.White;

        public string Glyph
        {
            get => _glyph;
            set { _glyph = value; Invalidate(); }
        }

        public float GlyphSize { get; set; } = 13f;

        public ActionButton()
        {
            Cursor = Cursors.Hand;
            Font = Theme.Semibold(13f);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var fill = !Enabled ? Theme.Gray : _pressed ? Theme.Darken(_fill, 0.12f) : _hover ? Theme.Lighten(_fill, 0.1f) : _fill;
            var border = _fill == Theme.Card ? Theme.InputBorder : Color.Empty;
            Theme.FillRounded(g, ClientRectangle, Theme.S(8), fill, border);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            Size textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
            Size glyphSize = string.IsNullOrEmpty(_glyph) ? Size.Empty : TextRenderer.MeasureText(g, _glyph, Theme.Icon(GlyphSize), Size.Empty, flags);
            int gap = textSize.IsEmpty || glyphSize.IsEmpty ? 0 : Theme.S(14);
            int x = (Width - glyphSize.Width - gap - textSize.Width) / 2;

            if (!glyphSize.IsEmpty)
            {
                TextRenderer.DrawText(g, _glyph, Theme.Icon(GlyphSize), new Rectangle(x, 0, glyphSize.Width, Height), TextColor, flags);
                x += glyphSize.Width + gap;
            }
            if (!textSize.IsEmpty)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textSize.Width + 2, Height), TextColor, flags);
        }
    }

    /// <summary>Поле ввода в скруглённой рамке.</summary>
    public class InputBox : PaintedControl
    {
        protected readonly TextBox Box;
        private Color _borderColor = Theme.InputBorder;

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        protected virtual int RightPadding => Theme.S(10);

        public event EventHandler Committed;

        public InputBox()
        {
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.InputFill,
                ForeColor = Theme.Text,
                Font = Theme.Semibold(10.5f)
            };
            Box.Leave += (s, e) => OnCommitted();
            Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    OnCommitted();
                }
            };
            Controls.Add(Box);
            Height = Theme.S(36);
            Cursor = Cursors.IBeam;
        }

        public string Value
        {
            get => Box.Text;
            set => Box.Text = value;
        }

        public void Commit() => OnCommitted();

        protected virtual void OnCommitted() => Committed?.Invoke(this, EventArgs.Empty);

        protected override void OnClick(EventArgs e)
        {
            Box.Focus();
            base.OnClick(e);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = Theme.S(10);
            Box.SetBounds(left, (Height - Box.Height) / 2, Math.Max(10, Width - left - RightPadding), Box.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.FillRounded(e.Graphics, ClientRectangle, Theme.S(8), Theme.InputFill, _borderColor);
        }
    }

    /// <summary>Числовое поле со стрелками ▲▼ (используется и для времени).</summary>
    public class SpinBox : InputBox
    {
        private int _value;

        public int Minimum { get; set; }
        public int Maximum { get; set; } = 100;
        public int Increment { get; set; } = 1;
        public bool Wrap { get; set; }
        public Func<int, string> Formatter { get; set; } = v => v.ToString();
        public Func<string, int?> Parser { get; set; } = s => int.TryParse(s, out var v) ? v : (int?)null;

        public event EventHandler ValueChanged;

        protected override int RightPadding => Theme.S(30);

        private Rectangle ArrowRect => new Rectangle(Width - Theme.S(28), 0, Theme.S(28), Height);

        public SpinBox()
        {
            Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; }
                if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; }
            };
            Box.MouseWheel += (s, e) => Step(Math.Sign(e.Delta));
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ValueInt
        {
            get => _value;
            set => SetValue(value, false);
        }

        private void SetValue(int value, bool raise)
        {
            if (Wrap)
            {
                int range = Maximum - Minimum + 1;
                value = ((value - Minimum) % range + range) % range + Minimum;
            }
            else
            {
                value = Math.Max(Minimum, Math.Min(Maximum, value));
            }
            bool changed = value != _value;
            _value = value;
            Box.Text = Formatter(value);
            if (changed && raise) ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Step(int direction)
        {
            // Значение «прилипает» к сетке шага: 08:07 + 15 мин = 08:15
            int snapped = direction > 0
                ? (_value / Increment + 1) * Increment
                : ((_value + Increment - 1) / Increment - 1) * Increment;
            SetValue(snapped, true);
        }

        protected override void OnCommitted()
        {
            var parsed = Parser(Box.Text.Trim());
            if (parsed.HasValue)
                SetValue(parsed.Value, true);
            else
                Box.Text = Formatter(_value);
            base.OnCommitted();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = ArrowRect.Contains(e.Location) ? Cursors.Hand : Cursors.IBeam;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (ArrowRect.Contains(e.Location))
            {
                OnCommitted();
                Step(e.Y < Height / 2 ? 1 : -1);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Step(Math.Sign(e.Delta));
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width - Theme.S(16);
            float cy = Height / 2f;
            float w = Theme.S(5), h = Theme.S(5), gap = Theme.S(2);
            using (var brush = new SolidBrush(Theme.Text))
            {
                g.FillPolygon(brush, new[] { new PointF(cx - w, cy - gap), new PointF(cx + w, cy - gap), new PointF(cx, cy - gap - h * 1.4f) });
                g.FillPolygon(brush, new[] { new PointF(cx - w, cy + gap), new PointF(cx + w, cy + gap), new PointF(cx, cy + gap + h * 1.4f) });
            }
        }

        public static SpinBox ForTime()
        {
            return new SpinBox
            {
                Minimum = 0,
                Maximum = 24 * 60 - 1,
                Increment = 15,
                Wrap = true,
                Formatter = v => $"{v / 60:00}:{v % 60:00}",
                Parser = s => TimeSpan.TryParse(s, out var t) && t.TotalMinutes < 24 * 60 ? (int)t.TotalMinutes : (int?)null
            };
        }
    }

    /// <summary>Квадратная кнопка с иконкой (например, «обзор папки»).</summary>
    public class IconButton : PaintedControl
    {
        private bool _hover;

        public string Glyph { get; set; }

        public IconButton(string glyph)
        {
            Glyph = glyph;
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(36), Theme.S(36));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.FillRounded(e.Graphics, ClientRectangle, Theme.S(8), _hover ? Theme.Background : Theme.InputFill, Theme.InputBorder);
            TextRenderer.DrawText(e.Graphics, Glyph, Theme.Icon(12f), ClientRectangle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public class FlatCheckBox : PaintedControl
    {
        private bool _checked;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public FlatCheckBox(string text)
        {
            Text = text;
            Font = Theme.Font(10.5f);
            ForeColor = Theme.Label;
            Cursor = Cursors.Hand;
            Height = Theme.S(24);
            Width = Theme.S(28) + TextRenderer.MeasureText(text, Font).Width;
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int box = Theme.S(14);
            var r = new Rectangle(Theme.S(1), (Height - box) / 2, box, box);
            var accent = Enabled ? Theme.Accent : Theme.Gray;
            if (_checked)
            {
                using (var brush = new SolidBrush(accent))
                    g.FillRectangle(brush, r);
                using (var pen = new Pen(Color.White, Math.Max(1.5f, Theme.Scale * 1.6f)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(r.X + box * 0.22f, r.Y + box * 0.52f),
                        new PointF(r.X + box * 0.42f, r.Y + box * 0.72f),
                        new PointF(r.X + box * 0.78f, r.Y + box * 0.3f)
                    });
                }
            }
            else
            {
                using (var pen = new Pen(Theme.InputBorder, Math.Max(1f, Theme.Scale * 1.5f)))
                    g.DrawRectangle(pen, r);
            }
            var textRect = new Rectangle(r.Right + Theme.S(10), 0, Width - r.Right - Theme.S(10), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.Gray,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public class StatusDot : PaintedControl
    {
        private Color _color = Theme.Gray;

        public Color DotColor
        {
            get => _color;
            set { _color = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(_color))
                e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Строка «Ключ: значение» в два цвета.</summary>
    public class KeyValueLabel : PaintedControl
    {
        private string _key;
        private string _value;

        public KeyValueLabel(string key)
        {
            _key = key + ": ";
            Font = Theme.Font(11f);
            Height = Theme.S(22);
        }

        public string Value
        {
            get => _value;
            set { _value = value; Invalidate(); }
        }

        /// <summary>Ширина, при которой строка помещается без многоточия.</summary>
        public int TextWidth
        {
            get
            {
                var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                return TextRenderer.MeasureText(_key, Font, Size.Empty, flags).Width +
                       TextRenderer.MeasureText(_value ?? "", Theme.Semibold(11f), Size.Empty, flags).Width + Theme.S(2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            int keyWidth = TextRenderer.MeasureText(e.Graphics, _key, Font, Size.Empty, flags).Width;
            TextRenderer.DrawText(e.Graphics, _key, Font, new Rectangle(0, 0, keyWidth, Height), Theme.Label, flags);
            TextRenderer.DrawText(e.Graphics, _value ?? "", Theme.Semibold(11f),
                new Rectangle(keyWidth, 0, Math.Max(0, Width - keyWidth), Height), Theme.Text, flags | TextFormatFlags.EndEllipsis);
        }
    }
}
