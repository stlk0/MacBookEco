using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MacBookEco.Core;
using MacBookEco.Telemetry;

namespace MacBookEco.App
{
    /// <summary>
    /// Owns the overview page widgets and projects telemetry snapshots into
    /// metric cards and graphs. It has no action, dialog, or platform logic.
    /// </summary>
    internal sealed class DashboardOverviewPage
    {
        private MetricCard _batteryCard;
        private MetricCard _cpuCard;
        private MetricCard _displayCard;
        private MetricCard _gpuCard;
        private RingBufferGraph _systemPowerGraph;
        private RingBufferGraph _cpuLoadGraph;
        private RingBufferGraph _cpuTemperatureGraph;
        private RingBufferGraph _gpuLoadGraph;
        private RingBufferGraph _gpuPowerGraph;
        private RingBufferGraph _gpuTemperatureGraph;
        private readonly Control _view;

        public DashboardOverviewPage()
        {
            _view = BuildView();
        }

        public Control View => _view;

        public void Update(TelemetrySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            UpdateBatteryCard(snapshot.Battery);
            UpdateCpuCard(snapshot.Cpu);
            UpdateDisplayCard(snapshot.Display);
            UpdateGpuCard(snapshot.Gpu);
            if (!snapshot.DashboardSampling)
            {
                return;
            }

            _systemPowerGraph.AddValue(
                snapshot.TimestampUtc,
                snapshot.Battery.DischargeWatts);
            _cpuLoadGraph.AddValue(
                snapshot.TimestampUtc,
                snapshot.Cpu.LoadPercent);
            _cpuTemperatureGraph.AddValue(
                snapshot.TimestampUtc,
                snapshot.Cpu.TemperatureCelsius);
            _gpuLoadGraph.AddValue(
                snapshot.TimestampUtc,
                snapshot.Gpu.LoadPercent);
            _gpuPowerGraph.AddValue(
                snapshot.TimestampUtc,
                snapshot.Gpu.PowerWatts);
            _gpuTemperatureGraph.AddValue(
                snapshot.TimestampUtc,
                snapshot.Gpu.TemperatureCelsius);
        }

        private Control BuildView()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = DashboardTheme.CanvasColor;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

            TableLayoutPanel metrics = new TableLayoutPanel();
            metrics.Dock = DockStyle.Fill;
            metrics.AutoSize = true;
            metrics.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            metrics.ColumnCount = 4;
            metrics.RowCount = 1;
            for (int index = 0; index < 4; index++)
            {
                metrics.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 25.0f));
            }

            _batteryCard = CreateMetricCard(
                "Battery",
                DashboardTheme.SuccessColor);
            _cpuCard = CreateMetricCard("CPU", DashboardTheme.AccentColor);
            _displayCard = CreateMetricCard(
                "Display",
                Color.FromArgb(61, 105, 173));
            _gpuCard = CreateMetricCard(
                "GPU",
                Color.FromArgb(162, 91, 47));
            metrics.Controls.Add(_batteryCard, 0, 0);
            metrics.Controls.Add(_cpuCard, 1, 0);
            metrics.Controls.Add(_displayCard, 2, 0);
            metrics.Controls.Add(_gpuCard, 3, 0);
            root.Controls.Add(metrics, 0, 0);

            TableLayoutPanel graphs = new TableLayoutPanel();
            graphs.Dock = DockStyle.Fill;
            graphs.ColumnCount = 3;
            graphs.RowCount = 2;
            graphs.Padding = new Padding(3, 5, 3, 2);
            for (int index = 0; index < 3; index++)
            {
                graphs.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 33.333f));
            }

            graphs.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f));
            graphs.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f));
            _systemPowerGraph = CreateGraph(
                "System battery draw",
                "Whole-system discharge, not CPU-only",
                "W",
                DashboardTheme.SuccessColor,
                "Available only while running from battery.");
            _systemPowerGraph.FixedMinimum = 0.0;
            _cpuLoadGraph = CreateGraph(
                "CPU load",
                "Processor activity over the last five minutes",
                "%",
                DashboardTheme.AccentColor,
                "Windows has not reported CPU activity yet.");
            _cpuLoadGraph.FixedMinimum = 0.0;
            _cpuLoadGraph.FixedMaximum = 100.0;
            _cpuLoadGraph.FillArea = false;
            _cpuTemperatureGraph = CreateGraph(
                "CPU temperature",
                "Optional hardware-monitor sensor",
                "\u00b0C",
                Color.FromArgb(206, 118, 39),
                "No safe user-mode CPU temperature source is available.");
            _cpuTemperatureGraph.FixedMinimum = 20.0;
            _cpuTemperatureGraph.FixedMaximum = 110.0;
            _gpuLoadGraph = CreateGraph(
                "GPU load",
                "Graphics activity over the last five minutes",
                "%",
                Color.FromArgb(121, 86, 176),
                "AMD ADL exposes no active GPU load sample.");
            _gpuLoadGraph.FixedMinimum = 0.0;
            _gpuLoadGraph.FixedMaximum = 100.0;
            _gpuLoadGraph.FillArea = false;
            _gpuPowerGraph = CreateGraph(
                "GPU power",
                "Read-only AMD ADL sensor",
                "W",
                Color.FromArgb(61, 105, 173),
                "The installed AMD driver exposes no power sensor.");
            _gpuPowerGraph.FixedMinimum = 0.0;
            _gpuTemperatureGraph = CreateGraph(
                "GPU temperature",
                "Read-only AMD ADL edge sensor",
                "\u00b0C",
                Color.FromArgb(177, 68, 68),
                "The installed AMD driver exposes no temperature sensor.");
            _gpuTemperatureGraph.FixedMinimum = 20.0;
            _gpuTemperatureGraph.FixedMaximum = 110.0;

            graphs.Controls.Add(_systemPowerGraph, 0, 0);
            graphs.Controls.Add(_cpuLoadGraph, 1, 0);
            graphs.Controls.Add(_cpuTemperatureGraph, 2, 0);
            graphs.Controls.Add(_gpuLoadGraph, 0, 1);
            graphs.Controls.Add(_gpuPowerGraph, 1, 1);
            graphs.Controls.Add(_gpuTemperatureGraph, 2, 1);
            root.Controls.Add(graphs, 0, 1);
            return root;
        }

        private static MetricCard CreateMetricCard(string title, Color accent)
        {
            MetricCard card = new MetricCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(4, 3, 4, 7);
            card.Title = title;
            card.PrimaryText = "N/A";
            card.SecondaryText = "Waiting for telemetry";
            card.AccentColor = accent;
            card.Status = MetricCardStatus.Unavailable;
            return card;
        }

        private static RingBufferGraph CreateGraph(
            string title,
            string subtitle,
            string unit,
            Color line,
            string emptyText)
        {
            RingBufferGraph graph = new RingBufferGraph(360);
            graph.Dock = DockStyle.Fill;
            graph.Margin = new Padding(4);
            graph.Font = DashboardTheme.CaptionFont;
            graph.Title = title;
            graph.Subtitle = subtitle;
            graph.Unit = unit;
            graph.LineColor = line;
            graph.EmptyText = emptyText;
            graph.TimeWindow = TimeSpan.FromMinutes(5.0);
            graph.TimeWindowLabel = "Last 5 min";
            return graph;
        }

        private void UpdateBatteryCard(BatteryTelemetry battery)
        {
            _batteryCard.PrimaryText = TelemetryText.Percent(battery.ChargePercent);
            string source = battery.AcOnline == true
                ? (battery.Charging == true
                    ? "AC \u00b7 charging"
                    : "AC power")
                : (battery.AcOnline == false ? "On battery" : "Power source N/A");
            _batteryCard.SecondaryText =
                TelemetryText.Watts(battery.DischargeWatts) + " \u00b7 " + source;
            _batteryCard.Status = ToCardStatus(battery.Availability);
            _batteryCard.StatusText = battery.AcOnline == false
                ? "Battery"
                : (battery.AcOnline == true ? "AC" : string.Empty);
        }

        private void UpdateCpuCard(CpuTelemetry cpu)
        {
            _cpuCard.PrimaryText = TelemetryText.Percent(cpu.LoadPercent);
            _cpuCard.SecondaryText = TelemetryText.Frequency(cpu.AverageMhz)
                + " \u00b7 " + TelemetryText.Temperature(cpu.TemperatureCelsius)
                + " \u00b7 " + TelemetryText.Watts(cpu.PowerWatts);
            _cpuCard.Status = ToCardStatus(cpu.Availability);
            _cpuCard.StatusText = cpu.TemperatureCelsius.HasValue
                ? "Sensors"
                : "Basic";
        }

        private void UpdateDisplayCard(DisplayTelemetry display)
        {
            _displayCard.PrimaryText = TelemetryText.Refresh(display.RefreshRateHz);
            _displayCard.SecondaryText = display.Width > 0
                ? display.Width.ToString(CultureInfo.InvariantCulture)
                    + "\u00d7"
                    + display.Height.ToString(CultureInfo.InvariantCulture)
                : "Resolution N/A";
            _displayCard.Status = ToCardStatus(display.Availability);
            DisplayModeDefinition currentMode =
                ProfileCatalog.GetModeForWindowsSelector(
                    display.RefreshRateHz);

            _displayCard.StatusText = currentMode == null
                ? string.Empty
                : currentMode.RequiresOwnedSupport
                    ? "Eco"
                    : currentMode.NativeRecovery ? "Native" : string.Empty;
        }

        private void UpdateGpuCard(GpuTelemetry gpu)
        {
            _gpuCard.PrimaryText = TelemetryText.Percent(gpu.LoadPercent);
            _gpuCard.SecondaryText = "Core "
                + TelemetryText.Frequency(gpu.CoreMhz)
                + " \u00b7 VRAM "
                + TelemetryText.Frequency(gpu.MemoryMhz)
                + " \u00b7 "
                + TelemetryText.Temperature(gpu.TemperatureCelsius);
            _gpuCard.Status = ToCardStatus(gpu.Availability);
            _gpuCard.StatusText = gpu.Availability == TelemetryAvailability.Available
                ? "Read-only"
                : string.Empty;
        }

        private static MetricCardStatus ToCardStatus(
            TelemetryAvailability availability)
        {
            switch (availability)
            {
                case TelemetryAvailability.Available:
                    return MetricCardStatus.Available;
                case TelemetryAvailability.Error:
                    return MetricCardStatus.Error;
                case TelemetryAvailability.Unavailable:
                case TelemetryAvailability.Unsupported:
                    return MetricCardStatus.Unavailable;
                default:
                    return MetricCardStatus.Warning;
            }
        }

    }
}
