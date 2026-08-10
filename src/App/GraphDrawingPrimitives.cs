using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace MacBookEco.App
{
    /// <summary>
    /// The three derived fonts a graph card draws with, kept for the base font
    /// they came from.
    ///
    /// Six graphs repaint on every telemetry tick, and each paint used to
    /// construct and destroy three Fonts, so a two-second tick churned
    /// eighteen GDI font handles on the UI thread. That is a poor trade in an
    /// application whose stated goal is not to perturb the machine it is
    /// measuring. One entry is enough: every graph on the page renders from
    /// the same base font, and the entry is only replaced when the theme font
    /// or DPI changes.
    /// </summary>
    internal sealed class GraphFonts
    {
        private static readonly object Gate = new object();
        private static GraphFonts _cached;

        private readonly string _familyName;
        private readonly float _baseSize;

        private GraphFonts(Font baseFont)
        {
            _familyName = baseFont.FontFamily.Name;
            _baseSize = baseFont.Size;
            Title = new Font(
                baseFont.FontFamily,
                baseFont.Size,
                FontStyle.Bold);
            Latest = new Font(
                baseFont.FontFamily,
                Math.Max(baseFont.Size, baseFont.Size + 1.5f),
                FontStyle.Bold);
            Small = new Font(
                baseFont.FontFamily,
                Math.Max(7.0f, baseFont.Size - 1.0f),
                FontStyle.Regular);
        }

        public Font Title { get; private set; }

        public Font Latest { get; private set; }

        public Font Small { get; private set; }

        public static GraphFonts For(Font baseFont)
        {
            lock (Gate)
            {
                GraphFonts current = _cached;
                if (current != null
                    && current._baseSize == baseFont.Size
                    && string.Equals(
                        current._familyName,
                        baseFont.FontFamily.Name,
                        StringComparison.Ordinal))
                {
                    return current;
                }

                // A superseded entry is dropped rather than disposed: a paint
                // on another thread may still hold it, and Font releases its
                // handle from its finalizer. This happens at most once per
                // theme or DPI change.
                _cached = new GraphFonts(baseFont);
                return _cached;
            }
        }
    }

    internal static class GraphDrawingPrimitives
    {
        /// <summary>
        /// The one definition of "how much bigger is this screen than 96 DPI".
        /// The metric card and the graph card used to clamp this differently
        /// (1.0 against 0.75), so below 96 DPI the two halves of the same
        /// dashboard would have scaled apart.
        /// </summary>
        public static float DpiScale(Graphics graphics)
        {
            return Math.Max(1.0f, graphics.DpiX / 96.0f);
        }

        public static float ScalePixels(float value, float scale)
        {
            return value * scale;
        }

        public static string FormatValue(double value, string unit)
        {
            double absolute = Math.Abs(value);
            string format = absolute >= 1000.0
                ? "0"
                : absolute >= 100.0
                    ? "0.#"
                    : "0.##";
            string text = value.ToString(format, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(unit) ? text : text + " " + unit;
        }

        public static StringFormat CreateTextFormat(StringAlignment alignment)
        {
            StringFormat format = new StringFormat(StringFormat.GenericDefault);
            format.Alignment = alignment;
            format.LineAlignment = StringAlignment.Near;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            return format;
        }

        public static GraphicsPath CreateRoundedRectangle(
            RectangleF rectangle,
            float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(
                radius * 2.0f,
                Math.Min(rectangle.Width, rectangle.Height));
            if (diameter <= 1.0f)
            {
                path.AddRectangle(rectangle);
                path.CloseFigure();
                return path;
            }

            RectangleF arc = new RectangleF(
                rectangle.Location,
                new SizeF(diameter, diameter));
            path.AddArc(arc, 180.0f, 90.0f);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270.0f, 90.0f);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0.0f, 90.0f);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90.0f, 90.0f);
            path.CloseFigure();
            return path;
        }
    }
}
