using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace scrlog.UI
{
    /// <summary>
    /// Страница в правой части окна. Раскладка считается от реальной ширины текста,
    /// а минимальный размер страницы ограничивает минимальный размер окна.
    /// </summary>
    public abstract class Page : UserControl
    {
        protected static readonly int Pad = Theme.S(24);
        protected static readonly int ContentTop = Theme.S(22);
        protected static readonly int Gap = Theme.S(12);
        protected static readonly int RowHeight = Theme.S(32);

        protected Page()
        {
            BackColor = Theme.Background;
            DoubleBuffered = true;
        }

        /// <summary>Ширина, меньше которой элементы начнут перекрываться.</summary>
        public abstract int MinimumContentWidth { get; }

        /// <summary>Высота, меньше которой страница не сжимается (остальное — прокрутка).</summary>
        public abstract int MinimumContentHeight { get; }

        protected static int TextWidth(Control control) => control.PreferredSize.Width;

        protected static int TextWidth(string text, Font font) =>
            TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

        /// <summary>Левый край полей ввода: правее самой длинной подписи в карточке.</summary>
        protected static int InputColumn(params Label[] labels) =>
            labels.Max(l => l.Left + TextWidth(l)) + Theme.S(14);

        protected static Label AddRow(Card card, string text, Control input)
        {
            var label = Theme.MakeLabel(text, Theme.Font(10.5f), Theme.Label);
            label.Left = Theme.S(14);
            card.Controls.Add(label);
            card.Controls.Add(input);
            return label;
        }

        /// <summary>Ставит подпись и поле в строку с верхним краем <paramref name="y"/>.</summary>
        protected static void PlaceRow(Label label, Control input, int x, int y, int width)
        {
            label.Top = y + (RowHeight - label.PreferredSize.Height) / 2;
            input.SetBounds(x, y + (RowHeight - input.Height) / 2, width, input is ComboBox ? input.Height : RowHeight);
        }

        protected static int ButtonWidth(ActionButton button)
        {
            int glyph = string.IsNullOrEmpty(button.Glyph) ? 0 : TextWidth(button.Glyph, Theme.Icon(button.GlyphSize)) + Theme.S(14);
            return TextWidth(button.Text, button.Font) + glyph + Theme.S(36);
        }
    }
}
