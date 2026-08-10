using System;
using System.Drawing;

namespace MacBookEco.App
{
    public enum MetricCardStatus
    {
        None,
        Available,
        Warning,
        Unavailable,
        Error
    }

    /// <summary>
    /// Presentation-only state mapping for MetricCard. It owns status labels,
    /// accessibility text and colors, independently of WinForms painting.
    /// </summary>
    internal static class MetricCardPresentation
    {
        public static MetricCardVisualState CreateVisualState(
            string title,
            string primaryText,
            string secondaryText,
            string statusText,
            Color accentColor,
            MetricCardStatus status,
            Color foreColor,
            bool enabled)
        {
            // Validated once, where the value enters: MetricCard.Status is the
            // only way a status reaches here, and this runs on every paint.
            MetricCardVisualState state = new MetricCardVisualState();
            state.Title = title ?? string.Empty;
            state.PrimaryText = primaryText ?? string.Empty;
            state.SecondaryText = secondaryText ?? string.Empty;
            state.StatusText = GetEffectiveStatusText(statusText, status);
            state.SurfaceColor = enabled
                ? DashboardTheme.SurfaceColor
                : DashboardTheme.MutedSurfaceColor;
            state.StatusColor = GetStatusColor(status, accentColor, enabled);
            state.AccentColor = !enabled || status == MetricCardStatus.Unavailable
                ? DashboardTheme.DisabledTextColor
                : status == MetricCardStatus.Error
                    ? DashboardTheme.DangerColor
                    : accentColor;
            state.PrimaryTextColor = !enabled
                    || status == MetricCardStatus.Unavailable
                ? DashboardTheme.DisabledTextColor
                : foreColor.IsEmpty
                    ? DashboardTheme.PrimaryTextColor
                    : foreColor;
            state.SecondaryTextColor = !enabled
                    || status == MetricCardStatus.Unavailable
                ? DashboardTheme.DisabledTextColor
                : DashboardTheme.SecondaryTextColor;
            return state;
        }

        public static string GetAccessibleName(string title)
        {
            return string.IsNullOrEmpty(title) ? "Metric" : title;
        }

        public static string GetAccessibleDescription(
            string primaryText,
            string secondaryText,
            string statusText,
            MetricCardStatus status,
            bool enabled)
        {
            string description = primaryText ?? string.Empty;
            if (!string.IsNullOrEmpty(secondaryText))
            {
                description += ". " + secondaryText;
            }

            string effectiveStatus = enabled
                ? GetEffectiveStatusText(statusText, status)
                : "Unavailable";
            if (!string.IsNullOrEmpty(effectiveStatus))
            {
                description += ". " + effectiveStatus;
            }

            return description;
        }

        public static void ValidateStatus(MetricCardStatus status)
        {
            switch (status)
            {
                case MetricCardStatus.None:
                case MetricCardStatus.Available:
                case MetricCardStatus.Warning:
                case MetricCardStatus.Unavailable:
                case MetricCardStatus.Error:
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        private static Color GetStatusColor(
            MetricCardStatus status,
            Color accentColor,
            bool enabled)
        {
            if (!enabled)
            {
                return DashboardTheme.DisabledTextColor;
            }

            switch (status)
            {
                case MetricCardStatus.Available:
                    return DashboardTheme.SuccessColor;
                case MetricCardStatus.Warning:
                    return DashboardTheme.WarningColor;
                case MetricCardStatus.Unavailable:
                    return DashboardTheme.DisabledTextColor;
                case MetricCardStatus.Error:
                    return DashboardTheme.DangerColor;
                default:
                    return accentColor;
            }
        }

        private static string GetEffectiveStatusText(
            string statusText,
            MetricCardStatus status)
        {
            if (!string.IsNullOrEmpty(statusText))
            {
                return statusText;
            }

            switch (status)
            {
                case MetricCardStatus.Available:
                    return "Available";
                case MetricCardStatus.Warning:
                    return "Attention";
                case MetricCardStatus.Unavailable:
                    return "Unavailable";
                case MetricCardStatus.Error:
                    return "Error";
                default:
                    return string.Empty;
            }
        }
    }

    internal sealed class MetricCardVisualState
    {
        public string Title;
        public string PrimaryText;
        public string SecondaryText;
        public string StatusText;
        public Color SurfaceColor;
        public Color AccentColor;
        public Color PrimaryTextColor;
        public Color SecondaryTextColor;
        public Color StatusColor;
    }
}
