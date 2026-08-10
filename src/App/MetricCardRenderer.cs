using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacBookEco.App
{
    internal static class MetricCardRenderer
    {
        public static void Render(
            Graphics graphics,
            Rectangle bounds,
            MetricCardVisualState state)
        {
            if (graphics == null)
            {
                throw new ArgumentNullException(nameof(graphics));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            bounds.Width = Math.Max(0, bounds.Width - 1);
            bounds.Height = Math.Max(0, bounds.Height - 1);
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                return;
            }

            float scale = GraphDrawingPrimitives.DpiScale(graphics);
            int radius = ScaleValue(DashboardTheme.CardCornerRadius, scale);
            int accentWidth = ScaleValue(5, scale);
            using (GraphicsPath cardPath =
                GraphDrawingPrimitives.CreateRoundedRectangle(bounds, radius))
            using (SolidBrush surfaceBrush = new SolidBrush(state.SurfaceColor))
            using (Pen borderPen = new Pen(DashboardTheme.BorderColor))
            {
                graphics.FillPath(surfaceBrush, cardPath);
                GraphicsState clippingState = graphics.Save();
                graphics.SetClip(cardPath);
                using (SolidBrush accentBrush = new SolidBrush(state.AccentColor))
                {
                    graphics.FillRectangle(
                        accentBrush,
                        bounds.Left,
                        bounds.Top,
                        accentWidth,
                        bounds.Height);
                }

                graphics.Restore(clippingState);
                graphics.DrawPath(borderPen, cardPath);
            }

            DrawContent(graphics, bounds, scale, accentWidth, state);
        }

        private static void DrawContent(
            Graphics graphics,
            Rectangle cardBounds,
            float scale,
            int accentWidth,
            MetricCardVisualState state)
        {
            int left = cardBounds.Left + accentWidth + ScaleValue(12, scale);
            int right = cardBounds.Right - ScaleValue(12, scale);
            int top = cardBounds.Top + ScaleValue(9, scale);
            int contentWidth = Math.Max(0, right - left);
            if (contentWidth == 0)
            {
                return;
            }

            TextFormatFlags flags = TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter;
            int badgeWidth = string.IsNullOrEmpty(state.StatusText)
                ? 0
                : DrawStatusBadge(
                    graphics,
                    state.StatusText,
                    state.StatusColor,
                    left,
                    right,
                    top,
                    contentWidth,
                    scale,
                    flags);
            int titleRight = badgeWidth > 0
                ? right - badgeWidth - ScaleValue(8, scale)
                : right;
            int titleHeight = Math.Max(
                DashboardTheme.CaptionStrongFont.Height,
                ScaleValue(18, scale));
            Rectangle titleBounds = new Rectangle(
                left,
                top,
                Math.Max(0, titleRight - left),
                titleHeight);
            if (titleBounds.Width > 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    state.Title,
                    DashboardTheme.CaptionStrongFont,
                    titleBounds,
                    state.SecondaryTextColor,
                    flags);
            }

            int primaryTop = top + titleHeight + ScaleValue(5, scale);
            int primaryHeight = Math.Max(
                DashboardTheme.MetricFont.Height,
                ScaleValue(24, scale));
            Rectangle primaryBounds = new Rectangle(
                left,
                primaryTop,
                contentWidth,
                primaryHeight);
            TextRenderer.DrawText(
                graphics,
                state.PrimaryText,
                DashboardTheme.MetricFont,
                primaryBounds,
                state.PrimaryTextColor,
                flags);

            int secondaryTop = primaryBounds.Bottom + ScaleValue(3, scale);
            int secondaryHeight = Math.Max(
                0,
                cardBounds.Bottom - ScaleValue(7, scale) - secondaryTop);
            if (secondaryHeight > 0 && !string.IsNullOrEmpty(state.SecondaryText))
            {
                TextRenderer.DrawText(
                    graphics,
                    state.SecondaryText,
                    DashboardTheme.CaptionFont,
                    new Rectangle(left, secondaryTop, contentWidth, secondaryHeight),
                    state.SecondaryTextColor,
                    flags);
            }
        }

        private static int DrawStatusBadge(
            Graphics graphics,
            string badgeText,
            Color statusColor,
            int left,
            int right,
            int top,
            int contentWidth,
            float scale,
            TextFormatFlags textFlags)
        {
            int dotSize = ScaleValue(7, scale);
            int innerGap = ScaleValue(5, scale);
            int horizontalPadding = ScaleValue(7, scale);
            Size measured = TextRenderer.MeasureText(
                graphics,
                badgeText,
                DashboardTheme.CaptionFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int maximumBadgeWidth = Math.Max(
                ScaleValue(28, scale),
                (int)Math.Round(contentWidth * 0.46f));
            int badgeWidth = Math.Min(
                maximumBadgeWidth,
                measured.Width + dotSize + innerGap + (horizontalPadding * 2));
            int badgeHeight = Math.Max(
                ScaleValue(20, scale),
                DashboardTheme.CaptionFont.Height + ScaleValue(4, scale));
            Rectangle badgeBounds = new Rectangle(
                right - badgeWidth,
                top,
                badgeWidth,
                badgeHeight);
            using (GraphicsPath badgePath =
                GraphDrawingPrimitives.CreateRoundedRectangle(
                    badgeBounds,
                    badgeHeight / 2))
            using (SolidBrush badgeBrush = new SolidBrush(
                BlendWithWhite(statusColor, 88)))
            using (SolidBrush dotBrush = new SolidBrush(statusColor))
            {
                graphics.FillPath(badgeBrush, badgePath);
                int dotLeft = badgeBounds.Left + horizontalPadding;
                int dotTop = badgeBounds.Top +
                    ((badgeBounds.Height - dotSize) / 2);
                graphics.FillEllipse(dotBrush, dotLeft, dotTop, dotSize, dotSize);
            }

            int textLeft = badgeBounds.Left + horizontalPadding + dotSize + innerGap;
            Rectangle textBounds = new Rectangle(
                textLeft,
                badgeBounds.Top,
                Math.Max(0, badgeBounds.Right - horizontalPadding - textLeft),
                badgeBounds.Height);
            if (textBounds.Width > 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    badgeText,
                    DashboardTheme.CaptionFont,
                    textBounds,
                    statusColor,
                    textFlags);
            }

            return badgeWidth;
        }

        private static int ScaleValue(int value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private static Color BlendWithWhite(Color color, int whitePercent)
        {
            int colorPercent = 100 - whitePercent;
            return Color.FromArgb(
                255,
                ((color.R * colorPercent) + (255 * whitePercent)) / 100,
                ((color.G * colorPercent) + (255 * whitePercent)) / 100,
                ((color.B * colorPercent) + (255 * whitePercent)) / 100);
        }

    }
}
