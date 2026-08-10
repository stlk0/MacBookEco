using System;

namespace MacBookEco.App
{
    internal struct TimeSeriesStatistics
    {
        public int Count;
        public double Minimum;
        public double Maximum;
        public double Sum;
        public double Average;
    }

    internal static class TimeSeriesStatisticsCalculator
    {
        public static TimeSeriesStatistics Calculate(
            TimeSeriesBuffer buffer,
            DateTime windowStart,
            DateTime windowEnd)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            TimeSeriesStatistics statistics = new TimeSeriesStatistics();
            statistics.Minimum = double.MaxValue;
            statistics.Maximum = double.MinValue;
            for (int index = 0; index < buffer.Count; index++)
            {
                TimeSeriesSample sample = buffer.GetChronologicalSample(index);
                if (!sample.IsValid
                    || sample.TimestampUtc < windowStart
                    || sample.TimestampUtc > windowEnd)
                {
                    continue;
                }

                statistics.Minimum = Math.Min(statistics.Minimum, sample.Value);
                statistics.Maximum = Math.Max(statistics.Maximum, sample.Value);
                statistics.Sum += sample.Value;
                statistics.Count++;
            }

            if (statistics.Count > 0)
            {
                statistics.Average = statistics.Sum / statistics.Count;
            }
            else
            {
                statistics.Minimum = 0.0;
                statistics.Maximum = 0.0;
                statistics.Average = 0.0;
            }

            return statistics;
        }
    }
}
