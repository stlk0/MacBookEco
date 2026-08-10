using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacBookEco.App
{
    /// <summary>
    /// Compact read-only dashboard metric host. Status presentation and GDI
    /// drawing are delegated so this control owns only WinForms lifecycle.
    /// </summary>
    public sealed class MetricCard : Control
    {
        private string _title;
        private string _primaryText;
        private string _secondaryText;
        private string _statusText;
        private Color _accentColor;
        private MetricCardStatus _status;

        public MetricCard()
        {
            _title = "Metric";
            _primaryText = "\u2014";
            _secondaryText = string.Empty;
            _statusText = string.Empty;
            _accentColor = DashboardTheme.AccentColor;
            _status = MetricCardStatus.None;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
            ForeColor = DashboardTheme.PrimaryTextColor;
            Font = DashboardTheme.BodyFont;
            MinimumSize = new Size(160, 90);
            Size = new Size(220, 105);
            TabStop = false;
            AccessibleRole = AccessibleRole.Grouping;
            UpdateAccessibilityText();
        }

        public string Title
        {
            get { return _title; }
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_title, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _title = normalized;
                UpdateAccessibilityText();
                Invalidate();
            }
        }

        public string PrimaryText
        {
            get { return _primaryText; }
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_primaryText, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _primaryText = normalized;
                UpdateAccessibilityText();
                Invalidate();
            }
        }

        public string SecondaryText
        {
            get { return _secondaryText; }
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_secondaryText, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _secondaryText = normalized;
                UpdateAccessibilityText();
                Invalidate();
            }
        }

        public Color AccentColor
        {
            get { return _accentColor; }
            set
            {
                Color normalized = value.IsEmpty
                    ? DashboardTheme.AccentColor
                    : value;
                if (_accentColor == normalized)
                {
                    return;
                }

                _accentColor = normalized;
                Invalidate();
            }
        }

        public MetricCardStatus Status
        {
            get { return _status; }
            set
            {
                MetricCardPresentation.ValidateStatus(value);
                if (_status == value)
                {
                    return;
                }

                _status = value;
                UpdateAccessibilityText();
                Invalidate();
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_statusText, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _statusText = normalized;
                UpdateAccessibilityText();
                Invalidate();
            }
        }

        protected override void OnEnabledChanged(EventArgs eventArgs)
        {
            base.OnEnabledChanged(eventArgs);
            UpdateAccessibilityText();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            MetricCardRenderer.Render(
                eventArgs.Graphics,
                ClientRectangle,
                MetricCardPresentation.CreateVisualState(
                    _title,
                    _primaryText,
                    _secondaryText,
                    _statusText,
                    _accentColor,
                    _status,
                    ForeColor,
                    Enabled));
            base.OnPaint(eventArgs);
        }

        private void UpdateAccessibilityText()
        {
            AccessibleName = MetricCardPresentation.GetAccessibleName(_title);
            AccessibleDescription = MetricCardPresentation.GetAccessibleDescription(
                _primaryText,
                _secondaryText,
                _statusText,
                _status,
                Enabled);
        }
    }
}
