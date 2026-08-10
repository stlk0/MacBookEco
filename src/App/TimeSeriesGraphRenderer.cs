using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace MacBookEco.App
{
    internal sealed class TimeSeriesGraphRenderOptions
    {
        public string Title;
        public string Subtitle;
        public string EmptyText;
        public string TimeWindowLabel;
        public string Unit;
        public Color LineColor;
        public double? FixedMinimum;
        public double? FixedMaximum;
        public bool FillArea;
        public TimeSpan TimeWindow;
    }

    /// <summary>
    /// Paints the graph card chrome and delegates polyline/axis drawing to the
    /// series renderer. It has no control lifecycle or mutable sample state.
    /// </summary>
    internal static class TimeSeriesGraphRenderer
    {
        public static void Render(
            Graphics graphics,
            Rectangle bounds,
            Font baseFont,
            Color backColor,
            Color foreColor,
            TimeSeriesBuffer samples,
            TimeSeriesGraphRenderOptions options)
        {
            if (graphics == null)
            {
                throw new ArgumentNullException(nameof(graphics));
            }

            if (baseFont == null)
            {
                throw new ArgumentNullException(nameof(baseFont));
            }

            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(backColor);
            if (bounds.Width < 30 || bounds.Height < 30)
            {
                return;
            }

            float scale = GraphDrawingPrimitives.DpiScale(graphics);
            float inset = GraphDrawingPrimitives.ScalePixels(12.0f, scale);
            float radius = GraphDrawingPrimitives.ScalePixels(7.0f, scale);
            bool hasSubtitle = !string.IsNullOrWhiteSpace(options.Subtitle);
            float headerHeight = GraphDrawingPrimitives.ScalePixels(
                hasSubtitle ? 49.0f : 37.0f,
                scale);
            float footerHeight = GraphDrawingPrimitives.ScalePixels(27.0f, scale);
            RectangleF card = new RectangleF(
                bounds.Left + 0.5f,
                bounds.Top + 0.5f,
                Math.Max(1.0f, bounds.Width - 1.0f),
                Math.Max(1.0f, bounds.Height - 1.0f));
            RectangleF plot = new RectangleF(
                bounds.Left + inset,
                bounds.Top + headerHeight,
                Math.Max(1.0f, bounds.Width - (inset * 2.0f)),
                Math.Max(1.0f, bounds.Height - headerHeight - footerHeight));

            Color borderColor = DashboardTheme.GraphBorderColor;
            Color plotColor = DashboardTheme.GraphPlotColor;
            Color gridColor = DashboardTheme.GraphGridColor;
            Color mutedColor = DashboardTheme.GraphMutedTextColor;
            using (GraphicsPath cardPath =
                GraphDrawingPrimitives.CreateRoundedRectangle(card, radius))
            using (Pen borderPen = new Pen(
                borderColor,
                Math.Max(1.0f, scale)))
            using (SolidBrush cardBrush = new SolidBrush(backColor))
            using (SolidBrush plotBrush = new SolidBrush(plotColor))
            using (SolidBrush textBrush = new SolidBrush(foreColor))
            using (SolidBrush mutedBrush = new SolidBrush(mutedColor))
            using (Pen gridPen = new Pen(
                gridColor,
                Math.Max(1.0f, scale)))
            {
                // Shared and long-lived: see GraphFonts. Deliberately not in
                // the using chain.
                GraphFonts fonts = GraphFonts.For(baseFont);
                Font titleFont = fonts.Title;
                Font latestFont = fonts.Latest;
                Font smallFont = fonts.Small;
                graphics.FillPath(cardBrush, cardPath);
                graphics.DrawPath(borderPen, cardPath);
                DrawHeader(
                    graphics,
                    bounds,
                    inset,
                    scale,
                    titleFont,
                    latestFont,
                    smallFont,
                    textBrush,
                    mutedBrush,
                    samples,
                    options);
                graphics.FillRectangle(plotBrush, plot);
                DrawGrid(graphics, plot, gridPen);

                DateTime windowStart;
                DateTime windowEnd;
                samples.GetVisibleWindow(
                    options.TimeWindow,
                    out windowStart,
                    out windowEnd);
                TimeSeriesStatistics statistics =
                    TimeSeriesStatisticsCalculator.Calculate(
                        samples,
                        windowStart,
                        windowEnd);
                double axisMinimum;
                double axisMaximum;
                TimeSeriesAxisRange.Calculate(
                    statistics,
                    options.FixedMinimum,
                    options.FixedMaximum,
                    out axisMinimum,
                    out axisMaximum);

                if (statistics.Count == 0)
                {
                    DrawEmptyState(
                        graphics,
                        plot,
                        scale,
                        latestFont,
                        smallFont,
                        textBrush,
                        mutedBrush,
                        options.EmptyText);
                }
                else
                {
                    GraphSeriesRenderer.DrawSeries(
                        graphics,
                        plot,
                        samples,
                        windowStart,
                        windowEnd,
                        axisMinimum,
                        axisMaximum,
                        options,
                        scale);
                    GraphSeriesRenderer.DrawAxisLabels(
                        graphics,
                        plot,
                        axisMinimum,
                        axisMaximum,
                        options.Unit,
                        scale,
                        smallFont,
                        mutedBrush,
                        plotColor);
                }

                DrawFooter(
                    graphics,
                    bounds,
                    plot,
                    statistics,
                    scale,
                    smallFont,
                    mutedBrush,
                    options);
            }
        }

        private static void DrawHeader(
            Graphics graphics,
            Rectangle bounds,
            float inset,
            float scale,
            Font titleFont,
            Font latestFont,
            Font smallFont,
            Brush textBrush,
            Brush mutedBrush,
            TimeSeriesBuffer samples,
            TimeSeriesGraphRenderOptions options)
        {
            string title = string.IsNullOrWhiteSpace(options.Title)
                ? "Telemetry"
                : options.Title;
            TimeSeriesSample latestSample;
            string latest = samples.TryGetLatest(out latestSample)
                && latestSample.IsValid
                ? GraphDrawingPrimitives.FormatValue(latestSample.Value, options.Unit)
                : "N/A";
            float top = bounds.Top + GraphDrawingPrimitives.ScalePixels(9.0f, scale);
            float latestWidth = Math.Min(
                GraphDrawingPrimitives.ScalePixels(132.0f, scale),
                Math.Max(
                    GraphDrawingPrimitives.ScalePixels(62.0f, scale),
                    bounds.Width * 0.38f));
            RectangleF latestRectangle = new RectangleF(
                bounds.Right - inset - latestWidth,
                top - GraphDrawingPrimitives.ScalePixels(1.0f, scale),
                latestWidth,
                GraphDrawingPrimitives.ScalePixels(23.0f, scale));
            RectangleF titleRectangle = new RectangleF(
                bounds.Left + inset,
                top,
                Math.Max(
                    1.0f,
                    latestRectangle.Left - bounds.Left - (inset * 1.5f)),
                GraphDrawingPrimitives.ScalePixels(21.0f, scale));

            using (StringFormat left = GraphDrawingPrimitives.CreateTextFormat(
                StringAlignment.Near))
            using (StringFormat right = GraphDrawingPrimitives.CreateTextFormat(
                StringAlignment.Far))
            {
                graphics.DrawString(title, titleFont, textBrush, titleRectangle, left);
                graphics.DrawString(latest, latestFont, textBrush, latestRectangle, right);
                if (!string.IsNullOrWhiteSpace(options.Subtitle))
                {
                    RectangleF subtitleRectangle = new RectangleF(
                        bounds.Left + inset,
                        top + GraphDrawingPrimitives.ScalePixels(21.0f, scale),
                        Math.Max(1.0f, bounds.Width - (inset * 2.0f)),
                        GraphDrawingPrimitives.ScalePixels(18.0f, scale));
                    graphics.DrawString(
                        options.Subtitle,
                        smallFont,
                        mutedBrush,
                        subtitleRectangle,
                        left);
                }
            }
        }

        private static void DrawGrid(Graphics graphics, RectangleF plot, Pen gridPen)
        {
            for (int line = 1; line < 4; line++)
            {
                float y = plot.Top + ((plot.Height * line) / 4.0f);
                graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            }

            for (int line = 1; line < 4; line++)
            {
                float x = plot.Left + ((plot.Width * line) / 4.0f);
                graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            }
        }

        private static void DrawEmptyState(
            Graphics graphics,
            RectangleF plot,
            float scale,
            Font latestFont,
            Font smallFont,
            Brush textBrush,
            Brush mutedBrush,
            string emptyText)
        {
            float centerY = plot.Top + (plot.Height / 2.0f);
            RectangleF unavailableRectangle = new RectangleF(
                plot.Left + GraphDrawingPrimitives.ScalePixels(8.0f, scale),
                centerY - GraphDrawingPrimitives.ScalePixels(23.0f, scale),
                Math.Max(
                    1.0f,
                    plot.Width - GraphDrawingPrimitives.ScalePixels(16.0f, scale)),
                GraphDrawingPrimitives.ScalePixels(23.0f, scale));
            RectangleF explanationRectangle = new RectangleF(
                plot.Left + GraphDrawingPrimitives.ScalePixels(8.0f, scale),
                centerY + GraphDrawingPrimitives.ScalePixels(1.0f, scale),
                Math.Max(
                    1.0f,
                    plot.Width - GraphDrawingPrimitives.ScalePixels(16.0f, scale)),
                GraphDrawingPrimitives.ScalePixels(33.0f, scale));

            using (StringFormat center = GraphDrawingPrimitives.CreateTextFormat(
                StringAlignment.Center))
            {
                graphics.DrawString(
                    "N/A",
                    latestFont,
                    textBrush,
                    unavailableRectangle,
                    center);
                if (!string.IsNullOrWhiteSpace(emptyText))
                {
                    graphics.DrawString(
                        emptyText,
                        smallFont,
                        mutedBrush,
                        explanationRectangle,
                        center);
                }
            }
        }

        private static void DrawFooter(
            Graphics graphics,
            Rectangle bounds,
            RectangleF plot,
            TimeSeriesStatistics statistics,
            float scale,
            Font smallFont,
            Brush mutedBrush,
            TimeSeriesGraphRenderOptions options)
        {
            float inset = GraphDrawingPrimitives.ScalePixels(12.0f, scale);
            float summaryTop = plot.Bottom +
                GraphDrawingPrimitives.ScalePixels(5.0f, scale);
            float windowWidth = Math.Min(
                GraphDrawingPrimitives.ScalePixels(116.0f, scale),
                Math.Max(
                    GraphDrawingPrimitives.ScalePixels(58.0f, scale),
                    bounds.Width * 0.32f));
            string summary = statistics.Count == 0
                ? "Avg N/A  |  Min N/A  |  Max N/A"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Avg {0}  |  Min {1}  |  Max {2}",
                    GraphDrawingPrimitives.FormatValue(
                        statistics.Average,
                        options.Unit),
                    GraphDrawingPrimitives.FormatValue(
                        statistics.Minimum,
                        options.Unit),
                    GraphDrawingPrimitives.FormatValue(
                        statistics.Maximum,
                        options.Unit));
            RectangleF summaryRectangle = new RectangleF(
                bounds.Left + inset,
                summaryTop,
                Math.Max(1.0f, bounds.Width - (inset * 2.0f) - windowWidth),
                GraphDrawingPrimitives.ScalePixels(18.0f, scale));
            RectangleF windowRectangle = new RectangleF(
                bounds.Right - inset - windowWidth,
                summaryTop,
                windowWidth,
                GraphDrawingPrimitives.ScalePixels(18.0f, scale));

            using (StringFormat left = GraphDrawingPrimitives.CreateTextFormat(
                StringAlignment.Near))
            using (StringFormat right = GraphDrawingPrimitives.CreateTextFormat(
                StringAlignment.Far))
            {
                graphics.DrawString(summary, smallFont, mutedBrush, summaryRectangle, left);
                if (!string.IsNullOrWhiteSpace(options.TimeWindowLabel))
                {
                    graphics.DrawString(
                        options.TimeWindowLabel,
                        smallFont,
                        mutedBrush,
                        windowRectangle,
                        right);
                }

            }
        }
    }
}
