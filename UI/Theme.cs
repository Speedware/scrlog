using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace scrlog.UI
{
    public static class Theme
    {
        public static readonly Color TitleBar = Color.FromArgb(74, 123, 163);
        public static readonly Color Sidebar = Color.FromArgb(69, 72, 87);
        public static readonly Color SidebarHover = Color.FromArgb(82, 86, 104);
        public static readonly Color SidebarSelected = Color.FromArgb(84, 121, 219);
        public static readonly Color SidebarMuted = Color.FromArgb(120, 124, 140);
        public static readonly Color Background = Color.FromArgb(238, 242, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(222, 229, 240);
        public static readonly Color InputFill = Color.FromArgb(247, 249, 252);
        public static readonly Color InputBorder = Color.FromArgb(218, 225, 236);
        public static readonly Color Text = Color.FromArgb(22, 24, 29);
        public static readonly Color Label = Color.FromArgb(60, 90, 138);
        public static readonly Color Muted = Color.FromArgb(120, 130, 148);
        public static readonly Color Accent = Color.FromArgb(52, 109, 211);
        public static readonly Color Green = Color.FromArgb(74, 154, 90);
        public static readonly Color Red = Color.FromArgb(217, 83, 95);
        public static readonly Color Gray = Color.FromArgb(160, 167, 180);

        public const string IconFont = "Segoe MDL2 Assets";

        public static class Glyph
        {
            public const string Home = "";
            public const string List = "";
            public const string Settings = "";
            public const string Folder = "";
            public const string Stop = "";
            public const string Record = "";
            public const string Monitor = "";
            public const string Refresh = "";
            public const string Play = "";
            public const string Delete = "";
        }

        private static float? _scale;

        /// <summary>Множитель DPI (процесс system-aware, масштаб фиксируется при запуске).</summary>
        public static float Scale
        {
            get
            {
                if (_scale == null)
                {
                    using (var g = Graphics.FromHwnd(IntPtr.Zero))
                        _scale = g.DpiX / 96f;
                }
                return _scale.Value;
            }
        }

        public static int S(int value) => (int)Math.Round(value * Scale);

        private static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        public static Font Font(float size, FontStyle style = FontStyle.Regular, string family = "Segoe UI")
        {
            string key = $"{family}|{size}|{style}";
            if (!_fonts.TryGetValue(key, out var font))
            {
                font = new Font(family, size, style, GraphicsUnit.Point);
                _fonts[key] = font;
            }
            return font;
        }

        public static Font Semibold(float size) => Font(size, FontStyle.Regular, "Segoe UI Semibold");

        public static Font Icon(float size) => Font(size, FontStyle.Regular, IconFont);

        public static Label MakeLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                BackColor = Color.Transparent
            };
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color fill, Color border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1.5f, bounds.Height - 1.5f);
            using (var path = RoundRect(r, radius))
            {
                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
                if (border != Color.Empty)
                    using (var pen = new Pen(border, Math.Max(1f, Scale)))
                        g.DrawPath(pen, path);
            }
        }

        public static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }

        public static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));
        }
    }
}
