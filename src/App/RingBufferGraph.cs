using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacBookEco.App
{
    /// <summary>
    /// WinForms host for a time-series graph. Sample storage, statistics,
    /// scaling, and GDI drawing live in dedicated collaborators.
    /// </summary>
    public sealed class RingBufferGraph : Control
    {
        private readonly TimeSeriesBuffer _samples;
        private string _title;
        private string _subtitle;
        private string _emptyText;
        private string _timeWindowLabel;
        private string _unit;
        private Color _lineColor;
        private double? _fixedMinimum;
        private double? _fixedMaximum;
        private bool _fillArea;
        private TimeSpan _timeWindow;
        // Reused rather than rebuilt per paint: it restates this control's own
        // fields, and six of these repaint on every telemetry tick.
        private readonly TimeSeriesGraphRenderOptions _renderOptions =
            new TimeSeriesGraphRenderOptions();

        public RingBufferGraph()
            : this(300)
        {
        }

        public RingBufferGraph(int capacity)
        {
            _samples = new TimeSeriesBuffer(capacity);
            _title = "Telemetry";
            _subtitle = string.Empty;
            _emptyText = "No data available";
            _timeWindowLabel = "Last 5 minutes";
            _unit = string.Empty;
            _lineColor = Color.FromArgb(45, 112, 214);
            _fillArea = true;
            _timeWindow = TimeSpan.FromMinutes(5.0);

            BackColor = Color.White;
            ForeColor = Color.FromArgb(30, 36, 45);
            MinimumSize = new Size(160, 110);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        public string Title
        {
            get { return _title; }
            set
            {
                _title = value ?? string.Empty;
                Invalidate();
            }
        }

        public string Subtitle
        {
            get { return _subtitle; }
            set
            {
                _subtitle = value ?? string.Empty;
                Invalidate();
            }
        }

        public string EmptyText
        {
            get { return _emptyText; }
            set
            {
                _emptyText = value ?? string.Empty;
                Invalidate();
            }
        }

        public string TimeWindowLabel
        {
            get { return _timeWindowLabel; }
            set
            {
                _timeWindowLabel = value ?? string.Empty;
                Invalidate();
            }
        }

        public string Unit
        {
            get { return _unit; }
            set
            {
                _unit = value ?? string.Empty;
                Invalidate();
            }
        }

        public Color LineColor
        {
            get { return _lineColor; }
            set
            {
                _lineColor = value;
                Invalidate();
            }
        }

        public double? FixedMinimum
        {
            get { return _fixedMinimum; }
            set
            {
                _fixedMinimum = value;
                Invalidate();
            }
        }

        public double? FixedMaximum
        {
            get { return _fixedMaximum; }
            set
            {
                _fixedMaximum = value;
                Invalidate();
            }
        }

        public bool FillArea
        {
            get { return _fillArea; }
            set
            {
                _fillArea = value;
                Invalidate();
            }
        }

        public TimeSpan TimeWindow
        {
            get { return _timeWindow; }
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _timeWindow = value;
                Invalidate();
            }
        }

        public void AddValue(DateTime timestamp, double? value)
        {
            // A republished snapshot is rejected by the buffer; repainting for
            // it would be six needless redraws every time the window is shown.
            if (_samples.Add(timestamp, value))
            {
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            _renderOptions.Title = _title;
            _renderOptions.Subtitle = _subtitle;
            _renderOptions.EmptyText = _emptyText;
            _renderOptions.TimeWindowLabel = _timeWindowLabel;
            _renderOptions.Unit = _unit;
            _renderOptions.LineColor = _lineColor;
            _renderOptions.FixedMinimum = _fixedMinimum;
            _renderOptions.FixedMaximum = _fixedMaximum;
            _renderOptions.FillArea = _fillArea;
            _renderOptions.TimeWindow = _timeWindow;
            TimeSeriesGraphRenderer.Render(
                eventArgs.Graphics,
                ClientRectangle,
                Font,
                BackColor,
                ForeColor,
                _samples,
                _renderOptions);
        }
    }
}
