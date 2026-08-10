using System;

namespace MacBookEco.App
{
    internal static class TimeSeriesAxisRange
    {
        public static void Calculate(
            TimeSeriesStatistics statistics,
            double? fixedMinimum,
            double? fixedMaximum,
            out double minimum,
            out double maximum)
        {
            if (statistics.Count == 0)
            {
                minimum = fixedMinimum ?? 0.0;
                maximum = fixedMaximum ?? (minimum + 1.0);
            }
            else
            {
                minimum = statistics.Minimum;
                maximum = statistics.Maximum;

                double rawSpan = maximum - minimum;
                double padding = Math.Max(rawSpan * 0.1, 0.5);
                if (!fixedMinimum.HasValue)
                {
                    minimum -= padding;
                }

                if (!fixedMaximum.HasValue)
                {
                    maximum += padding;
                }

                if (fixedMinimum.HasValue)
                {
                    minimum = fixedMinimum.Value;
                }

                if (fixedMaximum.HasValue)
                {
                    maximum = fixedMaximum.Value;
                }
            }

            if (double.IsNaN(minimum) || double.IsInfinity(minimum))
            {
                minimum = 0.0;
            }

            if (double.IsNaN(maximum) || double.IsInfinity(maximum))
            {
                maximum = minimum + 1.0;
            }

            if (maximum <= minimum)
            {
                if (fixedMinimum.HasValue && !fixedMaximum.HasValue)
                {
                    maximum = minimum + 1.0;
                }
                else if (!fixedMinimum.HasValue && fixedMaximum.HasValue)
                {
                    minimum = maximum - 1.0;
                }
                else
                {
                    maximum = minimum + 1.0;
                }
            }
        }
    }
}
