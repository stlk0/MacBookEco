using System;

namespace MacBookEco.App
{
    /// <summary>
    /// Fixed-capacity chronological sample store. Invalid numeric values are
    /// retained as explicit gaps so a renderer never connects them visually.
    /// </summary>
    internal sealed class TimeSeriesBuffer
    {
        private readonly double[] _values;
        private readonly bool[] _valid;
        private readonly long[] _utcTicks;
        private int _head;
        private int _count;

        public TimeSeriesBuffer(int capacity)
        {
            if (capacity < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _values = new double[capacity];
            _valid = new bool[capacity];
            _utcTicks = new long[capacity];
        }

        public int Count => _count;

        /// <summary>
        /// Stores a sample and reports whether it was accepted. The same
        /// snapshot is republished whenever the dashboard is shown, so a
        /// sample that is not newer than the newest stored one is dropped
        /// rather than plotted twice at the same instant. Keeping the series
        /// strictly increasing is also what lets the visible window read the
        /// newest timestamp without scanning.
        /// </summary>
        public bool Add(DateTime timestamp, double? value)
        {
            DateTime timestampUtc = NormalizeUtc(timestamp);
            TimeSeriesSample latest;
            if (TryGetLatest(out latest)
                && timestampUtc.Ticks <= latest.TimestampUtc.Ticks)
            {
                return false;
            }

            bool valid = value.HasValue
                && !double.IsNaN(value.Value)
                && !double.IsInfinity(value.Value);

            _valid[_head] = valid;
            _values[_head] = valid ? value.Value : 0.0;
            _utcTicks[_head] = timestampUtc.Ticks;
            _head = (_head + 1) % _values.Length;
            if (_count < _values.Length)
            {
                _count++;
            }

            return true;
        }

        public TimeSeriesSample GetChronologicalSample(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int oldest = _count == _values.Length ? _head : 0;
            int valueIndex = (oldest + index) % _values.Length;
            return new TimeSeriesSample(
                new DateTime(_utcTicks[valueIndex], DateTimeKind.Utc),
                _valid[valueIndex],
                _values[valueIndex]);
        }

        public bool TryGetLatest(out TimeSeriesSample sample)
        {
            if (_count == 0)
            {
                sample = new TimeSeriesSample(
                    DateTime.MinValue,
                    false,
                    0.0);
                return false;
            }

            int index = (_head - 1 + _values.Length) % _values.Length;
            sample = new TimeSeriesSample(
                new DateTime(_utcTicks[index], DateTimeKind.Utc),
                _valid[index],
                _values[index]);
            return true;
        }

        public void GetVisibleWindow(
            TimeSpan timeWindow,
            out DateTime windowStart,
            out DateTime windowEnd)
        {
            if (timeWindow <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeWindow));
            }

            // Samples are strictly increasing, so the newest one is the last.
            TimeSeriesSample latest;
            windowEnd = TryGetLatest(out latest) && latest.TimestampUtc.Ticks > 0L
                ? latest.TimestampUtc
                : DateTime.UtcNow;
            long durationTicks = Math.Min(timeWindow.Ticks, windowEnd.Ticks);
            windowStart = new DateTime(
                windowEnd.Ticks - durationTicks,
                DateTimeKind.Utc);
        }

        private static DateTime NormalizeUtc(DateTime timestamp)
        {
            if (timestamp.Kind == DateTimeKind.Utc)
            {
                return timestamp;
            }

            if (timestamp.Kind == DateTimeKind.Local)
            {
                return timestamp.ToUniversalTime();
            }

            return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        }
    }

    internal struct TimeSeriesSample
    {
        public TimeSeriesSample(DateTime timestampUtc, bool isValid, double value)
        {
            TimestampUtc = timestampUtc;
            IsValid = isValid;
            Value = value;
        }

        public readonly DateTime TimestampUtc;

        public readonly bool IsValid;

        public readonly double Value;
    }
}
