using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MacBookEco.App
{
    internal static class GraphSeriesRenderer
    {
        public static void DrawSeries(
            Graphics graphics,
            RectangleF plot,
            TimeSeriesBuffer samples,
            DateTime windowStart,
            DateTime windowEnd,
            double minimum,
            double maximum,
            TimeSeriesGraphRenderOptions options,
            float scale)
        {
            long startTicks = windowStart.Ticks;
            long durationTicks = Math.Max(1L, windowEnd.Ticks - startTicks);
            double span = maximum - minimum;
            if (span <= 0.000001)
            {
                span = 1.0;
            }

            List<PointF> segment = new List<PointF>();
            GraphicsState state = graphics.Save();
            graphics.SetClip(plot);
            try
            {
                using (Pen linePen = new Pen(
                    options.LineColor,
                    Math.Max(1.6f, GraphDrawingPrimitives.ScalePixels(1.8f, scale))))
                using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                    plot,
                    Color.FromArgb(50, options.LineColor),
                    Color.FromArgb(4, options.LineColor),
                    LinearGradientMode.Vertical))
                {
                    linePen.LineJoin = LineJoin.Round;
                    linePen.StartCap = LineCap.Round;
                    linePen.EndCap = LineCap.Round;

                    for (int index = 0; index < samples.Count; index++)
                    {
                        TimeSeriesSample sample = samples.GetChronologicalSample(
                            index);
                        long ticks = sample.TimestampUtc.Ticks;
                        if (ticks < startTicks || ticks > windowEnd.Ticks)
                        {
                            continue;
                        }

                        if (!sample.IsValid)
                        {
                            PaintSegment(
                                graphics,
                                plot,
                                segment,
                                linePen,
                                fillBrush,
                                options,
                                scale);
                            segment.Clear();
                            continue;
                        }

                        double xFraction = (ticks - startTicks) /
                            (double)durationTicks;
                        double yFraction = (sample.Value - minimum) / span;
                        xFraction = Math.Max(0.0, Math.Min(1.0, xFraction));
                        yFraction = Math.Max(0.0, Math.Min(1.0, yFraction));
                        segment.Add(new PointF(
                            plot.Left + (float)(plot.Width * xFraction),
                            plot.Bottom - (float)(plot.Height * yFraction)));
                    }

                    PaintSegment(
                        graphics,
                        plot,
                        segment,
                        linePen,
                        fillBrush,
                        options,
                        scale);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        public static void DrawAxisLabels(
            Graphics graphics,
            RectangleF plot,
            double minimum,
            double maximum,
            string unit,
            float scale,
            Font smallFont,
            Brush mutedBrush,
            Color plotColor)
        {
            string maximumText = GraphDrawingPrimitives.FormatValue(maximum, unit);
            string minimumText = GraphDrawingPrimitives.FormatValue(minimum, unit);
            float pad = GraphDrawingPrimitives.ScalePixels(4.0f, scale);
            SizeF maximumSize = graphics.MeasureString(maximumText, smallFont);
            SizeF minimumSize = graphics.MeasureString(minimumText, smallFont);

            using (SolidBrush backdrop = new SolidBrush(Color.FromArgb(218, plotColor)))
            {
                RectangleF maximumBackdrop = new RectangleF(
                    plot.Left + pad,
                    plot.Top + GraphDrawingPrimitives.ScalePixels(2.0f, scale),
                    maximumSize.Width + pad,
                    maximumSize.Height);
                RectangleF minimumBackdrop = new RectangleF(
                    plot.Left + pad,
                    plot.Bottom - minimumSize.Height
                        - GraphDrawingPrimitives.ScalePixels(2.0f, scale),
                    minimumSize.Width + pad,
                    minimumSize.Height);

                graphics.FillRectangle(backdrop, maximumBackdrop);
                graphics.FillRectangle(backdrop, minimumBackdrop);
                graphics.DrawString(
                    maximumText,
                    smallFont,
                    mutedBrush,
                    maximumBackdrop.Left,
                    maximumBackdrop.Top);
                graphics.DrawString(
                    minimumText,
                    smallFont,
                    mutedBrush,
                    minimumBackdrop.Left,
                    minimumBackdrop.Top);
            }
        }

        private static void PaintSegment(
            Graphics graphics,
            RectangleF plot,
            IList<PointF> points,
            Pen linePen,
            Brush fillBrush,
            TimeSeriesGraphRenderOptions options,
            float scale)
        {
            if (points.Count == 0)
            {
                return;
            }

            if (points.Count == 1)
            {
                // A single sample has no line to draw, only its own marker.
                DrawLatestMarker(graphics, points[0], options.LineColor, scale);
                return;
            }

            PointF[] linePoints = new PointF[points.Count];
            points.CopyTo(linePoints, 0);
            if (options.FillArea)
            {
                PointF[] areaPoints = new PointF[linePoints.Length + 2];
                Array.Copy(linePoints, areaPoints, linePoints.Length);
                areaPoints[linePoints.Length] = new PointF(
                    linePoints[linePoints.Length - 1].X,
                    plot.Bottom);
                areaPoints[linePoints.Length + 1] = new PointF(
                    linePoints[0].X,
                    plot.Bottom);
                graphics.FillPolygon(fillBrush, areaPoints);
            }

            graphics.DrawLines(linePen, linePoints);
            DrawLatestMarker(
                graphics,
                linePoints[linePoints.Length - 1],
                options.LineColor,
                scale);
        }

        private static void DrawLatestMarker(
            Graphics graphics,
            PointF point,
            Color color,
            float scale)
        {
            float size = GraphDrawingPrimitives.ScalePixels(4.0f, scale);
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillEllipse(
                    brush,
                    point.X - (size / 2.0f),
                    point.Y - (size / 2.0f),
                    size,
                    size);
            }
        }
    }
}
